using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace VietHoaInstaller.Services
{
    /// <summary>
    /// Dò tìm thư mục cài đặt của 1 game Steam dựa trên AppID, không cần người dùng tự chọn tay.
    /// Cách làm: tìm thư mục cài Steam (Windows: registry / Linux: đường dẫn chuẩn) -> đọc
    /// "libraryfolders.vdf" để lấy hết các ổ đĩa/thư mục thư viện Steam -> tìm file
    /// "appmanifest_{appid}.acf" trong từng thư viện để lấy đúng tên thư mục cài đặt thật của
    /// game (installdir), tránh đoán bừa tên thư mục.
    ///
    /// GHI CHÚ PORT LINUX: bản WPF gốc chỉ đọc Windows Registry (Microsoft.Win32.Registry) để tìm
    /// đường dẫn cài Steam — Linux không có registry nên toàn bộ phần đó không port được thẳng.
    /// Đã thêm nhánh GetSteamInstallPathLinux() dò các vị trí Steam thường cài trên Linux
    /// (native Steam, không phải Steam chạy qua Wine/Proton bên trong 1 prefix Windows khác).
    /// Format libraryfolders.vdf và appmanifest_*.acf là text thuần, giống hệt nhau trên cả 2 OS,
    /// nên phần parse phía dưới dùng chung, không cần sửa gì.
    /// </summary>
    public static class SteamLocatorService
    {
        /// <summary>Trả về thư mục cài đặt của game (đã tồn tại trên đĩa) nếu tìm thấy, ngược lại trả về null.</summary>
        public static string? FindGameInstallFolder(string steamAppId)
        {
            if (string.IsNullOrWhiteSpace(steamAppId))
                return null;

            string? steamPath = GetSteamInstallPath();
            if (steamPath == null || !Directory.Exists(steamPath))
                return null;

            foreach (var libraryFolder in GetLibraryFolders(steamPath))
            {
                string manifestPath = Path.Combine(libraryFolder, "steamapps", $"appmanifest_{steamAppId}.acf");
                if (!File.Exists(manifestPath))
                    continue;

                string? installDir = ParseInstallDir(manifestPath);
                if (installDir == null)
                    continue;

                string gameFolder = Path.Combine(libraryFolder, "steamapps", "common", installDir);
                if (Directory.Exists(gameFolder))
                    return gameFolder;
            }

            return null;
        }

        /// <summary>Tìm thư mục cài đặt gốc của Steam (không phải thư mục game) — tự chọn cách dò theo OS đang chạy.</summary>
        private static string? GetSteamInstallPath()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? GetSteamInstallPathWindows()
                : GetSteamInstallPathLinux();
        }

        /// <summary>Đọc registry để tìm thư mục cài đặt gốc của Steam trên Windows.</summary>
        private static string? GetSteamInstallPathWindows()
        {
            try
            {
                using var key64 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
                var path64 = key64?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(path64))
                    return path64;
            }
            catch { }

            try
            {
                using var key32 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                var path32 = key32?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(path32))
                    return path32;
            }
            catch { }

            try
            {
                using var keyUser = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var pathUser = keyUser?.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(pathUser))
                    return pathUser.Replace('/', '\\');
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Dò các vị trí Steam native (không qua Wine) thường cài trên Linux, theo thứ tự phổ biến nhất:
        /// ~/.steam/steam (symlink chuẩn mọi bản cài) -> ~/.local/share/Steam (vị trí thật, một số distro/Flatpak)
        /// -> ~/.var/app/com.valvesoftware.Steam/.local/share/Steam (Steam cài qua Flatpak, sandbox riêng).
        /// </summary>
        private static string? GetSteamInstallPathLinux()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? "";

            if (string.IsNullOrWhiteSpace(home))
                return null;

            string[] candidates =
            {
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"),
            };

            foreach (var candidate in candidates)
            {
                if (Directory.Exists(Path.Combine(candidate, "steamapps")))
                    return candidate;
            }

            return null;
        }

        /// <summary>Đọc file libraryfolders.vdf để lấy toàn bộ đường dẫn thư viện Steam (có thể nằm ở nhiều ổ đĩa khác nhau).</summary>
        private static List<string> GetLibraryFolders(string steamPath)
        {
            var folders = new List<string> { steamPath };

            string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
                return folders;

            try
            {
                string content = File.ReadAllText(vdfPath);
                // Mỗi thư viện có 1 dòng dạng: "path"		"D:\\SteamLibrary" (Windows) hoặc "/home/user/SteamLibrary" (Linux)
                foreach (Match m in Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\""))
                {
                    string path = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (!folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                        folders.Add(path);
                }
            }
            catch { }

            return folders;
        }

        /// <summary>Đọc giá trị "installdir" trong file appmanifest_xxx.acf — chính là tên thư mục thật của game.</summary>
        private static string? ParseInstallDir(string manifestPath)
        {
            try
            {
                string content = File.ReadAllText(manifestPath);
                var match = Regex.Match(content, "\"installdir\"\\s*\"([^\"]+)\"");
                if (!match.Success) return null;
                string installDir = match.Groups[1].Value;
                if (string.IsNullOrWhiteSpace(installDir) || installDir.Contains("..") || Path.IsPathRooted(installDir))
                    return null;
                return installDir;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Đọc "buildid" hiện tại của game đã cài, dùng để cảnh báo nếu bản Steam đang chạy khác với
        /// bản mà nhóm dịch đã test bản Việt hóa. Chỉ hoạt động nếu gameFolder nằm đúng theo cấu trúc
        /// thư viện Steam chuẩn (.../steamapps/common/&lt;installdir&gt;) — nếu người dùng copy game ra
        /// chỗ khác thì trả về null (bỏ qua kiểm tra, không chặn cài đặt).
        /// </summary>
        public static string? GetInstalledBuildId(string gameFolder, string steamAppId)
        {
            if (string.IsNullOrWhiteSpace(steamAppId) || string.IsNullOrWhiteSpace(gameFolder))
                return null;

            try
            {
                // gameFolder = ".../steamapps/common/<installdir>" -> đi lên 2 cấp để ra thư mục "steamapps"
                var commonDir = Directory.GetParent(gameFolder);
                var steamappsDir = commonDir != null ? Directory.GetParent(commonDir.FullName) : null;
                if (steamappsDir == null || !steamappsDir.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                    return null;

                string manifestPath = Path.Combine(steamappsDir.FullName, $"appmanifest_{steamAppId}.acf");
                if (!File.Exists(manifestPath))
                    return null;

                string content = File.ReadAllText(manifestPath);
                var match = Regex.Match(content, "\"buildid\"\\s*\"([^\"]+)\"");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
