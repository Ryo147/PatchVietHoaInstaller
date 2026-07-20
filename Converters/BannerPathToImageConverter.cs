using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace VietHoaInstaller.Converters
{
    /// <summary>
    /// Chuyển "/Assets/xxx.jpg" (đường dẫn ảnh nhúng sẵn trong exe, khai báo trong GameProfile.BannerImagePath)
    /// thành BitmapImage thật để hiển thị trực tiếp trong Image/ImageBrush qua binding.
    /// Trả về null nếu thiếu đường dẫn hoặc ảnh lỗi — chỗ gọi tự hiện nền màu placeholder thay vì crash.
    /// </summary>
    public class BannerPathToImageConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string path || string.IsNullOrWhiteSpace(path))
                return null;

            try
            {
                string relativePart = path.TrimStart('/');
                var packUri = new Uri($"pack://application:,,,/{relativePart}", UriKind.Absolute);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = packUri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 120; // Chỉ cần thumbnail nhỏ, không cần decode ảnh gốc full-size
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
