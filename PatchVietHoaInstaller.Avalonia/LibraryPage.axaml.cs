using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Linq;
using VietHoaInstaller.Models;

namespace VietHoaInstaller
{
    /// <summary>
    /// Trang liệt kê toàn bộ game trong GameCatalog. Không tự dò "đã cài đặt hay chưa" ở đây vì
    /// việc đó phụ thuộc vào 1 thư mục cụ thể (chỉ biết được khi đã chọn game + thư mục ở Trang chủ) —
    /// tránh hiển thị sai trạng thái, trang này chỉ phân biệt "khả dụng" và "sắp ra mắt".
    /// </summary>
    public partial class LibraryPage : UserControl
    {
        public event Action<GameProfile>? InstallRequested;

        public LibraryPage()
        {
            InitializeComponent();

            var items = GameCatalog.All
                .Select(profile => new GameListItem(profile))
                .ToList();

            GameList.ItemsSource = items;

            int available = items.Count(i => i.CanInstall);
            int comingSoon = items.Count - available;
            TxtProjectCount.Text = comingSoon > 0
                ? $"{items.Count} dự án — {available} hoàn thành, {comingSoon} đang thực hiện"
                : $"{items.Count} dự án hoàn thành";
        }

        private void BtnInstallGame_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: GameProfile profile })
                InstallRequested?.Invoke(profile);
        }
    }
}
