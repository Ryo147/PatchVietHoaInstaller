using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace VietHoaInstaller.Services
{
    public enum SelfIntegrityStatus
    {
        /// <summary>Không xác minh được (offline, rate-limit, đang chạy bản dev qua "dotnet run",
        /// đang chạy bản CŨ hơn latest release nên không có digest đúng để so, hoặc digest chưa có
        /// trên asset đó). KHÔNG coi là dấu hiệu bất thường — chỉ đơn giản là chưa đủ dữ liệu.</summary>
        Inconclusive,

        /// <summary>Hash khớp digest GitHub công bố cho đúng bản đang chạy -> file KHÔNG bị chỉnh sửa.</summary>
        Verified,

        /// <summary>Hash KHÔNG khớp digest GitHub công bố cho đúng bản đang chạy dù tên/asset tương ứng
        /// khớp -> rất có thể file thực thi đã bị chỉnh sửa (inject mã độc, đóng gói lại...).</summary>
        Mismatch
    }

    /// <summary>
    /// Tự kiểm tra chính file .exe đang chạy có đúng nguyên bản GitHub đã publish hay không, bằng cách
    /// so sánh SHA-256 của file đang chạy với digest GitHub trả về cho asset tương ứng.
    ///
    /// KHÁC với "token xác thực bản dựng" (BuildAuthenticity, xem AboutPage/VietHoaInstaller.csproj):
    /// token đó là hằng số NHÚNG SẴN trong file lúc build, nên nếu kẻ xấu chỉ chỉnh sửa trực tiếp file
    /// .exe đã build sẵn (không build lại từ source) thì token vẫn y nguyên -> không phát hiện được.
    /// Cơ chế ở đây khác về bản chất: giá trị "đáp án đúng" (digest) được LẤY TRỰC TIẾP từ GitHub API
    /// mỗi lần app khởi động, không nằm sẵn trong file -> kẻ chỉnh sửa .exe không thể tự sửa luôn đáp
    /// án đúng trừ khi họ xâm nhập được chính GitHub Releases của repo (khó hơn nhiều so với chỉnh 1
    /// file .exe lẻ). Đây là lớp phòng thủ ĐỘC LẬP, bổ sung cho token tĩnh chứ không thay thế.
    ///
    /// GIỚI HẠN CẦN HIỂU RÕ: đây vẫn là kiểm tra chạy BÊN TRONG chính file đang bị nghi ngờ. Một kẻ tấn
    /// công đủ trình độ, sau khi inject mã độc, có thể tìm và xóa/vô hiệu hóa luôn đoạn code gọi hàm
    /// này rồi đóng gói lại — lúc đó cơ chế này sẽ không tự chạy nữa và hoàn toàn im lặng.
    /// Vì vậy đây là "defense in depth" (tăng chi phí/độ khó cho kẻ tấn công không
    /// chuyên, bắt được các kiểu chỉnh sửa/đóng gói lại đơn giản), KHÔNG PHẢI bằng chứng tuyệt đối. Lớp
    /// phòng thủ mạnh và đáng tin hơn về bản chất toán học là ký số (Authenticode code signing) — sửa dù
    /// 1 byte cũng làm chữ ký sai ngay lập tức, và việc verify chữ ký (qua Explorer/PowerShell) không
    /// phụ thuộc vào code bên trong chính file đó còn nguyên vẹn hay không.
    /// </summary>
    public static class SelfIntegrityService
    {
        private const string Owner = "Ryo147";
        private const string Repo = "PatchVietHoaInstaller";

        public record SelfIntegrityResult(SelfIntegrityStatus Status, string? Detail = null);

        public static async Task<SelfIntegrityResult> CheckAsync(CancellationToken ct = default)
        {
            try
            {
                string? processPath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
                    return new SelfIntegrityResult(SelfIntegrityStatus.Inconclusive, "Không xác định được đường dẫn file đang chạy.");

                // Đang chạy qua "dotnet run"/debugger (ProcessPath trỏ tới dotnet.exe/host chung, không
                // phải file publish thật) -> không có gì để so, tránh báo động giả khi đang phát triển.
                string exeNameNoExt = Path.GetFileNameWithoutExtension(processPath);
                if (!string.Equals(exeNameNoExt, "PatchVietHoaInstaller", StringComparison.OrdinalIgnoreCase))
                    return new SelfIntegrityResult(SelfIntegrityStatus.Inconclusive, "Đang chạy bản dev, bỏ qua kiểm tra.");

                string appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "0.0.0.0";

                var fetchResult = await GitHubReleaseService.GetLatestReleaseWithStatusAsync(Owner, Repo, ct);
                var release = fetchResult.Release;
                if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                    return new SelfIntegrityResult(SelfIntegrityStatus.Inconclusive, "Không lấy được release mới nhất từ GitHub lúc này.");

                // Chỉ so sánh được khi đang chạy ĐÚNG bản mới nhất — vì ta chỉ tra được digest của asset
                // mới nhất, không có kho lưu digest của các bản cũ hơn để so cho chính xác.
                if (GitHubReleaseService.IsNewerVersion(appVersion, release.TagName))
                    return new SelfIntegrityResult(SelfIntegrityStatus.Inconclusive, "Đang chạy bản cũ hơn latest release, không đủ dữ liệu để so.");

                string assetHint = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "linux";
                var asset = GitHubReleaseService.FindAsset(release, assetHint);
                string? expectedHash = GitHubReleaseService.ExtractSha256Hex(asset?.Digest);
                if (string.IsNullOrWhiteSpace(expectedHash))
                    return new SelfIntegrityResult(SelfIntegrityStatus.Inconclusive, "Asset trên GitHub chưa có digest SHA-256 để so.");

                string actualHash;
                using (var sha256 = SHA256.Create())
                await using (var stream = new FileStream(processPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] hashBytes = await sha256.ComputeHashAsync(stream, ct);
                    actualHash = Convert.ToHexString(hashBytes);
                }

                bool match = string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
                return match
                    ? new SelfIntegrityResult(SelfIntegrityStatus.Verified)
                    : new SelfIntegrityResult(SelfIntegrityStatus.Mismatch,
                        $"SHA-256 thực tế:\n{actualHash}\n\nSHA-256 GitHub công bố:\n{expectedHash}");
            }
            catch (Exception ex)
            {
                // Lỗi bất kỳ (mạng, IO, quyền file...) -> Inconclusive, KHÔNG BAO GIỜ tự suy ra Mismatch
                // từ 1 exception, để tránh báo động giả (vd đang offline hoàn toàn).
                return new SelfIntegrityResult(SelfIntegrityStatus.Inconclusive, ex.Message);
            }
        }
    }
}