using System;
using System.Collections.Generic;

namespace VietHoaInstaller.Models
{
    /// <summary>
    /// Ghi lại những gì đã cài vào thư mục game, dùng để khôi phục khi gỡ Việt hóa.
    /// File này được lưu tại: {ThưMụcGame}\VietHoaBackup\manifest.json
    /// </summary>
    public class InstallManifest
    {
        public string GameFolder { get; set; } = "";
        public string PatchVersion { get; set; } = "1.0";
        public DateTime InstalledAtUtc { get; set; }

        /// <summary>
        /// Đường dẫn tương đối (so với thư mục game) của từng file đã bị ghi đè.
        /// Dùng để biết cần khôi phục / xóa file nào khi gỡ.
        /// </summary>
        public List<string> RelativeFiles { get; set; } = new();
    }
}
