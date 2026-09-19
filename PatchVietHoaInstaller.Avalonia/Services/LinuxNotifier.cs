using System;
using System.Diagnostics;

namespace VietHoaInstaller.Services
{
    /// <summary>
    /// Hiện thông báo desktop THẬT trên Linux qua `notify-send`. Lệnh này chính là một D-Bus client gọi
    /// org.freedesktop.Notifications — chạy giống nhau trên cả X11 lẫn Wayland, nên không cần tự viết
    /// tầng D-Bus riêng (thêm dependency để làm đúng việc đang làm).
    ///
    /// KHÔNG đảm bảo có trên mọi distro: bản Linux tối giản/server có thể thiếu `libnotify-bin`, hoặc
    /// không có notification daemon nào đang chạy. Vì vậy đây luôn là "cố gắng tốt nhất" —
    /// <see cref="App.ShowTrayNotification"/> tự fallback về đổi tooltip khay hệ thống khi hàm này trả
    /// về false. Hàm không bao giờ ném exception ra ngoài.
    /// </summary>
    internal static class LinuxNotifier
    {
        /// <summary>Thời gian tối đa chờ notify-send thoát. Lệnh chỉ gửi yêu cầu hiện popup rồi thoát
        /// ngay (không đợi người dùng đóng popup) nên 2 giây là quá đủ; vượt quá coi như có vấn đề, không
        /// chờ thêm để tránh chặn luồng gọi.</summary>
        private const int WaitForExitMs = 2000;

        public static bool Show(string title, string message)
        {
            if (!OperatingSystem.IsLinux())
                return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "notify-send",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                // Dùng ArgumentList thay vì nối chuỗi vào Arguments: .NET tự escape đúng cách và KHÔNG đi
                // qua shell trung gian, nên title/message chứa ký tự đặc biệt (" $ ` ; ...) không thể
                // thoát ra thành lệnh shell. Nội dung 2 tham số này đến từ tên game/version lấy qua
                // GitHub API — dữ liệu ngoài, phải coi là không tin cậy.
                psi.ArgumentList.Add("--app-name=PatchVietHoaInstaller");
                psi.ArgumentList.Add("--icon=dialog-information");
                psi.ArgumentList.Add(title);
                psi.ArgumentList.Add(message);

                using var process = Process.Start(psi);
                if (process == null)
                    return false;

                bool exited = process.WaitForExit(WaitForExitMs);
                return exited && process.ExitCode == 0;
            }
            catch
            {
                // notify-send không tồn tại, hoặc không có daemon nhận -> coi như thất bại để caller
                // fallback về tooltip. Không log, không popup lỗi: thông báo phụ trợ thất bại không phải
                // việc người dùng cần biết.
                return false;
            }
        }
    }
}
