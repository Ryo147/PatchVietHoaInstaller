using System;
using System.Windows;
using System.Windows.Media;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    public partial class UpdatesPage : System.Windows.Controls.UserControl
    {
        private const string AppUpdateOwner = "Ryo147";
        private const string AppUpdateRepo = "PatchVietHoaInstaller";

        private static string AppVersion =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        private GitHubReleaseAsset? _pendingAsset;
        private string? _pendingHash;

        /// <summary>Báo cho Shell (MainWindow) biết người dùng bấm "Cập nhật ngay", kèm asset + hash đã tìm sẵn.</summary>
        public event Action<GitHubReleaseAsset, string?>? UpdateNowRequested;

        public UpdatesPage()
        {
            InitializeComponent();
            TxtCurrentVersion.Text = $"Phiên bản hiện tại: v{AppVersion}";
            _ = LoadLatestReleaseAsync();
        }

        private async System.Threading.Tasks.Task LoadLatestReleaseAsync()
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
                TxtChangelog.Visibility = Visibility.Visible;
            }

            if (!string.IsNullOrWhiteSpace(release.HtmlUrl))
            {
                LinkOpenRelease.NavigateUri = new Uri(release.HtmlUrl);
                TxtOpenReleaseLink.Visibility = Visibility.Visible;
            }

            if (isNewer)
            {
                var asset = GitHubReleaseService.FindAsset(release, ".exe");
                if (asset != null)
                {
                    _pendingAsset = asset;
                    _pendingHash = GitHubReleaseService.ExtractSha256Hex(asset.Digest);
                    BtnUpdateNow.Visibility = Visibility.Visible;
                    BtnUpdateNow.IsEnabled = true;
                }
                else
                {
                    // Không có file .exe đính kèm -> không tự cập nhật được, chỉ còn cách tải thủ công qua link GitHub
                    TxtLatestStatus.Text += " (không thể tự cập nhật, vui lòng tải thủ công)";
                }
            }
        }

        private void SetStatusDot(string hexColor)
        {
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
        }

        private void BtnUpdateNow_Click(object sender, RoutedEventArgs e)
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

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            if (e.Uri == null)
                return;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}
