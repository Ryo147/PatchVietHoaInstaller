using System.Collections.Generic;

namespace VietHoaInstaller.Models
{
    /// <summary>
    /// Cấu hình đơn giản của tool, lưu cạnh file .exe (settings.json).
    /// Dùng để tự điền lại đường dẫn thư mục game lần mở sau.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// DEPRECATED — giữ lại chỉ để tương thích ngược với settings.json cũ (không còn được ghi mới).
        /// Trước đây đây là 1 chuỗi DUY NHẤT dùng chung cho MỌI game -> khi người dùng chuyển giữa các
        /// game trong GameCatalog, thư mục của game A bị điền nhầm vào game B. Đã thay bằng
        /// <see cref="GameFolders"/> (theo từng game). Xem HomePage.ApplySelectedProfile.
        /// </summary>
        public string LastGameFolder { get; set; } = "";

        /// <summary>
        /// Thư mục đã ghi nhớ RIÊNG cho từng game (key = GameProfile.Name), thay cho LastGameFolder cũ
        /// dùng chung 1 biến cho mọi game. Cũng được LibraryPage dùng để hiển thị chính xác game nào
        /// đã cài Việt hóa (thay vì chỉ phân biệt "khả dụng"/"sắp ra mắt" như trước).
        /// </summary>
        public Dictionary<string, string> GameFolders { get; set; } = new();

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
