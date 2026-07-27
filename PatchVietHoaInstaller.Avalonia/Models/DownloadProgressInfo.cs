namespace PatchVietHoaInstaller.Models
{
    public class DownloadProgressInfo
    {
        public long TotalBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public double SpeedBytesPerSec { get; set; }

        public double ProgressPercentage => TotalBytes > 0 ? (DownloadedBytes * 100.0 / TotalBytes) : 0;
    }
}