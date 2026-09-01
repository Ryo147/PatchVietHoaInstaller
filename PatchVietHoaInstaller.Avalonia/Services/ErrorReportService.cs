using Avalonia.Controls;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VietHoaInstaller.Services
{
    /// <summary>
    /// Dùng chung cho MỌI nơi trong app cần hỏi người dùng "có muốn báo lỗi không" — trước đây logic
    /// này nằm riêng trong HomePage.axaml.cs, chỉ áp dụng cho lỗi cài/gỡ Việt hóa. Tách ra đây để
    /// SettingsPage, LibraryPage, UpdatesPage, và cả bộ bắt lỗi toàn cục (App.axaml.cs) đều dùng được
    /// cùng 1 luồng báo lỗi nhất quán, không viết lại logic mở GitHub Issue nhiều lần.
    /// </summary>
    public static class ErrorReportService
    {
        /// <summary>Số dòng log gần nhất đính kèm vào nội dung report — đủ để thấy bối cảnh (đang làm
        /// gì trước khi lỗi) mà không làm URL prefill GitHub Issue quá dài.</summary>
        private const int RecentLogLineCount = 10;

        /// <summary>Giới hạn độ dài chi tiết lỗi trước khi nhúng vào URL — GitHub có giới hạn thực tế
        /// cho độ dài URL prefill New Issue, cắt bớt để tránh bị cắt cụt giữa chừng khó đọc.</summary>
        private const int MaxErrorDetailLength = 1500;

        /// <summary>
        /// Hiện dialog hỏi có muốn báo lỗi không; nếu đồng ý, mở sẵn trang tạo Issue trên GitHub với
        /// tiêu đề + nội dung (kèm vài dòng log gần nhất) đã điền sẵn để người dùng chỉ cần bấm gửi.
        /// </summary>
        /// <param name="owner">Cửa sổ cha để định vị dialog. Nếu null (vd. lỗi xảy ra quá sớm lúc app
        /// chưa kịp hiện cửa sổ nào), bỏ qua hoàn toàn — không có gì để hiện dialog lên.</param>
        /// <param name="title">Tiêu đề ngắn gọn mô tả loại lỗi, vd. "Cài đặt thất bại".</param>
        /// <param name="ex">Exception gốc.</param>
        /// <param name="gameName">Tên game đang thao tác lúc lỗi, nếu có — null nếu lỗi không gắn với
        /// 1 game cụ thể (vd. lỗi xảy ra ở SettingsPage).</param>
        public static async Task OfferReportAsync(Window? owner, string title, Exception ex, string? gameName = null)
        {
            if (owner == null)
                return;

            SimpleMessageBoxResult result;
            try
            {
                result = await SimpleMessageBox.ShowAsync(owner,
                    $"{title}:\n\n{ex.Message}\n\nBạn có muốn báo lỗi này cho nhóm dịch không?",
                    title, SimpleMessageBoxButtons.YesNo);
            }
            catch
            {
                return; // Không hiện được dialog (vd. owner đã đóng) -> bỏ qua, đừng crash thêm lần nữa.
            }

            if (result != SimpleMessageBoxResult.Yes)
                return;

            string appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

            string errorDetail = ex.ToString();
            if (errorDetail.Length > MaxErrorDetailLength)
                errorDetail = errorDetail[..MaxErrorDetailLength] + "\n... (đã cắt bớt, xem log đầy đủ trên máy nếu cần)";

            string recentLog = AppLog.GetRecentLinesText(RecentLogLineCount);

            string issueTitle = gameName != null ? $"[BÁO LỖI TỰ ĐỘNG] {title} - {gameName}" : $"[BÁO LỖI TỰ ĐỘNG] {title}";

            string issueBody =
                (gameName != null ? $"**Game:** {gameName}\n" : "") +
                $"**Phiên bản app:** v{appVersion}\n" +
                $"**Thời điểm:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"**Hệ điều hành:** {RuntimeInformation.OSDescription}\n\n" +
                $"**Chi tiết lỗi:**\n```\n{errorDetail}\n```\n\n" +
                (recentLog.Length > 0 ? $"**Nhật ký gần nhất trước khi lỗi:**\n```\n{recentLog}\n```\n\n" : "") +
                "**Mô tả thêm (nếu có):**\n(bạn có thể ghi thêm ở đây trước khi gửi)";

            string url = "https://github.com/Ryo147/PatchVietHoaInstaller/issues/new"
                + $"?title={Uri.EscapeDataString(issueTitle)}"
                + $"&body={Uri.EscapeDataString(issueBody)}";

            try
            {
                PlatformHelper.OpenUrlInBrowser(url);
            }
            catch
            {
                // Không mở được trình duyệt (hiếm khi xảy ra) -> bỏ qua, không chặn luồng chính.
            }
        }
    }
}
