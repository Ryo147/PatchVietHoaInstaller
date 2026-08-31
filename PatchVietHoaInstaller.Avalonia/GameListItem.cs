using Avalonia.Media;
using VietHoaInstaller.Models;
using VietHoaInstaller.Services;

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
        private static readonly SolidColorBrush InstalledBrush = new(Color.FromRgb(0x37, 0x8A, 0xDD));
        private static readonly SolidColorBrush AvailableBg = new(Color.FromArgb(0x33, 0x3D, 0xDC, 0x97));
        private static readonly SolidColorBrush ComingSoonBg = new(Color.FromArgb(0x33, 0x8B, 0x90, 0xA3));
        private static readonly SolidColorBrush InstalledBg = new(Color.FromArgb(0x33, 0x37, 0x8A, 0xDD));

        /// <summary>
        /// <paramref name="rememberedFolder"/>: thư mục đã ghi nhớ cho riêng game này (AppSettings.GameFolders),
        /// hoặc null nếu chưa từng chọn thư mục. Dùng để xác định chính xác "đã cài" thay vì chỉ phân biệt
        /// "khả dụng"/"sắp ra mắt" như trước — kiểm tra qua manifest.json thật trong thư mục đó, không đoán.
        /// </summary>
        public GameListItem(GameProfile profile, string? rememberedFolder)
        {
            Profile = profile;

            IsInstalled = !string.IsNullOrWhiteSpace(rememberedFolder)
                && System.IO.Directory.Exists(rememberedFolder)
                && new PatchInstallerService { ProfileName = profile.Name }.IsInstalled(rememberedFolder);
        }

        public GameProfile Profile { get; }
        public string Name => Profile.Name;
        public string BannerImagePath => Profile.BannerImagePath;
        public bool CanInstall => !Profile.IsComingSoon;
        public bool IsInstalled { get; }

        public string StatusText => Profile.IsComingSoon
            ? "Bản Việt hóa đang được thực hiện"
            : IsInstalled
                ? "Đã cài đặt trên máy này"
                : "Có thể cài đặt";

        public string BadgeText => Profile.IsComingSoon
            ? "Đang thực hiện"
            : IsInstalled
                ? "Đã cài"
                : "Hoàn thành";

        public IBrush StatusBrush => Profile.IsComingSoon ? ComingSoonBrush : (IsInstalled ? InstalledBrush : AvailableBrush);
        public IBrush BadgeForeground => Profile.IsComingSoon ? ComingSoonBrush : (IsInstalled ? InstalledBrush : AvailableBrush);
        public IBrush BadgeBackground => Profile.IsComingSoon ? ComingSoonBg : (IsInstalled ? InstalledBg : AvailableBg);
    }
}
