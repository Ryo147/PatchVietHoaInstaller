using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace VietHoaInstaller.Services
{
    public enum SimpleMessageBoxButtons { Ok, YesNo }
    public enum SimpleMessageBoxResult { Ok, Yes, No }

    /// <summary>
    /// GHI CHÚ PORT: Avalonia không có sẵn System.Windows.MessageBox như WPF. Đây là bản thay thế tối
    /// giản (1 Window nhỏ, modal, giữa màn hình chủ) đủ dùng cho các hộp thoại xác nhận/báo lỗi/cảnh báo
    /// đơn giản đang dùng khắp các Page.
    ///
    /// GHI CHÚ GIAO DIỆN: dùng custom chrome (SystemDecorations="None" + title bar/nút tự vẽ) giống hệt
    /// MainWindow thay vì để Windows tự vẽ title bar mặc định — trước đây SystemDecorations="Full" khiến
    /// hộp thoại hiện ra với thanh tiêu đề xám, icon mặc định, nút OK/Yes/No vuông vức không hover, lệch
    /// hẳn tông màu tối của cả app. 2 nút dùng lại Classes="PrimaryButton"/"SecondaryButton" đã có sẵn
    /// hover + bo góc từ App.axaml, không tự vẽ style riêng để tránh lệch pha với phần còn lại của app.
    /// </summary>
    public static class SimpleMessageBox
    {
        /// <param name="emphasizeCancel">
        /// Dùng cho xác nhận hành động PHÁ HỦY/KHÔNG THỂ HOÀN TÁC (gỡ Việt hóa, xóa file...). Khi bật:
        /// đảo cả vị trí lẫn kiểu nút — "Không" (an toàn) chuyển sang vị trí phải + style PrimaryButton
        /// (vị trí/màu người dùng quen bấm theo phản xạ/Enter), "Có" (phá hủy) chuyển sang trái + style
        /// SecondaryButton nhạt hơn. Mục đích: phá vỡ phản xạ "bấm nút bên phải" để buộc người dùng phải
        /// dừng lại đọc kỹ trước khi chọn "Có" — không đặt hành động phá hủy vào đúng vị trí/màu mà tay
        /// quen bấm nhanh.
        /// </param>
        /// <param name="confirmCooldownSeconds">
        /// Nút xác nhận ("Có"/"OK") bị khóa (disable) trong N giây đầu, hiện đếm ngược ngay trên chữ
        /// ("Có (5)" -> "Có (4)" -> ... -> "Có"), chỉ bấm được sau khi đếm về 0. Dùng cho xác nhận hành
        /// động PHÁ HỦY/KHÔNG THỂ HOÀN TÁC — ép người dùng phải chờ đủ thời gian đọc kỹ nội dung, không
        /// thể bấm ngay theo phản xạ dù đã đảo vị trí/màu nút (emphasizeCancel). 0 = không cooldown.
        /// </param>
        public static async Task<SimpleMessageBoxResult> ShowAsync(
            Window owner, string message, string title, SimpleMessageBoxButtons buttons = SimpleMessageBoxButtons.Ok,
            bool emphasizeCancel = false, int confirmCooldownSeconds = 0)
        {
            var tcs = new TaskCompletionSource<SimpleMessageBoxResult>();

            // GHI CHÚ: các brush/geometry này khai báo ở Application.Resources (App.axaml), KHÔNG phải
            // Window.Resources riêng của owner -> phải tra qua Application.Current, không phải
            // owner.Resources (bug cũ: owner.Resources.TryGetValue luôn fail âm thầm, rơi về màu đen
            // mặc định — trùng hợp gần giống tông tối của app nên không ai để ý).
            var appRes = Avalonia.Application.Current?.Resources;
            IBrush bgBrush = appRes?.TryGetValue("BgDarkBrush", out var bg) == true ? (IBrush?)bg ?? Brushes.Black : Brushes.Black;
            IBrush borderBrush = appRes?.TryGetValue("BorderBrush1", out var bd) == true ? (IBrush?)bd ?? Brushes.Gray : Brushes.Gray;
            IBrush textBrush = appRes?.TryGetValue("TextPrimaryBrush", out var tx) == true ? (IBrush?)tx ?? Brushes.White : Brushes.White;
            Geometry? closeIconGeometry = appRes?.TryGetValue("IconClose", out var ic) == true ? ic as Geometry : null;

            var dialog = new Window
            {
                Title = title,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.Transparent,
                SystemDecorations = SystemDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            };

            void CloseWith(SimpleMessageBoxResult result)
            {
                tcs.TrySetResult(result);
                dialog.Close();
            }

            // Áp cooldown lên nút xác nhận: disable, hiện đếm ngược trên chữ, tự enable + trả lại chữ gốc
            // khi về 0. Trả về DispatcherTimer để dialog.Closed có thể Stop() nếu người dùng đóng dialog
            // (Alt+F4/nút X) trước khi đếm xong -> tránh timer chạy ngầm sau khi dialog đã đóng.
            DispatcherTimer? StartConfirmCooldown(Button confirmBtn, string baseText, int seconds)
            {
                if (seconds <= 0) return null;

                confirmBtn.IsEnabled = false;
                int remaining = seconds;
                confirmBtn.Content = $"{baseText} ({remaining})";

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                timer.Tick += (_, _) =>
                {
                    remaining--;
                    if (remaining <= 0)
                    {
                        timer.Stop();
                        confirmBtn.Content = baseText;
                        confirmBtn.IsEnabled = true;
                    }
                    else
                    {
                        confirmBtn.Content = $"{baseText} ({remaining})";
                    }
                };
                timer.Start();
                return timer;
            }

            DispatcherTimer? cooldownTimer = null;

            // ===== Title bar tự vẽ (khớp MainWindow: kéo di chuyển + nút đóng riêng) =====
            var titleText = new TextBlock
            {
                Text = title,
                Foreground = textBrush,
                FontSize = 13,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0),
            };

            var closeBtn = new Button { Classes = { "TitleBarButton", "CloseButton" } };
            if (closeIconGeometry is not null)
            {
                closeBtn.Content = new Avalonia.Controls.Shapes.Path
                {
                    Classes = { "TitleBarIcon" },
                    Data = closeIconGeometry,
                    Width = 11,
                    Height = 11,
                    Stretch = Stretch.Uniform,
                };
            }
            else
            {
                // Không tìm thấy resource icon (không nên xảy ra) -> vẫn có chữ "X" để không mất nút đóng.
                closeBtn.Content = new TextBlock { Text = "✕", FontSize = 12, Foreground = textBrush };
            }
            closeBtn.Click += (_, _) => CloseWith(buttons == SimpleMessageBoxButtons.YesNo ? SimpleMessageBoxResult.No : SimpleMessageBoxResult.Ok);

            var titleBar = new Grid
            {
                Height = 40,
                Background = new SolidColorBrush(Color.Parse("#161A24")),
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            };
            titleBar.Children.Add(titleText);
            Grid.SetColumn(closeBtn, 1);
            titleBar.Children.Add(closeBtn);
            titleBar.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(dialog).Properties.IsLeftButtonPressed)
                    dialog.BeginMoveDrag(e);
            };

            // ===== Nội dung =====
            var messageText = new TextBlock
            {
                Text = message,
                Foreground = textBrush,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Thickness(20, 20, 20, 10),
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 16, 16),
                Spacing = 8,
            };

            if (buttons == SimpleMessageBoxButtons.YesNo)
            {
                var noBtn = new Button
                {
                    Content = "Không",
                    Width = 100,
                    Classes = { emphasizeCancel ? "PrimaryButton" : "SecondaryButton" },
                };
                noBtn.Click += (_, _) => CloseWith(SimpleMessageBoxResult.No);

                var yesBtn = new Button
                {
                    Content = "Có",
                    Width = 100,
                    Classes = { emphasizeCancel ? "SecondaryButton" : "PrimaryButton" },
                };
                yesBtn.Click += (_, _) => CloseWith(SimpleMessageBoxResult.Yes);

                // emphasizeCancel: đảo vị trí -> "Có" (phá hủy) sang trái, "Không" (an toàn) sang phải,
                // phá vỡ đúng chỗ tay quen bấm nhanh/Enter.
                if (emphasizeCancel)
                {
                    buttonPanel.Children.Add(yesBtn);
                    buttonPanel.Children.Add(noBtn);
                }
                else
                {
                    buttonPanel.Children.Add(noBtn);
                    buttonPanel.Children.Add(yesBtn);
                }

                cooldownTimer = StartConfirmCooldown(yesBtn, "Có", confirmCooldownSeconds);
            }
            else
            {
                var okBtn = new Button { Content = "OK", Width = 100, Classes = { "PrimaryButton" } };
                okBtn.Click += (_, _) => CloseWith(SimpleMessageBoxResult.Ok);
                buttonPanel.Children.Add(okBtn);

                cooldownTimer = StartConfirmCooldown(okBtn, "OK", confirmCooldownSeconds);
            }

            var contentPanel = new DockPanel();
            DockPanel.SetDock(buttonPanel, Dock.Bottom);
            contentPanel.Children.Add(buttonPanel);
            contentPanel.Children.Add(messageText);

            var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            body.Children.Add(titleBar);
            Grid.SetRow(contentPanel, 1);
            body.Children.Add(contentPanel);

            // Border ngoài bo góc + đổ bóng, khớp hệt MainWindow (Margin để chừa chỗ cho bóng đổ).
            var outerBorder = new Border
            {
                Margin = new Thickness(10),
                Background = bgBrush,
                CornerRadius = new CornerRadius(10),
                BoxShadow = BoxShadows.Parse("0 0 25 0 #99000000"),
            };
            var clipBorder = new Border
            {
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                ClipToBounds = true,
                Child = body,
            };
            outerBorder.Child = clipBorder;
            dialog.Content = outerBorder;

            // Nếu người dùng đóng bằng phím Alt+F4 thay vì bấm nút bên trong -> coi như "No"/"Ok" mặc định.
            // Đồng thời dừng timer cooldown nếu còn đang chạy (đóng dialog giữa chừng) -> tránh timer chạy
            // ngầm/leak sau khi dialog đã đóng.
            dialog.Closed += (_, _) =>
            {
                cooldownTimer?.Stop();
                tcs.TrySetResult(buttons == SimpleMessageBoxButtons.YesNo ? SimpleMessageBoxResult.No : SimpleMessageBoxResult.Ok);
            };

            await dialog.ShowDialog(owner);
            return await tcs.Task;
        }
    }
}