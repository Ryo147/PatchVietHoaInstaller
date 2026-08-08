using System;
using System.IO;
using System.Text.Json;
using VietHoaInstaller.Models;

namespace VietHoaInstaller.Services
{
    /// <summary>
    /// Đọc/ghi file settings.json ở thư mục dữ liệu người dùng (KHÔNG liên quan đến manifest.json
    /// trong thư mục game).
    /// </summary>
    public static class SettingsManager
    {
        private static readonly string SettingsDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VietHoaInstaller");

        private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

        /// <summary>Vị trí file settings.json CŨ (cạnh .exe) — chỉ dùng 1 lần để tự di chuyển dữ liệu
        /// người dùng đã lưu từ trước sang vị trí mới, tránh mất cài đặt khi cập nhật app.</summary>
        private static readonly string LegacySettingsPath =
            Path.Combine(AppContext.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                MigrateLegacySettingsIfNeeded();

                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch
            {
                // File hỏng hoặc không đọc được -> dùng settings mặc định
            }

            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Không chặn luồng chính nếu lỡ không ghi được settings (vd: ổ đĩa đầy)
            }
        }

        /// <summary>
        /// Nếu máy này có file settings.json cũ (từ bản app trước khi đổi vị trí lưu) mà chưa có file
        /// mới ở %AppData% -> copy sang vị trí mới 1 lần, để người dùng không bị mất cài đặt đã có.
        /// Không xóa file cũ (an toàn nếu người dùng lỡ hạ cấp về bản app cũ hơn).
        /// </summary>
        private static void MigrateLegacySettingsIfNeeded()
        {
            if (File.Exists(SettingsPath) || !File.Exists(LegacySettingsPath))
                return;

            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.Copy(LegacySettingsPath, SettingsPath);
            }
            catch
            {
                // Không migrate được thì thôi, Load() sẽ rơi về AppSettings() mặc định như bình thường.
            }
        }
    }
}