namespace TubaWinUi3.Services.AiPlayground;

/// <summary>一帧 BGRA 图像（32bpp，Top-down，行距 = Width*4）。</summary>
public sealed class FrameBuffer
{
    public required byte[] Bgra { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int Stride { get; init; }
}

/// <summary>
/// 图像帧来源抽象（本地图片 / 屏幕 / 窗口 / 摄像头）。
/// 实现须保证 <see cref="TryGrab"/> 非阻塞（允许返回 false 表示暂无新帧），
/// 并在 <see cref="IDisposable.Dispose"/> 里释放全部原生句柄。
/// </summary>
public interface IFrameSource : IDisposable
{
    string DisplayName { get; }
    (int Width, int Height) Size { get; }
    /// <summary>取当前帧；无新帧或未就绪返回 false。</summary>
    bool TryGrab(out FrameBuffer frame);

    /// <summary>从 <paramref name="frame"/> 的指定矩形裁剪/缩放到 <paramref name="dstWidth"/>×<paramref name="dstHeight"/>，用于预览。</summary>
    static (byte[] Data, int Width, int Height) ScaledCopy(FrameBuffer frame, int dstWidth, int dstHeight)
    {
        var outData = new byte[dstWidth * dstHeight * 4];
        for (int y = 0; y < dstHeight; y++)
        {
            int sy = (int)((long)y * frame.Height / dstHeight);
            int srow = sy * frame.Stride;
            int drow = y * dstWidth * 4;
            for (int x = 0; x < dstWidth; x++)
            {
                int sx = (int)((long)x * frame.Width / dstWidth);
                int si = srow + sx * 4;
                int di = drow + x * 4;
                outData[di] = frame.Bgra[si];
                outData[di + 1] = frame.Bgra[si + 1];
                outData[di + 2] = frame.Bgra[si + 2];
                outData[di + 3] = frame.Bgra[si + 3];
            }
        }
        return (outData, dstWidth, dstHeight);
    }
}
