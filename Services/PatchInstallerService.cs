using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
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

        public string PatchDownloadUrl { get; set; } = "https://github.com/Ryo147/PatchVH-Plague-Inc./releases/download/PatchLocalization/PatchVH_P.I_v.BETA.zip";
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
            return File.Exists(GetManifestPath(gameFolder));
        }

        /// <summary>
        /// Kiểm tra thư mục được chọn có đúng là thư mục game hay không, dựa trên
        /// danh sách <see cref="RequiredGameFiles"/>. Nếu đã cài Việt hóa trước đó
        /// (file đã bị ghi đè) thì vẫn coi là hợp lệ, vì lúc đó file gốc đã được
        /// backup và thay bằng file Việt hóa — không còn file gốc để so khớp.
        /// </summary>
        public GameFolderCheckResult ValidateGameFolder(string gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
                return new GameFolderCheckResult(false, "Thư mục không tồn tại.");

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
                progress.Report(new InstallProgress(0, "Đang kết nối máy chủ..."));

                await DownloadFileAsync(PatchDownloadUrl, zipPath, downloadPercent =>
                {
                    int overall = (int)(downloadPercent * 0.7);
                    progress.Report(new InstallProgress(overall, $"Đang tải bản Việt hóa... {downloadPercent}%"));
                }, ct);

                progress.Report(new InstallProgress(72, "Đang giải nén gói Việt hóa..."));
                Directory.CreateDirectory(extractDir);
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true), ct);
                progress.Report(new InstallProgress(82, "Giải nén hoàn tất."));

                var backupFolder = GetBackupFolder(gameFolder);
                Directory.CreateDirectory(backupFolder);

                var manifest = new InstallManifest
                {
                    GameFolder = gameFolder,
                    InstalledAtUtc = DateTime.UtcNow
                };

                var allFiles = Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories);
                int total = Math.Max(allFiles.Length, 1);
                int done = 0;

                foreach (var srcFile in allFiles)
                {
                    ct.ThrowIfCancellationRequested();

                    string relativePath = Path.GetRelativePath(extractDir, srcFile);
                    string destFile = Path.Combine(gameFolder, relativePath);
                    string destDir = Path.GetDirectoryName(destFile)!;
                    Directory.CreateDirectory(destDir);

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
        /// Khôi phục toàn bộ file gốc đã backup, xóa các file do bản Việt hóa tạo mới (nếu có).
        /// </summary>
        public Task UninstallAsync(string gameFolder, IProgress<InstallProgress> progress, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var manifest = LoadManifest(gameFolder)
                    ?? throw new InvalidOperationException("Không tìm thấy dữ liệu bản cài đặt để gỡ Việt hóa.");

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

                progress.Report(new InstallProgress(95, "Đang dọn dẹp bản sao lưu..."));
                try { Directory.Delete(backupFolder, recursive: true); }
                catch { }

                progress.Report(new InstallProgress(100, "Đã gỡ Việt hóa, khôi phục file gốc."));
            }, ct);
        }

        /// <summary>Tải file từ URL về đường dẫn cục bộ, báo % tiến trình qua callback.</summary>
        private static async Task DownloadFileAsync(string url, string destinationPath, Action<int> onProgress, CancellationToken ct)
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(
                destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;

                if (totalBytes is > 0)
                {
                    int pct = (int)(totalRead * 100 / totalBytes.Value);
                    onProgress(pct);
                }
            }
        }
    }
}