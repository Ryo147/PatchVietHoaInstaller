using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VietHoaInstaller.Models;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    /// <summary>
    /// Trang cài đặt/gỡ Việt hóa — toàn bộ logic thật của app nằm ở đây.
    ///
    /// GHI CHÚ PORT: 3 thay đổi chính so với bản WPF gốc:
    /// 1) OpenFolderDialog (WPF) -> IStorageProvider.OpenFolderPickerAsync (Avalonia), API async.
    /// 2) MessageBox.Show (WPF) -> SimpleMessageBox.ShowAsync (xem Services/SimpleMessageBox.cs).
    /// 3) BitmapImage + pack:// (WPF) -> Avalonia Bitmap + avares:// qua AssetLoader.
    /// Toàn bộ logic nghiệp vụ (validate, install, uninstall, auto-detect Steam...) giữ nguyên 100%.
    /// </summary>
    public partial class HomePage : UserControl
    {
        private static string AppVersion =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        private readonly PatchInstallerService _installer = new();
        private CancellationTokenSource? _cts;
        private GameProfile? _selectedProfile;

        // Đếm số lần UpdateBanner() được gọi -> chặn race condition khi người dùng đổi game liên tục:
        // lệnh decode ảnh CŨ (đang chạy nền) có thể hoàn thành SAU lệnh MỚI, nếu không chặn sẽ đè banner
        // đúng bằng banner sai (của game trước). Xem cách dùng trong UpdateBanner().
        private int _bannerRequestId;

        public HomePage()
        {
            InitializeComponent();
            AppendLog($"Khởi động ứng dụng v{AppVersion}");
            LoadDefaultGame();
            LoadLastGameFolder();
        }

        /// <summary>Thư mục game đang hiển thị trong ô đường dẫn — dùng khi shell cần lưu lại trước khi tự cập nhật.</summary>
        public string CurrentGameFolder => TxtGamePath.Text?.Trim() ?? "";

        /// <summary>Được gọi khi người dùng bấm "Đổi game" — Shell (MainWindow) sẽ chuyển sang trang Thư viện.</summary>
        public event Action? ChangeGameRequested;

        /// <summary>Cho phép trang Thư viện yêu cầu chọn sẵn 1 game khi chuyển về Trang chủ.</summary>
        public void SelectGame(GameProfile profile) => ApplySelectedProfile(profile);

        /// <summary>Xóa sạch nhật ký hoạt động — dùng bởi nút "Xóa nhật ký hoạt động" ở trang Cài đặt.</summary>
        public void ClearLog()
        {
            TxtLog.Inlines?.Clear();
            AppendLog("Đã xóa nhật ký hoạt động.");
        }

        private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

        private void LoadDefaultGame()
        {
            if (GameCatalog.All.Count > 0)
                ApplySelectedProfile(GameCatalog.All[0]);
        }

        private void BtnChangeGame_Click(object? sender, RoutedEventArgs e) => ChangeGameRequested?.Invoke();

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
            _installer.GitHubReleaseTag = profile.GitHubReleaseTag;
            _installer.AssetNameContains = profile.AssetNameContains;
            _installer.KnownPatchVersion = profile.KnownPatchVersion;

            if (string.IsNullOrWhiteSpace(profile.ApplicableGameVersion))
            {
                TxtApplicableVersion.IsVisible = false;
            }
            else
            {
                TxtApplicableVersion.Text = $"Áp dụng cho: {profile.ApplicableGameVersion}";
                TxtApplicableVersion.IsVisible = true;
            }

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

            // ===== XÓA đường dẫn cũ khi đổi game — bắt buộc chọn/dò lại thư mục đúng cho profile mới =====
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
        private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.StorageProvider is not { } storage) return;

            bool isLauncherBundle = _selectedProfile?.SkipGameFolderValidation == true;

            IStorageFolder? startFolder = null;
            if (Directory.Exists(TxtGamePath.Text))
            {
                try { startFolder = await storage.TryGetFolderFromPathAsync(new Uri(TxtGamePath.Text!)); }
                catch { /* không tìm được thư mục khởi điểm -> mở dialog ở vị trí mặc định */ }
            }

            var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = isLauncherBundle
                    ? "Chọn thư mục muốn cài Fluffy Mod Manager + PATCH"
                    : "Chọn thư mục cài đặt PATCH",
                AllowMultiple = false,
                SuggestedStartLocation = startFolder
            });

            var folder = result.Count > 0 ? result[0] : null;
            string? folderPath = folder?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(folderPath))
                return;

            TxtGamePath.Text = folderPath;

            var settings = SettingsManager.Load();
            settings.LastGameFolder = folderPath;
            SettingsManager.Save(settings);

            await RefreshStatusForFolderAsync(folderPath, showErrorDialog: true);
        }

        /// <summary>Cập nhật trạng thái + bật/tắt nút dựa trên thư mục game hiện tại (bản không hiện dialog lỗi).</summary>
        private void RefreshStatusForFolder(string gameFolder, bool showErrorDialog = false)
            => _ = RefreshStatusForFolderAsync(gameFolder, showErrorDialog);

        private async Task RefreshStatusForFolderAsync(string gameFolder, bool showErrorDialog)
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

                if (showErrorDialog && OwnerWindow != null)
                {
                    await SimpleMessageBox.ShowAsync(OwnerWindow, check.Message, "Thư mục không hợp lệ");
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
        private async void BtnInstall_Click(object? sender, RoutedEventArgs e)
        {
            if (OwnerWindow is not { } owner) return;

            if (_selectedProfile is { IsComingSoon: true })
            {
                await SimpleMessageBox.ShowAsync(owner,
                    "Bản Việt hóa cho game này chưa hoàn thành, chưa thể cài đặt.", "Chưa hỗ trợ");
                return;
            }

            string gameFolder = TxtGamePath.Text?.Trim() ?? "";

            if (!Directory.Exists(gameFolder))
            {
                bool isLauncherBundle = _selectedProfile?.SkipGameFolderValidation == true;
                await SimpleMessageBox.ShowAsync(owner,
                    isLauncherBundle
                        ? "Vui lòng chọn thư mục muốn cài trình khởi động trước."
                        : "Vui lòng chọn thư mục game hợp lệ trước.",
                    "Thiếu thông tin");
                return;
            }

            if (_installer.IsInstalled(gameFolder))
            {
                await SimpleMessageBox.ShowAsync(owner,
                    "Thư mục này đã được cài Việt hóa trước đó.\nVui lòng gỡ Việt hóa trước nếu muốn cài lại.",
                    "Đã cài đặt");
                RefreshStatusForFolder(gameFolder);
                return;
            }

            var folderCheck = _installer.ValidateGameFolder(gameFolder);
            if (!folderCheck.IsValid)
            {
                await SimpleMessageBox.ShowAsync(owner, folderCheck.Message, "Thư mục không hợp lệ");
                RefreshStatusForFolder(gameFolder);
                return;
            }

            if (_selectedProfile is { } profile && profile.SupportedBuildIds.Count > 0)
            {
                string? currentBuildId = SteamLocatorService.GetInstalledBuildId(gameFolder, profile.SteamAppId);

                // currentBuildId == null nghĩa là không đọc được -> không đủ dữ liệu để so sánh, bỏ qua cảnh báo.
                if (currentBuildId != null && !profile.SupportedBuildIds.Contains(currentBuildId))
                {
                    var buildWarning = await SimpleMessageBox.ShowAsync(owner,
                        $"Phiên bản game hiện tại (build {currentBuildId}) khác với bản mà nhóm dịch đã test bản Việt hóa này.\n" +
                        "Patch có thể không hoạt động đúng hoặc gây lỗi game.\n\nBạn có muốn tiếp tục cài đặt không?",
                        "Cảnh báo phiên bản game", SimpleMessageBoxButtons.YesNo);

                    if (buildWarning != SimpleMessageBoxResult.Yes)
                        return;
                }
            }

            AppendLog($"Bắt đầu cài đặt Việt hóa cho: {_selectedProfile?.Name ?? gameFolder}");

            SetBusyState(true);
            PanelProgress.IsVisible = true;
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

                // ===== Thông báo vị trí file cần chạy và hỏi có muốn chạy ngay không =====
                if (_selectedProfile is { } p && !string.IsNullOrWhiteSpace(p.LaunchExeRelativePath))
                {
                    string exePath = Path.Combine(gameFolder, p.ModFolderRelativePath, p.LaunchExeRelativePath);
                    if (File.Exists(exePath))
                    {
                        AppendLog($"Đã cài {Path.GetFileName(exePath)} tại: {exePath}");

                        var runNow = await SimpleMessageBox.ShowAsync(owner,
                            $"Cài đặt hoàn tất!\n\n{Path.GetFileName(exePath)} đã được cài tại:\n{exePath}\n\nBạn có muốn chạy {Path.GetFileName(exePath)} ngay bây giờ không?",
                            "Hoàn tất", SimpleMessageBoxButtons.YesNo);

                        if (runNow == SimpleMessageBoxResult.Yes)
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
                        PlatformHelper.OpenFolderInFileManager(gameFolder);
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"Không thể tự mở thư mục game: {ex.Message}");
                    }
                }

                // Hiện "Hoàn tất" trong chốc lát rồi tự ẩn thanh tiến trình
                await Task.Delay(800);
                PanelProgress.IsVisible = false;
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
                await OfferErrorReport("Cài đặt thất bại", ex);
            }
            finally
            {
                SetBusyState(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ================= GỠ VIỆT HÓA (THẬT) =================
        private async void BtnUninstall_Click(object? sender, RoutedEventArgs e)
        {
            if (OwnerWindow is not { } owner) return;

            string gameFolder = TxtGamePath.Text?.Trim() ?? "";

            if (SettingsManager.Load().ConfirmBeforeUninstall)
            {
                var confirm = await SimpleMessageBox.ShowAsync(owner,
                    "Bạn có chắc muốn gỡ bản Việt hóa và khôi phục file gốc?",
                    "Xác nhận gỡ Việt hóa", SimpleMessageBoxButtons.YesNo);

                if (confirm != SimpleMessageBoxResult.Yes)
                    return;
            }

            AppendLog($"Bắt đầu gỡ Việt hóa: {gameFolder}");

            SetBusyState(true);
            PanelProgress.IsVisible = true;
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
                PanelProgress.IsVisible = false;
                AppendLog("Gỡ Việt hóa thành công.");
            }
            catch (Exception ex)
            {
                SetStatus("Gỡ Việt hóa thất bại", "#FF5A4A");
                AppendLog($"LỖI: {ex.Message}");
                await OfferErrorReport("Gỡ Việt hóa thất bại", ex);
            }
            finally
            {
                SetBusyState(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ================= TỰ ĐỘNG DÒ TÌM THƯ MỤC GAME QUA STEAM =================
        private async void BtnAutoDetect_Click(object? sender, RoutedEventArgs e)
        {
            if (OwnerWindow is not { } owner) return;

            if (_selectedProfile is not { } profile)
            {
                await SimpleMessageBox.ShowAsync(owner, "Vui lòng chọn game trước.", "Thiếu thông tin");
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.SteamAppId))
            {
                await SimpleMessageBox.ShowAsync(owner,
                    "Game này chưa hỗ trợ tự động dò tìm, vui lòng bấm \"Chọn...\" để chọn thư mục thủ công.",
                    "Chưa hỗ trợ");
                return;
            }

            string? foundFolder = SteamLocatorService.FindGameInstallFolder(profile.SteamAppId);
            if (foundFolder == null)
            {
                AppendLog($"Tự động dò tìm thất bại cho: {profile.Name}");
                await SimpleMessageBox.ShowAsync(owner,
                    "Không tìm thấy game qua Steam. Có thể bạn chưa cài Steam, chưa cài game này, cài ở ổ đĩa " +
                    "Steam đã gỡ liên kết (offline library), hoặc đang dùng bản không qua Steam.\n" +
                    "Vui lòng bấm \"Chọn...\" để chọn thư mục thủ công.",
                    "Không tìm thấy");
                return;
            }

            AppendLog($"Tự động tìm thấy thư mục: {foundFolder}");
            TxtGamePath.Text = foundFolder;

            var settings = SettingsManager.Load();
            settings.LastGameFolder = foundFolder;
            SettingsManager.Save(settings);

            await RefreshStatusForFolderAsync(foundFolder, showErrorDialog: true);
        }

        /// <summary>
        /// Hiện lỗi cho người dùng, hỏi có muốn báo lỗi không. Nếu đồng ý, mở sẵn trang tạo Issue trên GitHub
        /// với tiêu đề + nội dung đã điền sẵn để người dùng chỉ cần bấm gửi.
        /// </summary>
        private async Task OfferErrorReport(string title, Exception ex)
        {
            if (OwnerWindow is not { } owner) return;

            var result = await SimpleMessageBox.ShowAsync(owner,
                $"{title}:\n\n{ex.Message}\n\nBạn có muốn báo lỗi này cho nhóm dịch không?",
                title, SimpleMessageBoxButtons.YesNo);

            if (result != SimpleMessageBoxResult.Yes)
                return;

            string gameName = _selectedProfile is { } profile ? profile.Name : "(chưa chọn game)";

            string issueTitle = $"[BÁO LỖI TỰ ĐỘNG] {title} - {gameName}";
            string errorDetail = ex.ToString();
            if (errorDetail.Length > 1500)
                errorDetail = errorDetail[..1500] + "\n... (đã cắt bớt, xem log đầy đủ trên máy nếu cần)";

            string issueBody =
                $"**Game:** {gameName}\n" +
                $"**Phiên bản app:** v{AppVersion}\n" +
                $"**Thời điểm:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"**Hệ điều hành:** {RuntimeInformation.OSDescription}\n\n" +
                $"**Chi tiết lỗi:**\n```\n{errorDetail}\n```\n\n" +
                "**Mô tả thêm (nếu có):**\n(bạn có thể ghi thêm ở đây trước khi gửi)";

            string url = "https://github.com/Ryo147/PatchVietHoaInstaller/issues/new"
                + $"?title={Uri.EscapeDataString(issueTitle)}"
                + $"&body={Uri.EscapeDataString(issueBody)}";

            try
            {
                PlatformHelper.OpenUrlInBrowser(url);
            }
            catch
            {
                // Không mở được trình duyệt (hiếm khi xảy ra) -> bỏ qua, không chặn luồng chính
            }
        }

        // ================= HELPER UI =================

        /// <summary>Ghi 1 dòng vào khung "Nhật ký hoạt động", kèm giờ:phút:giây, tô màu theo mức độ, tự cuộn xuống dòng mới nhất.</summary>
        private void AppendLog(string message)
        {
            string timestamp = $"[{DateTime.Now:HH:mm:ss}] ";
            IBrush messageBrush = ClassifyLogBrush(message);

            TxtLog.Inlines ??= new InlineCollection();
            TxtLog.Inlines.Add(new Run(timestamp) { Foreground = GetBrushResource("TextMutedBrush", Brushes.Gray) });
            TxtLog.Inlines.Add(new Run(message + "\n") { Foreground = messageBrush });

            LogScrollViewer.ScrollToEnd();
        }

        /// <summary>Lấy 1 SolidColorBrush đã khai báo trong App.axaml theo key, trả về fallback nếu thiếu
        /// (Avalonia's FindResource ném exception khi không thấy key, khác hành vi khoan dung hơn của WPF).</summary>
        private IBrush GetBrushResource(string key, IBrush fallback)
            => this.TryFindResource(key, out var value) && value is IBrush brush ? brush : fallback;

        /// <summary>Đoán mức độ nghiêm trọng của 1 dòng log qua từ khóa, để tô màu cho dễ quét mắt.</summary>
        private IBrush ClassifyLogBrush(string message)
        {
            if (message.Contains("LỖI", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Không thể", StringComparison.OrdinalIgnoreCase))
                return GetBrushResource("AccentBrush", Brushes.OrangeRed);

            if (message.Contains("CẢNH BÁO", StringComparison.OrdinalIgnoreCase))
                return GetBrushResource("WarnBrush", Brushes.Orange);

            if (message.Contains("thành công", StringComparison.OrdinalIgnoreCase))
                return GetBrushResource("SuccessBrush", Brushes.Green);

            if (message.Contains("LƯU Ý", StringComparison.OrdinalIgnoreCase))
                return GetBrushResource("WarnBrush", Brushes.Orange);

            return GetBrushResource("TextMutedBrush", Brushes.Gray);
        }

        /// <summary>Khóa các nút thao tác trong lúc đang cài/gỡ để tránh bấm chồng lệnh.</summary>
        private void SetBusyState(bool isBusy)
        {
            BtnBrowse.IsEnabled = !isBusy;

            if (isBusy)
            {
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = false;
            }
        }

        // Banner rộng ~700px logic (cửa sổ 720px, CanResize="False"), x2.5 để dư dả cho màn hình HiDPI.
        // Ảnh nguồn 2K/4K decode thẳng full-res rồi mới co lại lúc render sẽ rất nặng CPU/RAM và gây
        // giật khung hình animation — decode thẳng xuống kích thước này nhẹ hơn nhiều lần.
        private const int BannerDecodeTargetWidth = 1800;

        /// <summary>Đổi ảnh banner theo game đang chọn. Nếu thiếu đường dẫn hoặc ảnh lỗi thì giữ nguyên ảnh cũ, không crash app.</summary>
        private async void UpdateBanner(string bannerImagePath)
        {
            if (string.IsNullOrWhiteSpace(bannerImagePath))
                return;

            // "Vé số thứ tự" cho lần gọi này -> nếu có lệnh MỚI hơn gọi vào trong lúc đang decode nền,
            // lệnh CŨ này phải tự biết mình đã lỗi thời và không được ghi đè banner nữa (xem check bên dưới).
            int requestId = ++_bannerRequestId;

            try
            {
                var uri = new Uri(bannerImagePath, UriKind.Absolute);

                // Decode ở background thread: dù đã decode xuống kích thước nhỏ hơn, ảnh nguồn 2K/4K vẫn
                // tốn vài chục ms để giải mã — chạy trên UI thread sẽ làm animation chuyển trang/banner
                // đang chạy song song bị khựng lại giữa chừng (giật). Task.Run đẩy việc decode ra khỏi
                // UI thread, chỉ quay lại UI thread để gán Source sau khi đã có bitmap sẵn sàng.
                var bitmap = await Task.Run(() =>
                {
                    using var stream = AssetLoader.Open(uri);
                    return Bitmap.DecodeToWidth(stream, BannerDecodeTargetWidth);
                });

                // Trong lúc chờ decode, người dùng đã bấm sang game khác -> bỏ kết quả này, không ghi đè.
                if (requestId != _bannerRequestId)
                    return;

                ((ImageBrush)BannerBorder.Background!).Source = bitmap;

                // Hiệu ứng mờ-dần khi banner đổi ảnh (Transition khai báo sẵn trong XAML lo phần animate).
                // Phải đặt Opacity=1 ở tick dispatcher SAU (xem ghi chú tương tự trong SetStatus).
                BannerBorder.Opacity = 0.15;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    BannerBorder.Opacity = 1;
                }, Avalonia.Threading.DispatcherPriority.Render);
            }
            catch
            {
                // Ảnh banner bị thiếu/lỗi -> bỏ qua, giữ nguyên banner hiện tại thay vì crash app
            }
        }

        private void SetStatus(string text, string hexColor)
        {
            TxtStatus.Text = text;
            var brush = new SolidColorBrush(Color.Parse(hexColor));
            TxtStatus.Foreground = brush;
            StatusDot.Fill = brush;

            // Hiệu ứng "nảy" nhẹ mỗi khi trạng thái đổi. Avalonia's Transition nội suy tuyến tính đơn giản
            // (khác easing 3-keyframe của bản WPF gốc) — ưu tiên đúng hành vi hơn đúng tuyệt đối easing.
            // GHI CHÚ: phải đặt X=1.0 ở tick dispatcher SAU, nếu không cả 2 giá trị (0.9 rồi 1.0) sẽ được
            // gán trong cùng 1 frame và Transition sẽ không có gì để nội suy (nhảy thẳng, không "nảy").
            ((ScaleTransform)StatusRow.RenderTransform!).ScaleX = 0.9;
            ((ScaleTransform)StatusRow.RenderTransform!).ScaleY = 0.9;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ((ScaleTransform)StatusRow.RenderTransform!).ScaleX = 1.0;
                ((ScaleTransform)StatusRow.RenderTransform!).ScaleY = 1.0;
            }, Avalonia.Threading.DispatcherPriority.Render);
        }
    }
}