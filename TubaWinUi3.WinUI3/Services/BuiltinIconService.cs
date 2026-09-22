using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TubaWinUi3.Services;

/// <summary>
/// 内置工具的彩色矢量图标（<c>Assets/BuiltinIcons/&lt;tool-id&gt;.svg</c>）。
/// 没有对应 SVG 时一律返回 null，调用方回退到 <see cref="IBuiltinTool.Glyph"/> 的字体字形，
/// 所以漏配图标的工具只会在界面上退回旧样式，不会开天窗。
/// </summary>
public static class BuiltinIconService
{
    private const string RelativeFolder = "Assets/BuiltinIcons";
    private const string UriPrefix = "ms-appx:///" + RelativeFolder + "/";

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>图标 SVG 的绝对路径；没有这个文件返回 null（不抛异常）。</summary>
    internal static string? ResolveSvgPath(string? toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return null;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "BuiltinIcons", toolId + ".svg");
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>该内置工具是否有专属彩色图标。只做文件探测，任意线程可调用。</summary>
    public static bool Has(string? toolId) => ResolveSvgPath(toolId) is not null;

    /// <summary>
    /// 彩色图标；没有 SVG 或加载失败返回 null。同一 id 复用同一个实例。
    /// <para>
    /// 必须在 UI 线程调用：<see cref="SvgImageSource"/> 是 DependencyObject。
    /// 这也是各视图模型把图标做成惰性属性的原因——只有界面绑定读取时才会走到这里。
    /// </para>
    /// </summary>
    public static ImageSource? Get(string? toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return null;
        return Cache.GetOrAdd(toolId, Create);
    }

    private static ImageSource? Create(string toolId)
    {
        if (ResolveSvgPath(toolId) is null) return null;
        try
        {
            return new SvgImageSource(new Uri(UriPrefix + toolId + ".svg"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BuiltinIcon] {toolId} 加载失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>测试用：丢弃进程内缓存。</summary>
    internal static void ResetCache() => Cache.Clear();
}
