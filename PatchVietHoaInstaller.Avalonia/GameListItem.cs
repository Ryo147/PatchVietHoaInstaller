using Avalonia.Media;
using VietHoaInstaller.Models;

namespace VietHoaInstaller
{
    /// <summary>
    /// View-model nhỏ chỉ để hiển thị trong LibraryPage, bọc quanh 1 GameProfile.
    /// GHI CHÚ PORT: bản WPF gốc khai báo class này là "private nested class" bên trong LibraryPage.
    /// Avalonia's compiled bindings (x:DataType) cần type có thể resolve rõ ràng qua XAML compiler,
    /// nên tách ra thành 1 class riêng (internal, cùng namespace) thay vì nested private — tránh rủi ro
    /// build với cú pháp nested-type "+" trong XAML.
    /// </summary>
    public class GameListItem
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
            : "Có thể cài đặt";

        public string BadgeText => Profile.IsComingSoon ? "Đang thực hiện" : "Hoàn thành";
        public IBrush StatusBrush => Profile.IsComingSoon ? ComingSoonBrush : AvailableBrush;
        public IBrush BadgeForeground => Profile.IsComingSoon ? ComingSoonBrush : AvailableBrush;
        public IBrush BadgeBackground => Profile.IsComingSoon ? ComingSoonBg : AvailableBg;
    }
}
