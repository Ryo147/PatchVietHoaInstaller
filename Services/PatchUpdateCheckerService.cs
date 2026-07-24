using System.Collections.Generic;
using System.Threading.Tasks;
using VietHoaInstaller.Models;

namespace VietHoaInstaller.Services
{
    public record PatchUpdateInfo(GameProfile Profile, string NewVersion, string ReleaseUrl, GitHubReleaseAsset Asset);

    /// <summary>
    /// Kiểm tra tất cả GameProfile đã cấu hình GitHub, so sánh version tách từ tên asset mới nhất
    /// với GameProfile.KnownPatchVersion. Dùng cho tính năng chạy nền thông báo Patch mới.
    /// </summary>
    public static class PatchUpdateCheckerService
    {
        public static async Task<List<PatchUpdateInfo>> CheckAllAsync(IEnumerable<GameProfile> profiles)
        {
            var results = new List<PatchUpdateInfo>();

            foreach (var profile in profiles)
            {
                if (profile.IsComingSoon) continue;
                if (string.IsNullOrWhiteSpace(profile.GitHubOwner) || string.IsNullOrWhiteSpace(profile.GitHubRepo))
                    continue;

                var release = await GitHubReleaseService.GetLatestReleaseAsync(profile.GitHubOwner, profile.GitHubRepo);
                if (release == null) continue;

                var asset = GitHubReleaseService.FindAsset(release, profile.AssetNameContains);
                if (asset == null) continue;

                string? newVersion = GitHubReleaseService.ExtractVersionFromAssetName(asset.Name);
                if (string.IsNullOrWhiteSpace(newVersion)) continue;

                if (string.IsNullOrWhiteSpace(profile.KnownPatchVersion) ||
                    GitHubReleaseService.IsNewerVersion(profile.KnownPatchVersion, newVersion))
                {
                    results.Add(new PatchUpdateInfo(profile, newVersion, release.HtmlUrl, asset));
                }
            }

            return results;
        }
    }
}