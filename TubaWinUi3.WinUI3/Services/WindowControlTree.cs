using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using static Interop.UIAutomationClient.UIA_PropertyIds;

namespace TubaWinUi3.Services;

internal static class WindowControlTree
{
    internal sealed record Node(string Name, string Type, string ClassName, string AutomationId,
        Rectangle Bounds, bool Offscreen, List<Node> Children)
    {
        public string Label => string.IsNullOrWhiteSpace(Name) ? Type : $"{Type} · {Name}";
    }

    internal sealed record Snapshot(Node Root, int Count, bool Partial, WindowManagerService.InspectionState Window);
    internal sealed class WindowChangedException : Exception;

    public static async Task<Snapshot> ReadAsync(WindowManagerService.WindowInfo target, CancellationToken token)
    {
        // UIA clients should call providers from an MTA thread with no windows.
        return await Task.Run(() => Read(target, token), token).ConfigureAwait(false);
    }

    private static Snapshot Read(WindowManagerService.WindowInfo target, CancellationToken token)
    {
        if (WindowManagerService.GetInspectionState(target) is not { Visible: true } start)
            throw new InvalidOperationException("请先显示目标窗口，再刷新控件树。");

        IUIAutomation2 automation = (IUIAutomation2)new CUIAutomation8Class();
        automation.ConnectionTimeout = 1500;
        automation.TransactionTimeout = 1500;
        var cache = automation.CreateCacheRequest();
        foreach (int property in new[] { UIA_NamePropertyId, UIA_LocalizedControlTypePropertyId, UIA_ClassNamePropertyId,
            UIA_AutomationIdPropertyId, UIA_BoundingRectanglePropertyId, UIA_IsOffscreenPropertyId, UIA_IsPasswordPropertyId })
            cache.AddProperty(property);

        var walker = automation.ControlViewWalker;
        var clock = Stopwatch.StartNew();
        int count = 0;
        bool partial = false;
        var element = automation.ElementFromHandleBuildCache(target.Handle, cache) ??
            throw new InvalidOperationException("目标窗口没有提供可读取的控件树。");
        var root = ReadNode(element, 0);

        if (WindowManagerService.GetInspectionState(target) is not { Visible: true } end ||
            !start.Bounds.Equals(end.Bounds) || start.Dpi != end.Dpi)
            throw new WindowChangedException();
        return new(root, count, partial, start);

        Node ReadNode(IUIAutomationElement element, int depth)
        {
            token.ThrowIfCancellationRequested();
            count++;
            var bounds = element.CachedBoundingRectangle;
            var type = element.CachedLocalizedControlType;
            var node = new Node(element.CachedIsPassword != 0 ? "" : Limit(element.CachedName),
                string.IsNullOrWhiteSpace(type) ? "控件" : Limit(type), Limit(element.CachedClassName),
                Limit(element.CachedAutomationId), Rectangle.FromLTRB(bounds.left, bounds.top, bounds.right, bounds.bottom),
                element.CachedIsOffscreen != 0, []);

            if (ReachedLimit()) return node;
            try
            {
                for (var child = walker.GetFirstChildElementBuildCache(element, cache); child is not null;)
                {
                    if (depth >= 32 || ReachedLimit())
                    {
                        partial = true;
                        break;
                    }
                    node.Children.Add(ReadNode(child, depth + 1));
                    if (ReachedLimit()) break;
                    child = walker.GetNextSiblingElementBuildCache(child, cache);
                }
            }
            catch (COMException)
            {
                // A provider can remove controls while we walk. Keep the nodes already read.
                partial = true;
            }
            return node;
        }

        bool ReachedLimit()
        {
            token.ThrowIfCancellationRequested();
            if (count < 600 && clock.Elapsed < TimeSpan.FromSeconds(4)) return false;
            partial = true;
            return true;
        }
    }

    private static string Limit(string? value) => value switch
    {
        null => "",
        { Length: > 160 } => value[..(char.IsHighSurrogate(value[159]) ? 159 : 160)] + "…",
        _ => value
    };
}
