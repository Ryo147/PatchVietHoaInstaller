# Assets/RawSource/

Thả ảnh banner GỐC vào đây (4K, 2K, tái chế từ ảnh truyền thông/marketing — bất kỳ độ phân giải
hay định dạng nào: png/jpg/jpeg/webp/bmp). Đặt tên file trùng với tên banner sẽ dùng trong
`GameProfile.cs`, ví dụ: `DMC5_VH.png`.

Mỗi lần build project chính (`dotnet build` hoặc build từ IDE), MSBuild Target
`OptimizeRawBanners` sẽ tự động:
1. Resize về tối đa 1920px chiều rộng (giữ tỉ lệ).
2. Flatten alpha về màu nền app (#0a0810).
3. Nén JPEG quality 88.
4. Xuất ra `../<TênBanner>.jpg` (tức `Assets/<TênBanner>.jpg`), rồi tự đăng ký làm
   AvaloniaResource ngay trong lần build đó.

Sau khi build xong, chỉ cần copy dòng sau vào `BannerImagePath` trong `GameProfile.cs`:

    avares://PatchVietHoaInstaller/Assets/<TênBanner>.jpg

Ảnh gốc trong thư mục này KHÔNG được nhúng vào app (loại trừ khỏi AvaloniaResource) — chỉ có bản
đã tối ưu trong `Assets/*.jpg` mới được nhúng. Vẫn nên commit ảnh gốc vào git để giữ nguồn chất
lượng cao, phòng khi cần tối ưu lại (đổi tỉ lệ nén, kích thước...) trong tương lai.