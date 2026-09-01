using System;
using System.Collections.Generic;
using System.Linq;

namespace VietHoaInstaller.Services
{
    /// <summary>
    /// Bộ nhớ log gần nhất DÙNG CHUNG toàn app (không chỉ khung "Nhật ký hoạt động" ở HomePage) — mục
    /// đích duy nhất là cho <see cref="ErrorReportService"/> đính kèm vài dòng log trước khi lỗi xảy ra
    /// vào nội dung báo lỗi, giúp nhóm dịch biết người dùng đang làm gì (game nào, bước nào) ngay trước
    /// khi lỗi, mà không cần hỏi lại họ. KHÔNG phải log file trên đĩa — chỉ tồn tại trong RAM, mất khi
    /// đóng app, giữ tối đa <see cref="MaxEntries"/> dòng gần nhất.
    /// </summary>
    public static class AppLog
    {
        private const int MaxEntries = 200;
        private static readonly object Lock = new();
        private static readonly Queue<string> Entries = new();

        /// <summary>Ghi 1 dòng vào bộ nhớ log chung. Gọi song song với AppendLog() ở UI (HomePage), KHÔNG
        /// thay thế — đây chỉ là bản sao dạng text thuần để dùng cho báo lỗi, không hiển thị trực tiếp.</summary>
        public static void Add(string message)
        {
            lock (Lock)
            {
                Entries.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
                while (Entries.Count > MaxEntries)
                    Entries.Dequeue();
            }
        }

        /// <summary>Lấy tối đa <paramref name="count"/> dòng gần nhất, nối bằng xuống dòng. Trả về chuỗi
        /// rỗng nếu chưa có log nào (vd. lỗi xảy ra ngay lúc mới mở app).</summary>
        public static string GetRecentLinesText(int count)
        {
            lock (Lock)
            {
                if (Entries.Count == 0)
                    return "";

                return string.Join("\n", Entries.Skip(Math.Max(0, Entries.Count - count)));
            }
        }
    }
}
