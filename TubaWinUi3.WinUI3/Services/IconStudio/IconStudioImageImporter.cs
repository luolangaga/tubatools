using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Security.Cryptography;

namespace TubaWinUi3.Services;

/// <summary>
/// 图标工坊的图片导入：校验格式与大小，复制到数据目录，必要时归一化为 PNG。
/// 只接受本地文件，不下载远程图片；失败时抛出可直接展示的中文原因。
/// </summary>
internal static class IconStudioImageImporter
{
    private const long MaxBytes = 16 * 1024 * 1024;
    private const int MaxEdge = 1024;
    private static readonly string[] RasterExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico"];

    internal static string IconsDirectory => Path.Combine(ConfigManager.GetDataDir(), "IconStudio", "Icons");

    internal static string Import(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("图片路径为空。", nameof(sourcePath));
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("找不到图片文件。", source);

        var info = new FileInfo(source);
        if (info.Length <= 0) throw new InvalidDataException("图片文件为空。");
        if (info.Length > MaxBytes) throw new InvalidDataException("图片超过 16 MB，请先压缩后再选择。");

        var extension = info.Extension.ToLowerInvariant();
        Directory.CreateDirectory(IconsDirectory);

        if (extension == ".svg") return ImportSvg(source);
        if (!RasterExtensions.Contains(extension))
            throw new InvalidDataException("不支持该图片格式。可用格式：PNG、JPEG、BMP、GIF、ICO、SVG。");

        return ImportRaster(source, extension);
    }

    private static string ImportSvg(string source)
    {
        var text = File.ReadAllText(source);
        var head = text.AsSpan(0, Math.Min(text.Length, 8192));
        if (!head.Contains("<svg", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("这个 SVG 文件缺少 <svg> 根元素。");
        var bytes = File.ReadAllBytes(source);
        return Store(bytes, ".svg");
    }

    private static string ImportRaster(string source, string extension)
    {
        int width, height;
        try
        {
            using var stream = File.OpenRead(source);
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            width = image.Width;
            height = image.Height;
            if (width <= 0 || height <= 0) throw new InvalidDataException("图片尺寸无效。");
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            // GDI+ 的解码失败统一抛 OutOfMemoryException；不代表真的内存不足。
            throw new InvalidDataException("无法解码这张图片，请换一张 PNG/JPEG 图片。", ex);
        }

        // 小尺寸的普通位图原样保存，保留动画 GIF 与原始压缩；ICO 与超大图归一化为 PNG。
        if (extension != ".ico" && width <= MaxEdge && height <= MaxEdge)
            return Store(File.ReadAllBytes(source), extension == ".jpeg" ? ".jpg" : extension);

        using var normalized = Normalize(source, width, height);
        using var memory = new MemoryStream();
        normalized.Save(memory, ImageFormat.Png);
        return Store(memory.ToArray(), ".png");
    }

    private static Bitmap Normalize(string source, int width, int height)
    {
        var scale = Math.Min(1d, (double)MaxEdge / Math.Max(width, height));
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));
        var bitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var image = Image.FromFile(source))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(image, 0, 0, targetWidth, targetHeight);
        }
        return bitmap;
    }

    private static string Store(byte[] bytes, string extension)
    {
        Directory.CreateDirectory(IconsDirectory);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var path = Path.Combine(IconsDirectory, "icon-" + hash[..20] + extension);
        if (File.Exists(path)) return path;

        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            // 并发导入同一张图片：另一个线程已经落盘，复用它的文件。
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
        return path;
    }

    /// <summary>删除不再被任何覆盖项引用的导入文件；只在重置/切换预设后调用。</summary>
    internal static void Prune(IReadOnlyCollection<string> usedPaths)
    {
        try
        {
            if (!Directory.Exists(IconsDirectory)) return;
            var used = usedPaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(IconsDirectory, "icon-*"))
            {
                if (used.Contains(Path.GetFullPath(file))) continue;
                try { File.Delete(file); } catch { }
            }
        }
        catch
        {
            // 清理失败不影响图标设置；孤儿文件最多占一点磁盘。
        }
    }
}
