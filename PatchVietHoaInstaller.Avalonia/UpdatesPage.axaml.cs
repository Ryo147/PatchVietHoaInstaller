using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Threading.Tasks;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    public partial class UpdatesPage : UserControl
    {
        private const string AppUpdateOwner = "Ryo147";
        private const string AppUpdateRepo = "PatchVietHoaInstaller";

        private static string AppVersion =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "0.0.0.0";

        private GitHubReleaseAsset? _pendingAsset;
        private string? _pendingHash;
        private string? _releaseUrl;

        /// <summary>Báo cho Shell (MainWindow) biết người dùng bấm "Cập nhật ngay", kèm asset + hash đã tìm sẵn.</summary>
        public event Action<GitHubReleaseAsset, string?>? UpdateNowRequested;

        public UpdatesPage()
        {
            InitializeComponent();
            TxtCurrentVersion.Text = $"Phiên bản hiện tại: v{AppVersion}";
            _ = LoadLatestReleaseAsync();
        }

        private async Task LoadLatestReleaseAsync()
        {
            var release = await GitHubReleaseService.GetLatestReleaseAsync(AppUpdateOwner, AppUpdateRepo);

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                SetStatusDot("#8B90A3");
                TxtLatestStatus.Text = "Không thể kiểm tra bản cập nhật (mất mạng hoặc chưa có bản phát hành).";
                return;
            }

            bool isNewer = GitHubReleaseService.IsNewerVersion(AppVersion, release.TagName);

            if (isNewer)
            {
                SetStatusDot("#FF5A4A");
                TxtLatestStatus.Text = $"Có bản mới: {release.TagName}";
            }
            else
            {
                SetStatusDot("#3DDC97");
                TxtLatestStatus.Text = $"Bạn đang dùng bản mới nhất ({release.TagName}).";
            }

            // Changelog ngắn thôi — chỉ hiện khi nhóm dịch có ghi chú, tránh chiếm nhiều chỗ khi trống.
            if (!string.IsNullOrWhiteSpace(release.Body))
            {
                TxtChangelog.Text = release.Body;
                TxtChangelog.IsVisible = true;
            }

            if (!string.IsNullOrWhiteSpace(release.HtmlUrl))
            {
                _releaseUrl = release.HtmlUrl;
                TxtOpenReleaseLink.IsVisible = true;
            }

            if (isNewer)
            {
                // GHI CHÚ PORT: bản WPF gốc chỉ tìm asset đuôi ".exe" — đúng cho Windows nhưng sẽ không
                // bao giờ khớp trên Linux (asset build cho Linux, nếu có, sẽ không có đuôi .exe). Vì repo
                // GitHub hiện tại chỉ publish file .exe (build Windows), tự-cập-nhật trên Linux sẽ luôn
                // rơi vào nhánh "tải thủ công qua link GitHub" bên dưới cho tới khi nhóm dịch build và
                // đính kèm thêm asset riêng cho Linux trong quy trình release.
                string assetHint = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows) ? ".exe" : "linux";

                var asset = GitHubReleaseService.FindAsset(release, assetHint);
                if (asset != null)
                {
                    _pendingAsset = asset;
                    _pendingHash = GitHubReleaseService.ExtractSha256Hex(asset.Digest);
                    BtnUpdateNow.IsVisible = true;
                    BtnUpdateNow.IsEnabled = true;
                }
                else
                {
                    // Không có asset phù hợp với OS hiện tại -> không tự cập nhật được, chỉ còn cách tải thủ công qua link GitHub
                    TxtLatestStatus.Text += " (không thể tự cập nhật, vui lòng tải thủ công)";
                }
            }
        }

        private void SetStatusDot(string hexColor)
        {
            StatusDot.Fill = new SolidColorBrush(Color.Parse(hexColor));
        }

        private void BtnUpdateNow_Click(object? sender, RoutedEventArgs e)
        {
            if (_pendingAsset == null)
                return;

            UpdateNowRequested?.Invoke(_pendingAsset, _pendingHash);
        }

        /// <summary>MainWindow gọi để báo tiến trình tải/cài trong lúc tự cập nhật.</summary>
        public void SetUpdateInProgress(string message)
        {
            BtnUpdateNow.IsEnabled = false;
            TxtLatestStatus.Text = $"🔄 {message}";
        }

        /// <summary>MainWindow gọi khi tự cập nhật thất bại — cho phép người dùng thử lại.</summary>
        public void SetUpdateFailed()
        {
            BtnUpdateNow.IsEnabled = true;
            TxtLatestStatus.Text = "Đã có bản cập nhật mới, nhưng lần tải vừa rồi thất bại.";
        }

        private void BtnOpenRelease_Click(object? sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_releaseUrl))
                PlatformHelper.OpenUrlInBrowser(_releaseUrl);
        }
    }
}
