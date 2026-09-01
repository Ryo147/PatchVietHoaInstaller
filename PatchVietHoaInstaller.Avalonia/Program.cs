using Avalonia;
using System;
using System.Threading.Tasks;

namespace VietHoaInstaller
{
    internal static class Program
    {
        // GHI CHÚ PORT: WPF tự sinh sẵn hàm Main() ẩn (qua App.xaml Build Action). Avalonia yêu cầu khai
        // báo tường minh entry point + cấu hình platform (BuildAvaloniaApp) trước khi chạy vòng lặp UI.
        [STAThread]
        public static void Main(string[] args)
        {
            // Gắn SỚM NHẤT có thể (trước cả khi Avalonia khởi động) — xem App.HandleGlobalException để
            // biết giới hạn thực tế của 2 cơ chế bắt lỗi toàn cục này.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    App.HandleGlobalException(ex, "AppDomain.UnhandledException");
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                e.SetObserved(); // Đánh dấu đã xử lý -> tránh finalizer thread của .NET tự crash tiếp.
                App.HandleGlobalException(e.Exception, "TaskScheduler.UnobservedTaskException");
            };

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
