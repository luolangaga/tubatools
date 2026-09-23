using System.Drawing;
using System.Drawing.Imaging;
using System.Xml.Linq;
using Svg;

namespace TubaWinUi3.Tests;

/// <summary>
/// 图标资源（<c>Assets/*/​&lt;id&gt;.svg</c>）的共用校验：内置工具图标与侧边栏导航图标共用同一套
/// Fluent 规范与 Direct2D 兼容性约束，校验逻辑只写一份。
/// </summary>
internal static class IconAssetValidation
{
    /// <summary>Fluent 24px Regular 的统一线宽。</summary>
    internal const double FluentStrokeWidth = 1.5;

    /// <summary>
    /// Direct2D 不支持的写法（会静默不渲染）。<c>&lt;defs&gt;</c>/<c>linearGradient</c> 是支持的，
    /// 但本套图标只用纯色描边与实心块面，保持最小依赖面。
    /// </summary>
    private static readonly string[] UnsupportedMarkers =
        ["<style", "class=", "currentColor", "<text", "<filter", "<mask", "<use", "url(#"];

    internal static IReadOnlyList<string> SvgFiles(string folder)
        => Directory.GetFiles(folder, "*.svg").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();

    internal static IReadOnlyList<string> SvgIds(string folder)
        => SvgFiles(folder).Select(f => Path.GetFileNameWithoutExtension(f)!).ToList();

    /// <summary>
    /// 逐个校验：Fluent 写法（20×20 网格、1.5 描边、圆头圆角、≥2 色）与 Direct2D 兼容子集。
    /// 返回问题列表，空表示全部通过。
    /// </summary>
    internal static List<string> ValidateAll(string folder)
    {
        var problems = new List<string>();

        foreach (var file in SvgFiles(folder))
        {
            var name = Path.GetFileName(file);
            var text = File.ReadAllText(file);

            foreach (var marker in UnsupportedMarkers)
            {
                if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{name}：含 SvgImageSource 不支持的写法「{marker}」");
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(text);
            }
            catch (Exception ex)
            {
                problems.Add($"{name}：XML 解析失败（{ex.Message}）");
                continue;
            }

            var root = document.Root;
            if (root is null || root.Name.LocalName != "svg")
            {
                problems.Add($"{name}：根节点不是 <svg>");
                continue;
            }

            if ((string?)root.Attribute("viewBox") != "0 0 24 24")
                problems.Add($"{name}：viewBox 必须恰好是 \"0 0 24 24\"");

            var paths = root.Elements().Where(e => e.Name.LocalName == "path").ToList();
            if (paths.Count == 0)
            {
                problems.Add($"{name}：没有任何 <path>");
                continue;
            }

            var colors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var strokedCount = 0;

            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace((string?)path.Attribute("d")))
                {
                    problems.Add($"{name}：存在空的 path d");
                    continue;
                }

                var fill = (string?)path.Attribute("fill");
                var stroke = (string?)path.Attribute("stroke");

                if (string.IsNullOrWhiteSpace(fill) && string.IsNullOrWhiteSpace(stroke))
                {
                    problems.Add($"{name}：有 path 既没 fill 也没 stroke（Direct2D 不会画任何东西）");
                    continue;
                }

                AddColor(name, fill, colors, problems);
                AddColor(name, stroke, colors, problems);

                if (string.IsNullOrWhiteSpace(stroke)) continue;

                strokedCount++;

                // 显式写 fill 而不是靠根节点继承：Direct2D 若不继承会把描边图形填成实心黑块。
                if (string.IsNullOrWhiteSpace(fill) || !fill.Equals("none", StringComparison.OrdinalIgnoreCase))
                    problems.Add($"{name}：描边路径必须显式写 fill=\"none\"（当前 \"{fill}\"）");

                var width = (string?)path.Attribute("stroke-width");
                if (!double.TryParse(width, out var parsed) || Math.Abs(parsed - FluentStrokeWidth) > 0.01)
                    problems.Add($"{name}：描边线宽必须是 {FluentStrokeWidth}（当前 \"{width}\"），保持 Fluent 统一线重");

                if ((string?)path.Attribute("stroke-linecap") != "round")
                    problems.Add($"{name}：描边缺少 stroke-linecap=\"round\"");
                if ((string?)path.Attribute("stroke-linejoin") != "round")
                    problems.Add($"{name}：描边缺少 stroke-linejoin=\"round\"");
            }

            if (strokedCount == 0)
                problems.Add($"{name}：没有任何描边路径——本套图标是 Fluent 描边风格，实心块面只做高光");

            if (colors.Count < 2)
                problems.Add($"{name}：需要 2 种不同颜色（主色 + 高光），当前只有 {colors.Count} 种");
        }

        return problems;
    }

    /// <summary>
    /// 桌面快捷方式的 .ico 与界面都靠光栅化，这里确保每个图标能画出 256×256 且**不是空白**
    /// ——path 数据写坏了往往能解析成功但什么都不画。
    /// </summary>
    internal static List<string> ValidateRasterizes(string folder)
    {
        var problems = new List<string>();

        foreach (var file in SvgFiles(folder))
        {
            using var bitmap = SvgDocument.Open(file).Draw(256, 256);

            if (bitmap.Width != 256 || bitmap.Height != 256)
            {
                problems.Add($"{Path.GetFileName(file)}：光栅化尺寸不是 256×256（{bitmap.Width}×{bitmap.Height}）");
                continue;
            }

            var opaque = CountOpaquePixels(bitmap);
            if (opaque <= 200)
                problems.Add($"{Path.GetFileName(file)}：渲染后几乎空白（{opaque} 个不透明采样点），path 数据可能无效");
        }

        return problems;
    }

    private static void AddColor(string file, string? value, HashSet<string> colors, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            ColorTranslator.FromHtml(value);
        }
        catch
        {
            problems.Add($"{file}：无法解析的颜色「{value}」");
            return;
        }
        colors.Add(value);
    }

    private static int CountOpaquePixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var buffer = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            var count = 0;
            for (var y = 0; y < bitmap.Height; y += 4)
            {
                for (var x = 0; x < bitmap.Width; x += 4)
                {
                    if (buffer[y * data.Stride + x * 4 + 3] > 8) count++;
                }
            }
            return count;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
