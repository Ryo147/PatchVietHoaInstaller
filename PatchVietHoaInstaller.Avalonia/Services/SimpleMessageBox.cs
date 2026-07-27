using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;

namespace VietHoaInstaller.Services
{
    public enum SimpleMessageBoxButtons { Ok, YesNo }
    public enum SimpleMessageBoxResult { Ok, Yes, No }

    /// <summary>
    /// GHI CHÚ PORT: Avalonia không có sẵn System.Windows.MessageBox như WPF. Đây là bản thay thế tối
    /// giản (1 Window nhỏ, modal, giữa màn hình chủ) đủ dùng cho các hộp thoại xác nhận/báo lỗi/cảnh báo
    /// đơn giản đang dùng khắp các Page. Không cố gắng bắt chước 100% giao diện MessageBox của Windows —
    /// chỉ cần đúng hành vi (modal, trả về lựa chọn của người dùng) và chạy được trên mọi OS.
    /// </summary>
    public static class SimpleMessageBox
    {
        public static async Task<SimpleMessageBoxResult> ShowAsync(
            Window owner, string message, string title, SimpleMessageBoxButtons buttons = SimpleMessageBoxButtons.Ok)
        {
            var tcs = new TaskCompletionSource<SimpleMessageBoxResult>();

            var dialog = new Window
            {
                Title = title,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = owner.Resources.TryGetValue("BgDarkBrush", out var bg) ? (IBrush?)bg : Brushes.Black,
                SystemDecorations = SystemDecorations.Full
            };

            var messageText = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(20, 20, 20, 10)
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Avalonia.Thickness(0, 0, 16, 16),
                Spacing = 8
            };

            void CloseWith(SimpleMessageBoxResult result)
            {
                tcs.TrySetResult(result);
                dialog.Close();
            }

            if (buttons == SimpleMessageBoxButtons.YesNo)
            {
                var yesBtn = new Button { Content = "Có", Padding = new Avalonia.Thickness(16, 6) };
                yesBtn.Click += (_, _) => CloseWith(SimpleMessageBoxResult.Yes);

                var noBtn = new Button { Content = "Không", Padding = new Avalonia.Thickness(16, 6) };
                noBtn.Click += (_, _) => CloseWith(SimpleMessageBoxResult.No);

                buttonPanel.Children.Add(noBtn);
                buttonPanel.Children.Add(yesBtn);
            }
            else
            {
                var okBtn = new Button { Content = "OK", Padding = new Avalonia.Thickness(16, 6) };
                okBtn.Click += (_, _) => CloseWith(SimpleMessageBoxResult.Ok);
                buttonPanel.Children.Add(okBtn);
            }

            var layout = new DockPanel();
            DockPanel.SetDock(buttonPanel, Dock.Bottom);
            layout.Children.Add(buttonPanel);
            layout.Children.Add(messageText);

            dialog.Content = layout;

            // Nếu người dùng đóng bằng nút "X" của dialog thay vì bấm nút bên trong -> coi như "No"/"Ok" mặc định
            dialog.Closed += (_, _) => tcs.TrySetResult(
                buttons == SimpleMessageBoxButtons.YesNo ? SimpleMessageBoxResult.No : SimpleMessageBoxResult.Ok);

            await dialog.ShowDialog(owner);
            return await tcs.Task;
        }
    }
}
