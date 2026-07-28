using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
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

        // GHI CHÚ PORT: dùng SocketsHttpHandler tường minh thay vì để .NET tự chọn handler mặc định.
        // PooledConnectionLifetime/IdleTimeout giúp tránh việc HttpClient tái sử dụng 1 connection đã
        // bị server/CDN âm thầm đóng — nguyên nhân phổ biến của lỗi "unexpected EOF" khi tải file lớn.
        private static readonly SocketsHttpHandler _handler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(30)
        };

        // Dùng chung 1 HttpClient cho cả app thay vì tạo mới mỗi lần tải — tránh cạn kiệt socket khi tải nhiều lần.
        private static readonly HttpClient _http = new(_handler) { Timeout = Timeout.InfiniteTimeSpan };

        private const int MaxDownloadRetries = 4;

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
        public string GitHubReleaseTag { get; set; } = "";
        public string AssetNameContains { get; set; } = "";

        /// <summary>Baseline version dùng làm fallback khi API lỗi/không tìm được asset khớp lúc cài — copy từ GameProfile.KnownPatchVersion.</summary>
        public string KnownPatchVersion { get; set; } = "";

        public List<string> RequiredGameFiles { get; set; } = new()
            {
                Path.Combine("PlagueIncEvolved_Data", "resources.assets"),
                Path.Combine("PlagueIncEvolved_Data", "sharedassets0.assets")
            };
        private static string GetBackupFolder(string gameFolder) => Path.Combine(gameFolder, BackupFolderName);
        private static string GetManifestPath(string gameFolder) => Path.Combine(GetBackupFolder(gameFolder), ManifestFileName);

        /// <summary>
        /// Đọc version patch THỰC SỰ đã cài vào thư mục này (ghi lại lúc InstallAsync thành công), không phụ thuộc
        /// vào KnownPatchVersion hardcode trong app. Dùng cho update-checker để biết chính xác máy này đang có
        /// bản nào, thay vì đoán qua giá trị compile-time. Trả về "" nếu chưa cài / không đọc được manifest.
        /// </summary>
        public static string TryGetInstalledPatchVersion(string gameFolder, string? expectedProfileName = null)
        {
            if (string.IsNullOrWhiteSpace(gameFolder)) return "";
            var path = GetManifestPath(gameFolder);
            if (!File.Exists(path)) return "";

            try
            {
                var json = File.ReadAllText(path);
                var manifest = JsonSerializer.Deserialize<InstallManifest>(json);
                if (manifest == null) return "";

                // Nếu có chỉ định profile mong đợi, tránh đọc nhầm version của 1 game khác từng cài ở cùng thư mục này.
                if (!string.IsNullOrWhiteSpace(expectedProfileName) &&
                    !string.IsNullOrWhiteSpace(manifest.ProfileName) &&
                    manifest.ProfileName != expectedProfileName)
                {
                    return "";
                }

                return manifest.PatchVersion ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static readonly string[] AllowedDownloadHosts =
        {
            "github.com",
            "objects.githubusercontent.com",
            "codeload.github.com",
            "release-assets.githubusercontent.com"
        };

        private static void EnsureWithinRoot(string root, string relativePath, string context)
        {
            string rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{context} chứa đường dẫn không hợp lệ: {relativePath}");
        }
        internal static void EnsureSafeDownloadUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException($"Nguồn tải không an toàn (bắt buộc HTTPS): {url}");

            bool allowedHost = AllowedDownloadHosts.Any(h =>
                uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

            if (!allowedHost)
                throw new InvalidOperationException($"Nguồn tải không nằm trong danh sách cho phép: {uri.Host}");
        }
        /// <summary>Kiểm tra thư mục game đã được cài Việt hóa (bởi chính tool này) hay chưa.</summary>
        public bool IsInstalled(string gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder)) return false;
            if (!File.Exists(GetManifestPath(gameFolder))) return false;

            if (!string.IsNullOrWhiteSpace(ProfileName))
            {
                var existing = LoadManifest(gameFolder);
                if (existing != null && !string.IsNullOrWhiteSpace(existing.ProfileName))
                    return existing.ProfileName == ProfileName;
            }

            return true;
        }

        public bool SkipGameFolderValidation { get; set; } = false;
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

        public async Task InstallAsync(string gameFolder, IProgress<InstallProgress> progress, CancellationToken ct = default)
        {
            if (!Directory.Exists(gameFolder))
                throw new DirectoryNotFoundException("Thư mục game không tồn tại. Vui lòng chọn lại.");

            if (IsInstalled(gameFolder))
                throw new InvalidOperationException("Thư mục này đã được cài Việt hóa trước đó. Vui lòng gỡ Việt hóa trước nếu muốn cài lại.");

            var folderCheck = ValidateGameFolder(gameFolder);
            if (!folderCheck.IsValid)
                throw new InvalidOperationException(folderCheck.Message);
            string stableKey = string.IsNullOrWhiteSpace(ProfileName) ? "default" : ProfileName;
            foreach (char c in Path.GetInvalidFileNameChars())
                stableKey = stableKey.Replace(c, '_');

            string tempDir = Path.Combine(Path.GetTempPath(), "VietHoaInstaller_dl_" + stableKey);
            string zipPath = Path.Combine(tempDir, "patch.zip");
            string extractDir = Path.Combine(tempDir, "extracted_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            bool installSucceeded = false;
            try
            {
                string downloadUrl = PatchDownloadUrl;
                string installedVersion = KnownPatchVersion; // fallback nếu API lỗi/không match asset
                if (!string.IsNullOrWhiteSpace(GitHubOwner) && !string.IsNullOrWhiteSpace(GitHubRepo))
                {
                    progress.Report(new InstallProgress(0, "Đang kiểm tra bản patch mới nhất..."));
                    var release = await GitHubReleaseService.GetReleaseForProfileAsync(GitHubOwner, GitHubRepo, GitHubReleaseTag, ct);
                    var asset = release != null ? GitHubReleaseService.FindAsset(release, AssetNameContains) : null;

                    if (asset != null)
                    {
                        downloadUrl = asset.BrowserDownloadUrl;
                        string? autoHash = GitHubReleaseService.ExtractSha256Hex(asset.Digest);
                        if (autoHash != null)
                        {
                            ExpectedHash = NormalizeHash(autoHash);
                            HashAlgorithmName = "SHA256";
                        }

                        // Version thật của file sắp cài, tách từ chính tên asset — đây mới là "sự thật" cần lưu lại,
                        // không phải KnownPatchVersion hardcode (thứ chỉ đại diện cho lúc app được build).
                        string? assetVersion = GitHubReleaseService.ExtractVersionFromAssetName(asset.Name);
                        if (!string.IsNullOrWhiteSpace(assetVersion))
                            installedVersion = assetVersion;
                    }
                }

                progress.Report(new InstallProgress(0, "Đang kết nối máy chủ..."));
                await DownloadWithRetryAsync(downloadUrl, zipPath, progress, ct);

                progress.Report(new InstallProgress(72, "Đang giải nén gói Việt hóa..."));
                Directory.CreateDirectory(extractDir);
                await Task.Run(() => ExtractZipWithProgress(zipPath, extractDir, progress, ct), ct);
                progress.Report(new InstallProgress(82, "Giải nén hoàn tất."));

                InstallManifest manifest = InstallMode == GameInstallMode.CopyToModFolder
                    ? CopyToModFolder(gameFolder, extractDir, progress)
                    : OverwriteGameFiles(gameFolder, extractDir, progress);
                manifest.PatchVersion = installedVersion;

                Directory.CreateDirectory(GetBackupFolder(gameFolder));
                File.WriteAllText(
                    GetManifestPath(gameFolder),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

                progress.Report(new InstallProgress(100, "Hoàn tất! Đã cài đặt Việt hóa."));
                installSucceeded = true;
            }
            finally
            {
                try { Directory.Delete(extractDir, recursive: true); } catch { }

                if (installSucceeded)
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }

        private const long MaxTotalUncompressedBytes = 10L * 1024 * 1024 * 1024; // 10 GB
        private const long MaxSingleEntryUncompressedBytes = 4L * 1024 * 1024 * 1024; // 4 GB
        private const int MaxEntryCount = 50_000;

        private static void ExtractZipWithProgress(string zipPath, string extractDir, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries.ToList();

            if (entries.Count > MaxEntryCount)
                throw new InvalidOperationException($"Gói Việt hóa có quá nhiều file ({entries.Count}), vượt giới hạn an toàn.");

            int total = Math.Max(entries.Count, 1);
            int done = 0;
            string extractDirFull = Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar;

            if (entries.Sum(e => e.Length) > MaxTotalUncompressedBytes)
                throw new InvalidOperationException(
                    $"Gói Việt hóa sau khi giải nén vượt quá giới hạn an toàn ({MaxTotalUncompressedBytes / (1024 * 1024 * 1024)}GB). Có thể file đã bị hỏng hoặc đã bị chỉnh sửa.");

            long actualTotalWritten = 0;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                if (entry.Length > MaxSingleEntryUncompressedBytes)
                    throw new InvalidOperationException($"File '{entry.FullName}' trong gói Việt hóa vượt quá giới hạn kích thước 1 file.");

                string destPath = Path.GetFullPath(Path.Combine(extractDir, entry.FullName));
                if (!destPath.StartsWith(extractDirFull, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"File patch chứa đường dẫn không hợp lệ, có thể đã bị chỉnh sửa: {entry.FullName}");

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destPath);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                    using var entryStream = entry.Open();
                    using var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

                    var buffer = new byte[81920];
                    int read;
                    while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        actualTotalWritten += read;
                        if (actualTotalWritten > MaxTotalUncompressedBytes)
                            throw new InvalidOperationException("Dữ liệu giải nén vượt quá giới hạn an toàn — gói Việt hóa có thể đã bị can thiệp.");
                        outStream.Write(buffer, 0, read);
                    }
                }

                done++;
                int pct = 72 + (int)((done / (double)total) * 10);
                progress.Report(new InstallProgress(pct, $"Đang giải nén ({done}/{total} file)..."));
            }
        }

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

        public Task UninstallAsync(string gameFolder, IProgress<InstallProgress> progress, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var manifest = LoadManifest(gameFolder)
                    ?? throw new InvalidOperationException("Không tìm thấy dữ liệu bản cài đặt để gỡ Việt hóa.");
                if (!string.IsNullOrWhiteSpace(manifest.ModFolderRelativePath))
                    EnsureWithinRoot(gameFolder, manifest.ModFolderRelativePath, "manifest.json (ModFolderRelativePath)");

                if (manifest.InstallMode == GameInstallMode.CopyToModFolder.ToString())
                {
                    string modFolder = Path.Combine(gameFolder, manifest.ModFolderRelativePath);
                    int total = Math.Max(manifest.RelativeFiles.Count, 1);
                    int done = 0;

                    foreach (var relativePath in manifest.RelativeFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        EnsureWithinRoot(modFolder, relativePath, "manifest.json (RelativeFiles)");
                        string file = Path.Combine(modFolder, relativePath);
                        if (File.Exists(file)) File.Delete(file);

                        done++;
                        int pct = (int)((done / (double)total) * 90);
                        progress.Report(new InstallProgress(pct, $"Đang xóa file mod ({done}/{total})..."));
                    }

                    try
                    {
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
                    var backupFolder = GetBackupFolder(gameFolder);
                    int total = Math.Max(manifest.RelativeFiles.Count, 1);
                    int done = 0;

                    foreach (var relativePath in manifest.RelativeFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        EnsureWithinRoot(gameFolder, relativePath, "manifest.json (RelativeFiles)");
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

        private static string FormatBytes(double bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            while (bytes >= 1024 && order < sizes.Length - 1)
            {
                order++;
                bytes = bytes / 1024;
            }
            return $"{bytes:0.##} {sizes[order]}";
        }

        /// <summary>
        /// Lớp bọc ngoài DownloadWithResumeAndVerifyAsync: tự retry khi gặp lỗi mạng thoáng qua
        /// (mất kết nối giữa chừng, "unexpected EOF", timeout...). Vì DownloadWithResumeAndVerifyAsync
        /// giữ lại phần đã tải (existingBytes) khi thất bại, mỗi lần retry sẽ resume tiếp từ chỗ dở
        /// dang thay vì tải lại từ đầu — quan trọng khi chạy qua Wine/Proton hoặc mạng không ổn định.
        /// GHI CHÚ: trước khi port, hàm gốc KHÔNG có retry loop nào — 1 lần đứt mạng là fail thẳng.
        /// </summary>
        private async Task DownloadWithRetryAsync(string url, string destinationPath, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            for (int attempt = 1; attempt <= MaxDownloadRetries; attempt++)
            {
                try
                {
                    await DownloadWithResumeAndVerifyAsync(url, destinationPath, progress, ct);
                    return;
                }
                catch (Exception ex) when (
                    attempt < MaxDownloadRetries &&
                    (ex is IOException || ex is System.Net.Sockets.SocketException || ex is HttpRequestException))
                {
                    int delaySeconds = attempt * 2; // backoff tăng dần: 2s, 4s, 6s...
                    progress.Report(new InstallProgress(0,
                        $"Mất kết nối khi đang tải (lần {attempt}/{MaxDownloadRetries}). Thử lại sau {delaySeconds}s..."));
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                }
            }
        }

        private async Task DownloadWithResumeAndVerifyAsync(string url, string destinationPath, IProgress<InstallProgress> progress, CancellationToken ct)
        {
            EnsureSafeDownloadUrl(url);
            long existingBytes = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingBytes > 0)
                request.Headers.Range = new RangeHeaderValue(existingBytes, null);

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            // Server trả 416 (Range Not Satisfiable) khi file dở dang cũ (existingBytes) không còn khớp
            // với dung lượng thật trên server nữa — ví dụ file .zip trên GitHub đã bị thay bằng bản khác,
            // hoặc file cũ đã đủ/lớn hơn dung lượng mới. Trường hợp này: xóa file dở dang và tải lại từ đầu
            // thay vì để app crash.
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                response.Dispose();
                try { File.Delete(destinationPath); } catch { }
                existingBytes = 0;

                using var freshRequest = new HttpRequestMessage(HttpMethod.Get, url);
                response = await _http.SendAsync(freshRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            }

            using var _responseDisposer = response;

            bool serverSupportsResume = response.StatusCode == HttpStatusCode.PartialContent;
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

            // ===== BIẾN ĐO TỐC ĐỘ & UI =====
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long lastSpeedTickMs = 0;
            long lastSpeedBytes = totalRead;
            double currentSpeed = 0;

            // Cập nhật dung lượng mỗi 30ms (tạo cảm giác thời gian thực mà không giật lag UI)
            long lastUiTickMs = 0;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;

                long currentMs = stopwatch.ElapsedMilliseconds;

                // 1. TÍNH TỐC ĐỘ MẠNG RIÊNG (STRICT 100ms)
                if (currentMs - lastSpeedTickMs >= 100)
                {
                    double elapsedSec = (currentMs - lastSpeedTickMs) / 1000.0;
                    currentSpeed = elapsedSec > 0 ? (totalRead - lastSpeedBytes) / elapsedSec : 0;

                    lastSpeedTickMs = currentMs;
                    lastSpeedBytes = totalRead;
                }

                // 2. CẬP NHẬT DUNG LƯỢNG (CURRENT/MAX) THEO THỜI GIAN THỰC (30ms)
                if (currentMs - lastUiTickMs >= 20)
                {
                    lastUiTickMs = currentMs;

                    int downloadPercent = totalBytes > 0 ? (int)(totalRead * 100 / totalBytes) : 0;
                    int overall = (int)(downloadPercent * 0.7); // dải 0% -> 70%

                    string downloadedStr = FormatBytes(totalRead);
                    string totalStr = totalBytes > 0 ? FormatBytes(totalBytes) : "Unknown";
                    string speedStr = FormatBytes(currentSpeed);

                    progress.Report(new InstallProgress(overall, $"Đang tải: {downloadedStr} / {totalStr} • {speedStr}/s"));
                }
            }

            // Báo cáo lần cuối cho dung lượng chính xác tuyệt đối khi tải xong
            progress.Report(new InstallProgress(70, $"Đang tải: {FormatBytes(totalRead)} / {FormatBytes(totalBytes)} • {FormatBytes(currentSpeed)}/s"));

            fileStream.Close();

            if (!string.IsNullOrWhiteSpace(ExpectedHash))
            {
                progress.Report(new InstallProgress(70, "Đang xác thực file tải về..."));
                bool valid = await VerifyHashAsync(destinationPath);
                if (!valid)
                {
                    try { File.Delete(destinationPath); } catch { }
                    throw new IOException(
                                    "File patch tải về bị lỗi hoặc không toàn vẹn (sai hash). Vui lòng thử cài đặt lại.");
                }
            }
        }
        private static string NormalizeHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return "";
            string trimmed = hash.Trim();

            int colonIdx = trimmed.IndexOf(':');
            if (colonIdx >= 0 && colonIdx < trimmed.Length - 1)
                trimmed = trimmed[(colonIdx + 1)..];

            return trimmed.Trim();
        }

        private async Task<bool> VerifyHashAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(ExpectedHash))
                return true;

            using HashAlgorithm hasher = HashAlgorithmName.Equals("MD5", StringComparison.OrdinalIgnoreCase)
                ? MD5.Create()
                : SHA256.Create();

            await using var stream = File.OpenRead(filePath);
            byte[] hashBytes = await hasher.ComputeHashAsync(stream, CancellationToken.None);
            string actualHash = Convert.ToHexString(hashBytes);

            return NormalizeHash(actualHash).Equals(NormalizeHash(ExpectedHash), StringComparison.OrdinalIgnoreCase);
        }
    }
}