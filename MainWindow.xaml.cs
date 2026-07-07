using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VietHoaInstaller.Models;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    public partial class MainWindow : Window
    {
        // TODO: tăng số này mỗi khi build bản release mới, đồng thời đặt tag GitHub release trùng số này (vd tag "v1.0.1")
        // Đọc version thật từ .csproj (thuộc tính <Version>) thay vì hardcode 2 chỗ dễ quên đồng bộ.
        private static string AppVersion =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        private const string AppUpdateOwner = "Ryo147";
        private const string AppUpdateRepo = "PatchVietHoaInstaller";

        private readonly PatchInstallerService _installer = new();
        private CancellationTokenSource? _cts;

        public MainWindow()
        {
            InitializeComponent();
            RunAppVersion.Text = $"v{AppVersion}";
            LoadGameCatalog();
            LoadLastGameFolder();
            _ = CheckForAppUpdateAsync();
        }

        private void LoadGameCatalog()
        {
            CmbGame.ItemsSource = Models.GameCatalog.All;
            if (Models.GameCatalog.All.Count > 0)
                CmbGame.SelectedIndex = 0;
        }

        private void CmbGame_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbGame.SelectedItem is Models.GameProfile profile)
            {
                _installer.PatchDownloadUrl = profile.PatchDownloadUrl;
                _installer.RequiredGameFiles = profile.RequiredGameFiles;
                _installer.InstallMode = profile.InstallMode;
                _installer.ModFolderRelativePath = profile.ModFolderRelativePath;
                _installer.ExpectedHash = profile.ExpectedHash;
                _installer.HashAlgorithmName = profile.HashAlgorithmName;
                _installer.GitHubOwner = profile.GitHubOwner;
                _installer.GitHubRepo = profile.GitHubRepo;
                _installer.AssetNameContains = profile.AssetNameContains;

                UpdateBanner(profile.BannerImagePath);

                if (Directory.Exists(TxtGamePath.Text))
                    RefreshStatusForFolder(TxtGamePath.Text, showErrorDialog: false);
            }
        }

        // ================= TITLE BAR =================
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ================= KHỞI ĐỘNG: TỰ ĐIỀN LẠI THƯ MỤC GAME LẦN TRƯỚC =================
        private void LoadLastGameFolder()
        {
            var settings = SettingsManager.Load();

            if (!string.IsNullOrWhiteSpace(settings.LastGameFolder) && Directory.Exists(settings.LastGameFolder))
            {
                TxtGamePath.Text = settings.LastGameFolder;
                RefreshStatusForFolder(settings.LastGameFolder, showErrorDialog: false);
            }
            else
            {
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = false;
            }
        }

        // ================= CHỌN THƯ MỤC GAME =================
        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Chọn thư mục cài đặt game",
                Multiselect = false
            };

            if (Directory.Exists(TxtGamePath.Text))
                dialog.InitialDirectory = TxtGamePath.Text;

            if (dialog.ShowDialog(this) == true)
            {
                TxtGamePath.Text = dialog.FolderName;

                var settings = SettingsManager.Load();
                settings.LastGameFolder = dialog.FolderName;
                SettingsManager.Save(settings);

                RefreshStatusForFolder(dialog.FolderName, showErrorDialog: true);
            }
        }

        /// <summary>Cập nhật trạng thái + bật/tắt nút dựa trên thư mục game hiện tại.</summary>
        private void RefreshStatusForFolder(string gameFolder, bool showErrorDialog = false)
        {
            // 1) Đã cài Việt hóa trước đó chưa?
            if (_installer.IsInstalled(gameFolder))
            {
                SetStatus("Đã cài đặt Việt hóa", "#3DDC97");
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = true;
                return;
            }

            // 2) Có đúng là thư mục game không?
            var check = _installer.ValidateGameFolder(gameFolder);
            if (!check.IsValid)
            {
                SetStatus("Sai thư mục game", "#FF5A4A");
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = false;

                if (showErrorDialog)
                {
                    MessageBox.Show(check.Message, "Thư mục không hợp lệ",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            // 3) Hợp lệ và chưa cài -> sẵn sàng cài đặt
            SetStatus("Chưa cài đặt Việt hóa", "#FFB454");
            BtnInstall.IsEnabled = true;
            BtnUninstall.IsEnabled = false;
        }
        // ================= CÀI ĐẶT PATCH (THẬT) =================
        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            string gameFolder = TxtGamePath.Text.Trim();

            if (!Directory.Exists(gameFolder))
            {
                MessageBox.Show("Vui lòng chọn thư mục game hợp lệ trước.", "Thiếu thông tin",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_installer.IsInstalled(gameFolder))
            {
                MessageBox.Show("Thư mục này đã được cài Việt hóa trước đó.\nVui lòng gỡ Việt hóa trước nếu muốn cài lại.",
                    "Đã cài đặt", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshStatusForFolder(gameFolder);
                return;
            }

            var folderCheck = _installer.ValidateGameFolder(gameFolder);
            if (!folderCheck.IsValid)
            {
                MessageBox.Show(folderCheck.Message, "Thư mục không hợp lệ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshStatusForFolder(gameFolder);
                return;
            }
            SetBusyState(true);
            PanelProgress.Visibility = Visibility.Visible;
            SetStatus("Đang cài đặt Việt hóa...", "#FFB454");

            _cts = new CancellationTokenSource();
            var progress = new Progress<InstallProgress>(p =>
            {
                ProgressInstall.Value = p.Percent;
                TxtPercent.Text = $"{p.Percent}%";
                TxtProgressDetail.Text = p.Message;
            });

            try
            {
                await _installer.InstallAsync(gameFolder, progress, _cts.Token);

                SetStatus("Đã cài đặt Việt hóa", "#3DDC97");
                BtnUninstall.IsEnabled = true;
                BtnInstall.IsEnabled = false;

                // Hiện "Hoàn tất" trong chốc lát rồi tự ẩn thanh tiến trình
                await Task.Delay(800);
                PanelProgress.Visibility = Visibility.Collapsed;
                ProgressInstall.Value = 0;
                TxtPercent.Text = "0%";
            }
            catch (OperationCanceledException)
            {
                SetStatus("Đã hủy cài đặt", "#FFB454");
            }
            catch (Exception ex)
            {
                SetStatus("Cài đặt thất bại", "#FF5A4A");
                OfferErrorReport("Cài đặt thất bại", ex);
            }
            finally
            {
                SetBusyState(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ================= GỠ VIỆT HÓA (THẬT) =================
        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            string gameFolder = TxtGamePath.Text.Trim();

            var confirm = MessageBox.Show(
                "Bạn có chắc muốn gỡ bản Việt hóa và khôi phục file gốc?",
                "Xác nhận gỡ Việt hóa",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            SetBusyState(true);
            PanelProgress.Visibility = Visibility.Visible;
            SetStatus("Đang gỡ Việt hóa...", "#FFB454");

            _cts = new CancellationTokenSource();
            var progress = new Progress<InstallProgress>(p =>
            {
                ProgressInstall.Value = p.Percent;
                TxtPercent.Text = $"{p.Percent}%";
                TxtProgressDetail.Text = p.Message;
            });

            try
            {
                await _installer.UninstallAsync(gameFolder, progress, _cts.Token);

                SetStatus("Chưa cài đặt Việt hóa", "#FFB454");
                BtnInstall.IsEnabled = true;
                BtnUninstall.IsEnabled = false;
                ProgressInstall.Value = 0;
                TxtPercent.Text = "0%";
                PanelProgress.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                SetStatus("Gỡ Việt hóa thất bại", "#FF5A4A");
                OfferErrorReport("Gỡ Việt hóa thất bại", ex);
            }
            finally
            {
                SetBusyState(false);
                _cts?.Dispose();
                _cts = null;
            }
        }
        // ================= TỰ ĐỘNG DÒ TÌM THƯ MỤC GAME QUA STEAM =================
        private void BtnAutoDetect_Click(object sender, RoutedEventArgs e)
        {
            if (CmbGame.SelectedItem is not Models.GameProfile profile)
            {
                MessageBox.Show("Vui lòng chọn game trước.", "Thiếu thông tin",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.SteamAppId))
            {
                MessageBox.Show("Game này chưa hỗ trợ tự động dò tìm, vui lòng bấm \"Chọn...\" để chọn thư mục thủ công.",
                    "Chưa hỗ trợ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string? foundFolder = SteamLocatorService.FindGameInstallFolder(profile.SteamAppId);
            if (foundFolder == null)
            {
                MessageBox.Show(
                    "Không tìm thấy game qua Steam. Có thể bạn chưa cài Steam, chưa cài game này, " +
                    "hoặc cài ở ổ đĩa Steam đã gỡ liên kết (offline library).\nVui lòng bấm \"Chọn...\" để chọn thư mục thủ công.",
                    "Không tìm thấy", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TxtGamePath.Text = foundFolder;

            var settings = SettingsManager.Load();
            settings.LastGameFolder = foundFolder;
            SettingsManager.Save(settings);

            RefreshStatusForFolder(foundFolder, showErrorDialog: true);
        }

        // ================= KIỂM TRA CẬP NHẬT ỨNG DỤNG =================
        private Services.GitHubReleaseAsset? _pendingAppUpdateAsset;
        private string? _pendingAppUpdateHash;

        private async Task CheckForAppUpdateAsync()
        {
            var release = await GitHubReleaseService.GetLatestReleaseAsync(AppUpdateOwner, AppUpdateRepo);
            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                return; // Mất mạng / chưa có release / hết rate limit -> im lặng bỏ qua, không làm phiền người dùng

            if (!GitHubReleaseService.IsNewerVersion(AppVersion, release.TagName))
                return; // Đang dùng bản mới nhất rồi

            var asset = GitHubReleaseService.FindAsset(release, ".exe");
            if (asset == null)
                return; // Release không có file .exe đính kèm -> không tự cập nhật được, im lặng bỏ qua

            _pendingAppUpdateAsset = asset;
            _pendingAppUpdateHash = GitHubReleaseService.ExtractSha256Hex(asset.Digest);

            RunUpdateMessage.Text = $"🔔 Đã có bản cập nhật mới ({release.TagName})!";
            LinkUpdateDownload.NavigateUri = new Uri(release.HtmlUrl);
            PanelUpdateBanner.Visibility = Visibility.Visible;
        }

        // ================= TỰ TẢI + TỰ CÀI BẢN CẬP NHẬT =================
        private async void BtnUpdateNow_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingAppUpdateAsset == null)
                return;

            var confirm = MessageBox.Show(
                "App sẽ tự tải bản cập nhật, đóng lại và khởi động lại phiên bản mới. Tiếp tục?",
                "Xác nhận cập nhật", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            BtnUpdateNow.IsEnabled = false;

            var progress = new Progress<Services.AppUpdateProgress>(p =>
            {
                RunUpdateMessage.Text = $"🔄 {p.Message}";
            });

            try
            {
                string newExePath = await Services.AppUpdaterService.DownloadNewVersionAsync(
                    _pendingAppUpdateAsset.BrowserDownloadUrl, _pendingAppUpdateHash, progress, CancellationToken.None);

                RunUpdateMessage.Text = "✅ Đang khởi động lại để áp dụng bản cập nhật...";

                // Lưu lại thư mục game hiện tại trước khi thoát, để mở app mới lên vẫn tự điền lại như cũ
                var settings = SettingsManager.Load();
                settings.LastGameFolder = TxtGamePath.Text.Trim();
                SettingsManager.Save(settings);

                Services.AppUpdaterService.LaunchUpdaterAndExit(newExePath, () => Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                BtnUpdateNow.IsEnabled = true;
                RunUpdateMessage.Text = "🔔 Đã có bản cập nhật mới!";
                MessageBox.Show(
                    $"Không thể tự động cập nhật:\n\n{ex.Message}\n\nBạn có thể bấm \"Xem chi tiết\" để tải thủ công.",
                    "Lỗi cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Hiện lỗi cho người dùng, hỏi có muốn báo lỗi không. Nếu đồng ý, mở sẵn trang tạo Issue trên GitHub
        /// với tiêu đề + nội dung đã điền sẵn (tên game, version app, chi tiết lỗi) để người dùng chỉ cần bấm gửi.
        /// </summary>
        private void OfferErrorReport(string title, Exception ex)
        {
            var result = MessageBox.Show(
                $"{title}:\n\n{ex.Message}\n\nBạn có muốn báo lỗi này cho nhóm dịch không?",
                title, MessageBoxButton.YesNo, MessageBoxImage.Error);

            if (result != MessageBoxResult.Yes)
                return;

            string gameName = CmbGame.SelectedItem is Models.GameProfile profile ? profile.Name : "(chưa chọn game)";

            string issueTitle = $"[Lỗi tự động] {title} - {gameName}";
            string errorDetail = ex.ToString();
            if (errorDetail.Length > 1500)
                errorDetail = errorDetail[..1500] + "\n... (đã cắt bớt, xem log đầy đủ trên máy nếu cần)";

            string issueBody =
                $"**Game:** {gameName}\n" +
                $"**Phiên bản app:** v{AppVersion}\n" +
                $"**Thời điểm:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"**Hệ điều hành:** {Environment.OSVersion}\n\n" +
                $"**Chi tiết lỗi:**\n```\n{errorDetail}\n```\n\n" +
                "**Mô tả thêm (nếu có):**\n(bạn có thể ghi thêm ở đây trước khi gửi)";

            string url = "https://github.com/Ryo147/PatchVH/issues/new"
                + $"?title={Uri.EscapeDataString(issueTitle)}"
                + $"&body={Uri.EscapeDataString(issueBody)}";

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Không mở được trình duyệt (hiếm khi xảy ra) -> bỏ qua, không chặn luồng chính
            }
        }

        // ================= HELPER =================

        /// <summary>Khóa các nút thao tác trong lúc đang cài/gỡ để tránh bấm chồng lệnh.</summary>
        private void SetBusyState(bool isBusy)
        {
            BtnBrowse.IsEnabled = !isBusy;

            if (isBusy)
            {
                // Khi bắt đầu chạy, luôn tắt cả 2 nút hành động để tránh bấm chồng
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = false;
            }
        }

        /// <summary>Đổi ảnh banner theo game đang chọn. Nếu thiếu đường dẫn hoặc ảnh lỗi thì giữ nguyên ảnh cũ, không crash app.</summary>
        private void UpdateBanner(string bannerImagePath)
        {
            if (string.IsNullOrWhiteSpace(bannerImagePath))
                return;

            try
            {
                // QUAN TRỌNG: phải dùng pack URI đầy đủ ("pack://application:,,,/...") khi tạo Uri bằng code.
                // Viết "/Assets/xxx.png" rồi UriKind.Relative sẽ bị hiểu nhầm thành đường dẫn ổ đĩa (C:\Assets\xxx.png)
                // chứ không phải ảnh nhúng sẵn trong file .exe.
                string relativePart = bannerImagePath.TrimStart('/');
                var packUri = new Uri($"pack://application:,,,/{relativePart}", UriKind.Absolute);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = packUri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                BannerImageBrush.ImageSource = bitmap;
            }
            catch
            {
                // Ảnh banner bị thiếu/lỗi -> bỏ qua, giữ nguyên banner hiện tại thay vì crash app
            }
        }

        private void SetStatus(string text, string hexColor)
        {
            TxtStatus.Text = text;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            TxtStatus.Foreground = brush;
            StatusDot.Fill = brush;
        }
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            // Mở liên kết bằng trình duyệt mặc định của máy tính
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        private void TxtGamePath_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }
    }
}