using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VietHoaInstaller.Models;

namespace VietHoaInstaller.Services
{
    public class DownloadService
    {
        public async Task DownloadFileWithSpeedAsync(string url, string outputFilePath, IProgress<DownloadProgressInfo> progress, CancellationToken cancellationToken = default)
        {
            using (var client = new HttpClient())
            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                long totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                using (var fileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
                {
                    var buffer = new byte[8192];
                    int bytesRead;
                    long totalDownloaded = 0;

                    var stopwatch = Stopwatch.StartNew();
                    long lastDownloadedBytes = 0;
                    long lastTickMs = 0;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                        totalDownloaded += bytesRead;

                        if (stopwatch.ElapsedMilliseconds - lastTickMs >= 500)
                        {
                            double elapsedSeconds = (stopwatch.ElapsedMilliseconds - lastTickMs) / 1000.0;
                            double speed = (totalDownloaded - lastDownloadedBytes) / elapsedSeconds;

                            progress?.Report(new DownloadProgressInfo
                            {
                                TotalBytes = totalBytes,
                                DownloadedBytes = totalDownloaded,
                                SpeedBytesPerSec = speed
                            });

                            lastDownloadedBytes = totalDownloaded;
                            lastTickMs = stopwatch.ElapsedMilliseconds;
                        }
                    }

                    progress?.Report(new DownloadProgressInfo
                    {
                        TotalBytes = totalBytes,
                        DownloadedBytes = totalDownloaded,
                        SpeedBytesPerSec = 0
                    });
                }
            }
        }

        // Hàm hỗ trợ định dạng Byte thành KB, MB
        public static string FormatBytes(double bytes)
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
    }
}