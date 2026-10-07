using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>预处理后的图像张量（CHW 布局，已 rescale / 归一化）。</summary>
public sealed class PreparedImage
{
    public required float[] Data { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
}

/// <summary>
/// 图像预处理（System.Drawing 实现）：
/// 解码（含 EXIF 方向）→ 缩放/裁剪 → 归一化 → CHW 浮点张量。
/// 各模型的归一化参数与尺寸来自对应仓库的 preprocessor_config.json（已在目录中核对）。
/// </summary>
public static class ImagePreprocess
{
    private static readonly float[] ImageNetMean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] ImageNetStd = [0.229f, 0.224f, 0.225f];
    private static readonly float[] ClipMean = [0.48145466f, 0.4578275f, 0.40821073f];
    private static readonly float[] ClipStd = [0.26862954f, 0.26130258f, 0.27577711f];

    public static (int Width, int Height) GetImageSize(string path)
    {
        using var bmp = new Bitmap(path);
        return (bmp.Width, bmp.Height);
    }

    /// <summary>图像分类：短边缩放 + 中心裁剪。timmCrop（resnet crop_pct=0.875）先缩到 size/0.875。</summary>
    public static PreparedImage PrepareClassification(string path, int size, bool timmCrop)
    {
        int resizeTo = timmCrop ? (int)Math.Round(size / 0.875) : size;
        using var bmp = LoadOriented(path);
        using var resized = ResizeShortest(bmp, resizeTo);
        using var cropped = CenterCrop(resized, size, size);
        return ToChw(cropped, ImageNetMean, ImageNetStd);
    }

    /// <summary>CLIP 视觉编码器：短边 224 + 中心裁剪 + CLIP 归一化。</summary>
    public static PreparedImage PrepareClipVision(string path, int size = 224)
    {
        using var bmp = LoadOriented(path);
        using var resized = ResizeShortest(bmp, size);
        using var cropped = CenterCrop(resized, size, size);
        return ToChw(cropped, ClipMean, ClipStd);
    }

    /// <summary>
    /// YOLO：直接拉伸到正方形，值域 0..1（仅 ÷255，不做 mean/std 归一化）。
    /// **实测结论**（Xenova/yolov9-c + 官方 city-streets.jpg）：必须 0..1 —— 输出 `[N,6]`
    /// 的 NMS 结果（24 个框）与官方 README 逐值吻合；若喂 0..255 会得到 3000+ 个垃圾框。
    /// </summary>
    public static PreparedImage PrepareYolo(string path, int size)
    {
        using var bmp = LoadOriented(path);
        using var resized = ResizeStretch(bmp, size, size);
        return ToChw(resized, null, null);   // ToChw 已 ÷255 → 0..1
    }

    /// <summary>DETR：短边 800 / 长边 1333 + ImageNet 归一化（do_pad 在单图批下无效果）。</summary>
    public static PreparedImage PrepareDetr(string path, int shortestEdge, int longestEdge)
    {
        using var bmp = LoadOriented(path);
        using var resized = ResizeShortestCapped(bmp, shortestEdge, longestEdge);
        return ToChw(resized, ImageNetMean, ImageNetStd);
    }

    /// <summary>
    /// YOLO：直接从 BGRA 帧（摄像头/屏幕/窗口）预处理，等价于 <see cref="PrepareYolo(string,int)"/>：
    /// 拉伸到 size×size、值域 0..1、不做 mean/std。
    /// </summary>
    public static PreparedImage PrepareYolo(FrameBuffer frame, int size)
    {
        using var bmp = FrameToBitmap(frame);
        using var resized = ResizeStretch(bmp, size, size);
        return ToChw(resized, null, null);
    }

    /// <summary>DETR：直接从 BGRA 帧预处理（短边 800 / 长边 1333 + ImageNet 归一化）。</summary>
    public static PreparedImage PrepareDetr(FrameBuffer frame, int shortestEdge, int longestEdge)
    {
        using var bmp = FrameToBitmap(frame);
        using var resized = ResizeShortestCapped(bmp, shortestEdge, longestEdge);
        return ToChw(resized, ImageNetMean, ImageNetStd);
    }

    /// <summary>把 BGRA 帧封装为 <see cref="Bitmap"/>（32bppArgb）。</summary>
    public static Bitmap FrameToBitmap(FrameBuffer frame)
    {
        var bmp = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        var rect = new Rectangle(0, 0, frame.Width, frame.Height);
        var bits = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = bits.Stride;
            var row = new byte[stride];
            for (int y = 0; y < frame.Height; y++)
            {
                int copy = Math.Min(stride, frame.Width * 4);
                Buffer.BlockCopy(frame.Bgra, y * frame.Stride, row, 0, copy);
                Marshal.Copy(row, 0, bits.Scan0 + y * stride, stride);
            }
        }
        finally
        {
            bmp.UnlockBits(bits);
        }
        return bmp;
    }

    // ── 基础操作 ────────────────────────────────────────────────────

    /// <summary>按 EXIF 方向信息摆正图片。</summary>
    public static Bitmap LoadOriented(string path)
    {
        var bmp = new Bitmap(path);
        try
        {
            if (bmp.PropertyIdList.Contains(0x0112))
            {
                var prop = bmp.GetPropertyItem(0x0112);
                if (prop?.Value is { Length: >= 2 } value)
                {
                    int orientation = value[0]; // EXIF Orientation 为 1..8 的单字节
                    switch (orientation)
                    {
                        case 3: bmp.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                        case 6: bmp.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                        case 8: bmp.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
                    }
                }
            }
        }
        catch
        {
            // 方向信息损坏时按原样使用
        }
        return bmp;
    }

    public static Bitmap ResizeStretch(Bitmap src, int width, int height)
    {
        var dst = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(dst);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        using var attributes = new ImageAttributes();
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        graphics.DrawImage(src, new Rectangle(0, 0, dst.Width, dst.Height),
            0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attributes);
        return dst;
    }

    public static Bitmap ResizeShortest(Bitmap src, int targetShortest)
    {
        var scale = (double)targetShortest / Math.Min(src.Width, src.Height);
        return ResizeStretch(src,
            (int)Math.Round(src.Width * scale),
            (int)Math.Round(src.Height * scale));
    }

    public static Bitmap ResizeShortestCapped(Bitmap src, int shortestEdge, int longestEdge)
    {
        var scale = Math.Min(
            (double)shortestEdge / Math.Min(src.Width, src.Height),
            (double)longestEdge / Math.Max(src.Width, src.Height));
        return ResizeStretch(src,
            (int)Math.Round(src.Width * scale),
            (int)Math.Round(src.Height * scale));
    }

    public static Bitmap CenterCrop(Bitmap src, int width, int height)
    {
        int cropWidth = Math.Min(width, src.Width);
        int cropHeight = Math.Min(height, src.Height);
        if (cropWidth == src.Width && cropHeight == src.Height)
            return src.Clone(new Rectangle(0, 0, src.Width, src.Height), PixelFormat.Format32bppArgb);
        int x = Math.Max(0, (src.Width - cropWidth) / 2);
        int y = Math.Max(0, (src.Height - cropHeight) / 2);
        return src.Clone(new Rectangle(x, y, cropWidth, cropHeight), PixelFormat.Format32bppArgb);
    }

    /// <summary>转 CHW 浮点（channel 顺序 RGB，值域 0..1，再套用 mean/std）。</summary>
    public static PreparedImage ToChw(Bitmap bmp, float[]? mean, float[]? std)
    {
        int width = bmp.Width;
        int height = bmp.Height;
        var data = new float[3 * width * height];
        int plane = width * height;
        var rect = new Rectangle(0, 0, width, height);
        var bits = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = bits.Stride;
            var buffer = new byte[stride * height];
            Marshal.Copy(bits.Scan0, buffer, 0, buffer.Length);
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x * 4; // BGRA 字节序
                    float b = buffer[i] / 255f;
                    float g = buffer[i + 1] / 255f;
                    float r = buffer[i + 2] / 255f;
                    int o = y * width + x;
                    data[o] = ApplyNormalize(r, 0, mean, std);
                    data[plane + o] = ApplyNormalize(g, 1, mean, std);
                    data[2 * plane + o] = ApplyNormalize(b, 2, mean, std);
                }
            }
        }
        finally
        {
            bmp.UnlockBits(bits);
        }
        return new PreparedImage { Data = data, Width = width, Height = height };
    }

    private static float ApplyNormalize(float value, int channel, float[]? mean, float[]? std) =>
        mean is null || std is null ? value : (value - mean[channel]) / std[channel];
}
