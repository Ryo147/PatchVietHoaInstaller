using System;
using System.IO;
using System.Text.Json;
using VietHoaInstaller.Models;

namespace VietHoaInstaller.Services
{
    /// <summary>
    /// Đọc/ghi file settings.json nằm cạnh file .exe của tool
    /// (KHÔNG liên quan đến manifest.json trong thư mục game).
    /// </summary>
    public static class SettingsManager
    {
        private static readonly string SettingsPath =
            Path.Combine(AppContext.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
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
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Không chặn luồng chính nếu lỡ không ghi được settings (vd: thư mục chỉ đọc)
            }
        }
    }
}
