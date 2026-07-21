using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using VietHoaInstaller.Models;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    /// <summary>
    /// Trang cài đặt/gỡ Việt hóa. Đây là toàn bộ logic của MainWindow.xaml.cs cũ (v2.2.0),
    /// chỉ chuyển từ Window sang UserControl để chạy trong shell nhiều trang mới.
    /// </summary>
    public partial class HomePage : System.Windows.Controls.UserControl
    {
        private static string AppVersion =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        private readonly PatchInstallerService _installer = new();
        private CancellationTokenSource? _cts;
        private GameProfile? _selectedProfile;

        public HomePage()
        {
            InitializeComponent();
            AppendLog($"Khởi động ứng dụng v{AppVersion}");
            LoadDefaultGame();
            LoadLastGameFolder();
        }

        /// <summary>Thư mục game đang hiển thị trong ô đường dẫn — dùng khi shell cần lưu lại trước khi tự cập nhật.</summary>
        public string CurrentGameFolder => TxtGamePath.Text.Trim();

        /// <summary>Được gọi khi người dùng bấm "Đổi game" — Shell (MainWindow) sẽ chuyển sang trang Thư viện.</summary>
        public event Action? ChangeGameRequested;

        /// <summary>Cho phép trang Thư viện yêu cầu chọn sẵn 1 game khi chuyển về Trang chủ.</summary>
        public void SelectGame(GameProfile profile) => ApplySelectedProfile(profile);

        /// <summary>Xóa sạch nhật ký hoạt động — dùng bởi nút "Xóa nhật ký hoạt động" ở trang Cài đặt.</summary>
        public void ClearLog()
        {
            TxtLog.Text = "";
            AppendLog("Đã xóa nhật ký hoạt động.");
        }

        private void LoadDefaultGame()
        {
            if (Models.GameCatalog.All.Count > 0)
                ApplySelectedProfile(Models.GameCatalog.All[0]);
        }

        private void BtnChangeGame_Click(object sender, RoutedEventArgs e) => ChangeGameRequested?.Invoke();

        private void ApplySelectedProfile(GameProfile profile)
        {
            _selectedProfile = profile;
            TxtSelectedGame.Text = profile.Name;

            _installer.PatchDownloadUrl = profile.PatchDownloadUrl;
            _installer.RequiredGameFiles = profile.RequiredGameFiles;
            _installer.InstallMode = profile.InstallMode;
            _installer.ModFolderRelativePath = profile.ModFolderRelativePath;
            _installer.SkipGameFolderValidation = profile.SkipGameFolderValidation;
            _installer.ProfileName = profile.Name;
            _installer.ExpectedHash = profile.ExpectedHash;
            _installer.HashAlgorithmName = profile.HashAlgorithmName;
            _installer.GitHubOwner = profile.GitHubOwner;
            _installer.GitHubRepo = profile.GitHubRepo;
            _installer.AssetNameContains = profile.AssetNameContains;

            if (profile.IsComingSoon)
            {
                TxtGamePath.Text = "";
                BtnBrowse.IsEnabled = false;
                BtnAutoDetect.IsEnabled = false;
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = false;
                SetStatus("Chưa hoàn thành bản Việt hóa", "#FF5A4A");
                UpdateBanner(profile.BannerImagePath);
                AppendLog($"Đã chọn game: {profile.Name} (bản Việt hóa chưa hoàn thành, chưa thể cài đặt).");
                return; // dừng ở đây, không chạy các bước validate/refresh thư mục phía dưới
            }

            BtnBrowse.IsEnabled = true;

            // ===== Đổi nhãn tùy theo profile có phải bundle trình khởi động (Fluffy...) hay không =====
            LblFolderLabel.Text = profile.SkipGameFolderValidation
                ? "Thư mục cài đặt FluffyModManager + PATCH:"
                : "Thư mục game:";
            BtnAutoDetect.IsEnabled = !profile.SkipGameFolderValidation;

            UpdateBanner(profile.BannerImagePath);
            AppendLog($"Đã chọn game: {profile.Name}");

            if (!string.IsNullOrWhiteSpace(profile.InstallNote))
                AppendLog($"LƯU Ý: {profile.InstallNote}");

            // ===== XÓA đường dẫn cũ khi đổi game — bắt buộc chọn/dò lại thư mục đúng cho profile mới,
            // tránh cài nhầm patch của game A vào thư mục của game B khi profile mới bỏ qua validate. =====
            TxtGamePath.Text = "";
            BtnInstall.IsEnabled = false;
            BtnUninstall.IsEnabled = false;
            SetStatus("Chưa chọn thư mục", "#FFB454");

            if (Directory.Exists(TxtGamePath.Text))
                RefreshStatusForFolder(TxtGamePath.Text, showErrorDialog: false);
        }

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
            bool isLauncherBundle = _selectedProfile?.SkipGameFolderValidation == true;

            var dialog = new OpenFolderDialog
            {
                Title = isLauncherBundle
                    ? "Chọn thư mục muốn cài Fluffy Mod Manager + PATCH"
                    : "Chọn thư mục cài đặt PATCH",
                Multiselect = false
            };

            if (Directory.Exists(TxtGamePath.Text))
                dialog.InitialDirectory = TxtGamePath.Text;

            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
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
            AppendLog($"Kiểm tra thư mục: {gameFolder}");

            // 1) Đã cài Việt hóa trước đó chưa?
            if (_installer.IsInstalled(gameFolder))
            {
                SetStatus("Đã cài đặt Việt hóa", "#3DDC97");
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = true;
                AppendLog("-> Đã cài đặt Việt hóa từ trước.");
                return;
            }

            // 2) Có đúng là thư mục game không?
            var check = _installer.ValidateGameFolder(gameFolder);
            if (!check.IsValid)
            {
                SetStatus("Sai thư mục game", "#FF5A4A");
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = false;
                AppendLog("-> Sai thư mục game, không tìm thấy file gốc cần thiết.");

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
            AppendLog("-> Thư mục hợp lệ, sẵn sàng cài đặt.");
        }

        // ================= CÀI ĐẶT PATCH (THẬT) =================
        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile is { IsComingSoon: true })
            {
                MessageBox.Show("Bản Việt hóa cho game này chưa hoàn thành, chưa thể cài đặt.",
                    "Chưa hỗ trợ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string gameFolder = TxtGamePath.Text.Trim();

            if (!Directory.Exists(gameFolder))
            {
                bool isLauncherBundle = _selectedProfile?.SkipGameFolderValidation == true;
                MessageBox.Show(
                    isLauncherBundle
                        ? "Vui lòng chọn thư mục muốn cài trình khởi động trước."
                        : "Vui lòng chọn thư mục game hợp lệ trước.",
                    "Thiếu thông tin", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            if (_selectedProfile is Models.GameProfile profile && profile.SupportedBuildIds.Count > 0)
            {
                string? currentBuildId = SteamLocatorService.GetInstalledBuildId(gameFolder, profile.SteamAppId);

                // currentBuildId == null nghĩa là không đọc được (vd: game không cài qua Steam library chuẩn)
                // -> không đủ dữ liệu để so sánh, bỏ qua cảnh báo thay vì báo sai.
                if (currentBuildId != null && !profile.SupportedBuildIds.Contains(currentBuildId))
                {
                    var buildWarning = MessageBox.Show(
                        $"Phiên bản game hiện tại (build {currentBuildId}) khác với bản mà nhóm dịch đã test bản Việt hóa này.\n" +
                        "Patch có thể không hoạt động đúng hoặc gây lỗi game.\n\nBạn có muốn tiếp tục cài đặt không?",
                        "Cảnh báo phiên bản game", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (buildWarning != MessageBoxResult.Yes)
                        return;
                }
            }

            AppendLog($"Bắt đầu cài đặt Việt hóa cho: {_selectedProfile?.Name ?? gameFolder}");

            SetBusyState(true);
            PanelProgress.Visibility = Visibility.Visible;
            SetStatus("Đang cài đặt Việt hóa...", "#FFB454");

            _cts = new CancellationTokenSource();
            string? lastLoggedMessage = null;
            var progress = new Progress<InstallProgress>(p =>
            {
                ProgressInstall.Value = p.Percent;
                TxtPercent.Text = $"{p.Percent}%";
                TxtProgressDetail.Text = p.Message;

                if (p.Message != lastLoggedMessage)
                {
                    lastLoggedMessage = p.Message;
                    AppendLog(p.Message);
                }
            });

            try
            {
                await _installer.InstallAsync(gameFolder, progress, _cts.Token);

                SetStatus("Đã cài đặt Việt hóa", "#3DDC97");
                BtnUninstall.IsEnabled = true;
                BtnInstall.IsEnabled = false;
                AppendLog("Cài đặt Việt hóa thành công.");

                // ===== Thông báo vị trí FluffyModManager.exe và hỏi có muốn chạy ngay không =====
                if (_selectedProfile is Models.GameProfile p && !string.IsNullOrWhiteSpace(p.LaunchExeRelativePath))
                {
                    string exePath = Path.Combine(gameFolder, p.ModFolderRelativePath, p.LaunchExeRelativePath);
                    if (File.Exists(exePath))
                    {
                        AppendLog($"Đã cài {Path.GetFileName(exePath)} tại: {exePath}");

                        var runNow = MessageBox.Show(
                            $"Cài đặt hoàn tất!\n\n{Path.GetFileName(exePath)} đã được cài tại:\n{exePath}\n\nBạn có muốn chạy {Path.GetFileName(exePath)} ngay bây giờ không?",
                            "Hoàn tất", MessageBoxButton.YesNo, MessageBoxImage.Question);

                        if (runNow == MessageBoxResult.Yes)
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = exePath,
                                    WorkingDirectory = Path.GetDirectoryName(exePath),
                                    UseShellExecute = true
                                });
                            }
                            catch (Exception ex)
                            {
                                AppendLog($"Không thể chạy {Path.GetFileName(exePath)}: {ex.Message}");
                            }
                        }
                    }
                    else
                    {
                        AppendLog($"CẢNH BÁO: không tìm thấy {exePath} sau khi cài — kiểm tra lại nội dung file zip patch.");
                    }
                }

                // ===== Tự động mở thư mục game (nếu người dùng đã bật ở trang Cài đặt) =====
                if (SettingsManager.Load().AutoOpenFolderAfterInstall)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = gameFolder,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Không thể tự mở thư mục game: {ex.Message}");
                    }
                }

                // Hiện "Hoàn tất" trong chốc lát rồi tự ẩn thanh tiến trình
                await Task.Delay(800);
                PanelProgress.Visibility = Visibility.Collapsed;
                ProgressInstall.Value = 0;
                TxtPercent.Text = "0%";
            }
            catch (OperationCanceledException)
            {
                SetStatus("Đã hủy cài đặt", "#FFB454");
                AppendLog("Đã hủy cài đặt.");
            }
            catch (Exception ex)
            {
                SetStatus("Cài đặt thất bại", "#FF5A4A");
                AppendLog($"LỖI: {ex.Message}");
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

            if (SettingsManager.Load().ConfirmBeforeUninstall)
            {
                var confirm = MessageBox.Show(
                    "Bạn có chắc muốn gỡ bản Việt hóa và khôi phục file gốc?",
                    "Xác nhận gỡ Việt hóa",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            AppendLog($"Bắt đầu gỡ Việt hóa: {gameFolder}");

            SetBusyState(true);
            PanelProgress.Visibility = Visibility.Visible;
            SetStatus("Đang gỡ Việt hóa...", "#FFB454");

            _cts = new CancellationTokenSource();
            string? lastLoggedMessage = null;
            var progress = new Progress<InstallProgress>(p =>
            {
                ProgressInstall.Value = p.Percent;
                TxtPercent.Text = $"{p.Percent}%";
                TxtProgressDetail.Text = p.Message;

                if (p.Message != lastLoggedMessage)
                {
                    lastLoggedMessage = p.Message;
                    AppendLog(p.Message);
                }
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
                AppendLog("Gỡ Việt hóa thành công.");
            }
            catch (Exception ex)
            {
                SetStatus("Gỡ Việt hóa thất bại", "#FF5A4A");
                AppendLog($"LỖI: {ex.Message}");
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
            if (_selectedProfile is not Models.GameProfile profile)
            {
                MessageBox.Show("Vui lòng chọn game trước.", "Thiếu thông tin",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.SteamAppId))
            {
                MessageBox.Show(
                    "Game này chưa hỗ trợ tự động dò tìm, vui lòng bấm \"Chọn...\" để chọn thư mục thủ công.",
                    "Chưa hỗ trợ", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string? foundFolder = SteamLocatorService.FindGameInstallFolder(profile.SteamAppId);
            if (foundFolder == null)
            {
                AppendLog($"Tự động dò tìm thất bại cho: {profile.Name}");
                MessageBox.Show(
                    "Không tìm thấy game qua Steam. Có thể bạn chưa cài Steam, chưa cài game này, cài ở ổ đĩa " +
                    "Steam đã gỡ liên kết (offline library), hoặc đang dùng bản không qua Steam.\n" +
                    "Vui lòng bấm \"Chọn...\" để chọn thư mục thủ công.",
                    "Không tìm thấy",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppendLog($"Tự động tìm thấy thư mục: {foundFolder}");
            TxtGamePath.Text = foundFolder;

            var settings = SettingsManager.Load();
            settings.LastGameFolder = foundFolder;
            SettingsManager.Save(settings);

            RefreshStatusForFolder(foundFolder, showErrorDialog: true);
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

            string gameName = _selectedProfile is Models.GameProfile profile ? profile.Name : "(chưa chọn game)";

            string issueTitle = $"[BÁO LỖI TỰ ĐỘNG] {title} - {gameName}";
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

        /// <summary>Ghi 1 dòng vào khung "Nhật ký hoạt động", kèm giờ:phút:giây, tô màu theo mức độ, tự cuộn xuống dòng mới nhất.</summary>
        private void AppendLog(string message)
        {
            string timestamp = $"[{DateTime.Now:HH:mm:ss}] ";
            Brush messageBrush = ClassifyLogBrush(message);

            TxtLog.Inlines.Add(new Run(timestamp) { Foreground = (Brush)FindResource("TextMutedBrush") });
            TxtLog.Inlines.Add(new Run(message + "\n") { Foreground = messageBrush });

            LogScrollViewer.ScrollToBottom();
        }

        /// <summary>Đoán mức độ nghiêm trọng của 1 dòng log qua từ khóa, để tô màu cho dễ quét mắt (không đổi ý nghĩa nội dung).</summary>
        private Brush ClassifyLogBrush(string message)
        {
            if (message.Contains("LỖI", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Không thể", StringComparison.OrdinalIgnoreCase))
                return (Brush)FindResource("AccentBrush");

            if (message.Contains("CẢNH BÁO", StringComparison.OrdinalIgnoreCase))
                return (Brush)FindResource("WarnBrush");

            if (message.Contains("thành công", StringComparison.OrdinalIgnoreCase))
                return (Brush)FindResource("SuccessBrush");

            if (message.Contains("LƯU Ý", StringComparison.OrdinalIgnoreCase))
                return (Brush)FindResource("WarnBrush");
            
            return (Brush)FindResource("TextMutedBrush");
        }

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
                bitmap.DecodePixelWidth = 700;   // Ép decode nhỏ lại, khớp độ rộng banner thật trên UI
                bitmap.EndInit();
                bitmap.Freeze();                 // Cho phép GC dọn dẹp tốt hơn, tránh giữ tham chiếu thừa

                BannerImageBrush.ImageSource = bitmap;

                // Hiệu ứng mờ-dần khi banner đổi ảnh, thay vì đổi ảnh đột ngột
                BannerBorder.Opacity = 0.15;
                BannerBorder.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0.15, 1, TimeSpan.FromMilliseconds(260)));
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

            // Hiệu ứng "nảy" nhẹ mỗi khi trạng thái đổi, để người dùng dễ nhận ra có thay đổi
            var bounce = new DoubleAnimationUsingKeyFrames();
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(1.05, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            StatusBadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
            StatusBadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
        }
    }
}
