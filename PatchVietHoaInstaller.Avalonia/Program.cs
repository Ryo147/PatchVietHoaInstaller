using Avalonia;
using System;

namespace VietHoaInstaller
{
    internal static class Program
    {
        // GHI CHÚ PORT: WPF tự sinh sẵn hàm Main() ẩn (qua App.xaml Build Action). Avalonia yêu cầu khai
        // báo tường minh entry point + cấu hình platform (BuildAvaloniaApp) trước khi chạy vòng lặp UI.
        [STAThread]
        public static void Main(string[] args)
            => BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
