using System;
using System.Runtime.InteropServices;

namespace VietHoaInstaller.Services
{
    /// <summary>
    /// Hiện thông báo thật của Windows (balloon qua Shell_NotifyIcon — Windows 10/11 tự động hiển thị
    /// dạng toast trong Action Center). Dùng P/Invoke thuần thay vì System.Windows.Forms để không phải
    /// đổi TargetFramework sang "net10.0-windows" (sẽ làm hỏng khả năng build cho Linux/macOS).
    /// </summary>
    internal static class WindowsBalloonNotifier
    {
        private const int NIM_ADD = 0x0, NIM_MODIFY = 0x1;
        private const int NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
        private const int NIIF_INFO = 0x1;
        private const int CALLBACK_MESSAGE = 0x0400 + 1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int w, int h, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        private static readonly IntPtr HWND_MESSAGE = new(-3);
        private static IntPtr _hwnd = IntPtr.Zero;
        private static bool _iconAdded;
        private static readonly object _lock = new();

        private static bool EnsureWindowAndIcon()
        {
            lock (_lock)
            {
                if (_iconAdded) return true;
                try
                {
                    _hwnd = CreateWindowEx(0, "STATIC", "VietHoaInstallerNotify", 0, 0, 0, 0, 0,
                        HWND_MESSAGE, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
                    if (_hwnd == IntPtr.Zero) return false;

                    var data = new NOTIFYICONDATA
                    {
                        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                        hWnd = _hwnd,
                        uID = 1,
                        uFlags = NIF_ICON | NIF_TIP,
                        uCallbackMessage = CALLBACK_MESSAGE,
                        hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32512)), // IDI_APPLICATION
                        szTip = "PatchVietHoaInstaller",
                        szInfo = "",
                        szInfoTitle = ""
                    };
                    _iconAdded = Shell_NotifyIcon(NIM_ADD, ref data);
                    return _iconAdded;
                }
                catch { return false; }
            }
        }

        /// <summary>Hiện balloon/toast thật. Không throw ra ngoài — đây chỉ là thông báo phụ, lỗi ở đây
        /// không được làm gãy luồng kiểm tra version chính.</summary>
        public static bool Show(string title, string message)
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                if (!EnsureWindowAndIcon()) return false;

                var data = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = 1,
                    uFlags = NIF_INFO,
                    szTip = "PatchVietHoaInstaller",
                    szInfoTitle = title.Length > 63 ? title[..63] : title,
                    szInfo = message.Length > 255 ? message[..255] : message,
                    dwInfoFlags = NIIF_INFO,
                    uTimeoutOrVersion = 10000
                };
                return Shell_NotifyIcon(NIM_MODIFY, ref data);
            }
            catch { return false; }
        }
    }
}