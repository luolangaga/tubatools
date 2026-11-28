using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>离线账户头像：按用户名哈希生成像素风 Minecraft 头部（Steve 风格）。</summary>
internal static class McAvatar
{
    public static ImageSource Create(string name, int pixels = 64)
    {
        var bitmap = new WriteableBitmap(pixels, pixels);
        var buffer = new byte[pixels * pixels * 4];

        var hash = Hash(name);
        var skin = Shade(198, 134, 98, (int)(hash % 18) - 9);
        var hair = HairColor(hash);
        var eye = Color(0xFF, 0xEE, 0xEE, 0xEE);
        var iris = IrisColor(hash);
        var mouth = Shade(120, 66, 50, 0);

        var cell = Math.Max(1, pixels / 8);
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            Color color;
            if (y <= 1) color = hair;
            else if (y == 2) color = x is 0 or 7 ? hair : skin;
            else if (y == 3)
            {
                if (x is 1 or 6) color = eye;
                else if (x is 2 or 5) color = iris;
                else color = skin;
            }
            else if (y == 4 && x is 3 or 4) color = Shade(170, 108, 76, 0);
            else if (y == 6 && x is 3 or 4) color = mouth;
            else color = skin;

            Fill(buffer, pixels, x * cell, y * cell, cell, cell, color);

            // 右下轻微阴影，像素画更立体。
            if (y == 7) Fill(buffer, pixels, x * cell, 7 * cell + cell - Math.Max(1, cell / 8), cell, Math.Max(1, cell / 8), Shade(120, 78, 52, 0));
            if (x == 7) Fill(buffer, pixels, 7 * cell + cell - Math.Max(1, cell / 8), y * cell, Math.Max(1, cell / 8), cell, Shade(120, 78, 52, 0));
        }

        using (var stream = bitmap.PixelBuffer.AsStream())
            stream.Write(buffer, 0, buffer.Length);
        bitmap.Invalidate();
        return bitmap;
    }

    public static Brush CreateBrush(string name)
    {
        var image = Create(name, 64);
        return new ImageBrush
        {
            ImageSource = image,
            Stretch = Stretch.UniformToFill
        };
    }

    private static void Fill(byte[] buffer, int size, int x, int y, int width, int height, Color color)
    {
        for (var py = y; py < Math.Min(size, y + height); py++)
        for (var px = x; px < Math.Min(size, x + width); px++)
        {
            var index = (py * size + px) * 4;
            buffer[index] = color.B;
            buffer[index + 1] = color.G;
            buffer[index + 2] = color.R;
            buffer[index + 3] = color.A;
        }
    }

    private static uint Hash(string value)
    {
        uint hash = 2166136261;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return hash;
    }

    private static Color HairColor(uint hash) => ((hash >> 3) % 5) switch
    {
        0 => Color(0xFF, 0x3B, 0x2A, 0x20),
        1 => Color(0xFF, 0x5A, 0x3A, 0x1E),
        2 => Color(0xFF, 0x2B, 0x2B, 0x2B),
        3 => Color(0xFF, 0x8A, 0x5A, 0x2B),
        _ => Color(0xFF, 0xC7, 0x9A, 0x4A)
    };

    private static Color IrisColor(uint hash) => ((hash >> 9) % 4) switch
    {
        0 => Color(0xFF, 0x3D, 0x5A, 0xA8),
        1 => Color(0xFF, 0x3E, 0x7A, 0x3A),
        2 => Color(0xFF, 0x6B, 0x4A, 0x9C),
        _ => Color(0xFF, 0x2E, 0x6E, 0x8A)
    };

    private static Color Shade(int r, int g, int b, int delta) => Color(0xFF,
        (byte)Math.Clamp(r + delta, 0, 255), (byte)Math.Clamp(g + delta, 0, 255), (byte)Math.Clamp(b + delta, 0, 255));

    private static Color Color(byte a, byte r, byte g, byte b) => Microsoft.UI.ColorHelper.FromArgb(a, r, g, b);
}
