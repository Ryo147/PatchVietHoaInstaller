using System.Collections.Generic;

namespace VietHoaInstaller.Models
{
    public class GameProfile
    {
        public string Name { get; set; } = "";
        public string PatchDownloadUrl { get; set; } = "";
        public List<string> RequiredGameFiles { get; set; } = new();

        public GameInstallMode InstallMode { get; set; } = GameInstallMode.OverwriteFiles;

        /// <summary>Chỉ dùng khi InstallMode = CopyToModFolder — thư mục con (tính từ thư mục game) để copy mod vào.</summary>
        public string ModFolderRelativePath { get; set; } = "";

        /// <summary>Đường dẫn ảnh banner hiển thị khi chọn game này, dạng pack URI, ví dụ: "/Assets/PlagueIncVH.jpg".</summary>
        public string BannerImagePath { get; set; } = "";

        /// <summary>Steam AppID của game, dùng để tự động dò tìm thư mục cài đặt qua Steam (xem SteamLocatorService).</summary>
        public string SteamAppId { get; set; } = "";

        /// <summary>Hash (SHA-256/MD5) của file zip patch mới nhất, dùng để xác thực tính toàn vẹn sau khi tải. Để rỗng nếu chưa công bố hash.</summary>
        public string ExpectedHash { get; set; } = "";

        /// <summary>Thuật toán tương ứng với ExpectedHash: "SHA256" (khuyến nghị) hoặc "MD5".</summary>
        public string HashAlgorithmName { get; set; } = "SHA256";

        /// <summary>Chủ repo GitHub chứa release patch, dùng để tự động lấy link tải + hash mới nhất. Để rỗng nếu chưa muốn dùng auto-update, tool sẽ dùng PatchDownloadUrl hardcode.</summary>
        public string GitHubOwner { get; set; } = "Ryo147";

        /// <summary>Tên repo GitHub chứa release patch.</summary>
        public string GitHubRepo { get; set; } = "PatchVH";

        /// <summary>Lọc đúng file trong release nếu 1 release có nhiều asset (vd release chung cho nhiều game). Để rỗng nếu release chỉ có 1 file.</summary>
        public string AssetNameContains { get; set; } = "PATCHVH";

        /// <summary>
        /// Danh sách buildid Steam đã test/xác nhận bản Việt hóa hoạt động đúng (xem SteamLocatorService.GetInstalledBuildId).
        /// Để rỗng = chưa cấu hình, tool sẽ BỎ QUA kiểm tra này (không cảnh báo gì).
        /// Cần nhóm dịch tự cập nhật danh sách này sau khi test — buildid đổi mỗi khi game được Steam cập nhật.
        /// </summary>
        public List<string> SupportedBuildIds { get; set; } = new();
        /// <summary>
        /// True nếu đích cài không phải thư mục game thật (vd: bundle FluffyModManager),
        /// khi đó bỏ qua kiểm tra RequiredGameFiles — cho phép người dùng chọn thư mục bất kỳ.
        /// </summary>
        public bool SkipGameFolderValidation { get; set; } = false;
        /// <summary>
        /// Đường dẫn tương đối (tính từ thư mục vừa cài) của file .exe cần hỏi mở sau khi cài xong
        /// (vd: "FluffyModManager.exe"). Để rỗng nếu không cần tự mở gì.
        /// </summary>
        public string LaunchExeRelativePath { get; set; } = "";
        /// <summary>
        /// True nếu bản Việt hóa game này CHƯA HOÀN THÀNH (đang phát triển/thử nghiệm).
        /// Khi đó: ẩn/khóa nút "Cài đặt Patch", hiện trạng thái cảnh báo riêng, và chặn cài đặt
        /// dù người dùng có lỡ chọn được thư mục hợp lệ đi nữa — tránh cài patch dở vào game thật.
        /// </summary>
        public bool IsComingSoon { get; set; } = false;
    }
    public static class GameCatalog
    {
        public static List<GameProfile> All { get; } = new()
        {
            new GameProfile
            {
                Name = "Plague Inc: Evolved",
                ExpectedHash = "46ad3b2f97934edcf692a3c70bd137d298438895f196439ae0370185ae150e44",
                PatchDownloadUrl = "https://github.com/Ryo147/PatchVH/releases/download/1/PATCHVH_P.I._RELEASE_v1.0.zip",
                RequiredGameFiles = new()
                {
                    @"PlagueIncEvolved_Data\resources.assets",
                    @"PlagueIncEvolved_Data\sharedassets0.assets"
                },
                BannerImagePath = "/Assets/PlagueIncVH.jpg",
                SteamAppId = "246620",
                GitHubOwner = "Ryo147",
                GitHubRepo = "PatchVH",
                AssetNameContains = "P.I" // TODO: đổi khớp đúng tên file asset thật trong release nếu 1 release có nhiều game
            },
            new GameProfile
            {
                Name = "Resident Evil 2 Remake (DX11_NON-RT) w/ Fluffy Mod Manager",
                PatchDownloadUrl = "", // TODO: đổi link thật
                IsComingSoon = true,
                RequiredGameFiles = new() { @"re2.exe" }, // file dùng để nhận diện đúng thư mục game
                InstallMode = GameInstallMode.CopyToModFolder,
                ModFolderRelativePath = "", // zip đã có sẵn "natives\..." ở gốc -> copy thẳng vào gốc thư mục game, không cộng thêm thư mục con
                BannerImagePath = "/Assets/RE2_DX11.png", // TODO: đảm bảo file này nằm trong thư mục Assets
                SteamAppId = "883710",
                SkipGameFolderValidation = true,
                LaunchExeRelativePath = "RE2R-Mod/Modmanager.exe",   // <-- thêm
                GitHubOwner = "Ryo147",
                GitHubRepo = "PatchVH",
                AssetNameContains = "RE2R"
            },
            // TODO: thêm game khác của nhóm, copy y hệt khối trên và đổi 3 dòng
        };
    }
}