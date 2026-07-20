using System.Windows.Navigation;

namespace VietHoaInstaller
{
    public partial class AboutPage : System.Windows.Controls.UserControl
    {
        private static string AppVersion =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        public AboutPage()
        {
            InitializeComponent();
            TxtVersionGroup.Text = $"Phiên bản {AppVersion} · Nhóm Dịch 2000s";
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
    }
}
