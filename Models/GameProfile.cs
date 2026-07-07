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
        public string GitHubOwner { get; set; } = "";

        /// <summary>Tên repo GitHub chứa release patch.</summary>
        public string GitHubRepo { get; set; } = "";

        /// <summary>Lọc đúng file trong release nếu 1 release có nhiều asset (vd release chung cho nhiều game). Để rỗng nếu release chỉ có 1 file.</summary>
        public string AssetNameContains { get; set; } = "";
    }
    public static class GameCatalog
    {
        public static List<GameProfile> All { get; } = new()
        {
            new GameProfile
            {
                Name = "[BETA EARLY ACCESS] Plague Inc: Evolved",
                PatchDownloadUrl = "https://github.com/Ryo147/PatchVH/releases/download/PatchLocalization/PatchVH_P.I_v.BETA.zip",
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
                Name = "[CHƯA HOÀN THIỆN] Resident Evil 2 Remake (DX11_NON-RT)",
                PatchDownloadUrl = "https://github.com/Ryo147/PatchVH/releases/download/PatchLocalization/RE2R_DX11_PATCHVH_ALPHA.zip", // TODO: đổi link thật
                RequiredGameFiles = new() { @"re2.exe" }, // file dùng để nhận diện đúng thư mục game
                InstallMode = GameInstallMode.CopyToModFolder,
                ModFolderRelativePath = "", // zip đã có sẵn "natives\..." ở gốc -> copy thẳng vào gốc thư mục game, không cộng thêm thư mục con
                BannerImagePath = "/Assets/RE2_DX11.png", // TODO: đảm bảo file này nằm trong thư mục Assets
                SteamAppId = "883710",
                GitHubOwner = "Ryo147",
                GitHubRepo = "PatchVH",
                AssetNameContains = "RE2R"
            },
            // TODO: thêm game khác của nhóm, copy y hệt khối trên và đổi 3 dòng
        };
    }
}