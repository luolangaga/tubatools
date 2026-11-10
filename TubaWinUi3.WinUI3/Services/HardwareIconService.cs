using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TubaWinUi3.Services;

/// <summary>
/// 硬件详情页参数卡片的彩色矢量图标（<c>Assets/HwIcons/&lt;key&gt;.svg</c>）。
/// 与侧栏、内置工具共用 128px 彩色填充渐变风格；同时用于硬件概览和详情分区。
/// 没有对应 SVG 时返回 null，卡片头部只显示标题。
/// </summary>
public static class HardwareIconService
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static string KeyForLabel(string label) => label switch
    {
        "型号" or "型号信息" => "computer",
        "系统" or "系统信息" => "system",
        "运行时间" => "uptime",
        "主板" or "BIOS" => "motherboard",
        "处理器" => "cpu",
        "内存" => "memory",
        "显卡" => "gpu",
        "NPU" => "npu",
        "显示器" => "display",
        "硬盘" => "disk",
        "电池" => "battery",
        "声卡" => "sound",
        "网卡" => "network",
        "接口支持" => "usb",
        _ => "devices"
    };

    internal static string? ResolveSvgPath(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "HwIcons", key + ".svg");
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>该分组是否有专属彩色图标。只做文件探测，任意线程可调用。</summary>
    public static bool Has(string? key) => ResolveSvgPath(key) is not null;

    /// <summary>
    /// 彩色图标；没有 SVG 或加载失败返回 null。必须在 UI 线程调用（<see cref="SvgImageSource"/> 是 DependencyObject）。
    /// </summary>
    public static ImageSource? Get(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return Cache.GetOrAdd(key, Create);
    }

    private static ImageSource? Create(string key)
    {
        if (ResolveSvgPath(key) is not { } path) return null;
        try
        {
            // 从实际分发的文件加载，避免非打包运行时的 PRI/包资源 URI 解析失败。
            var source = new SvgImageSource();
            _ = LoadAsync(source, path, key);
            return source;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareIcon] {key} 加载失败：{ex.Message}");
            return null;
        }
    }

    private static async Task LoadAsync(SvgImageSource source, string path, string key)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var status = await source.SetSourceAsync(stream.AsRandomAccessStream());
            if (status != SvgImageSourceLoadStatus.Success)
            {
                Cache.TryRemove(key, out _);
                Debug.WriteLine($"[HardwareIcon] {key} SVG 解码失败：{status}");
            }
        }
        catch (Exception ex)
        {
            Cache.TryRemove(key, out _);
            Debug.WriteLine($"[HardwareIcon] {key} 加载失败：{ex.Message}");
        }
    }

    /// <summary>测试用：丢弃进程内缓存。</summary>
    internal static void ResetCache() => Cache.Clear();
}
