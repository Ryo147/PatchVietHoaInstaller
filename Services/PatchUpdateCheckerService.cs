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
    /// </summary>
    public record PatchCheckResult(List<PatchUpdateInfo> Updates, bool AnyCheckFailed);

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

            foreach (var profile in profiles)
            {
                if (profile.IsComingSoon) continue;
                if (string.IsNullOrWhiteSpace(profile.GitHubOwner) || string.IsNullOrWhiteSpace(profile.GitHubRepo))
                    continue;

                var release = await GitHubReleaseService.GetReleaseForProfileAsync(profile.GitHubOwner, profile.GitHubRepo, profile.GitHubReleaseTag);
                if (release == null) { anyCheckFailed = true; continue; }

                var asset = GitHubReleaseService.FindAsset(release, profile.AssetNameContains);
                if (asset == null) { anyCheckFailed = true; continue; }

                string? newVersion = GitHubReleaseService.ExtractVersionFromAssetName(asset.Name);
                if (string.IsNullOrWhiteSpace(newVersion)) { anyCheckFailed = true; continue; }

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

            return new PatchCheckResult(results, anyCheckFailed);
        }
    }
}