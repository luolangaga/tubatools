using TubaWinUi3.Services;
using TubaWinUi3.Services.AiPlayground;
using Xunit;

namespace TubaWinUi3.Tests;

public class PhoneCameraTests
{
    [Theory]
    [InlineData(100, 0, 10, 320, 1, 30)]
    [InlineData(4000, 60, 120, 1920, 20, 90)]
    [InlineData(960, 10, 60, 960, 10, 60)]
    [InlineData(1280, 15, 75, 1280, 15, 75)]
    public void Create_ClampsToSafeRanges(
        int width, int fps, int quality,
        int expectedWidth, int expectedFps, int expectedQuality)
    {
        var options = CameraStreamOptions.Create(width, fps, quality);
        Assert.Equal(expectedWidth, options.Width);
        Assert.Equal(expectedFps, options.Fps);
        Assert.Equal(expectedQuality, options.Quality);
    }

    [Fact]
    public void Encoder_ProducesJpegHeader()
    {
        var frame = CreateFrame(64, 48);
        var jpeg = CameraJpegEncoder.Encode(frame, 64, 70);

        Assert.True(jpeg.Length > 128);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
        Assert.Equal(0xFF, jpeg[2]);
    }

    [Fact]
    public void Encoder_ScalesDownWithoutThrowing()
    {
        var frame = CreateFrame(128, 96);
        var jpeg = CameraJpegEncoder.Encode(frame, 48, 60);

        Assert.True(jpeg.Length > 64);
        Assert.Equal(0xFF, jpeg[0]);
        Assert.Equal(0xD8, jpeg[1]);
    }

    private static FrameBuffer CreateFrame(int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;
                bgra[index] = (byte)(x * 255 / Math.Max(1, width - 1));       // B
                bgra[index + 1] = (byte)(y * 255 / Math.Max(1, height - 1));  // G
                bgra[index + 2] = 180;                                        // R
                bgra[index + 3] = 255;                                        // A
            }
        }
        return new FrameBuffer { Bgra = bgra, Width = width, Height = height, Stride = width * 4 };
    }
}
