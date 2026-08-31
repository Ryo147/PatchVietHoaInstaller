using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Linq;
using VietHoaInstaller.Models;
using VietHoaInstaller.Services;

namespace VietHoaInstaller
{
    /// <summary>
    /// Trang liệt kê toàn bộ game trong GameCatalog. Trạng thái "Đã cài" được xác định qua thư mục đã
    /// ghi nhớ RIÊNG cho từng game (AppSettings.GameFolders) — nếu thư mục đó còn tồn tại và có
    /// manifest.json hợp lệ, coi là đã cài. Không có thư mục ghi nhớ (chưa từng cài qua tool này, hoặc
    /// cài ở máy khác) -> hiển thị "Hoàn thành" (khả dụng) như trước, không suy diễn sai.
    /// </summary>
    public partial class LibraryPage : UserControl
    {
        public event Action<GameProfile>? InstallRequested;

        public LibraryPage()
        {
            InitializeComponent();

            var settings = SettingsManager.Load();

            var items = GameCatalog.All
                .Select(profile =>
                {
                    settings.GameFolders.TryGetValue(profile.Name, out var rememberedFolder);
                    return new GameListItem(profile, rememberedFolder);
                })
                .ToList();

            GameList.ItemsSource = items;

            int available = items.Count(i => i.CanInstall);
            int installed = items.Count(i => i.IsInstalled);
            int comingSoon = items.Count - available;
            TxtProjectCount.Text = comingSoon > 0
                ? $"{items.Count} dự án — {available} hoàn thành ({installed} đã cài), {comingSoon} đang thực hiện"
                : $"{items.Count} dự án hoàn thành ({installed} đã cài)";
        }

        private void BtnInstallGame_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: GameProfile profile })
                InstallRequested?.Invoke(profile);
        }
    }
}
