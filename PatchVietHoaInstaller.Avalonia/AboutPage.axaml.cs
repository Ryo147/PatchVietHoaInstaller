using Avalonia.Controls;
using Avalonia.Interactivity;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    public partial class AboutPage : UserControl
    {
        private static string AppVersion =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        public AboutPage()
        {
            InitializeComponent();
            TxtVersionGroup.Text = $"Phiên bản {AppVersion} · Nhóm Dịch 2000s";

            if (string.IsNullOrWhiteSpace(BuildAuthenticity.Token))
            {
                TxtBuildAuthenticity.Text = "KHÔNG XÁC MINH ĐƯỢC NGUỒN GỐC BẢN DỰNG.\nVui lòng chỉ tải file từ Dich2000s.vercel.app hoặc GitHub chính thức của nhóm.";
                TxtBuildAuthenticity.Foreground = Avalonia.Media.Brushes.OrangeRed;
            }
            else
            {
                // Ghi chú: đây là mã nội bộ (SHA-256 của BuildSecret.local.txt) để xác nhận bản build được
                // tạo bởi CI chính thức của nhóm — KHÔNG phải checksum của file cài đặt (GitHub cũng hiện
                // 1 mã SHA-256 khác cho từng file đính kèm trên trang Releases, dễ nhầm 2 mã này với
                // nhau). Muốn kiểm tra file cài đặt có nguyên vẹn không, so khớp mã đó trên trang
                // Releases, không phải mã hiện ở đây.
                TxtBuildAuthenticity.Text =
                    $"Mã xác thực bản dựng (SHA-256): {BuildAuthenticity.Token}\n" +
                    "(Mã nội bộ xác nhận bản dựng chính thức - không phải checksum của phần mềm được " +
                    "hiển thị trên trang GitHub Releases)";
            }
        }

        private void Link_Discord_Click(object? sender, RoutedEventArgs e)
            => PlatformHelper.OpenUrlInBrowser("https://discord.gg/snadgBATJm");

        private void Link_Facebook_Click(object? sender, RoutedEventArgs e)
            => PlatformHelper.OpenUrlInBrowser("https://www.facebook.com/dich2000s/");

        private void Link_GitHub_Click(object? sender, RoutedEventArgs e)
            => PlatformHelper.OpenUrlInBrowser("https://github.com/Ryo147/PatchVietHoaInstaller");

        private void Link_ReportBug_Click(object? sender, RoutedEventArgs e)
            => PlatformHelper.OpenUrlInBrowser("https://github.com/Ryo147/PatchVietHoaInstaller/issues/new");
    }
}