using System.Runtime.InteropServices;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace TubaWinUi3.Tui.Services;

internal static class ToolIconService
{
    public const int Size = 6;
    public const int RowsPerIcon = Size / 2;
    private const int SampleScale = 4;
    private const int SampleSize = Size * SampleScale;
    private static readonly Dictionary<string, IRenderable?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IRenderable? Get(string path)
    {
        if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!Cache.TryGetValue(path, out var icon))
            Cache[path] = icon = Load(path);
        return icon;
    }

    public static void InvalidateCache() => Cache.Clear();

    private static IRenderable? Load(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            ExtractIconExW(path, 0, IntPtr.Zero, out var icon, 1);
            if (icon == IntPtr.Zero)
                return null;

            try { return Render(ReadPixels(icon)); }
            finally { DestroyIcon(icon); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static unsafe int?[]? ReadPixels(IntPtr icon)
    {
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
            return null;

        try
        {
            var header = new BitmapInfoHeader
            {
                Size = (uint)sizeof(BitmapInfoHeader),
                Width = SampleSize,
                Height = -SampleSize, // Top-down rows.
                Planes = 1,
                BitCount = 32
            };
            var bitmap = CreateDIBSection(dc, in header, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero)
                return null;

            try
            {
                var previous = SelectObject(dc, bitmap);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1))
                    return null;

                try
                {
                    // Compare the same icon over black and white to recover coverage, including legacy AND masks.
                    var surface = new Span<uint>((void*)bits, SampleSize * SampleSize);
                    surface.Clear();
                    if (!DrawIconEx(dc, 0, 0, icon, SampleSize, SampleSize, 0, IntPtr.Zero, 3) || !GdiFlush())
                        return null;
                    var black = surface.ToArray();
                    surface.Fill(0x00ffffff);
                    if (!DrawIconEx(dc, 0, 0, icon, SampleSize, SampleSize, 0, IntPtr.Zero, 3) || !GdiFlush())
                        return null;

                    return Downsample(black, surface);
                }
                finally { SelectObject(dc, previous); }
            }
            finally { DeleteObject(bitmap); }
        }
        finally { DeleteDC(dc); }
    }

    private static int?[] Downsample(ReadOnlySpan<uint> black, ReadOnlySpan<uint> white)
    {
        var pixels = new int?[Size * Size];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                int red = 0, green = 0, blue = 0, coverage = 0;
                for (var dy = 0; dy < SampleScale; dy++)
                {
                    for (var dx = 0; dx < SampleScale; dx++)
                    {
                        var index = (y * SampleScale + dy) * SampleSize + x * SampleScale + dx;
                        var dark = black[index];
                        var darkRed = (int)(dark >> 16) & 255;
                        red += darkRed;
                        green += (int)(dark >> 8) & 255;
                        blue += (int)dark & 255;
                        coverage += 255 - Math.Clamp(((int)(white[index] >> 16) & 255) - darkRed, 0, 255);
                    }
                }

                if (coverage >= 80 * SampleScale * SampleScale)
                    pixels[y * Size + x] = (ToColor(red, coverage) << 16) | (ToColor(green, coverage) << 8)
                        | ToColor(blue, coverage);
            }
        }

        return pixels;
    }

    private static int ToColor(int sum, int coverage) => Math.Min(255, sum * 255 / coverage);

    private static IRenderable? Render(int?[]? pixels)
    {
        if (pixels == null || !pixels.Any(pixel => pixel.HasValue))
            return null;

        var rows = new IRenderable[RowsPerIcon];
        for (var y = 0; y < RowsPerIcon; y++)
        {
            var row = new StringBuilder();
            for (var x = 0; x < Size; x++)
            {
                var top = pixels[(y * 2) * Size + x];
                var bottom = pixels[(y * 2 + 1) * Size + x];
                if (top.HasValue && bottom.HasValue)
                    row.Append($"[#{top:X6} on #{bottom:X6}]▀[/]");
                else if (top.HasValue)
                    row.Append($"[#{top:X6}]▀[/]");
                else if (bottom.HasValue)
                    row.Append($"[#{bottom:X6}]▄[/]");
                else
                    row.Append(' ');
            }

            rows[y] = new Markup(row.ToString());
        }

        return new Rows(rows);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint ExtractIconExW(string path, int index, IntPtr largeIcons, out IntPtr smallIcon, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height,
        uint step, IntPtr brush, uint flags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, in BitmapInfoHeader header, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    private static extern bool GdiFlush();
}
