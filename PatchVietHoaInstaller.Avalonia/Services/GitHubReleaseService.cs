using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace VietHoaInstaller.Services
{
    public class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        /// <summary>Dạng "sha256:abcdef...". GitHub chỉ trả field này cho asset upload sau tháng 6/2025, asset cũ sẽ là null.</summary>
        [JsonPropertyName("digest")]
        public string? Digest { get; set; }
    }

    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset> Assets { get; set; } = new();
    }

    /// <summary>
    /// Lý do 1 lần gọi GitHub API KHÔNG trả về release tươi thành công. Trước đây mọi lỗi (rate-limit,
    /// mất mạng thật, config sai owner/repo/tag...) đều bị gộp chung thành "mất mạng" khiến người dùng
    /// hiểu nhầm dù mạng hoàn toàn bình thường. Tách rõ ra để UI báo đúng nguyên nhân và đúng hành động
    /// nên làm (chờ vài phút vs kiểm tra lại mạng vs báo lỗi cho nhóm dịch).
    /// </summary>
    public enum GitHubFetchStatus
    {
        Success,
        RateLimited,
        NetworkError,
        NotFound,
        OtherHttpError
    }

    /// <param name="Release">
    /// Release lấy được (asset + digest...). Có thể KHÁC null dù <paramref name="Status"/> != Success,
    /// nếu đó là dữ liệu CŨ còn lưu trong cache từ lần gọi thành công gần nhất (vd đang bị rate-limit
    /// nhưng có bản đã lấy được vài phút trước) — dùng tạm còn hơn coi như thất bại hoàn toàn.
    /// <see cref="IsStale"/> cho biết dữ liệu này có phải "tươi" (vừa lấy trong lần gọi này) hay không.
    /// </param>
    public record GitHubFetchResult(GitHubFetchStatus Status, GitHubRelease? Release, bool IsStale = false);

    /// <summary>
    /// Gọi GitHub Releases API (public, không cần token) để: (1) kiểm tra bản cập nhật mới của chính
    /// app cài đặt, và (2) tự động lấy link tải + hash SHA-256 mới nhất của từng bản patch, thay vì
    /// phải hardcode link trong GameProfile rồi phải sửa code mỗi khi ra bản patch mới.
    /// </summary>
    public static class GitHubReleaseService
    {
        // GitHub API yêu cầu bắt buộc phải có User-Agent, nếu không sẽ bị từ chối request (403).
        private static readonly HttpClient _http = CreateClient();

        // ===== CACHE (giảm tiêu tốn quota rate-limit 60 request/giờ dùng chung theo IP) =====
        // 1. Trong CacheTtl: trả thẳng dữ liệu cũ, KHÔNG gọi mạng luôn -> tiết kiệm request lẫn thời gian.
        // 2. Sau CacheTtl: gọi lại kèm header "If-None-Match" (ETag lần trước). Nếu GitHub trả 304 (không
        //    có gì mới) thì theo tài liệu chính thức của GitHub, request dạng conditional trả 304 KHÔNG
        //    bị tính vào rate-limit -> gần như "hỏi miễn phí" xem có bản mới hay chưa.
        private static readonly Dictionary<string, CacheEntry> _cache = new();
        private static readonly object _cacheLock = new();
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

        private sealed class CacheEntry
        {
            public DateTime FetchedAtUtc;
            public string? ETag;
            public GitHubRelease? Release;
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VietHoaInstaller", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.Timeout = TimeSpan.FromSeconds(10);
            return client;
        }

        /// <summary>Lấy thông tin release theo đúng tag chỉ định, kèm lý do rõ ràng nếu thất bại.
        /// Dùng cho các GameProfile đã tách tag riêng, để game này không bị "che" khi có game khác tạo release mới hơn.</summary>
        public static Task<GitHubFetchResult> GetReleaseByTagWithStatusAsync(string owner, string repo, string tag, CancellationToken ct = default)
        {
            string url = $"https://api.github.com/repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(tag)}";
            return FetchWithCacheAsync(url, ct);
        }

        /// <summary>Lấy thông tin bản release mới nhất của 1 repo, kèm lý do rõ ràng nếu thất bại.</summary>
        public static Task<GitHubFetchResult> GetLatestReleaseWithStatusAsync(string owner, string repo, CancellationToken ct = default)
        {
            string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            return FetchWithCacheAsync(url, ct);
        }

        /// <summary>
        /// Điểm gọi API duy nhất mà PatchInstallerService/PatchUpdateCheckerService nên dùng, kèm lý do
        /// rõ ràng nếu thất bại (xem <see cref="GitHubFetchStatus"/>). Nếu GameProfile có cấu hình
        /// <paramref name="releaseTag"/> riêng -> gọi đúng release đó. Nếu để rỗng -> fallback về release
        /// "latest" chung của repo (hành vi cũ, chỉ an toàn khi repo chỉ có đúng 1 release đang hoạt động).
        /// </summary>
        public static Task<GitHubFetchResult> GetReleaseForProfileWithStatusAsync(string owner, string repo, string? releaseTag, CancellationToken ct = default)
            => string.IsNullOrWhiteSpace(releaseTag)
                ? GetLatestReleaseWithStatusAsync(owner, repo, ct)
                : GetReleaseByTagWithStatusAsync(owner, repo, releaseTag, ct);

        // ===== Các hàm cũ giữ nguyên chữ ký (Release-or-null) để tương thích ngược cho nơi gọi không
        // cần phân biệt lý do thất bại (vd PatchInstallerService vốn đã có logic fallback riêng). =====

        public static async Task<GitHubRelease?> GetReleaseByTagAsync(string owner, string repo, string tag, CancellationToken ct = default)
            => (await GetReleaseByTagWithStatusAsync(owner, repo, tag, ct)).Release;

        /// <summary>Trả về null nếu lỗi mạng/repo không có release/hết rate limit. Xem <see cref="GetLatestReleaseWithStatusAsync"/> nếu cần biết rõ lý do.</summary>
        public static async Task<GitHubRelease?> GetLatestReleaseAsync(string owner, string repo, CancellationToken ct = default)
            => (await GetLatestReleaseWithStatusAsync(owner, repo, ct)).Release;

        public static async Task<GitHubRelease?> GetReleaseForProfileAsync(string owner, string repo, string? releaseTag, CancellationToken ct = default)
            => (await GetReleaseForProfileWithStatusAsync(owner, repo, releaseTag, ct)).Release;

        private static async Task<GitHubFetchResult> FetchWithCacheAsync(string url, CancellationToken ct)
        {
            CacheEntry? cached;
            lock (_cacheLock)
            {
                _cache.TryGetValue(url, out cached);
            }

            // Vẫn còn trong TTL cache -> trả thẳng, KHÔNG gọi mạng, tiết kiệm quota + nhanh hơn.
            if (cached != null && DateTime.UtcNow - cached.FetchedAtUtc < CacheTtl)
            {
                return new GitHubFetchResult(GitHubFetchStatus.Success, cached.Release, IsStale: false);
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (!string.IsNullOrEmpty(cached?.ETag))
                    request.Headers.TryAddWithoutValidation("If-None-Match", cached!.ETag);

                using var response = await _http.SendAsync(request, ct);

                // 304: dữ liệu không đổi so với lần trước -> dùng lại cache. Theo tài liệu GitHub, request
                // dạng conditional trả 304 KHÔNG bị tính vào rate-limit, nên nhánh này gần như "miễn phí".
                if (response.StatusCode == HttpStatusCode.NotModified && cached != null)
                {
                    lock (_cacheLock) { cached.FetchedAtUtc = DateTime.UtcNow; }
                    return new GitHubFetchResult(GitHubFetchStatus.Success, cached.Release, IsStale: false);
                }

                if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (IsRateLimitResponse(response))
                    {
                        // Vẫn còn cache cũ (dù đã hết hạn TTL) -> thà dùng tạm còn hơn coi như thất bại hẳn.
                        return cached?.Release != null
                            ? new GitHubFetchResult(GitHubFetchStatus.RateLimited, cached.Release, IsStale: true)
                            : new GitHubFetchResult(GitHubFetchStatus.RateLimited, null);
                    }
                    return new GitHubFetchResult(GitHubFetchStatus.OtherHttpError, cached?.Release, IsStale: cached?.Release != null);
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new GitHubFetchResult(GitHubFetchStatus.NotFound, null);

                if (!response.IsSuccessStatusCode)
                    return new GitHubFetchResult(GitHubFetchStatus.OtherHttpError, cached?.Release, IsStale: cached?.Release != null);

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: ct);

                string? etag = response.Headers.ETag?.Tag;
                lock (_cacheLock)
                {
                    _cache[url] = new CacheEntry { FetchedAtUtc = DateTime.UtcNow, ETag = etag, Release = release };
                }

                return new GitHubFetchResult(GitHubFetchStatus.Success, release);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Net.Sockets.SocketException)
            {
                // Lỗi kết nối thật sự (DNS/timeout/mất mạng) -> khác hẳn rate-limit, nhưng vẫn thử dùng
                // cache cũ nếu có, thay vì thất bại trắng.
                return cached?.Release != null
                    ? new GitHubFetchResult(GitHubFetchStatus.NetworkError, cached.Release, IsStale: true)
                    : new GitHubFetchResult(GitHubFetchStatus.NetworkError, null);
            }
        }

        /// <summary>GitHub báo hết quota qua header "X-RateLimit-Remaining: 0" kèm mã 403, hoặc mã 429.</summary>
        private static bool IsRateLimitResponse(HttpResponseMessage response)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return true;

            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values))
                return values.FirstOrDefault() == "0";

            return false;
        }

        /// <summary>
        /// Trong các asset của release, tìm asset khớp tên chứa <paramref name="nameContains"/> (không phân biệt hoa thường).
        /// Nếu release chỉ có đúng 1 asset, trả về asset đó luôn (không cần khớp tên). Nếu release có
        /// NHIỀU asset (vd gộp chung nhiều game) mà không khớp tên nào, trả về null — để nơi gọi tự
        /// rơi về link hardcode đúng game, tránh lấy nhầm file của game khác.
        /// </summary>
        public static GitHubReleaseAsset? FindAsset(GitHubRelease release, string? nameContains)
        {
            if (release.Assets.Count == 0)
                return null;

            List<GitHubReleaseAsset> matches = string.IsNullOrWhiteSpace(nameContains)
                ? release.Assets
                : release.Assets.FindAll(a => a.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase));

            if (matches.Count > 0)
                return PickNewest(matches);

            // Không khớp tên nào & release chỉ có đúng 1 asset -> giữ hành vi cũ (an toàn cho 1 game = 1 release)
            return release.Assets.Count == 1 ? release.Assets[0] : null;
        }

        private static GitHubReleaseAsset PickNewest(List<GitHubReleaseAsset> matches)
        {
            GitHubReleaseAsset best = matches[0];
            Version? bestVersion = ParseVersionOrNull(ExtractVersionFromAssetName(best.Name));

            for (int i = 1; i < matches.Count; i++)
            {
                var candidateVersion = ParseVersionOrNull(ExtractVersionFromAssetName(matches[i].Name));
                if (candidateVersion != null && (bestVersion == null || candidateVersion > bestVersion))
                {
                    best = matches[i];
                    bestVersion = candidateVersion;
                }
            }
            return best;
        }

        private static Version? ParseVersionOrNull(string? versionStr)
            => versionStr != null && Version.TryParse(versionStr, out var v) ? v : null;

        /// <summary>Tách phần hash hex ra khỏi chuỗi digest dạng "sha256:abcdef..." của GitHub. Trả về null nếu không có.</summary>
        public static string? ExtractSha256Hex(string? digest)
        {
            if (string.IsNullOrWhiteSpace(digest))
                return null;

            const string prefix = "sha256:";
            return digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? digest[prefix.Length..]
                : null;
        }

        /// <summary>So sánh 2 chuỗi version dạng "v1.2.3" hoặc "1.2.3". Trả về true nếu latestTag mới hơn currentVersion.</summary>
        public static bool IsNewerVersion(string currentVersion, string latestTag)
        {
            string Clean(string s) => s.TrimStart('v', 'V');

            if (Version.TryParse(Clean(currentVersion), out var current) &&
                Version.TryParse(Clean(latestTag), out var latest))
            {
                return latest > current;
            }

            // Không parse được dạng Version chuẩn -> so sánh chuỗi thô, khác nhau thì coi là có bản mới
            return !string.Equals(Clean(currentVersion), Clean(latestTag), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Tách số version ra khỏi tên file asset theo quy ước "..._v{version}.zip", vd:
        /// "PATCHVH_P.I._v1.1.zip" -> "1.1". Trả về null nếu tên file không khớp quy ước.
        /// </summary>
        public static string? ExtractVersionFromAssetName(string assetName)
        {
            var match = Regex.Match(assetName, @"_v(\d+(?:\.\d+){1,3})(?=\.[a-zA-Z0-9]+$)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
