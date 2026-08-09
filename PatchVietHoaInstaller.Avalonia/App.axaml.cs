using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
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
    }
}