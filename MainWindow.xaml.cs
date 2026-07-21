using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    /// <summary>
    /// Shell của ứng dụng (v3.0.0): chỉ còn title bar, banner cập nhật, thanh điều hướng
    /// và vùng hiển thị trang hiện tại. Toàn bộ logic cài đặt/gỡ Việt hóa đã chuyển sang HomePage.
    /// </summary>
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

            SetPage(_homePage);

            // FIX: NavHome.IsChecked="True" khai báo trong XAML trước ContentControl "MainContent" trong
            // cây markup, nên sự kiện Checked của nó fire ngay trong lúc InitializeComponent() — lúc đó field
            // MainContent CHƯA được gán -> NavButton_Checked bị "MainContent == null" chặn return sớm ->
            // AnimateNavIndicator(0) không bao giờ chạy -> gạch chân giữ nguyên vị trí thô X=0 (lệch sát trái)
            // thay vì nằm giữa "Trang chủ". Đặt lại vị trí tường minh ở đây, không animation, để không bị giật.
            SetNavIndicatorPositionInstant(0);

            _ = CheckForAppUpdateAsync();
        }

        // ================= ĐIỀU HƯỚNG GIỮA CÁC TRANG =================
        private void NavButton_Checked(object sender, RoutedEventArgs e)
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
                NavUpdateDot.Visibility = Visibility.Collapsed;

            // Cả 5 tab giờ nằm chung 1 hàng ngang, chia đều nhau -> chỉ cần biết thứ tự
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

        /// <summary>Trượt gạch chân chỉ báo tới đúng tab đang chọn (mỗi tab rộng cố định 92px, gạch chân rộng 40px, canh giữa).</summary>
        private void AnimateNavIndicator(int slotIndex)
        {
            double targetX = GetNavIndicatorTargetX(slotIndex);

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            NavIndicatorTransform.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        }

        /// <summary>Đặt NGAY vị trí gạch chân chỉ báo, không animation. Dùng lúc khởi động app để tránh
        /// gạch chân "giật" trượt từ vị trí gốc lệch trái (X=0 khai báo trong XAML) sang chỗ đúng
        /// trước mắt người dùng — chỉ cần nó xuất hiện đúng chỗ ngay từ khung hình đầu tiên.</summary>
        private void SetNavIndicatorPositionInstant(int slotIndex)
        {
            NavIndicatorTransform.BeginAnimation(TranslateTransform.XProperty, null);
            NavIndicatorTransform.X = GetNavIndicatorTargetX(slotIndex);
        }

        private double GetNavIndicatorTargetX(int slotIndex)
        {
            const double slotWidth = 92;
            return slotWidth * slotIndex + (slotWidth - NavIndicator.Width) / 2.0;
        }

        /// <summary>Đổi trang kèm hiệu ứng mờ dần + trượt nhẹ từ dưới lên, thay vì đổi Content đột ngột.</summary>
        private void SetPage(UserControl page)
        {
            MainContent.Content = page;

            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            MainContentTransform.Y = 10;
            MainContent.Opacity = 0;

            MainContent.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            MainContentTransform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
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
            ToastHost.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));

            try
            {
                await Task.Delay(1600, cts.Token);
                ToastHost.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)));
            }
            catch (TaskCanceledException)
            {
                // Có toast mới đè lên trước khi toast cũ kịp ẩn -> bỏ qua, để toast mới tự lo việc ẩn
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

        // ================= KIỂM TRA CẬP NHẬT ỨNG DỤNG (khi mở app) =================
        // Chỉ để quyết định có chấm đỏ trên nút "Cập nhật" hay không. Toàn bộ chi tiết (đổi nhật ký,
        // nút tải về...) người dùng sẽ thấy khi tự bấm vào tab Cập nhật (UpdatesPage tự kiểm tra lại).
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

            NavUpdateDot.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// UpdatesPage tự kiểm tra bản mới và đã tìm sẵn asset + hash; khi người dùng bấm "Cập nhật ngay"
        /// ở đó, nó báo về đây để thực hiện tải + tự thay thế + khởi động lại (cần quyền của cửa sổ chính).
        /// </summary>
        private async void OnUpdateNowRequested(Services.GitHubReleaseAsset asset, string? expectedHash)
        {
            var confirm = MessageBox.Show(
                "App sẽ tự tải bản cập nhật, đóng lại và khởi động lại phiên bản mới. Tiếp tục?",
                "Xác nhận cập nhật", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            _updatesPage.SetUpdateInProgress("Đang chuẩn bị tải...");

            var progress = new Progress<Services.AppUpdateProgress>(p =>
            {
                _updatesPage.SetUpdateInProgress(p.Message);
            });

            try
            {
                string newExePath = await Services.AppUpdaterService.DownloadNewVersionAsync(
                    asset.BrowserDownloadUrl, expectedHash, progress, CancellationToken.None);

                _updatesPage.SetUpdateInProgress("Đang khởi động lại để áp dụng bản cập nhật...");

                // Lưu lại thư mục game hiện tại (đọc từ HomePage) trước khi thoát,
                // để mở app mới lên vẫn tự điền lại như cũ.
                var settings = SettingsManager.Load();
                settings.LastGameFolder = _homePage.CurrentGameFolder;
                SettingsManager.Save(settings);

                Services.AppUpdaterService.LaunchUpdaterAndExit(newExePath, () => Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                _updatesPage.SetUpdateFailed();
                MessageBox.Show(
                    $"Không thể tự động cập nhật:\n\n{ex.Message}\n\nBạn có thể bấm \"Mở trang GitHub\" để tải thủ công.",
                    "Lỗi cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}