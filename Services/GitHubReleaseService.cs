using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

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

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset> Assets { get; set; } = new();
    }

    /// <summary>
    /// Gọi GitHub Releases API (public, không cần token) để: (1) kiểm tra bản cập nhật mới của chính
    /// app cài đặt, và (2) tự động lấy link tải + hash SHA-256 mới nhất của từng bản patch, thay vì
    /// phải hardcode link trong GameProfile rồi phải sửa code mỗi khi ra bản patch mới.
    /// </summary>
    public static class GitHubReleaseService
    {
        // GitHub API yêu cầu bắt buộc phải có User-Agent, nếu không sẽ bị từ chối request (403).
        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VietHoaInstaller", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.Timeout = TimeSpan.FromSeconds(10);
            return client;
        }

        /// <summary>Lấy thông tin bản release mới nhất của 1 repo. Trả về null nếu lỗi mạng/repo không có release/hết rate limit.</summary>
        public static async Task<GitHubRelease?> GetLatestReleaseAsync(string owner, string repo, CancellationToken ct = default)
        {
            try
            {
                string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
                using var response = await _http.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                    return null;

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                return await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: ct);
            }
            catch
            {
                // Không có mạng, hết rate limit (60 request/giờ không token), hoặc repo/release không tồn tại
                // -> coi như "không kiểm tra được", để nơi gọi tự quyết định dùng giá trị fallback đã hardcode.
                return null;
            }
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

            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                var match = release.Assets.Find(a =>
                    a.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            // Chỉ fallback về asset đầu tiên khi release CHỈ CÓ ĐÚNG 1 file — an toàn cho trường hợp
            // 1 game = 1 release. Nếu release có nhiều asset (kiểu gộp N game chung 1 release) mà không
            // khớp tên nào, TRẢ VỀ NULL thay vì đoán bừa — tránh cài nhầm patch của game khác.
            return release.Assets.Count == 1 ? release.Assets[0] : null;
        }

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
    }
}