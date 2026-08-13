# PatchVietHoaInstaller

Phần mềm cài đặt Patch Việt Hóa dành cho nhóm Dịch Những Năm 2000 (Dịch 2000s). Hỗ trợ cài đặt tự động, sao lưu file gốc và kiểm tra cập nhật cho các bản Việt hóa game do nhóm hoàn thành.

> **Lưu ý quan trọng:** Phần mềm này hoàn toàn miễn phí. Mọi chi phí phải bỏ ra để lấy phần mềm này đều là lừa đảo. Phần mềm được viết ra để phục vụ cộng đồng game thủ mong muốn chơi các bản Việt hóa chất lượng và không cồng kềnh khi cài đặt.

**Develop by Claude / Edit by Nhựa Inox (Ryo147)**


## Tính năng chính

- **Tự tìm thư mục cài đặt game** – Phần mềm tự động quét Registry và các đường dẫn phổ biến (Steam, thư mục cài đặt thủ công) để định vị game đã cài trên máy.
- **Cài đặt 1 click** – Chỉ cần chọn game và nhấn cài đặt, phần mềm sẽ tự động tải và áp dụng bản Việt hóa cho các game đã được nhóm hỗ trợ.
- **Sao lưu (Backup) file gốc** – Tự động backup các file bị ghi đè khi cài patch, giúp bạn dễ dàng khôi phục lại bản gốc khi cần gỡ bản Việt hóa.
- **Tối ưu dung lượng** – Sử dụng các phần mềm bên ngoài như Fluffy Mod Manager để áp dụng patch một cách tối ưu, giảm thiểu dung lượng tải về.
- **Kiểm tra cập nhật tự động** – Chạy nền dưới khay hệ thống (system tray) để kiểm tra xem có bản patch mới cho game nào không.
- **Hiển thị phiên bản game tương thích** – Cho biết rõ game của bạn có tương thích với bản patch hiện tại hay không trước khi cài đặt.
- **Hỗ trợ đa nền tảng** – Bắt đầu từ v4.0.0, phần mềm hỗ trợ cả Windows và Linux.
- **Giao diện dễ sử dụng** – Thiết kế trực quan với các tab Dự án, Thư viện, Cài đặt và Cập nhật.


## Hướng dẫn sử dụng

### Yêu cầu hệ thống

| Nền tảng | Yêu cầu tối thiểu |
|---|---|
| **Windows** | Windows 10 trở lên, .NET Runtime tương thích |
| **Linux** | Bất kỳ bản phân phối nào hỗ trợ .NET (Ubuntu, Fedora, Arch, ...). *Lưu ý: Bản Linux có thể còn lỗi do thiếu môi trường thử nghiệm.* |

### Cài đặt

1. Tải file cài đặt phù hợp với HĐH của bạn từ trang Releases (https://github.com/Ryo147/PatchVietHoaInstaller/releases).
   - **Windows:** `PatchVietHoaInstaller.exe`
   - **Linux:** `PatchVietHoaInstaller-linux.zip`
2. Chạy file thực thi (trên Linux có thể cần cấp quyền execute: `chmod +x PatchVietHoaInstaller`).
3. Chọn game bạn muốn cài đặt bản Việt hóa từ danh sách.
4. Nhấn **Cài đặt** và chờ quá trình hoàn tất.

### Gỡ bản Việt hóa

Phần mềm hỗ trợ sao lưu file gốc trong quá trình cài patch. Bạn có thể dùng tính năng khôi phục để gỡ bản Việt hóa và trả lại trạng thái ban đầu của game.


## Độ an toàn

Do bộ cài có cơ chế can thiệp ghi đè file và truy cập Registry để tìm đường dẫn game, một số trình Antivirus có thể sẽ cảnh báo nhầm (False Positive).

Xem báo cáo chi tiết trên VirusTotal: [Windows](https://www.virustotal.com/gui/file/bf19593291b93b3ef76733c48d8b86f9443b3de5b81034d322d9af06a6572d27) | [Linux](https://www.virustotal.com/gui/file/b3b1c02617bdc0087f834a76876624154b3e0aa01c165699838b35ebf0f2564c)


## Phiên bản gần đây

### 4.2.0.1 (2026-08-04)

- Cập nhật danh sách phiên bản game/app được hỗ trợ.
- Chuẩn hóa chuỗi phiên bản ứng dụng sang định dạng 4 phần (x.y.z.w).

### 4.2.0 (2026-07-28)

- Cải thiện tính năng Kiểm tra phiên bản PatchVH.
- Sửa lỗi tải Patch sai hash trên Linux.
- Thêm dịch vụ thông báo native (NotificationService) cho hệ điều hành.

### 4.1.0 (2026-07-27)

- Thêm tính năng chạy nền dưới khay hệ thống (system tray) để kiểm tra cập nhật Patch.
- Hiển thị phiên bản game tương thích.
- Thay đổi nhẹ phần văn bản hiển thị ở tab Dự án.

### 4.0.0 (2026-07-27)

- **Hỗ trợ thêm HĐH Linux** thông qua port sang Avalonia UI framework (cross-platform).
- Cấu trúc lại toàn bộ dự án với kiến trúc mới: tách biệt các module Services, Models, Converters.
- Thêm các trang: About, Home, Library, Settings, Updates.
- Thêm dịch vụ: Download, GitHub Release, Patch Installer, Patch Update Checker, Platform Helper, Settings Manager, Steam Locator.
- *Lưu ý: Bản Linux có thể có lỗi hoặc bug vì không có môi trường thử nghiệm đầy đủ.*


## Tải về

Tải phiên bản mới nhất tại trang Releases (https://github.com/Ryo147/PatchVietHoaInstaller/releases).

| Nền tảng | File | Dung lượng (xấp xỉ) |
|---|---|---|
| Windows | PatchVietHoaInstaller.exe | ~74 MB |
| Linux | PatchVietHoaInstaller-linux.zip | ~40 MB |


## Đóng góp & Phản hồi

Nếu bạn gặp lỗi hoặc có góp ý, vui lòng mở Issue trên GitHub: https://github.com/Ryo147/PatchVietHoaInstaller/issues


## Giấy phép

Dự án được phân phối theo giấy phép Apache License 2.0 (https://www.apache.org/licenses/LICENSE-2.0).


## DISCLAIMER

Nhóm mình chỉ là nhóm Việt hóa game nhỏ lẻ. Nên website và phần mềm đều là do Claude x GLM-5.2 lập trình. Nếu các bạn không vừa ý, mong các bạn thông cảm và góp ý nhẹ nhàng. Nhóm mình sẽ cố gắng cải thiện hơn trong tương lai