namespace VietHoaInstaller.Models
{
    /// <summary>
    /// Cấu hình đơn giản của tool, lưu cạnh file .exe (settings.json).
    /// Dùng để tự điền lại đường dẫn thư mục game lần mở sau.
    /// </summary>
    public class AppSettings
    {
        public string LastGameFolder { get; set; } = "";
    }
}
