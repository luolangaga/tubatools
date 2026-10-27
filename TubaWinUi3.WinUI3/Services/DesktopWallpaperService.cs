using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>Reads the current desktop background locally for the model's screen.</summary>
internal static class DesktopWallpaperService
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, StringBuilder value, uint flags);

    public static async Task<string?> LoadDataUriAsync()
    {
        // Catch loader/JIT failures as well as unreadable images: wallpaper is optional.
        try { return await Task.Run(ReadDataUri); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DesktopWallpaper] {ex.Message}");
            return null;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static string? ReadDataUri()
    {
        try
        {
            var buffer = new StringBuilder(32768);
            var path = SystemParametersInfoW(0x0073, (uint)buffer.Capacity, buffer, 0) ? buffer.ToString() : null;
            if (!File.Exists(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
            if (!File.Exists(path)) return null;

            using var source = Image.FromFile(path);
            double scale = Math.Min(1, 2048d / Math.Max(source.Width, source.Height));
            using var resized = new Bitmap(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
            using (var graphics = Graphics.FromImage(resized))
            {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, resized.Width, resized.Height);
            }
            using var output = new MemoryStream();
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
            resized.Save(output, ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid), parameters);
            return "data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DesktopWallpaper] {ex.Message}");
            return null;
        }
    }
}
