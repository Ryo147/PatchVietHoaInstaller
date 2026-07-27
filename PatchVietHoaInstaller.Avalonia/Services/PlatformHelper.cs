using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VietHoaInstaller.Services
{
    /// <summary>
    /// GHI CHÚ PORT: bản WPF gốc dùng Process.Start(UseShellExecute=true) trực tiếp cho URL/thư mục —
    /// chỉ đúng trên Windows. Linux không có "ShellExecute", cần gọi đúng lệnh mở mặc định theo OS
    /// (xdg-open trên Linux, open trên macOS).
    /// </summary>
    public static class PlatformHelper
    {
        public static void OpenUrlInBrowser(string url)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
        }

        public static void OpenFolderInFileManager(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", path);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", path);
        }
    }
}
