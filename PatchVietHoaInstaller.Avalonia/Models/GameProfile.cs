using System.Collections.Generic;
using System.IO;

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

        /// <summary>Đường dẫn ảnh banner hiển thị khi chọn game này. Dùng dạng "avares://..." trên Avalonia (xem BannerPathToImageConverter).</summary>
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

        /// <summary>
        /// Tag/release GitHub RIÊNG cho game này (vd "1", "re2r", "plague-inc"...). Để rỗng = dùng chung
        /// release "latest" của cả repo với các game khác (hành vi cũ, chỉ an toàn khi repo chỉ có 1 release
        /// hoạt động). Khi tách tag riêng cho từng game, một release mới của game A sẽ KHÔNG ảnh hưởng tới
        /// việc tool tìm patch của game B nữa — mỗi game tự quản lý release/tag của chính nó.
        /// </summary>
        public string GitHubReleaseTag { get; set; } = "";

        /// <summary>Lọc đúng file trong release nếu 1 release có nhiều asset (vd release chung cho nhiều game). Để rỗng nếu release chỉ có 1 file.</summary>
        public string AssetNameContains { get; set; } = "";

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
        /// Đường dẫn tương đối (tính từ thư mục vừa cài) của file .exe/binary cần hỏi mở sau khi cài xong
        /// (vd: "FluffyModManager.exe"). Để rỗng nếu không cần tự mở gì.
        /// </summary>
        public string LaunchExeRelativePath { get; set; } = "";
        /// <summary>
        /// True nếu bản Việt hóa game này CHƯA HOÀN THÀNH (đang phát triển/thử nghiệm).
        /// Khi đó: ẩn/khóa nút "Cài đặt Patch", hiện trạng thái cảnh báo riêng, và chặn cài đặt
        /// dù người dùng có lỡ chọn được thư mục hợp lệ đi nữa — tránh cài patch dở vào game thật.
        /// </summary>
        public bool IsComingSoon { get; set; } = false;

        /// <summary>
        /// Version hiện tại của bản Patch (KHÔNG phải version app). Cập nhật số này mỗi khi ra bản patch
        /// mới cho game này — làm song song với cập nhật ExpectedHash. Dùng để so sánh với version tách
        /// được từ tên asset mới nhất trên GitHub (PatchUpdateCheckerService). Định dạng: "1.0", "1.1"...
        /// </summary>
        public string KnownPatchVersion { get; set; } = "";

        /// <summary>
        /// Phiên bản GAME (không phải version app) mà bản Việt hóa này đã test/áp dụng — hardcode thủ công,
        /// chỉ mang tính hiển thị cho người dùng biết, KHÔNG dùng để so sánh tự động (khác với SupportedBuildIds).
        /// Vd: "Bản Steam 1.3.11" hoặc để rỗng nếu chưa muốn hiện.
        /// </summary>
        public string ApplicableGameVersion { get; set; } = "";

        public string? InstallNote { get; set; }
    }

    public static class GameCatalog
    {
        // GHI CHÚ PORT LINUX: RequiredGameFiles trước đây hardcode bằng @"Folder\file.ext" (backslash).
        // Trên Linux, "\" KHÔNG phải ký tự phân cách thư mục -> File.Exists sẽ tìm sai tên file.
        // Dùng Path.Combine(...) để .NET tự chọn đúng separator theo OS đang chạy.
        public static List<GameProfile> All { get; } = new()
        {
            new GameProfile
            {
                Name = "Plague Inc: Evolved",
                ExpectedHash = "46ad3b2f97934edcf692a3c70bd137d298438895f196439ae0370185ae150e44",
                PatchDownloadUrl = "https://github.com/Ryo147/PatchVH/releases/download/1/PATCHVH_P.I._v1.0.1.zip",
                RequiredGameFiles = new()
                {
                    Path.Combine("PlagueIncEvolved_Data", "resources.assets"),
                    Path.Combine("PlagueIncEvolved_Data", "sharedassets0.assets")
                },
                BannerImagePath = "avares://PatchVietHoaInstaller/Assets/PlagueIncVH.jpg",
                SteamAppId = "246620",
                GitHubOwner = "Ryo147",
                GitHubRepo = "PatchVH",
                GitHubReleaseTag = "1",
                AssetNameContains = "P.I",
                ApplicableGameVersion = "1.24.0.2",
                // Khớp với version trong PatchDownloadUrl ở trên ("..._v1.0.zip"). Nếu để rỗng,
                // PatchUpdateCheckerService sẽ luôn coi release hiện tại là "bản mới" ở lần kiểm tra đầu,
                // dù người dùng đã có đúng bản mới nhất -> báo giả liên tục.
                KnownPatchVersion = "1.0.1"
            },
            new GameProfile
            {
                Name = "Resident Evil 2 Remake (DX11_NON-RT) w/ Fluffy Mod Manager",
                PatchDownloadUrl = "EMPTY", // TODO: đổi link thật
                IsComingSoon = true,
                RequiredGameFiles = new() { "re2.exe" }, // file dùng để nhận diện đúng thư mục game
                InstallMode = GameInstallMode.CopyToModFolder,
                ModFolderRelativePath = "", // zip đã có sẵn "natives\..." ở gốc -> copy thẳng vào gốc thư mục game, không cộng thêm thư mục con
                BannerImagePath = "avares://PatchVietHoaInstaller/Assets/RE2_DX11.png", // TODO: đảm bảo file này nằm trong thư mục Assets
                SteamAppId = "883710",
                SkipGameFolderValidation = true,
                LaunchExeRelativePath = Path.Combine("RE2R-Mod", "Modmanager.exe"),
                GitHubOwner = "Ryo147",
                GitHubRepo = "PatchVH",
                GitHubReleaseTag = "", // TODO: điền tag/release thật của RE2R trước khi bỏ IsComingSoon, để không dùng chung "latest" với game khác
                AssetNameContains = "RE2R",
                InstallNote = "Đây là PATCH đi kèm với FluffyModManager nên bạn cần CHỌN THƯ MỤC THỦ CÔNG. Bản dịch có sử dụng phương ngữ miền Nam vào lời thoại nhân vật. Cân nhắc trước khi chơi."
            },
            // TODO: thêm game khác của nhóm, copy y hệt khối trên và đổi 3 dòng
        };
    }
}
