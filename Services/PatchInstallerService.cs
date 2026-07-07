using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VietHoaInstaller.Models;

namespace VietHoaInstaller.Services
{
    /// <summary>
    /// Báo tiến trình về cho UI: Percent (0-100) và dòng thông báo đang làm gì.
    /// </summary>
    public record InstallProgress(int Percent, string Message);

    /// <summary>
    /// Kết quả kiểm tra xem thư mục được chọn có đúng là thư mục game hay không.
    /// </summary>
    public record GameFolderCheckResult(bool IsValid, string Message);

    public class PatchInstallerService
    {
        private const string BackupFolderName = "VietHoaBackup";
        private const string ManifestFileName = "manifest.json";

        // Dùng chung 1 HttpClient cho cả app thay vì tạo mới mỗi lần tải — tránh cạn kiệt socket khi tải nhiều lần.
        private static readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

        public string PatchDownloadUrl { get; set; } = "https://github.com/Ryo147/PatchVH-Plague-Inc./releases/download/PatchLocalization/PatchVH_P.I_v.BETA.zip";
        public GameInstallMode InstallMode { get; set; } = GameInstallMode.OverwriteFiles;
        public string ModFolderRelativePath { get; set; } = "";

        /// <summary>Hash kỳ vọng của file zip patch, dùng để xác thực tính toàn vẹn sau khi tải xong. Để rỗng nếu chưa có, sẽ bỏ qua bước xác thực.</summary>
        public string ExpectedHash { get; set; } = "";

        /// <summary>Thuật toán hash tương ứng với ExpectedHash: "MD5" hoặc "SHA256" (mặc định).</summary>
        public string HashAlgorithmName { get; set; } = "SHA256";

        /// <summary>Nếu có, tool sẽ thử gọi GitHub API lấy link + hash bản patch mới nhất trước khi cài, thay vì dùng PatchDownloadUrl hardcode.</summary>
        public string GitHubOwner { get; set; } = "";
        public string GitHubRepo { get; set; } = "";
        public string AssetNameContains { get; set; } = "";

        public List<string> RequiredGameFiles { get; set; } = new()
            {
                @"PlagueIncEvolved_Data\resources.assets",
                @"PlagueIncEvolved_Data\sharedassets0.assets"
            };
        private string GetBackupFolder(string gameFolder) => Path.Combine(gameFolder, BackupFolderName);
        private string GetManifestPath(string gameFolder) => Path.Combine(GetBackupFolder(gameFolder), ManifestFileName);

        /// <summary>Kiểm tra thư mục game đã được cài Việt hóa (bởi chính tool này) hay chưa.</summary>
        public bool IsInstalled(string gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder)) return false;
            if (!File.Exists(GetManifestPath(gameFolder))) return false;

            // Nếu đã biết đang cài cho profile nào, và manifest cũ có ghi rõ ProfileName,
            // chỉ coi là "đã cài" nếu đúng là bản cài của CÙNG profile này. Tránh báo nhầm
            // "đã cài trước đó" khi 2 profile khác nhau (vd Plague Inc và Fluffy bundle) lỡ trỏ chung 1 thư mục.
            if (!string.IsNullOrWhiteSpace(ProfileName))
            {
                var existing = LoadManifest(gameFolder);
                if (existing != null && !string.IsNullOrWhiteSpace(existing.ProfileName))
                    return existing.ProfileName == ProfileName;
            }

            return true;
        }

        /// <summary>
        /// Kiểm tra thư mục được chọn có đúng là thư mục game hay không, dựa trên
        /// danh sách <see cref="RequiredGameFiles"/>. Nếu đã cài Việt hóa trước đó
        /// (file đã bị ghi đè) thì vẫn coi là hợp lệ, vì lúc đó file gốc đã được
        /// backup và thay bằng file Việt hóa — không còn file gốc để so khớp.
        /// </summary>
        public bool SkipGameFolderValidation { get; set; } = false;
        /// <summary>Tên profile game hiện đang chọn, dùng để phân biệt các profile khác nhau lỡ dùng chung 1 thư mục.</summary>
        public string ProfileName { get; set; } = "";
        public GameFolderCheckResult ValidateGameFolder(string gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
                return new GameFolderCheckResult(false, "Thư mục không tồn tại.");

            if (SkipGameFolderValidation)
                return new GameFolderCheckResult(true, "");

            if (IsInstalled(gameFolder))
                return new GameFolderCheckResult(true, "");

            if (RequiredGameFiles.Count == 0)
                return new GameFolderCheckResult(true, "");

            var missing = RequiredGameFiles
                .Where(relativePath => !File.Exists(Path.Combine(gameFolder, relativePath)))
                .ToList();

            if (missing.Count > 0)
            {
                string detail = string.Join("\n", missing.Select(f => "  • " + f));
                return new GameFolderCheckResult(false,
                    $"Đây không phải thư mục game. Không tìm thấy file:\n{detail}");
            }

            return new GameFolderCheckResult(true, "");
        }

        public InstallManifest? LoadManifest(string gameFolder)
        {
            var path = GetManifestPath(gameFolder);
            if (!File.Exists(path)) return null;

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<InstallManifest>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Tải patch từ server, backup file gốc, rồi ghi đè file Việt hóa vào thư mục game.
        /// </summary>
        public async Task InstallAsync(string gameFolder, IProgress<InstallProgress> progress, CancellationToken ct = default)
        {
            if (!Directory.Exists(gameFolder))
                throw new DirectoryNotFoundException("Thư mục game không tồn tại. Vui lòng chọn lại.");

            if (IsInstalled(gameFolder))
                throw new InvalidOperationException("Thư mục này đã được cài Việt hóa trước đó. Vui lòng gỡ Việt hóa trước nếu muốn cài lại.");

            var folderCheck = ValidateGameFolder(gameFolder);
            if (!folderCheck.IsValid)
                throw new InvalidOperationException(folderCheck.Message);

            string tempDir = Path.Combine(Path.GetTempPath(), "VietHoaInstaller_" + Guid.NewGuid().ToString("N"));
            string zipPath = Path.Combine(tempDir, "patch.zip");
            string extractDir = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(tempDir);

            try
            {
                // ===== BƯỚC 0: THỬ LẤY LINK + HASH PATCH MỚI NHẤT TỪ GITHUB (nếu có cấu hình repo) =====
                string downloadUrl = PatchDownloadUrl;
                if (!string.IsNullOrWhiteSpace(GitHubOwner) && !string.IsNullOrWhiteSpace(GitHubRepo))
                {
                    progress.Report(new InstallProgress(0, "Đang kiểm tra bản patch mới nhất..."));
                    var release = await GitHubReleaseService.GetLatestReleaseAsync(GitHubOwner, GitHubRepo, ct);
                    var asset = release != null ? GitHubReleaseService.FindAsset(release, AssetNameContains) : null;

                    if (asset != null)
                    {
                        downloadUrl = asset.BrowserDownloadUrl;
                        string? autoHash = GitHubReleaseService.ExtractSha256Hex(asset.Digest);
                        if (autoHash != null)
                        {
                            ExpectedHash = autoHash;
                            HashAlgorithmName = "SHA256";
                        }
                    }
                    // Không lấy được từ GitHub (mất mạng/hết rate limit) -> downloadUrl vẫn giữ nguyên PatchDownloadUrl hardcode ở trên, không chặn cài đặt.
                }

                progress.Report(new InstallProgress(0, "Đang kết nối máy chủ..."));
                await DownloadWithResumeAndVerifyAsync(downloadUrl, zipPath, progress, ct);

                progress.Report(new InstallProgress(72, "Đang giải nén gói Việt hóa..."));
                Directory.CreateDirectory(extractDir);
                await Task.Run(() => ExtractZipWithProgress(zipPath, extractDir, progress, ct), ct);
                progress.Report(new InstallProgress(82, "Giải nén hoàn tất."));

                // ===== BƯỚC 3: ÁP DỤNG PATCH (82% - 98%) — theo đúng InstallMode =====
                InstallManifest manifest = InstallMode == GameInstallMode.CopyToModFolder
                    ? CopyToModFolder(gameFolder, extractDir, progress)
                    : OverwriteGameFiles(gameFolder, extractDir, progress);

                // ===== BƯỚC 4: LƯU MANIFEST =====
                Directory.CreateDirectory(GetBackupFolder(gameFolder));
                File.WriteAllText(
                    GetManifestPath(gameFolder),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

                progress.Report(new InstallProgress(100, "Hoàn tất! Đã cài đặt Việt hóa."));
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch { }
            }
        }

        /// <summary>
        /// Giải nén zip thủ công từng entry (thay vì gọi ZipFile.ExtractToDirectory một phát),
        /// để báo tiến trình thật theo số file đã giải nén — tránh thanh progress bị đứng im rồi nhảy khựng.
        /// </summary>
        private static void ExtractZipWithProgress(string zipPath, string extractDir, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries.ToList();

            int total = Math.Max(entries.Count, 1);
            int done = 0;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                string destPath = Path.Combine(extractDir, entry.FullName);

                // Entry là thư mục (không có tên file) -> chỉ cần tạo thư mục
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                }

                done++;
                int pct = 72 + (int)((done / (double)total) * 10); // dải 72% -> 82%
                progress.Report(new InstallProgress(pct, $"Đang giải nén ({done}/{total} file)..."));
            }
        }

        /// <summary>Kiểu cũ: ghi đè file gốc, có backup để khôi phục (Plague Inc).</summary>
        private InstallManifest OverwriteGameFiles(string gameFolder, string extractDir, IProgress<InstallProgress> progress)
        {
            var backupFolder = GetBackupFolder(gameFolder);
            Directory.CreateDirectory(backupFolder);

            var manifest = new InstallManifest
            {
                GameFolder = gameFolder,
                InstalledAtUtc = DateTime.UtcNow,
                InstallMode = GameInstallMode.OverwriteFiles.ToString(),
                ProfileName = ProfileName
            };

            var allFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            int total = Math.Max(allFiles.Length, 1);
            int done = 0;

            foreach (var srcFile in allFiles)
            {
                string relativePath = Path.GetRelativePath(extractDir, srcFile);
                string destFile = Path.Combine(gameFolder, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                string backupFile = Path.Combine(backupFolder, relativePath);
                if (File.Exists(destFile) && !File.Exists(backupFile))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
                    File.Copy(destFile, backupFile, overwrite: false);
                }

                File.Copy(srcFile, destFile, overwrite: true);
                manifest.RelativeFiles.Add(relativePath);

                done++;
                int pct = 82 + (int)((done / (double)total) * 16);
                progress.Report(new InstallProgress(pct, $"Đang cài đặt file ({done}/{total})..."));
            }

            return manifest;
        }

        /// <summary>Kiểu mới: chỉ copy vào thư mục mod riêng, không đụng file gốc (RE2R kiểu Fluffy Mod Manager).</summary>
        private InstallManifest CopyToModFolder(string gameFolder, string extractDir, IProgress<InstallProgress> progress)
        {
            string modFolder = Path.Combine(gameFolder, ModFolderRelativePath);
            Directory.CreateDirectory(modFolder);

            var manifest = new InstallManifest
            {
                GameFolder = gameFolder,
                InstalledAtUtc = DateTime.UtcNow,
                InstallMode = GameInstallMode.CopyToModFolder.ToString(),
                ModFolderRelativePath = ModFolderRelativePath,
                ProfileName = ProfileName
            };

            var allFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
            int total = Math.Max(allFiles.Length, 1);
            int done = 0;

            foreach (var srcFile in allFiles)
            {
                string relativePath = Path.GetRelativePath(extractDir, srcFile);
                string destFile = Path.Combine(modFolder, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

                File.Copy(srcFile, destFile, overwrite: true);
                manifest.RelativeFiles.Add(relativePath);

                done++;
                int pct = 82 + (int)((done / (double)total) * 16);
                progress.Report(new InstallProgress(pct, $"Đang copy mod ({done}/{total})..."));
            }

            return manifest;
        }

        /// <summary>
        /// Gỡ Việt hóa: nếu cài kiểu CopyToModFolder thì chỉ xóa file mod;
        /// nếu cài kiểu OverwriteFiles thì khôi phục file gốc từ backup.
        /// </summary>
        public Task UninstallAsync(string gameFolder, IProgress<InstallProgress> progress, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var manifest = LoadManifest(gameFolder)
                    ?? throw new InvalidOperationException("Không tìm thấy dữ liệu bản cài đặt để gỡ Việt hóa.");

                if (manifest.InstallMode == GameInstallMode.CopyToModFolder.ToString())
                {
                    // Kiểu mod folder: chỉ cần xóa các file đã copy, không cần khôi phục gì
                    string modFolder = Path.Combine(gameFolder, manifest.ModFolderRelativePath);
                    int total = Math.Max(manifest.RelativeFiles.Count, 1);
                    int done = 0;

                    foreach (var relativePath in manifest.RelativeFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        string file = Path.Combine(modFolder, relativePath);
                        if (File.Exists(file)) File.Delete(file);

                        done++;
                        int pct = (int)((done / (double)total) * 90);
                        progress.Report(new InstallProgress(pct, $"Đang xóa file mod ({done}/{total})..."));
                    }

                    try
                    {
                        // Chỉ dọn dẹp thư mục con nếu ModFolderRelativePath không rỗng.
                        // Nếu rỗng, modFolder chính là gốc thư mục game -> TUYỆT ĐỐI không được xóa.
                        if (!string.IsNullOrWhiteSpace(manifest.ModFolderRelativePath)
                            && Directory.Exists(modFolder)
                            && !Directory.EnumerateFileSystemEntries(modFolder).Any())
                        {
                            Directory.Delete(modFolder);
                        }
                    }
                    catch { }
                }
                else
                {
                    // Kiểu cũ: khôi phục file gốc từ backup
                    var backupFolder = GetBackupFolder(gameFolder);
                    int total = Math.Max(manifest.RelativeFiles.Count, 1);
                    int done = 0;

                    foreach (var relativePath in manifest.RelativeFiles)
                    {
                        ct.ThrowIfCancellationRequested();

                        string destFile = Path.Combine(gameFolder, relativePath);
                        string backupFile = Path.Combine(backupFolder, relativePath);

                        if (File.Exists(backupFile))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                            File.Copy(backupFile, destFile, overwrite: true);
                        }
                        else if (File.Exists(destFile))
                        {
                            File.Delete(destFile);
                        }

                        done++;
                        int pct = (int)((done / (double)total) * 90);
                        progress.Report(new InstallProgress(pct, $"Đang khôi phục file gốc ({done}/{total})..."));
                    }
                }

                progress.Report(new InstallProgress(95, "Đang dọn dẹp bản sao lưu..."));
                try { Directory.Delete(GetBackupFolder(gameFolder), recursive: true); }
                catch { }

                progress.Report(new InstallProgress(100, "Đã gỡ Việt hóa."));
            }, ct);
        }

        /// <summary>
        /// Tải file từ URL về đường dẫn cục bộ. Nếu đã có file tải dở (từ lần trước bị mất mạng),
        /// tự động resume tiếp bằng HTTP Range request thay vì tải lại từ đầu. Sau khi tải xong,
        /// nếu có ExpectedHash thì xác thực tính toàn vẹn — nếu sai, xóa file lỗi và ném lỗi để tải lại từ đầu.
        /// </summary>
        private async Task DownloadWithResumeAndVerifyAsync(string url, string destinationPath, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            long existingBytes = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingBytes > 0)
                request.Headers.Range = new RangeHeaderValue(existingBytes, null);

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            // Server không hỗ trợ resume (trả về 200 thay vì 206 Partial Content) -> tải lại từ đầu cho an toàn
            bool serverSupportsResume = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
            if (existingBytes > 0 && !serverSupportsResume)
            {
                existingBytes = 0;
            }

            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;
            long totalBytes = existingBytes + (contentLength ?? 0);

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(
                destinationPath,
                existingBytes > 0 && serverSupportsResume ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long totalRead = existingBytes;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    int downloadPercent = (int)(totalRead * 100 / totalBytes);
                    int overall = (int)(downloadPercent * 0.7); // dải 0% -> 70%
                    progress.Report(new InstallProgress(overall, $"Đang tải bản Việt hóa... {downloadPercent}%"));
                }
            }

            fileStream.Close();

            if (!string.IsNullOrWhiteSpace(ExpectedHash))
            {
                progress.Report(new InstallProgress(70, "Đang xác thực file tải về..."));
                bool valid = await VerifyHashAsync(destinationPath);
                if (!valid)
                {
                    try { File.Delete(destinationPath); } catch { }
                    throw new InvalidOperationException(
                        "File patch tải về bị lỗi hoặc không toàn vẹn (sai hash). Vui lòng thử cài đặt lại.");
                }
            }
        }

        /// <summary>So khớp hash (MD5/SHA-256) của file đã tải với ExpectedHash. Nếu ExpectedHash rỗng thì luôn coi là hợp lệ (bỏ qua xác thực).</summary>
        private async Task<bool> VerifyHashAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(ExpectedHash))
                return true;

            using System.Security.Cryptography.HashAlgorithm hasher = HashAlgorithmName.ToUpperInvariant() == "MD5"
                ? MD5.Create()
                : SHA256.Create();

            await using var stream = File.OpenRead(filePath);
            byte[] hashBytes = await hasher.ComputeHashAsync(stream, CancellationToken.None);
            string actualHash = Convert.ToHexString(hashBytes);

            return actualHash.Equals(ExpectedHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}