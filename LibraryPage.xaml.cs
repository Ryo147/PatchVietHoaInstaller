using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using VietHoaInstaller.Models;

namespace VietHoaInstaller
{
    /// <summary>
    /// Trang liệt kê toàn bộ game trong GameCatalog. Không tự dò "đã cài đặt hay chưa" ở đây vì
    /// việc đó phụ thuộc vào 1 thư mục cụ thể (chỉ biết được khi đã chọn game + thư mục ở Trang chủ) —
    /// tránh hiển thị sai trạng thái, trang này chỉ phân biệt "khả dụng" và "sắp ra mắt".
    /// </summary>
    public partial class LibraryPage : System.Windows.Controls.UserControl
    {
        public event Action<GameProfile>? InstallRequested;

        public LibraryPage()
        {
            InitializeComponent();

            var items = Models.GameCatalog.All
                .Select(profile => new GameListItem(profile))
                .ToList();

            GameList.ItemsSource = items;

            int available = items.Count(i => i.CanInstall);
            int comingSoon = items.Count - available;
            TxtProjectCount.Text = comingSoon > 0
                ? $"{items.Count} dự án — {available} khả dụng, {comingSoon} sắp ra mắt"
                : $"{items.Count} dự án khả dụng";
        }

        private void BtnInstallGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button { Tag: GameProfile profile })
                InstallRequested?.Invoke(profile);
        }

        /// <summary>View-model nhỏ chỉ để hiển thị, bọc quanh 1 GameProfile.</summary>
        private class GameListItem
        {
            private static readonly SolidColorBrush AvailableBrush = new(Color.FromRgb(0x3D, 0xDC, 0x97));
            private static readonly SolidColorBrush ComingSoonBrush = new(Color.FromRgb(0x8B, 0x90, 0xA3));
            private static readonly SolidColorBrush AvailableBg = new(Color.FromArgb(0x33, 0x3D, 0xDC, 0x97));
            private static readonly SolidColorBrush ComingSoonBg = new(Color.FromArgb(0x33, 0x8B, 0x90, 0xA3));

            public GameListItem(GameProfile profile)
            {
                Profile = profile;
            }

            public GameProfile Profile { get; }
            public string Name => Profile.Name;
            public string BannerImagePath => Profile.BannerImagePath;
            public bool CanInstall => !Profile.IsComingSoon;

            public string StatusText => Profile.IsComingSoon
                ? "Bản Việt hóa đang được thực hiện"
                : "Sẵn sàng cài đặt";

            public string BadgeText => Profile.IsComingSoon ? "Sắp ra mắt" : "Khả dụng";
            public Brush StatusBrush => Profile.IsComingSoon ? ComingSoonBrush : AvailableBrush;
            public Brush BadgeForeground => Profile.IsComingSoon ? ComingSoonBrush : AvailableBrush;
            public Brush BadgeBackground => Profile.IsComingSoon ? ComingSoonBg : AvailableBg;
        }
    }
}