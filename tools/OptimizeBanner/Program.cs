using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// ============================================================================
// Vấn đề tool này giải quyết:
// Ảnh banner luôn được tái chế từ ảnh truyền thông/marketing gốc (thường 4K, đôi khi 2K),
// trong khi banner trong app chỉ hiển thị ~700px. Nhúng thẳng ảnh gốc làm phình dung lượng
// app VÀ làm chậm lúc decode (PNG phải giải nén toàn bộ ảnh gốc trước khi thu nhỏ, không có
// "scaled decode" như JPEG) -> gây giật khi chuyển tab Trang chủ.
//
// Tool chạy TỰ ĐỘNG mỗi lần build (gắn qua Target "OptimizeRawBanners" trong .csproj chính) —
// KHÔNG cần nhớ chạy tay. Quy trình: thả ảnh gốc (4K, bất kỳ định dạng) vào
// Assets/RawSource/<TênBanner>.<đuôi>, build app bình thường -> tool tự resize + nén +
// xuất ra Assets/<TênBanner>.jpg trước khi Avalonia gom resource vào assembly.
//
// 2 chế độ chạy:
//   Chạy tay 1 ảnh:  dotnet run --project tools/OptimizeBanner -- <ảnh_gốc> <TênBanner>
//   Chạy hàng loạt:  dotnet run --project tools/OptimizeBanner -- --all <thư_mục_RawSource> <thư_mục_Assets>
//                     (MSBuild Target gọi tự động ở chế độ này, có so sánh timestamp để bỏ qua
//                     ảnh chưa đổi -> build lại không tốn công xử lý ảnh không cần thiết)
// ============================================================================

const int MaxWidth = 1920;
const int JpegQuality = 88;
var backgroundColor = Color.ParseHex("0a0810"); // khớp nền app #0a0810 (Dich2000s theme)

if (args.Length >= 1 && args[0] == "--all")
{
    if (args.Length < 3)
    {
        Console.WriteLine("Cách dùng: dotnet run -- --all <thư_mục_RawSource> <thư_mục_Assets>");
        return 1;
    }
    return await RunBatchAsync(rawDir: args[1], assetsDir: args[2]);
}

if (args.Length < 2)
{
    Console.WriteLine("Cách dùng: dotnet run --project tools/OptimizeBanner -- <đường_dẫn_ảnh_gốc> <TênBanner>");
    Console.WriteLine("       hoặc: dotnet run --project tools/OptimizeBanner -- --all <thư_mục_RawSource> <thư_mục_Assets>");
    return 1;
}

return await OptimizeOneAsync(args[0], Path.Combine(FindDefaultAssetsDir(), args[1] + ".jpg"), args[1]);

// ================= Chế độ hàng loạt (dùng trong build tự động) =================
async Task<int> RunBatchAsync(string rawDir, string assetsDir)
{
    if (!Directory.Exists(rawDir))
    {
        Console.WriteLine($"[OptimizeBanner] Không có thư mục {rawDir} -> bỏ qua (chưa có ảnh gốc nào cần xử lý).");
        return 0;
    }

    Directory.CreateDirectory(assetsDir);
    var rawFiles = Directory.EnumerateFiles(rawDir)
        .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                 || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                 || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                 || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                 || f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (rawFiles.Count == 0)
    {
        Console.WriteLine("[OptimizeBanner] Assets/RawSource/ rỗng -> không có gì để làm.");
        return 0;
    }

    int processed = 0, skipped = 0;
    foreach (var rawPath in rawFiles)
    {
        string bannerName = Path.GetFileNameWithoutExtension(rawPath);
        string outputPath = Path.Combine(assetsDir, bannerName + ".jpg");

        // Bỏ qua nếu đã tối ưu rồi và ảnh gốc không đổi -> build lại không xử lý ảnh thừa mỗi lần.
        if (File.Exists(outputPath) && File.GetLastWriteTimeUtc(outputPath) >= File.GetLastWriteTimeUtc(rawPath))
        {
            skipped++;
            continue;
        }

        await OptimizeOneAsync(rawPath, outputPath, bannerName);
        processed++;
    }

    Console.WriteLine($"[OptimizeBanner] Xong: {processed} ảnh vừa tối ưu, {skipped} ảnh đã sẵn có (bỏ qua).");
    return 0;
}

// ================= Xử lý 1 ảnh =================
async Task<int> OptimizeOneAsync(string inputPath, string outputPath, string bannerName)
{
    if (!File.Exists(inputPath))
    {
        Console.WriteLine($"Không tìm thấy file: {inputPath}");
        return 1;
    }

    using var image = await Image.LoadAsync<Rgba32>(inputPath);
    long originalBytes = new FileInfo(inputPath).Length;

    if (image.Width > MaxWidth)
    {
        int newHeight = (int)Math.Round(image.Height * (MaxWidth / (double)image.Width));
        image.Mutate(x => x.Resize(MaxWidth, newHeight, KnownResamplers.Lanczos3));
    }

    // Banner hiển thị full-bleed, JPEG không hỗ trợ kênh alpha -> flatten về màu nền app trước khi lưu.
    image.Mutate(x => x.BackgroundColor(backgroundColor));

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    await image.SaveAsJpegAsync(outputPath, new JpegEncoder { Quality = JpegQuality });

    long outputBytes = new FileInfo(outputPath).Length;
    double reducedPct = originalBytes > 0 ? 100.0 * (1 - outputBytes / (double)originalBytes) : 0;
    Console.WriteLine($"[OptimizeBanner] {bannerName}: {originalBytes / 1024.0 / 1024.0:F1}MB -> {outputBytes / 1024.0:F0}KB (giảm {reducedPct:F0}%)");
    return 0;
}

static string FindDefaultAssetsDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        string candidate = Path.Combine(dir.FullName, "PatchVietHoaInstaller.Avalonia", "Assets");
        if (Directory.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    throw new InvalidOperationException("Không tìm thấy PatchVietHoaInstaller.Avalonia/Assets ở thư mục cha nào.");
}