using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    public partial class SettingsPage : UserControl
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

        private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

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
            TxtRememberedFolder.Text = settings.GameFolders.Count == 0
                ? "(chưa có)"
                : $"{settings.GameFolders.Count} game đã ghi nhớ thư mục";

            _isLoading = false;
        }

        // GHI CHÚ PORT: Avalonia's ToggleSwitch (thay CheckBox+ToggleSwitchStyle của bản WPF gốc) chỉ có
        // 1 sự kiện IsCheckedChanged, không tách Checked/Unchecked riêng như CheckBox của WPF -> gộp 2
        // handler cũ (vốn đã trỏ chung 1 hàm) thành 1, hành vi giữ nguyên y hệt.

        private void ChkAutoUpdate_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var settings = SettingsManager.Load();
            settings.AutoCheckUpdate = ChkAutoUpdate.IsChecked == true;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");
        }

        private void ChkConfirmUninstall_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var settings = SettingsManager.Load();
            settings.ConfirmBeforeUninstall = ChkConfirmUninstall.IsChecked == true;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");
        }

        private void ChkAlwaysOnTop_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            bool isOn = ChkAlwaysOnTop.IsChecked == true;
            var settings = SettingsManager.Load();
            settings.AlwaysOnTop = isOn;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");

            AlwaysOnTopChanged?.Invoke(isOn);
        }

        private void ChkAutoOpenFolder_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var settings = SettingsManager.Load();
            settings.AutoOpenFolderAfterInstall = ChkAutoOpenFolder.IsChecked == true;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");
        }

        private void ChkMinimizeToTray_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var settings = SettingsManager.Load();
            settings.MinimizeToTrayOnClose = ChkMinimizeToTray.IsChecked == true;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");
            TraySettingsChanged?.Invoke();
        }

        private void ChkAutoCheckPatch_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

            var settings = SettingsManager.Load();
            settings.AutoCheckPatchUpdate = ChkAutoCheckPatch.IsChecked == true;
            SettingsManager.Save(settings);
            ToastRequested?.Invoke("Đã lưu cài đặt");
            TraySettingsChanged?.Invoke();
        }

        private void TxtPatchCheckInterval_LostFocus(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;

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

        private async void BtnClearFolder_Click(object? sender, RoutedEventArgs e)
        {
            var settings = SettingsManager.Load();

            if (settings.GameFolders.Count == 0)
                return; // Không có gì để xóa — không cần làm phiền bằng dialog.

            if (OwnerWindow is not { } owner) return;

            var confirm = await SimpleMessageBox.ShowAsync(owner,
                $"Xóa thư mục đã ghi nhớ cho TẤT CẢ {settings.GameFolders.Count} game? " +
                "Lần cài/gỡ tiếp theo cho mỗi game sẽ phải chọn lại thư mục từ đầu (không xóa file game hay bản Việt hóa đã cài).",
                "Xác nhận xóa thư mục đã ghi nhớ",
                SimpleMessageBoxButtons.YesNo,
                emphasizeCancel: true,
                confirmCooldownSeconds: 3);

            if (confirm != SimpleMessageBoxResult.Yes)
                return;

            settings.GameFolders.Clear();
            SettingsManager.Save(settings);
            TxtRememberedFolder.Text = "(chưa có)";
            ToastRequested?.Invoke("Đã xóa thư mục đã ghi nhớ cho tất cả game");
        }

        private async void BtnClearLog_Click(object? sender, RoutedEventArgs e)
        {
            if (OwnerWindow is not { } owner) return;

            var confirm = await SimpleMessageBox.ShowAsync(owner,
                "Xóa toàn bộ nhật ký hoạt động ở Trang chủ?",
                "Xác nhận", SimpleMessageBoxButtons.YesNo);

            if (confirm == SimpleMessageBoxResult.Yes)
            {
                ClearActivityLogRequested?.Invoke();
                ToastRequested?.Invoke("Đã xóa nhật ký hoạt động");
            }
        }
    }
}