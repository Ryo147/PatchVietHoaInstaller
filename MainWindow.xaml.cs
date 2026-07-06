using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using VietHoaInstaller.Models;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    public partial class MainWindow : Window
    {
        private readonly PatchInstallerService _installer = new();
        private CancellationTokenSource? _cts;

        public MainWindow()
        {
            InitializeComponent();
            LoadLastGameFolder();
        }

        // ================= TITLE BAR =================
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ================= KHỞI ĐỘNG: TỰ ĐIỀN LẠI THƯ MỤC GAME LẦN TRƯỚC =================
        private void LoadLastGameFolder()
        {
            var settings = SettingsManager.Load();

            if (!string.IsNullOrWhiteSpace(settings.LastGameFolder) && Directory.Exists(settings.LastGameFolder))
            {
                TxtGamePath.Text = settings.LastGameFolder;
                RefreshStatusForFolder(settings.LastGameFolder, showErrorDialog: false);
            }
            else
            {
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = false;
            }
        }

        // ================= CHỌN THƯ MỤC GAME =================
        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Chọn thư mục cài đặt game",
                Multiselect = false
            };

            if (Directory.Exists(TxtGamePath.Text))
                dialog.InitialDirectory = TxtGamePath.Text;

            if (dialog.ShowDialog(this) == true)
            {
                TxtGamePath.Text = dialog.FolderName;

                var settings = SettingsManager.Load();
                settings.LastGameFolder = dialog.FolderName;
                SettingsManager.Save(settings);

                RefreshStatusForFolder(dialog.FolderName, showErrorDialog: true);
            }
        }

        /// <summary>Cập nhật trạng thái + bật/tắt nút dựa trên thư mục game hiện tại.</summary>
        private void RefreshStatusForFolder(string gameFolder, bool showErrorDialog = false)
        {
            // 1) Đã cài Việt hóa trước đó chưa?
            if (_installer.IsInstalled(gameFolder))
            {
                SetStatus("Đã cài đặt Việt hóa", "#3DDC97");
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = true;
                return;
            }

            // 2) Có đúng là thư mục game không?
            var check = _installer.ValidateGameFolder(gameFolder);
            if (!check.IsValid)
            {
                SetStatus("Sai thư mục game", "#FF5A4A");
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = false;

                if (showErrorDialog)
                {
                    MessageBox.Show(check.Message, "Thư mục không hợp lệ",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            // 3) Hợp lệ và chưa cài -> sẵn sàng cài đặt
            SetStatus("Chưa cài đặt Việt hóa", "#FFB454");
            BtnInstall.IsEnabled = true;
            BtnUninstall.IsEnabled = false;
        }
        // ================= CÀI ĐẶT PATCH (THẬT) =================
        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            string gameFolder = TxtGamePath.Text.Trim();

            if (!Directory.Exists(gameFolder))
            {
                MessageBox.Show("Vui lòng chọn thư mục game hợp lệ trước.", "Thiếu thông tin",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_installer.IsInstalled(gameFolder))
            {
                MessageBox.Show("Thư mục này đã được cài Việt hóa trước đó.\nVui lòng gỡ Việt hóa trước nếu muốn cài lại.",
                    "Đã cài đặt", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshStatusForFolder(gameFolder);
                return;
            }

            var folderCheck = _installer.ValidateGameFolder(gameFolder);
            if (!folderCheck.IsValid)
            {
                MessageBox.Show(folderCheck.Message, "Thư mục không hợp lệ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshStatusForFolder(gameFolder);
                return;
            }
            SetBusyState(true);
            PanelProgress.Visibility = Visibility.Visible;
            SetStatus("Đang cài đặt Việt hóa...", "#FFB454");

            _cts = new CancellationTokenSource();
            var progress = new Progress<InstallProgress>(p =>
            {
                ProgressInstall.Value = p.Percent;
                TxtPercent.Text = $"{p.Percent}%";
            });

            try
            {
                await _installer.InstallAsync(gameFolder, progress, _cts.Token);

                SetStatus("Đã cài đặt Việt hóa", "#3DDC97");
                BtnUninstall.IsEnabled = true;
                BtnInstall.IsEnabled = false;

                // Hiện "Hoàn tất" trong chốc lát rồi tự ẩn thanh tiến trình
                await Task.Delay(800);
                PanelProgress.Visibility = Visibility.Collapsed;
                ProgressInstall.Value = 0;
                TxtPercent.Text = "0%";
            }
            catch (OperationCanceledException)
            {
                SetStatus("Đã hủy cài đặt", "#FFB454");
            }
            catch (Exception ex)
            {
                SetStatus("Cài đặt thất bại", "#FF5A4A");
                MessageBox.Show(
                    $"Không thể cài đặt Việt hóa:\n\n{ex.Message}",
                    "Lỗi cài đặt", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusyState(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ================= GỠ VIỆT HÓA (THẬT) =================
        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            string gameFolder = TxtGamePath.Text.Trim();

            var confirm = MessageBox.Show(
                "Bạn có chắc muốn gỡ bản Việt hóa và khôi phục file gốc?",
                "Xác nhận gỡ Việt hóa",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
                return;

            SetBusyState(true);
            PanelProgress.Visibility = Visibility.Visible;
            SetStatus("Đang gỡ Việt hóa...", "#FFB454");

            _cts = new CancellationTokenSource();
            var progress = new Progress<InstallProgress>(p =>
            {
                ProgressInstall.Value = p.Percent;
                TxtPercent.Text = $"{p.Percent}%";
            });

            try
            {
                await _installer.UninstallAsync(gameFolder, progress, _cts.Token);

                SetStatus("Chưa cài đặt Việt hóa", "#FFB454");
                BtnInstall.IsEnabled = true;
                BtnUninstall.IsEnabled = false;
                ProgressInstall.Value = 0;
                TxtPercent.Text = "0%";
                PanelProgress.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                SetStatus("Gỡ Việt hóa thất bại", "#FF5A4A");
                MessageBox.Show(
                    $"Không thể gỡ Việt hóa:\n\n{ex.Message}",
                    "Lỗi gỡ Việt hóa", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusyState(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ================= HELPER =================

        /// <summary>Khóa các nút thao tác trong lúc đang cài/gỡ để tránh bấm chồng lệnh.</summary>
        private void SetBusyState(bool isBusy)
        {
            BtnBrowse.IsEnabled = !isBusy;

            if (isBusy)
            {
                // Khi bắt đầu chạy, luôn tắt cả 2 nút hành động để tránh bấm chồng
                BtnInstall.IsEnabled = false;
                BtnUninstall.IsEnabled = false;
            }
        }

        private void SetStatus(string text, string hexColor)
        {
            TxtStatus.Text = text;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
            TxtStatus.Foreground = brush;
            StatusDot.Fill = brush;
        }

        private void TxtGamePath_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {

        }
    }
}
