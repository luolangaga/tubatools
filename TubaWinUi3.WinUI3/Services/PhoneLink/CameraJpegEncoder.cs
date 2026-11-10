using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using TubaWinUi3.Services.AiPlayground;

namespace TubaWinUi3.Services;

/// <summary>把摄像头 BGRA 帧缩放并编码成 JPEG（GDI+，与截屏路径同一套编码器）。</summary>
internal static class CameraJpegEncoder
{
    private static readonly Lazy<ImageCodecInfo> JpegCodec = new(() =>
        ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid));

    public static byte[] Encode(FrameBuffer frame, int maxWidth, long quality)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
            throw new InvalidOperationException("摄像头帧为空。");

        var width = Math.Clamp(maxWidth, 1, frame.Width);
        var height = Math.Max(2, (int)Math.Round((double)frame.Height * width / frame.Width));
        if ((height & 1) != 0) height--;

        using var source = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        var data = source.LockBits(
            new Rectangle(0, 0, frame.Width, frame.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = frame.Width * 4;
            for (var y = 0; y < frame.Height; y++)
                Marshal.Copy(frame.Bgra, y * frame.Stride, data.Scan0 + y * data.Stride, rowBytes);
        }
        finally
        {
            source.UnlockBits(data);
        }

        using var output = new MemoryStream();
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, Math.Clamp(quality, 20, 95));
        if (width == frame.Width)
        {
            source.Save(output, JpegCodec.Value, parameters);
        }
        else
        {
            using var scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(scaled))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            scaled.Save(output, JpegCodec.Value, parameters);
        }
        return output.ToArray();
    }
}
