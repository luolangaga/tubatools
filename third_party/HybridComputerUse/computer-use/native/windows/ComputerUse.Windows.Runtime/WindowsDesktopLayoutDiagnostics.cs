using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed record WindowsDesktopLayoutWindow(
    int ZOrder,
    long Handle,
    long OwnerHandle,
    int ProcessId,
    string ClassName,
    int X,
    int Y,
    int Width,
    int Height,
    uint Style,
    uint ExtendedStyle);

internal sealed record WindowsDesktopLayoutSnapshot(
    int DesktopX,
    int DesktopY,
    int DesktopWidth,
    int DesktopHeight,
    long ForegroundHandle,
    IReadOnlyList<WindowsDesktopLayoutWindow> Windows,
    string FrameId);

internal static class WindowsDesktopLayoutDiagnostics
{
    private const int MaxDiagnosticWindows = 64;

    internal static WindowsDesktopLayoutSnapshot Create(
        int desktopX,
        int desktopY,
        int desktopWidth,
        int desktopHeight,
        long foregroundHandle,
        IReadOnlyList<WindowsDesktopLayoutWindow> windows)
    {
        var builder = new StringBuilder();
        builder.Append(desktopX).Append(',')
            .Append(desktopY).Append(',')
            .Append(desktopWidth).Append(',')
            .Append(desktopHeight).Append(';')
            .Append("foreground:")
            .Append(foregroundHandle)
            .Append(';');
        foreach (var window in windows)
        {
            builder.Append(window.ProcessId).Append(':')
                .Append(window.Handle).Append(':')
                .Append(window.ClassName).Append(':')
                .Append(window.X).Append(',')
                .Append(window.Y).Append(',')
                .Append(window.Width).Append(',')
                .Append(window.Height).Append(';');
        }
        var frameId = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
        return new WindowsDesktopLayoutSnapshot(
            desktopX,
            desktopY,
            desktopWidth,
            desktopHeight,
            foregroundHandle,
            windows,
            frameId);
    }

    internal static JsonObject Difference(
        string requestedFrameId,
        WindowsDesktopLayoutSnapshot? requested,
        WindowsDesktopLayoutSnapshot current)
    {
        var result = new JsonObject
        {
            ["event"] = "desktop_layout_stale",
            ["requested_frame_id"] = requestedFrameId,
            ["current_frame_id"] = current.FrameId,
            ["requested_layout_known"] = requested is not null,
            ["current"] = Summary(current),
        };
        if (requested is null)
        {
            return result;
        }

        result["requested"] = Summary(requested);
        result["desktop_bounds_changed"] =
            requested.DesktopX != current.DesktopX ||
            requested.DesktopY != current.DesktopY ||
            requested.DesktopWidth != current.DesktopWidth ||
            requested.DesktopHeight != current.DesktopHeight;
        result["foreground_changed"] =
            requested.ForegroundHandle != current.ForegroundHandle;

        var requestedByHandle = requested.Windows.ToDictionary(
            window => window.Handle);
        var currentByHandle = current.Windows.ToDictionary(
            window => window.Handle);
        result["added"] = WindowArray(
            current.Windows.Where(
                window => !requestedByHandle.ContainsKey(window.Handle)));
        result["removed"] = WindowArray(
            requested.Windows.Where(
                window => !currentByHandle.ContainsKey(window.Handle)));
        result["changed"] = ChangedArray(
            requested.Windows,
            currentByHandle);
        return result;
    }

    private static JsonObject Summary(WindowsDesktopLayoutSnapshot layout)
    {
        return new JsonObject
        {
            ["desktop"] = new JsonArray(
                layout.DesktopX,
                layout.DesktopY,
                layout.DesktopWidth,
                layout.DesktopHeight),
            ["foreground_hwnd"] = HexHandle(layout.ForegroundHandle),
            ["window_count"] = layout.Windows.Count,
            ["windows_truncated"] =
                layout.Windows.Count > MaxDiagnosticWindows,
            ["windows"] = WindowArray(layout.Windows),
        };
    }

    private static JsonArray ChangedArray(
        IEnumerable<WindowsDesktopLayoutWindow> requested,
        IReadOnlyDictionary<long, WindowsDesktopLayoutWindow> currentByHandle)
    {
        var result = new JsonArray();
        foreach (var previous in requested)
        {
            if (!currentByHandle.TryGetValue(
                    previous.Handle,
                    out var current) ||
                previous == current)
            {
                continue;
            }
            if (result.Count >= MaxDiagnosticWindows)
            {
                break;
            }
            result.Add(new JsonObject
            {
                ["hwnd"] = HexHandle(previous.Handle),
                ["before"] = WindowJson(previous),
                ["after"] = WindowJson(current),
            });
        }
        return result;
    }

    private static JsonArray WindowArray(
        IEnumerable<WindowsDesktopLayoutWindow> windows)
    {
        var result = new JsonArray();
        foreach (var window in windows.Take(MaxDiagnosticWindows))
        {
            result.Add(WindowJson(window));
        }
        return result;
    }

    private static JsonObject WindowJson(WindowsDesktopLayoutWindow window)
    {
        return new JsonObject
        {
            ["z_order"] = window.ZOrder,
            ["hwnd"] = HexHandle(window.Handle),
            ["owner_hwnd"] = HexHandle(window.OwnerHandle),
            ["pid"] = window.ProcessId,
            ["class"] = window.ClassName,
            ["style"] = HexValue(window.Style),
            ["extended_style"] = HexValue(window.ExtendedStyle),
            ["bounds"] = new JsonArray(
                window.X,
                window.Y,
                window.Width,
                window.Height),
        };
    }

    private static string HexHandle(long handle)
    {
        return $"0x{handle:x}";
    }

    private static string HexValue(uint value)
    {
        return $"0x{value:x8}";
    }
}
