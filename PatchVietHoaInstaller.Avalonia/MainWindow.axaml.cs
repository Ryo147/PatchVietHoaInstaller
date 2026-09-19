using Avalonia;
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
        private CancellationTokenSource? _pageTransitionCts;

        // Lưu lại 2 collection Transitions khai báo sẵn trong XAML (chỉ đọc 1 lần) để có thể tắt/bật
        // tạm thời trong SetPage() — xem ghi chú chi tiết trong SetPage().
        private Avalonia.Animation.Transitions? _contentOpacityTransitions;
        private Avalonia.Animation.Transitions? _contentTransformTransitions;

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

            Opened += ClampHeightToScreen;
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

        /// <summary>
        /// Height=960 khai báo cứng trong XAML + CanResize="False" = trên màn hình thấp hoặc có DPI
        /// scaling (1080p ở 125% chỉ còn ~826 DIP dùng được, 150% còn ~688), cửa sổ tràn khỏi màn hình
        /// và người dùng KHÔNG kéo nhỏ lại được. Thu vừa vùng làm việc thật của màn hình đang hiển thị.
        /// Banner (477px cố định) giữ nguyên, phần bị thu là khung Nhật ký hoạt động ở dưới.
        /// ponytail: chỉ clamp chiều cao. Nếu màn hình thấp tới mức body bị bóp khó đọc thì mới cần cho
        /// banner co giãn theo — đợi có người báo mới làm.
        /// </summary>
        private void ClampHeightToScreen(object? sender, EventArgs e)
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen == null) return;

            double usable = screen.WorkingArea.Height / screen.Scaling - 20;

            // MinHeight=680 là ngưỡng cho content đọc thoải mái — nhưng trên màn thật sự thấp (vd
            // 1366x768 + DPI scaling 125%, rất phổ biến ở laptop giá rẻ: usable chỉ còn ~560px), chính
            // MinHeight lại CHẶN Avalonia co nhỏ hơn 680, ép cửa sổ tràn màn hình dù dòng dưới có gán
            // Height bao nhiêu đi nữa. Phải hạ MinHeight xuống TRƯỚC — vừa màn hình luôn quan trọng hơn
            // giữ đúng 1 con số cứng. 400 là sàn tuyệt đối, dưới mức đó banner+nav đã vỡ layout, không
            // cố fit thêm nữa.
            if (usable < MinHeight)
                MinHeight = Math.Max(usable, 400);

            if (Height > usable)
            {
                Height = usable;
                Position = new PixelPoint(Position.X, screen.WorkingArea.Y);
            }
        }

        // ============ RESIZE KÉO TAY ============
        // SystemDecorations="None" nghĩa là OS không vẽ viền resize sẵn như cửa sổ thường -> phải tự bắt
        // PointerPressed ở 8 vùng cạnh/góc (khai báo trong MainWindow.axaml, nằm trong dải Margin="10"
        // vốn chỉ để chứa đổ bóng) rồi gọi thẳng BeginResizeDrag(WindowEdge, PointerPressedEventArgs) —
        // đây là API chuẩn của Avalonia cho đúng tình huống này, hệ điều hành tự lo phần kéo mượt/preview,
        // không cần tự tính toán lại Width/Height theo delta chuột.
        private void ResizeTop_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.North, e);
        private void ResizeBottom_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.South, e);
        private void ResizeLeft_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.West, e);
        private void ResizeRight_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.East, e);
        private void ResizeTopLeft_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.NorthWest, e);
        private void ResizeTopRight_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.NorthEast, e);
        private void ResizeBottomLeft_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.SouthWest, e);
        private void ResizeBottomRight_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResizeDrag(WindowEdge.SouthEast, e);

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

            // Đọc thư mục đã ghi nhớ RIÊNG cho từng game (AppSettings.GameFolders). Trước đây mọi profile
            // đều resolve về đúng 1 biến LastGameFolder dùng chung -> chỉ ĐÚNG 1 game được kiểm tra bằng
            // version thật trong manifest, các game còn lại luôn rơi về KnownPatchVersion hardcode nên báo
            // cập nhật sai (bỏ sót bản mới, hoặc báo có bản mới dù đã cài). TryGetInstalledPatchVersion vẫn
            // tự đối chiếu ProfileName trong manifest nên đọc nhầm thư mục là an toàn — nhưng giờ không
            // còn phải dựa vào lớp an toàn đó nữa.
            var checkResult = await PatchUpdateCheckerService.CheckAllAsync(
                Models.GameCatalog.All,
                resolveGameFolder: profile => settings.GameFolders.TryGetValue(profile.Name, out var folder) ? folder : null);
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
                    string message = checkResult.FailureStatus switch
                    {
                        GitHubFetchStatus.RateLimited =>
                            "Đã đạt giới hạn số lần kiểm tra tới GitHub trong giờ này (không phải do mất mạng). Vui lòng thử lại sau ít phút.",
                        GitHubFetchStatus.NetworkError =>
                            "Không kết nối được tới GitHub. Vui lòng kiểm tra lại kết nối mạng rồi thử lại.",
                        GitHubFetchStatus.NotFound =>
                            "Không tìm thấy đúng bản Patch trên GitHub (có thể do cấu hình repo/tag/tên file bị sai). Vui lòng báo cho nhóm.",
                        _ => "Không kiểm tra được bản Patch lúc này. Vui lòng thử lại sau ít phút."
                    };
                    App.ShowTrayNotification("Không kiểm tra được bản Patch", message);
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
            // Hủy animation "chuyển trang" đang chờ xử lý (nếu người dùng bấm đổi tab liên tục) -> tránh
            // trường hợp callback cũ chạy đè lên callback mới, gây hiệu ứng giật ngược về giữa chừng.
            _pageTransitionCts?.Cancel();
            var cts = new CancellationTokenSource();
            _pageTransitionCts = cts;

            var transform = (TranslateTransform)MainContent.RenderTransform!;

            // Đọc & lưu lại Transitions khai báo trong XAML (chỉ cần đọc đúng 1 lần đầu tiên).
            _contentOpacityTransitions ??= MainContent.Transitions;
            _contentTransformTransitions ??= transform.Transitions;

            // ===== BƯỚC 1: Đặt về trạng thái "ẩn" (Y=10, Opacity=0) NGAY LẬP TỨC, KHÔNG animate =====
            // Đây chính là nguyên nhân gây "giật ngược": Transition gắn sẵn trên Y/Opacity sẽ tự động
            // animate MỌI lần đổi giá trị, kể cả bước reset này. Vì Y đang ở 0 (vị trí trang cũ), gán
            // Y=10 sẽ khiến trang CŨ tự trượt xuống trong khi Content đã đổi sang trang MỚI -> nhìn như
            // nội dung đổi trước rồi mới giật/trượt ngược. Nên phải tắt Transitions trước khi reset.
            MainContent.Transitions = null;
            transform.Transitions = null;

            transform.Y = 10;
            MainContent.Opacity = 0;
            MainContent.Content = page;

            // ===== BƯỚC 2: Bật lại Transitions rồi mới đặt giá trị đích -> lần này mới thực sự animate =====
            // Phải đợi tới DispatcherPriority.Render để chắc chắn khung hình "ẩn" ở trên đã kịp render
            // 1 frame, nếu không cả 2 bước dễ bị gộp chung 1 frame và animation vẫn bị đơ/nhảy thẳng.
            Dispatcher.UIThread.Post(() =>
            {
                if (cts.IsCancellationRequested) return;

                MainContent.Transitions = _contentOpacityTransitions;
                transform.Transitions = _contentTransformTransitions;

                transform.Y = 0;
                MainContent.Opacity = 1;
            }, DispatcherPriority.Render);
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

                // GHI CHÚ: trước đây có lưu tạm settings.LastGameFolder ở đây để mở app mới lên tự điền
                // lại thư mục. Không còn cần: HomePage đã ghi vào GameFolders[tên game] NGAY LÚC người
                // dùng chọn/dò thư mục, không đợi tới lúc tự cập nhật mới lưu.

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