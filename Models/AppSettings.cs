using System.Collections.Generic;

namespace VietHoaInstaller.Models
{
    /// <summary>
    /// Cấu hình đơn giản của tool, lưu cạnh file .exe (settings.json).
    /// Dùng để tự điền lại đường dẫn thư mục game lần mở sau.
    /// </summary>
    public class AppSettings
    {
        public string LastGameFolder { get; set; } = "";

        /// <summary>Tắt để bỏ qua bước tự kiểm tra bản cập nhật ứng dụng mỗi khi mở app.</summary>
        public bool AutoCheckUpdate { get; set; } = true;

        /// <summary>Hiện hộp thoại hỏi lại trước khi gỡ Việt hóa. Tắt để gỡ ngay không cần xác nhận.</summary>
        public bool ConfirmBeforeUninstall { get; set; } = true;

        /// <summary>Giữ cửa sổ app luôn nổi trên các cửa sổ khác (Window.Topmost).</summary>
        public bool AlwaysOnTop { get; set; } = false;

        /// <summary>Tự mở File Explorer tại thư mục game ngay khi cài đặt Việt hóa hoàn tất.</summary>
        public bool AutoOpenFolderAfterInstall { get; set; } = false;

        public bool AutoCheckPatchUpdate { get; set; } = true;
        public int PatchCheckIntervalMinutes { get; set; } = 60;
        public bool MinimizeToTrayOnClose { get; set; } = true;

        /// <summary>Version Patch mới nhất ĐÃ THÔNG BÁO cho từng game (key = GameProfile.Name) — tránh báo lặp.</summary>
        public Dictionary<string, string> LastNotifiedPatchVersions { get; set; } = new();
    }
}
