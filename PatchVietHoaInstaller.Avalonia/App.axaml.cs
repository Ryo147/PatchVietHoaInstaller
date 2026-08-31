using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Threading.Tasks;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    /// <summary>
    /// GHI CHÚ PORT: bản WPF gốc tạo tray icon bằng System.Windows.Forms.NotifyIcon (WinForms) ngay
    /// trong MainWindow. WinForms không chạy trên Linux, nên đã đổi sang Avalonia.Controls.TrayIcon —
    /// API tray icon chính thức, cross-platform của Avalonia (Windows/macOS luôn hoạt động; trên Linux
    /// phụ thuộc desktop environment có hỗ trợ chuẩn StatusNotifierItem/AppIndicator hay không — hầu hết
    /// GNOME/KDE/XFCE hiện đại đều hỗ trợ, một số DE tối giản có thể cần cài thêm extension tray).
    /// TrayIcon trong Avalonia thuộc về Application (không thuộc về 1 Window cụ thể), nên khởi tạo ở đây.
    /// </summary>
    public partial class App : Application
    {
        public static TrayIcon? TrayIconInstance { get; private set; }

        /// <summary>Bắn ra khi người dùng bấm "Mở phần mềm" / double-click tray icon.</summary>
        public static event Action? OpenRequested;

        /// <summary>Bắn ra khi người dùng bấm "Kiểm tra bản Patch mới" trong menu tray.</summary>
        public static event Action? CheckPatchRequested;

        /// <summary>Bắn ra khi người dùng bấm "Thoát" trong menu tray.</summary>
        public static event Action? ExitRequested;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;

                InitializeTrayIcon();
                _ = RunStartupSecurityChecksAsync(mainWindow);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void InitializeTrayIcon()
        {
            WindowIcon? icon = null;
            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://PatchVietHoaInstaller/Assets/Dich2000sICON.ico"));
                icon = new WindowIcon(stream);
            }
            catch
            {
                // Thiếu icon -> dùng icon mặc định của hệ điều hành, không chặn khởi động app
            }

            var openItem = new NativeMenuItem("Mở phần mềm");
            openItem.Click += (_, _) => OpenRequested?.Invoke();

            var checkPatchItem = new NativeMenuItem("Kiểm tra bản Patch mới");
            checkPatchItem.Click += (_, _) => CheckPatchRequested?.Invoke();

            var exitItem = new NativeMenuItem("Thoát");
            exitItem.Click += (_, _) => ExitRequested?.Invoke();

            var menu = new NativeMenu
            {
                Items =
                {
                    openItem,
                    checkPatchItem,
                    new NativeMenuItemSeparator(),
                    exitItem
                }
            };

            var trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "PatchVietHoaInstaller",
                Menu = menu,
                IsVisible = true
            };
            trayIcon.Clicked += (_, _) => OpenRequested?.Invoke();

            TrayIcon.SetIcons(this, new TrayIcons { trayIcon });
            TrayIconInstance = trayIcon;
        }

        /// <summary>Gọi khi cần hiện thông báo dạng balloon/toast từ hệ điều hành (vd: có bản Patch mới).
        /// GHI CHÚ: Avalonia TrayIcon không có BalloonTip sẵn như WinForms NotifyIcon trên mọi nền tảng —
        /// dùng ToolTipText cập nhật tạm thời làm phương án thay thế đơn giản, tương thích cả 3 OS.</summary>
        public static void ShowTrayNotification(string title, string message)
        {
            if (OperatingSystem.IsWindows() && WindowsBalloonNotifier.Show(title, message))
                return;
            if (TrayIconInstance == null) return;
            TrayIconInstance.ToolTipText = $"{title}\n{message}";
        }

        /// <summary>
        /// Gộp các bước kiểm tra bảo mật chạy ngầm lúc khởi động app: (1) BuildAuthenticity — token
        /// nhúng sẵn lúc build, kiểm tra tức thời không cần mạng; (2) SelfIntegrityService — so SHA-256
        /// file đang chạy với digest GitHub, cần gọi mạng. Đây là 2 lớp phòng thủ ĐỘC LẬP với nhau (xem
        /// ghi chú trong SelfIntegrityService.cs), nên chạy cả hai chứ không thay thế nhau. Chờ 1 nhịp
        /// ngắn để MainWindow kịp hiện lên trước, tránh MessageBox cảnh báo bật ra đè lên lúc cửa sổ
        /// chính còn đang khởi tạo/chưa activate — SimpleMessageBox cần owner window đã hiển thị để
        /// định vị/căn giữa đúng.
        /// </summary>
        private static async Task RunStartupSecurityChecksAsync(Window owner)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));

            await RunBuildAuthenticityCheckAsync(owner);
            await RunSelfIntegrityCheckAsync(owner);

            // Chỗ này để thêm các bước security-check khác sau này (nếu có), theo cùng pattern:
            // âm thầm return khi không đủ dữ liệu để kết luận, chỉ popup khi CHẮC CHẮN có vấn đề.
        }

        /// <summary>
        /// Kiểm tra tức thời (không cần mạng): file .exe đang chạy có được build kèm BuildSecret.local.txt
        /// hay không (nhúng thành <see cref="Services.BuildAuthenticity.Token"/> lúc build, xem
        /// VietHoaInstaller.csproj — file secret đó chỉ tồn tại trên máy build chính thức của nhóm,
        /// không commit vào repo). Bỏ qua khi đang chạy bản dev qua "dotnet run" (ProcessPath không
        /// khớp tên PatchVietHoaInstaller) — lúc dev bình thường máy sẽ không có BuildSecret.local.txt,
        /// KHÔNG coi đó là dấu hiệu bất thường. Chỉ cảnh báo khi đây LÀ bản .exe đã publish nhưng lại
        /// thiếu token, tức rất có thể không phải bản do chính nhóm build & phát hành (tự build lại từ
        /// source rồi phát tán, hoặc file đã bị đóng gói lại).
        /// </summary>
        private static async Task RunBuildAuthenticityCheckAsync(Window owner)
        {
            string? processPath = Environment.ProcessPath;
            string exeNameNoExt = string.IsNullOrWhiteSpace(processPath)
                ? string.Empty
                : System.IO.Path.GetFileNameWithoutExtension(processPath);
            if (!string.Equals(exeNameNoExt, "PatchVietHoaInstaller", StringComparison.OrdinalIgnoreCase))
                return; // Đang chạy bản dev, bỏ qua kiểm tra.

            if (!string.IsNullOrWhiteSpace(Services.BuildAuthenticity.Token))
                return; // Có token -> bản build chính thức, không cần cảnh báo.

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Services.SimpleMessageBox.ShowAsync(owner,
                    "Bản phần mềm bạn đang chạy KHÔNG có mã xác thực nguồn gốc bản dựng (BuildAuthenticity) " +
                    "mà nhóm Dịch 2000s nhúng khi phát hành chính thức. Đây có thể là bản tự build lại từ " +
                    "source, hoặc file đã bị chỉnh sửa/đóng gói lại bởi bên thứ ba.\n\n" +
                    "Khuyến nghị: chỉ tải bản cài đặt từ đúng trang GitHub Releases chính thức: " +
                    "github.com/Ryo147/PatchVietHoaInstaller/releases hoặc\ndich2000s.vercel.app",
                    "CẢNH BÁO NGUỒN GỐC BẢN DỰNG", Services.SimpleMessageBoxButtons.Ok);
            });
        }

        /// <summary>
        /// Kiểm tra ngầm (không chặn UI) xem chính file .exe đang chạy có khớp SHA-256 GitHub đã công bố
        /// cho bản mới nhất hay không. Chỉ hiện cảnh báo khi CHẮC CHẮN sai khớp (Mismatch) — mọi trường
        /// hợp không đủ dữ liệu (offline, bản dev, đang chạy bản cũ hơn...) đều im lặng bỏ qua, xem
        /// SelfIntegrityService để biết chi tiết vì sao thiết kế vậy.
        /// </summary>
        private static async Task RunSelfIntegrityCheckAsync(Window owner)
        {
            var result = await Services.SelfIntegrityService.CheckAsync();
            if (result.Status != Services.SelfIntegrityStatus.Mismatch)
                return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await Services.SimpleMessageBox.ShowAsync(owner,
                    "File thực thi bạn đang chạy KHÔNG khớp checksum SHA-256 mà nhóm Dịch 2000s công bố " +
                    "chính thức trên GitHub cho phiên bản này. Đây có thể là dấu hiệu file đã bị chỉnh " +
                    "sửa/chèn mã độc bởi bên thứ ba.\n\n" +
                    "Khuyến nghị: NGỪNG dùng bản này, quét virus toàn máy, và chỉ tải lại từ đúng trang " +
                    "GitHub Releases chính thức: github.com/Ryo147/PatchVietHoaInstaller/releases " +
                    "hoặc Dich2000s.vercel.app" +
                    (result.Detail != null ? $"\n\n{result.Detail}" : ""),
                    "CẢNH BÁO XÁC THỰC FILE", Services.SimpleMessageBoxButtons.Ok);
            });
        }
    }
}