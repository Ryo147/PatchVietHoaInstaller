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
    /// </summary>
    public class BannerPathToImageConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                var uri = new Uri(path, UriKind.Absolute);
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
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
