using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Identifiers;

namespace ComputerUse.Windows.Runtime;

// One node of a snapshot read through a UIA cache request. Its facts were
// read in the same cross-process call as its siblings, so building a record
// needs no further call except for document text and text selection.
internal sealed class CachedUiaNode(
    AutomationElement element,
    UiaNodeFacts? facts,
    CachedUiaNode? parent,
    int indexInParent)
{
    internal AutomationElement Element { get; } = element;
    // Null when the node could not be read.
    internal UiaNodeFacts? Facts { get; } = facts;
    internal CachedUiaNode? Parent { get; } = parent;
    internal int IndexInParent { get; } = indexInParent;
    // Each link is read once, when the traversal first asks for it.
    internal bool FirstChildRead { get; set; }
    internal CachedUiaNode? FirstChild { get; set; }
    internal bool NextSiblingRead { get; set; }
    internal CachedUiaNode? NextSibling { get; set; }
    // Whether the node belongs to the selected logical window; decided once.
    internal bool? Owned { get; set; }
    // A spreadsheet grid's cells were read by hit tests; its children are
    // not walked.
    internal bool CellsRead { get; set; }
}

internal sealed record UiaNodeFacts(
    ControlType? ControlType,
    string Name,
    Rectangle Bounds,
    WindowsKeyboardFocusRead KeyboardFocus,
    WindowsVisibilityRead Visibility,
    bool? IsEnabled,
    string AutomationId,
    string ClassName,
    string LocalizedControlType,
    string HelpText,
    int[] RuntimeId,
    long NativeWindowHandle,
    bool Invoke,
    bool Toggle,
    bool SelectionItem,
    bool Value,
    bool Text,
    bool RangeValue,
    bool ExpandCollapse,
    bool ScrollItem,
    bool Window,
    string? ValueText,
    bool ValueReadOnly,
    double? RangeValueNumber,
    bool RangeValueReadOnly,
    ToggleState? ToggleState,
    bool? IsSelected)
{
    internal bool HasKeyboardFocus => KeyboardFocus.Value == true;
}

internal sealed class UiaSnapshotCache
{
    private readonly AutomationBase _automation;
    private readonly CacheRequest _element;
    private readonly ITreeWalker _walker;
    private readonly ITreeWalker _rawWalker;

    internal UiaSnapshotCache(AutomationBase automation)
    {
        _automation = automation;
        _element = Request(TreeScope.Element);
        _walker = automation.TreeWalkerFactory.GetControlViewWalker();
        _rawWalker = automation.TreeWalkerFactory.GetRawViewWalker();
    }

    // Reads the facts of root alone.
    internal CachedUiaNode ReadRoot(AutomationElement root)
    {
        using (_element.Activate())
        {
            var cached = CacheRequest.IsCachingActive
                ? root.FindFirst(TreeScope.Element, TrueCondition.Default) ?? root
                : root;
            return new CachedUiaNode(cached, ReadFacts(cached), null, 0);
        }
    }

    // The tree is walked one control-view node per call, the way the walker
    // always did, but each call also returns the node's facts. A request for
    // a whole subtree or all children of a parent is one unbounded call: a
    // LibreOffice Calc grid kept the application busy long after such a
    // call timed out, and its next observation failed.
    internal CachedUiaNode? ReadFirstChild(CachedUiaNode parent)
    {
        if (!parent.FirstChildRead)
        {
            var child = Step(() => _walker.GetFirstChild(parent.Element));
            parent.FirstChild = child is null ? null : new CachedUiaNode(child.Value.Element, child.Value.Facts, parent, 0);
            parent.FirstChildRead = true;
        }
        return parent.FirstChild;
    }

    internal CachedUiaNode? ReadNextSibling(CachedUiaNode node)
    {
        if (node.Parent is null) { return null; }
        if (!node.NextSiblingRead)
        {
            var sibling = Step(() => _walker.GetNextSibling(node.Element));
            node.NextSibling = sibling is null
                ? null
                : new CachedUiaNode(sibling.Value.Element, sibling.Value.Facts, node.Parent, node.IndexInParent + 1);
            node.NextSiblingRead = true;
        }
        return node.NextSibling;
    }

    // Reads the element at a screen point with its facts in one call. The
    // node records parent as its parent; the caller checks that it is one.
    internal CachedUiaNode? ReadAtPoint(Point point, CachedUiaNode parent)
    {
        var hit = Step(() => _automation.FromPoint(point));
        return hit is null ? null : new CachedUiaNode(hit.Value.Element, hit.Value.Facts, parent, 0);
    }

    internal bool IsImmediateChildOf(CachedUiaNode child, CachedUiaNode parent)
    {
        using (CacheRequest.ForceNoCache())
        {
            var owner = _rawWalker.GetParent(child.Element);
            return owner is not null && _automation.Compare(owner, parent.Element);
        }
    }

    internal bool IsOrdinaryRowChildOf(CachedUiaNode child, CachedUiaNode table)
    {
        using (CacheRequest.ForceNoCache())
        {
            var row = _rawWalker.GetParent(child.Element);
            if (row is null || _automation.Compare(row, table.Element)) { return false; }
            // MSAA ROLE_SYSTEM_ROW is an ordinary row contract even when
            // its UIA ControlType is unsupported. Follow exactly that row
            // to the table; never traverse arbitrary containers or children.
            var legacy = row.Patterns.LegacyIAccessible.PatternOrDefault;
            if (legacy is null || (uint)legacy.Role.Value != 0x1C) { return false; }
            var owner = _rawWalker.GetParent(row);
            return owner is not null && _automation.Compare(owner, table.Element);
        }
    }

    internal bool IsSameElement(CachedUiaNode left, CachedUiaNode right) =>
        _automation.Compare(left.Element, right.Element);

    private (AutomationElement Element, UiaNodeFacts Facts)? Step(Func<AutomationElement?> read)
    {
        using (_element.Activate())
        {
            var element = read();
            return element is null ? null : (element, ReadFacts(element));
        }
    }

    private CacheRequest Request(TreeScope scope)
    {
        var request = new CacheRequest
        {
            TreeScope = scope,
            // The walker chooses the nodes; the request caches each node it
            // returns. A control-view filter here made UIA skip a container
            // that the control-view walker itself returns (Edge's title bar).
            TreeFilter = TrueCondition.Default,
            AutomationElementMode = AutomationElementMode.Full,
        };
        var element = _automation.PropertyLibrary.Element;
        var patterns = _automation.PatternLibrary;
        foreach (var property in new[]
                 {
                     element.ControlType, element.Name, element.BoundingRectangle,
                     element.HasKeyboardFocus, element.IsOffscreen, element.IsEnabled,
                     element.AutomationId, element.ClassName, element.LocalizedControlType,
                     element.HelpText, element.RuntimeId, element.NativeWindowHandle,
                     patterns.InvokePattern.AvailabilityProperty!,
                     patterns.TogglePattern.AvailabilityProperty!,
                     patterns.SelectionItemPattern.AvailabilityProperty!,
                     patterns.ValuePattern.AvailabilityProperty!,
                     patterns.TextPattern.AvailabilityProperty!,
                     patterns.RangeValuePattern.AvailabilityProperty!,
                     patterns.ExpandCollapsePattern.AvailabilityProperty!,
                     patterns.ScrollItemPattern.AvailabilityProperty!,
                     patterns.WindowPattern.AvailabilityProperty!,
                     _automation.PropertyLibrary.Value.Value,
                     _automation.PropertyLibrary.Value.IsReadOnly,
                     _automation.PropertyLibrary.RangeValue.Value,
                     _automation.PropertyLibrary.RangeValue.IsReadOnly,
                     _automation.PropertyLibrary.Toggle.ToggleState,
                     _automation.PropertyLibrary.SelectionItem.IsSelected,
                 })
        {
            request.Add(property);
        }
        return request;
    }

    // While a cache request is active FlaUI reads every property from the
    // cache; otherwise each read is a call to the provider.
    private UiaNodeFacts ReadFacts(AutomationElement element)
    {
        var properties = element.Properties;
        var framework = element.FrameworkAutomationElement;
        var library = _automation.PropertyLibrary;
        var patterns = _automation.PatternLibrary;
        return new UiaNodeFacts(
            properties.ControlType.TryGetValue(out var controlType) ? controlType : null,
            properties.Name.TryGetValue(out var name) ? name ?? string.Empty : string.Empty,
            properties.BoundingRectangle.TryGetValue(out var bounds) ? bounds : Rectangle.Empty,
            WindowsKeyboardFocusRead.Read(() =>
            {
                var supported = properties.HasKeyboardFocus.TryGetValue(out var focused);
                return (supported, focused);
            }),
            WindowsVisibilityRead.Read(() =>
            {
                var supported = properties.IsOffscreen.TryGetValue(out var offscreen);
                return (supported, offscreen);
            }),
            properties.IsEnabled.TryGetValue(out var enabled) ? enabled : null,
            properties.AutomationId.TryGetValue(out var automationId) ? automationId ?? string.Empty : string.Empty,
            properties.ClassName.TryGetValue(out var className) ? className ?? string.Empty : string.Empty,
            properties.LocalizedControlType.TryGetValue(out var localized) ? localized ?? string.Empty : string.Empty,
            properties.HelpText.TryGetValue(out var helpText) ? helpText ?? string.Empty : string.Empty,
            properties.RuntimeId.TryGetValue(out var runtimeId) ? runtimeId ?? Array.Empty<int>() : Array.Empty<int>(),
            properties.NativeWindowHandle.TryGetValue(out var hwnd) ? hwnd.ToInt64() : 0,
            TryRead<bool>(patterns.InvokePattern.AvailabilityProperty!, out var invoke) && invoke,
            TryRead<bool>(patterns.TogglePattern.AvailabilityProperty!, out var toggle) && toggle,
            TryRead<bool>(patterns.SelectionItemPattern.AvailabilityProperty!, out var selectionItem) && selectionItem,
            TryRead<bool>(patterns.ValuePattern.AvailabilityProperty!, out var value) && value,
            TryRead<bool>(patterns.TextPattern.AvailabilityProperty!, out var text) && text,
            TryRead<bool>(patterns.RangeValuePattern.AvailabilityProperty!, out var range) && range,
            TryRead<bool>(patterns.ExpandCollapsePattern.AvailabilityProperty!, out var expand) && expand,
            TryRead<bool>(patterns.ScrollItemPattern.AvailabilityProperty!, out var scrollItem) && scrollItem,
            TryRead<bool>(patterns.WindowPattern.AvailabilityProperty!, out var window) && window,
            TryRead<string>(library.Value.Value, out var valueText) ? valueText : null,
            !TryRead<bool>(library.Value.IsReadOnly, out var valueReadOnly) || valueReadOnly,
            TryRead<double>(library.RangeValue.Value, out var rangeValue) ? rangeValue : null,
            !TryRead<bool>(library.RangeValue.IsReadOnly, out var rangeReadOnly) || rangeReadOnly,
            TryRead<ToggleState>(library.Toggle.ToggleState, out var toggleState) ? toggleState : null,
            TryRead<bool>(library.SelectionItem.IsSelected, out var selected) ? selected : null);

        bool TryRead<T>(PropertyId property, out T result)
        {
            try
            {
                if (framework.TryGetPropertyValue<T>(property, out var read) && read is not null)
                {
                    result = read;
                    return true;
                }
            }
            catch
            {
                // An unsupported or failed pattern property is an absent fact.
            }
            result = default!;
            return false;
        }
    }
}

// A node of the desktop search for window roots and popups. The search needs
// three facts per node, read for all children of a parent in one call.
internal sealed class UiaDiscoveryNode(
    AutomationElement element,
    int processId,
    ControlType? controlType,
    long nativeWindowHandle,
    UiaDiscoveryNode? parent,
    int indexInParent)
{
    internal AutomationElement Element { get; } = element;
    internal int ProcessId { get; } = processId;
    internal ControlType? ControlType { get; } = controlType;
    internal long NativeWindowHandle { get; } = nativeWindowHandle;
    internal UiaDiscoveryNode? Parent { get; } = parent;
    internal int IndexInParent { get; } = indexInParent;
    internal IReadOnlyList<UiaDiscoveryNode>? Children { get; set; }
}

internal sealed class UiaDiscoveryReader
{
    private readonly AutomationBase _automation;
    private readonly ITreeWalker _walker;
    private readonly CacheRequest _children;
    private readonly CacheRequest _element;
    private UiaDiscoveryNode? _desktop;

    internal UiaDiscoveryReader(AutomationBase automation, bool rawView)
    {
        _automation = automation;
        _walker = rawView
            ? automation.TreeWalkerFactory.GetRawViewWalker()
            : automation.TreeWalkerFactory.GetControlViewWalker();
        // The children request's filter selects the view of the top-level
        // elements. A walker step chooses its node itself, so its request
        // must not filter the node out.
        _children = Request(TreeScope.Element | TreeScope.Children, rawView);
        _element = Request(TreeScope.Element, true);

        CacheRequest Request(TreeScope scope, bool unfiltered)
        {
            var request = new CacheRequest
            {
                TreeScope = scope,
                TreeFilter = unfiltered
                    ? TrueCondition.Default
                    : new PropertyCondition(automation.PropertyLibrary.Element.IsControlElement, true),
                AutomationElementMode = AutomationElementMode.Full,
            };
            var element = automation.PropertyLibrary.Element;
            request.Add(element.ProcessId);
            request.Add(element.ControlType);
            request.Add(element.NativeWindowHandle);
            return request;
        }
    }

    // The desktop node, kept so that every search of this reader reuses the
    // top-level elements it already read.
    internal UiaDiscoveryNode Desktop(AutomationElement desktop) =>
        _desktop ??= new(desktop, 0, null, 0, null, 0);

    // The desktop's children, the top-level elements, arrive in one call.
    // Below them the search reads one node per call, so a wide subtree
    // costs only the nodes the search budget allows.
    internal UiaDiscoveryNode? FirstChild(UiaDiscoveryNode node)
    {
        if (ReferenceEquals(node, _desktop))
        {
            var children = DesktopChildren(node);
            return children.Count > 0 ? children[0] : null;
        }
        return Step(() => _walker.GetFirstChild(node.Element), node, 0);
    }

    internal UiaDiscoveryNode? NextSibling(UiaDiscoveryNode node)
    {
        if (node.Parent is null) { return null; }
        if (ReferenceEquals(node.Parent, _desktop))
        {
            var siblings = DesktopChildren(node.Parent);
            return node.IndexInParent + 1 < siblings.Count ? siblings[node.IndexInParent + 1] : null;
        }
        return Step(() => _walker.GetNextSibling(node.Element), node.Parent, node.IndexInParent + 1);
    }

    private IReadOnlyList<UiaDiscoveryNode> DesktopChildren(UiaDiscoveryNode desktop)
    {
        if (desktop.Children is not null) { return desktop.Children; }
        using (_children.Activate())
        {
            if (CacheRequest.IsCachingActive)
            {
                var cached = desktop.Element.FindFirst(TreeScope.Element, TrueCondition.Default);
                var elements = cached?.CachedChildren ?? Array.Empty<AutomationElement>();
                desktop.Children = elements.Select((child, index) => Wrap(child, desktop, index)).ToArray();
                return desktop.Children;
            }
        }
        var live = new List<UiaDiscoveryNode>();
        for (var child = _walker.GetFirstChild(desktop.Element);
             child is not null;
             child = _walker.GetNextSibling(child))
        {
            live.Add(Wrap(child, desktop, live.Count));
        }
        desktop.Children = live;
        return live;
    }

    private UiaDiscoveryNode? Step(Func<AutomationElement?> read, UiaDiscoveryNode parent, int index)
    {
        using (_element.Activate())
        {
            var element = read();
            return element is null ? null : Wrap(element, parent, index);
        }
    }

    // Reads cached values while a request is active, live values otherwise.
    private static UiaDiscoveryNode Wrap(AutomationElement child, UiaDiscoveryNode parent, int index)
    {
        var properties = child.Properties;
        return new UiaDiscoveryNode(
            child,
            properties.ProcessId.TryGetValue(out var pid) ? pid : 0,
            properties.ControlType.TryGetValue(out var type) ? type : null,
            properties.NativeWindowHandle.TryGetValue(out var hwnd) ? hwnd.ToInt64() : 0,
            parent,
            index);
    }
}

// The control-view and raw-view desktop readers of one operation. Sharing
// them lets window-root and popup discovery read the desktop's children once.
internal sealed class UiaDesktopDiscovery(AutomationBase automation)
{
    internal UiaDiscoveryReader ControlView { get; } = new(automation, rawView: false);
    internal UiaDiscoveryReader RawView { get; } = new(automation, rawView: true);
}
