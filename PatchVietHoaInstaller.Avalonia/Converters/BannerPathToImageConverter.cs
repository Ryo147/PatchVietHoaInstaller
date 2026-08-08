using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace VietHoaInstaller.Converters
{
    /// <summary>
    /// Chuyển "avares://VietHoaInstaller.Avalonia/Assets/xxx.jpg" (đường dẫn ảnh nhúng sẵn trong assembly,
    /// khai báo trong GameProfile.BannerImagePath) thành Bitmap thật để hiển thị trực tiếp trong Image
    /// qua binding. Trả về null nếu thiếu đường dẫn hoặc ảnh lỗi — chỗ gọi tự hiện nền màu placeholder
    /// thay vì crash.
    ///
    /// GHI CHÚ PORT: bản WPF gốc dùng BitmapImage + Uri "pack://application:,,,/...". Avalonia không có
    /// pack URI — thay bằng "avares://{TênAssembly}/..." và load qua AssetLoader.Open(), theo đúng khuyến
    /// nghị chính thức của Avalonia cho resource nhúng sẵn (AvaloniaResource trong .csproj).
    ///
    /// GHI CHÚ HIỆU NĂNG: converter này chỉ dùng cho ảnh bìa NHỎ (52x52 trong LibraryPage) nên decode
    /// thẳng xuống DecodeTargetWidth thay vì "new Bitmap(stream)" decode nguyên độ phân giải gốc rồi mới
    /// co lại lúc render. Nếu ảnh nguồn là 2K/4K, decode full-res tốn CPU/RAM rất nhiều lần không cần
    /// thiết cho 1 ô 52px — đây chính là nguyên nhân gây giật khi cuộn/hiển thị danh sách game.
    /// </summary>
    public class BannerPathToImageConverter : IValueConverter
    {
        // 52px logic x 3 (đủ bù cho màn hình scale tới 300%) — dư dả cho ảnh bìa nhỏ trong LibraryPage.
        private const int DecodeTargetWidth = 160;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                var uri = new Uri(path, UriKind.Absolute);
                using var stream = AssetLoader.Open(uri);
                return Bitmap.DecodeToWidth(stream, DecodeTargetWidth);
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}