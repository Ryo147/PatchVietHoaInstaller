using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace VietHoaInstaller.Services
{
    public record AppUpdateProgress(int Percent, string Message);

    /// <summary>
    /// Tự tải bản mới của chính app và tự thay thế file đang chạy.
    /// Cả Windows lẫn Linux đều không cho ghi đè trực tiếp 1 file binary đang chạy, nên cách làm chuẩn là:
    /// 1) Tải file mới về 1 chỗ tạm.
    /// 2) Viết ra 1 script trung gian (bat trên Windows / sh trên Linux), cho nó đợi vài giây để app hiện
    ///    tại thoát hẳn (giải phóng khóa file), rồi move đè file mới vào đúng vị trí file cũ, khởi động
    ///    lại app, và tự xóa chính nó.
    /// 3) Chạy script đó ở tiến trình riêng, rồi tắt app hiện tại.
    ///
    /// GHI CHÚ PORT LINUX: bản WPF gốc chỉ có nhánh Windows (viết .bat, chạy qua cmd.exe). Đã thêm
    /// nhánh Linux dùng shell script (sh) + "chmod +x" cho file mới trước khi khởi động lại, vì Linux
    /// không tự cấp quyền thực thi cho file vừa tải về như Windows.
    /// </summary>
    public static class AppUpdaterService
    {
        private static readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

        /// <summary>Tải file update mới về thư mục tạm, xác thực hash nếu có, rồi trả về đường dẫn file đã tải.</summary>
        public static async Task<string> DownloadNewVersionAsync(
            string downloadUrl, string? expectedSha256, IProgress<AppUpdateProgress> progress, CancellationToken ct)
        {
            PatchInstallerService.EnsureSafeDownloadUrl(downloadUrl);

            string extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
            string tempExePath = Path.Combine(Path.GetTempPath(), "VietHoaInstaller_update_" + Guid.NewGuid().ToString("N") + extension);

            using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(tempExePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;
                int lastReportedPercent = -1;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;

                    if (totalBytes is > 0)
                    {
                        int pct = (int)(totalRead * 100 / totalBytes.Value);
                        if (pct != lastReportedPercent)
                        {
                            lastReportedPercent = pct;
                            progress.Report(new AppUpdateProgress(pct, $"Đang tải bản cập nhật... {pct}%"));
                        }
                    }
                }
            }
            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                try { File.Delete(tempExePath); } catch { }
                throw new InvalidOperationException(
                    "Không thể xác thực tính bản cập nhật (thiếu SHA-256 từ GitHub). " +
                    "Vui lòng tải bản mới tại trang GitHub của phần mềm.");
            }

            progress.Report(new AppUpdateProgress(100, "Đang xác thực file tải về..."));
            using var sha256 = SHA256.Create();
            await using var verifyStream = File.OpenRead(tempExePath);
            byte[] hashBytes = await sha256.ComputeHashAsync(verifyStream, ct);
            string actualHash = Convert.ToHexString(hashBytes);

            if (!actualHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(tempExePath); } catch { }
                throw new InvalidOperationException("File cập nhật tải về bị lỗi (sai hash). Vui lòng thử lại sau.");
            }

            return tempExePath;
        }

        /// <summary>
        /// Viết ra script trung gian (bat trên Windows, sh trên Linux), chạy nó, rồi tắt app hiện tại
        /// để script có thể thay thế file binary đang chạy. Gọi hàm này xong thì app sẽ tự thoát —
        /// nên gọi ở bước cuối cùng, sau khi đã lưu mọi thứ cần lưu.
        /// </summary>
        public static void LaunchUpdaterAndExit(string newExePath, Action shutdownAppAction)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                LaunchUpdaterWindows(newExePath);
            else
                LaunchUpdaterLinux(newExePath);

            shutdownAppAction();
        }

        private static void LaunchUpdaterWindows(string newExePath)
        {
            string currentExePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Không xác định được đường dẫn file .exe hiện tại.");

            string batPath = Path.Combine(Path.GetTempPath(), "VietHoaInstaller_apply_update.bat");

            // Đợi 2 giây cho app thoát hẳn (giải phóng khóa file) -> thử move đè, nếu vẫn bị khóa thì thử lại vài lần
            // -> khởi động lại app mới -> tự xóa chính file .bat này.
            string script =
                "@echo off\r\n" +
                "timeout /t 2 /nobreak > nul\r\n" +
                ":retry\r\n" +
                $"move /y \"{newExePath}\" \"{currentExePath}\" > nul 2>&1\r\n" +
                "if errorlevel 1 (\r\n" +
                "    timeout /t 1 /nobreak > nul\r\n" +
                "    goto retry\r\n" +
                ")\r\n" +
                $"start \"\" \"{currentExePath}\"\r\n" +
                "del \"%~f0\"\r\n";

            File.WriteAllText(batPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batPath}\"\"",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        private static void LaunchUpdaterLinux(string newExePath)
        {
            string currentExePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Không xác định được đường dẫn file thực thi hiện tại.");

            string shPath = Path.Combine(Path.GetTempPath(), "viethoainstaller_apply_update.sh");

            // Tương đương bản Windows: đợi app thoát hẳn -> cấp quyền thực thi cho file mới (Linux không
            // tự làm việc này như Windows) -> move đè -> khởi động lại -> tự xóa script.
            // "$$" trong sh không dùng được vì script chạy detached; dùng chính đường dẫn script (~$0) để tự xóa.
            string script =
                "#!/bin/sh\n" +
                "sleep 2\n" +
                "for i in 1 2 3 4 5 6 7 8 9 10; do\n" +
                $"  if mv -f \"{newExePath}\" \"{currentExePath}\" 2>/dev/null; then\n" +
                "    break\n" +
                "  fi\n" +
                "  sleep 1\n" +
                "done\n" +
                $"chmod +x \"{currentExePath}\"\n" +
                $"nohup \"{currentExePath}\" >/dev/null 2>&1 &\n" +
                "rm -f \"$0\"\n";

            File.WriteAllText(shPath, script);

            // File cần quyền thực thi trước khi chạy được — Linux không có khái niệm "executable"
            // gắn theo phần mở rộng như Windows (.exe), phải cấp quyền tường minh qua chmod.
            try
            {
                var chmod = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{shPath}\"",
                    UseShellExecute = false
                });
                chmod?.WaitForExit();
            }
            catch
            {
                // Nếu chmod thất bại, bước dưới (chạy qua "sh <path>") vẫn hoạt động vì không cần quyền
                // thực thi trên chính file .sh khi gọi tường minh qua interpreter "sh".
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"\"{shPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
    }
}
