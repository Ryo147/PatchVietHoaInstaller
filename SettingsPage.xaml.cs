using System;
using System.Windows;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    public partial class SettingsPage : System.Windows.Controls.UserControl
    {
        private bool _isLoading;

        /// <summary>MainWindow lắng nghe để bật/tắt Topmost ngay lập tức, không cần khởi động lại app.</summary>
        public event Action<bool>? AlwaysOnTopChanged;

        /// <summary>MainWindow lắng nghe để xóa nhật ký hoạt động ở Trang chủ.</summary>
        public event Action? ClearActivityLogRequested;

        /// <summary>MainWindow lắng nghe để hiện thông báo nhỏ (toast) xác nhận đã lưu.</summary>
        public event Action<string>? ToastRequested;

        /// <summary>MainWindow lắng nghe để khởi động lại timer kiểm tra Patch ngay khi đổi cài đặt liên quan tới tray.</summary>
        public event Action? TraySettingsChanged;

        public SettingsPage()
        {
            InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {
            _isLoading = true;

            var settings = SettingsManager.Load();
            ChkAutoUpdate.IsChecked = settings.AutoCheckUpdate;
            ChkConfirmUninstall.IsChecked = settings.ConfirmBeforeUninstall;
            ChkAlwaysOnTop.IsChecked = settings.AlwaysOnTop;
            ChkAutoOpenFolder.IsChecked = settings.AutoOpenFolderAfterInstall;
            ChkMinimizeToTray.IsChecked = settings.MinimizeToTrayOnClose;
            ChkAutoCheckPatch.IsChecked = settings.AutoCheckPatchUpdate;
            TxtPatchCheckInterval.Text = Math.Max(15, settings.PatchCheckIntervalMinutes).ToString();
            TxtRememberedFolder.Text = string.IsNullOrWhiteSpace(settings.LastGameFolder)
                ? "(chưa có)"
                : settings.LastGameFolder;

            _isLoading = false;
        }

        private void ChkAutoUpdate_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            var settings = SettingsManager.Load();
            settings.AutoCheckUpdate = ChkAutoUpdate.IsChecked == true;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");
        }

        private void ChkConfirmUninstall_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            var settings = SettingsManager.Load();
            settings.ConfirmBeforeUninstall = ChkConfirmUninstall.IsChecked == true;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");
        }

        private void ChkAlwaysOnTop_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            bool isOn = ChkAlwaysOnTop.IsChecked == true;
            var settings = SettingsManager.Load();
            settings.AlwaysOnTop = isOn;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");

            AlwaysOnTopChanged?.Invoke(isOn);
        }

        private void ChkAutoOpenFolder_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            var settings = SettingsManager.Load();
            settings.AutoOpenFolderAfterInstall = ChkAutoOpenFolder.IsChecked == true;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");
        }

        private void ChkMinimizeToTray_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            var settings = SettingsManager.Load();
            settings.MinimizeToTrayOnClose = ChkMinimizeToTray.IsChecked == true;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");
            TraySettingsChanged?.Invoke();
        }

        private void ChkAutoCheckPatch_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            var settings = SettingsManager.Load();
            settings.AutoCheckPatchUpdate = ChkAutoCheckPatch.IsChecked == true;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");
            TraySettingsChanged?.Invoke();
        }

        private void TxtPatchCheckInterval_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isLoading)
                return;

            if (!int.TryParse(TxtPatchCheckInterval.Text, out int minutes) || minutes < 15)
            {
                minutes = 15;
            }
            TxtPatchCheckInterval.Text = minutes.ToString();

            var settings = SettingsManager.Load();
            settings.PatchCheckIntervalMinutes = minutes;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");
            TraySettingsChanged?.Invoke();
        }

        private void BtnClearFolder_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsManager.Load();
            settings.LastGameFolder = "";
            SettingsManager.Save(settings);
            TxtRememberedFolder.Text = "(chưa có)";
            ToastRequested?.Invoke("Đã xóa thư mục đã ghi nhớ");
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Xóa toàn bộ nhật ký hoạt động ở Trang chủ?",
                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm == MessageBoxResult.Yes)
            {
                ClearActivityLogRequested?.Invoke();
                ToastRequested?.Invoke("Đã xóa nhật ký hoạt động");
            }
        }
    }
}