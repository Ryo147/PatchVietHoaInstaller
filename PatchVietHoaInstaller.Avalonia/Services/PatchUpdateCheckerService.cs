using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VietHoaInstaller.Models;

namespace VietHoaInstaller.Services
{
    public record PatchUpdateInfo(GameProfile Profile, string NewVersion, string ReleaseUrl, GitHubReleaseAsset Asset);

    /// <summary>
    /// Kết quả kiểm tra: Updates là các bản mới tìm được; AnyCheckFailed = true nếu có ít nhất 1 profile
    /// KHÔNG kiểm tra được (API lỗi/rate-limit/mất mạng/không tìm thấy asset khớp) — khác hẳn ý nghĩa với
    /// "đã kiểm tra xong và đúng là chưa có bản mới". Dùng để UI không báo nhầm "chưa có bản mới" khi
    /// thực ra là chưa hỏi được GitHub.
    /// FailureStatus: lý do cụ thể nếu AnyCheckFailed = true, để UI hiển thị đúng thông báo (rate-limit
    /// khác hẳn mất mạng thật, khác hẳn lỗi cấu hình owner/repo/tag) thay vì gộp chung "mất mạng".
    /// Ưu tiên hiển thị theo thứ tự: NetworkError > RateLimited > NotFound > OtherHttpError — vì mất
    /// mạng thật là điều người dùng cần biết trước tiên nếu xảy ra đồng thời.
    /// </summary>
    public record PatchCheckResult(List<PatchUpdateInfo> Updates, bool AnyCheckFailed, GitHubFetchStatus? FailureStatus = null);

    /// <summary>
    /// Kiểm tra tất cả GameProfile đã cấu hình GitHub, so sánh version tách từ tên asset mới nhất
    /// với version THẬT SỰ đã cài trên máy (đọc từ manifest.json do PatchInstallerService ghi lại lúc
    /// cài đặt thành công) — không phụ thuộc vào KnownPatchVersion hardcode trong app nữa.
    /// Nếu chưa cài (không có manifest) thì fallback về KnownPatchVersion để vẫn có 1 baseline hợp lý.
    /// Dùng cho tính năng chạy nền thông báo Patch mới.
    /// </summary>
    public static class PatchUpdateCheckerService
    {
        /// <param name="profiles">Danh sách GameProfile cần kiểm tra.</param>
        /// <param name="resolveGameFolder">
        /// Hàm trả về thư mục game đã cài ứng với 1 profile (để đọc manifest tại đó), hoặc null/"" nếu
        /// chưa biết/chưa cài. MainWindow truyền vào dựa trên AppSettings.LastGameFolder hiện tại.
        /// </param>
        public static async Task<PatchCheckResult> CheckAllAsync(
            IEnumerable<GameProfile> profiles,
            Func<GameProfile, string?>? resolveGameFolder = null)
        {
            var results = new List<PatchUpdateInfo>();
            bool anyCheckFailed = false;
            var failureStatuses = new List<GitHubFetchStatus>();

            foreach (var profile in profiles)
            {
                if (profile.IsComingSoon) continue;
                if (string.IsNullOrWhiteSpace(profile.GitHubOwner) || string.IsNullOrWhiteSpace(profile.GitHubRepo))
                    continue;

                var fetchResult = await GitHubReleaseService.GetReleaseForProfileWithStatusAsync(profile.GitHubOwner, profile.GitHubRepo, profile.GitHubReleaseTag);
                var release = fetchResult.Release;
                if (release == null)
                {
                    anyCheckFailed = true;
                    failureStatuses.Add(fetchResult.Status);
                    continue;
                }

                var asset = GitHubReleaseService.FindAsset(release, profile.AssetNameContains);
                if (asset == null)
                {
                    // Release lấy được nhưng không tìm thấy asset khớp tên -> lỗi cấu hình (AssetNameContains
                    // sai hoặc release chưa đính kèm đúng file), không phải lỗi mạng/rate-limit.
                    anyCheckFailed = true;
                    failureStatuses.Add(GitHubFetchStatus.NotFound);
                    continue;
                }

                string? newVersion = GitHubReleaseService.ExtractVersionFromAssetName(asset.Name);
                if (string.IsNullOrWhiteSpace(newVersion))
                {
                    // Tên asset không khớp quy ước "..._v{version}.ext" -> cũng là lỗi cấu hình/đặt tên.
                    anyCheckFailed = true;
                    failureStatuses.Add(GitHubFetchStatus.NotFound);
                    continue;
                }

                // Ưu tiên đọc version THẬT đã cài trên máy (ground truth). Chỉ fallback về KnownPatchVersion
                // hardcode khi chưa cài lần nào qua tool này (chưa có manifest) hoặc không xác định được thư mục game.
                string? gameFolder = resolveGameFolder?.Invoke(profile);
                string installedVersion = string.IsNullOrWhiteSpace(gameFolder)
                    ? ""
                    : PatchInstallerService.TryGetInstalledPatchVersion(gameFolder, profile.Name);

                string baselineVersion = !string.IsNullOrWhiteSpace(installedVersion)
                    ? installedVersion
                    : profile.KnownPatchVersion;

                if (string.IsNullOrWhiteSpace(baselineVersion) ||
                    GitHubReleaseService.IsNewerVersion(baselineVersion, newVersion))
                {
                    results.Add(new PatchUpdateInfo(profile, newVersion, release.HtmlUrl, asset));
                }
            }

            GitHubFetchStatus? primaryFailure = null;
            foreach (var candidate in new[]
                     {
                         GitHubFetchStatus.NetworkError,
                         GitHubFetchStatus.RateLimited,
                         GitHubFetchStatus.NotFound,
                         GitHubFetchStatus.OtherHttpError
                     })
            {
                if (failureStatuses.Contains(candidate)) { primaryFailure = candidate; break; }
            }

            return new PatchCheckResult(results, anyCheckFailed, primaryFailure);
        }
    }
}