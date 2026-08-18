using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace VietHoaInstaller.Services
{
    /// <summary>
    /// "Token xác thực bản dựng" — xem VietHoaInstaller.csproj (mục EmbeddedResource) để biết cách
    /// BuildSecret.local.txt được nhúng vào assembly.
    ///
    /// Token = SHA-256(nội dung BuildSecret.local.txt lúc build). Bản build không có file bí mật đó
    /// (vd ai fork/clone repo công khai rồi tự build) sẽ không có resource này -> Token rỗng.
    /// </summary>
    internal static class BuildAuthenticity
    {
        private const string ResourceName = "VietHoaInstaller.BuildSecret.txt";

        public static readonly string Token = ComputeToken();

        private static string ComputeToken()
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
                if (stream == null)
                    return string.Empty;

                using var reader = new StreamReader(stream, Encoding.UTF8);
                string raw = reader.ReadToEnd().Trim();
                if (string.IsNullOrEmpty(raw))
                    return string.Empty;

                using var sha256 = SHA256.Create();
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(raw));
                return Convert.ToHexString(hash);
            }
            catch
            {
                // Bất kỳ lỗi nào (resource hỏng, encoding lạ...) -> coi như không có token, không throw
                // làm crash app chỉ vì tính năng phụ này.
                return string.Empty;
            }
        }
    }
}