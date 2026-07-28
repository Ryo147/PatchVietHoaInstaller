using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    public partial class MainWindow : Window
    {
        private static string AppVersion =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        private const string AppUpdateOwner = "Ryo147";
        private const string AppUpdateRepo = "PatchVietHoaInstaller";

        private readonly HomePage _homePage = new();
        private readonly LibraryPage _libraryPage = new();
        private readonly UpdatesPage _updatesPage = new();
        private readonly SettingsPage _settingsPage = new();
        private readonly AboutPage _aboutPage = new();
        private DispatcherTimer? _patchCheckTimer;
        private bool _isExitingForReal = false;

        public MainWindow()
        {
            InitializeComponent();

            Topmost = SettingsManager.Load().AlwaysOnTop;

            _libraryPage.InstallRequested += OnInstallRequestedFromLibrary;
            _homePage.ChangeGameRequested += () => NavLibrary.IsChecked = true;
            _updatesPage.UpdateNowRequested += OnUpdateNowRequested;
            _settingsPage.AlwaysOnTopChanged += isOn => Topmost = isOn;
            _settingsPage.ClearActivityLogRequested += () => _homePage.ClearLog();
            _settingsPage.ToastRequested += ShowToast;
            _settingsPage.TraySettingsChanged += RestartPatchUpdateTimer;

            App.OpenRequested += OnTrayOpenRequested;
            App.CheckPatchRequested += () => _ = CheckForPatchUpdatesAsync(showBalloonIfFound: true, forceToastIfNone: true);
            App.ExitRequested += () =>
            {
                _isExitingForReal = true;
                (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
            };

            SetPage(_homePage);
            SetNavIndicatorPositionInstant(0);

            Closing += MainWindow_Closing;

            _ = CheckForPatchUpdatesAsync(showBalloonIfFound: true);
            StartPatchUpdateTimer();
            _ = CheckForAppUpdateAsync();
        }

        private void OnTrayOpenRequested()
        {
            Dispatcher.UIThread.Post(() =>
            {
                _ = CheckForPatchUpdatesAsync(showBalloonIfFound: true, forceToastIfNone: true);
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });
        }

        private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            var settings = SettingsManager.Load();
            if (!_isExitingForReal && settings.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            _patchCheckTimer?.Stop();
        }

        private void StartPatchUpdateTimer()
        {
            var settings = SettingsManager.Load();
            if (!settings.AutoCheckPatchUpdate) return;

            _patchCheckTimer = new DispatcherTimer
            {
                // Chặn dưới 15 phút để tránh spam GitHub API (giới hạn 60 request/giờ không token)
                Interval = TimeSpan.FromMinutes(Math.Max(15, settings.PatchCheckIntervalMinutes))
            };
            _patchCheckTimer.Tick += async (_, _) => await CheckForPatchUpdatesAsync(showBalloonIfFound: true);
            _patchCheckTimer.Start();
        }

        /// <summary>Gọi lại mỗi khi người dùng đổi cài đặt Tray/Auto-check ở trang Cài đặt, để áp dụng ngay
        /// không cần khởi động lại app.</summary>
        private void RestartPatchUpdateTimer()
        {
            _patchCheckTimer.Tick += async (_, _) => await CheckForPatchUpdatesAsync(showBalloonIfFound: true, forceToastIfNone: true);
            _patchCheckTimer?.Stop();
            _patchCheckTimer = null;
            StartPatchUpdateTimer();
        }

        private async Task CheckForPatchUpdatesAsync(bool showBalloonIfFound, bool forceToastIfNone = false)
        {
            var settings = SettingsManager.Load();
            if (!settings.AutoCheckPatchUpdate && !forceToastIfNone) return;

            // Kiến trúc hiện tại chỉ lưu 1 "thư mục game gần nhất" dùng chung cho mọi profile
            // (AppSettings.LastGameFolder). TryGetInstalledPatchVersion tự kiểm tra ProfileName trong
            // manifest nên nếu thư mục này không phải của đúng game, nó sẽ trả về "" một cách an toàn
            // (rơi về KnownPatchVersion) thay vì đọc nhầm version của game khác.
            var checkResult = await PatchUpdateCheckerService.CheckAllAsync(
                Models.GameCatalog.All,
                resolveGameFolder: _ => settings.LastGameFolder);
            var updates = checkResult.Updates;

            if (updates.Count > 0)
            {
                NavUpdateDot.IsVisible = true; // tận dụng chấm đỏ có sẵn ở nút "Cập nhật"

                // LastNotifiedPatchVersions chỉ dùng để tránh SPAM tray-balloon lặp lại từ timer chạy nền
                // cho cùng 1 bản đã báo rồi — KHÔNG được dùng để quyết định "có bản mới hay không". Kiểm tra
                // thủ công (forceToastIfNone) luôn phải trả lời đúng sự thật, bất kể đã từng thông báo chưa.
                bool alreadyNotifiedAll = updates.All(u =>
                    settings.LastNotifiedPatchVersions.TryGetValue(u.Profile.Name, out var notified) &&
                    notified == u.NewVersion);
                bool shouldShowBalloon = forceToastIfNone || !alreadyNotifiedAll;

                foreach (var u in updates)
                    settings.LastNotifiedPatchVersions[u.Profile.Name] = u.NewVersion;
                SettingsManager.Save(settings);

                if (showBalloonIfFound && shouldShowBalloon)
                {
                    string names = string.Join(", ", updates.Select(u => $"{u.Profile.Name} (v{u.NewVersion})"));
                    App.ShowTrayNotification("Có bản Patch Việt Hóa mới", $"Đã có bản cập nhật cho: {names}. Mở app để tải về.");
                }
            }
            else if (forceToastIfNone)
            {
                if (checkResult.AnyCheckFailed)
                {
                    // Khác hẳn "chưa có bản mới" — đây là app KHÔNG hỏi được GitHub (rate-limit/mất mạng/config sai).
                    App.ShowTrayNotification("Không kiểm tra được bản Patch",
                        "Không kết nối được tới GitHub lúc này (có thể do mất mạng hoặc giới hạn request). Vui lòng thử lại sau ít phút.");
                }
                else
                {
                    App.ShowTrayNotification("Kiểm tra bản Patch", "Chưa có bản Patch mới nào.");
                }
            }
        }

        // ================= ĐIỀU HƯỚNG GIỮA CÁC TRANG =================
        private void NavButton_Checked(object? sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || MainContent == null)
                return;

            string tag = rb.Tag as string ?? "";

            UserControl? page = tag switch
            {
                "Home" => _homePage,
                "Library" => _libraryPage,
                "Updates" => _updatesPage,
                "Settings" => _settingsPage,
                "About" => _aboutPage,
                _ => null
            };

            if (page != null)
                SetPage(page);

            // Vào tab Cập nhật rồi thì coi như người dùng đã thấy thông báo -> tắt chấm đỏ
            if (tag == "Updates")
                NavUpdateDot.IsVisible = false;

            // Cả 5 tab nằm chung 1 hàng ngang, chia đều nhau -> chỉ cần biết thứ tự
            int slotIndex = tag switch
            {
                "Home" => 0,
                "Library" => 1,
                "Updates" => 2,
                "Settings" => 3,
                "About" => 4,
                _ => 0
            };
            AnimateNavIndicator(slotIndex);
        }

        /// <summary>Trượt gạch chân chỉ báo tới đúng tab đang chọn. Trên Avalonia, chỉ cần đổi giá trị X —
        /// Transition khai báo sẵn trong XAML (TranslateTransform.Transitions) tự lo phần animate mượt.</summary>
        private void AnimateNavIndicator(int slotIndex)
        {
            ((TranslateTransform)NavIndicator.RenderTransform!).X = GetNavIndicatorTargetX(slotIndex);
        }

        /// <summary>Đặt NGAY vị trí gạch chân chỉ báo khi khởi động app, tránh giật animation từ X=0.</summary>
        private void SetNavIndicatorPositionInstant(int slotIndex)
        {
            ((TranslateTransform)NavIndicator.RenderTransform!).X = GetNavIndicatorTargetX(slotIndex);
        }

        private double GetNavIndicatorTargetX(int slotIndex)
        {
            const double slotWidth = 92;
            return slotWidth * slotIndex + (slotWidth - NavIndicator.Width) / 2.0;
        }

        /// <summary>Đổi trang kèm hiệu ứng mờ dần + trượt nhẹ từ dưới lên (Transition khai báo trong XAML).</summary>
        private void SetPage(UserControl page)
        {
            ((TranslateTransform)MainContent.RenderTransform!).Y = 10;
            MainContent.Opacity = 0;
            MainContent.Content = page;

            // Đặt lại giá trị đích ngay sau khi gán Content -> Transition tự nội suy từ (10, 0 độ mờ) về (0, 1).
            Dispatcher.UIThread.Post(() =>
            {
                ((TranslateTransform)MainContent.RenderTransform!).Y = 0;
                MainContent.Opacity = 1;
            });
        }

        /// <summary>Khi bấm "Cài đặt" 1 game ở trang Thư viện: nhảy về Trang chủ và chọn sẵn game đó.</summary>
        private void OnInstallRequestedFromLibrary(Models.GameProfile profile)
        {
            NavHome.IsChecked = true;
            _homePage.SelectGame(profile);
        }

        private CancellationTokenSource? _toastCts;

        /// <summary>Hiện 1 thông báo nhỏ, thoáng qua ở đáy cửa sổ (vd "Đã lưu") rồi tự mờ dần biến mất.</summary>
        private async void ShowToast(string message)
        {
            _toastCts?.Cancel();
            var cts = new CancellationTokenSource();
            _toastCts = cts;

            TxtToast.Text = message;
            ToastHost.Opacity = 1;

            try
            {
                await Task.Delay(1600, cts.Token);
                ToastHost.Opacity = 0;
            }
            catch (TaskCanceledException)
            {
                // Có toast mới đè lên trước khi toast cũ kịp ẩn -> bỏ qua, để toast mới tự lo việc ẩn
            }
        }

        // ================= TITLE BAR =================
        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        }

        private void BtnMinimize_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnClose_Click(object? sender, RoutedEventArgs e) => Close();

        // ================= KIỂM TRA CẬP NHẬT ỨNG DỤNG (khi mở app) =================
        private async Task CheckForAppUpdateAsync()
        {
            var settings = SettingsManager.Load();
            if (!settings.AutoCheckUpdate)
                return; // Người dùng đã tắt tự động kiểm tra cập nhật ở trang Cài đặt

            var release = await GitHubReleaseService.GetLatestReleaseAsync(AppUpdateOwner, AppUpdateRepo);
            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                return; // Mất mạng / chưa có release / hết rate limit -> im lặng bỏ qua

            if (!GitHubReleaseService.IsNewerVersion(AppVersion, release.TagName))
                return; // Đang dùng bản mới nhất rồi

            NavUpdateDot.IsVisible = true;
        }

        /// <summary>
        /// UpdatesPage tự kiểm tra bản mới và đã tìm sẵn asset + hash; khi người dùng bấm "Cập nhật ngay"
        /// ở đó, nó báo về đây để thực hiện tải + tự thay thế + khởi động lại (cần quyền của cửa sổ chính).
        /// </summary>
        private async void OnUpdateNowRequested(GitHubReleaseAsset asset, string? expectedHash)
        {
            var confirm = await SimpleMessageBox.ShowAsync(this,
                "App sẽ tự tải bản cập nhật, đóng lại và khởi động lại phiên bản mới. Tiếp tục?",
                "Xác nhận cập nhật", SimpleMessageBoxButtons.YesNo);

            if (confirm != SimpleMessageBoxResult.Yes)
                return;

            _updatesPage.SetUpdateInProgress("Đang chuẩn bị tải...");

            var progress = new Progress<AppUpdateProgress>(p =>
            {
                _updatesPage.SetUpdateInProgress(p.Message);
            });

            try
            {
                string newExePath = await AppUpdaterService.DownloadNewVersionAsync(
                    asset.BrowserDownloadUrl, expectedHash, progress, CancellationToken.None);

                _updatesPage.SetUpdateInProgress("Đang khởi động lại để áp dụng bản cập nhật...");

                // Lưu lại thư mục game hiện tại (đọc từ HomePage) trước khi thoát,
                // để mở app mới lên vẫn tự điền lại như cũ.
                var settings = SettingsManager.Load();
                settings.LastGameFolder = _homePage.CurrentGameFolder;
                SettingsManager.Save(settings);

                AppUpdaterService.LaunchUpdaterAndExit(newExePath, () =>
                    (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown());
            }
            catch (Exception ex)
            {
                _updatesPage.SetUpdateFailed();
                await SimpleMessageBox.ShowAsync(this,
                    $"Không thể tự động cập nhật:\n\n{ex.Message}\n\nBạn có thể bấm \"Mở trang GitHub\" để tải thủ công.",
                    "Lỗi cập nhật");
            }
        }
    }
}