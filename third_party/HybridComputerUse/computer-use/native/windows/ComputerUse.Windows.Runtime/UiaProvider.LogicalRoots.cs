using System.Diagnostics;
using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider
{
    private NativeWindowProjection ProjectNativeWindows(
        Process process,
        IReadOnlyList<NativeDesktop.WindowRecord> windows)
    {
        var candidates = windows.Select(
            window => new OwnedNativeWindowCandidate(
                window.Handle.ToInt64(),
                window.OwnerHandle.ToInt64(),
                window.ProcessId,
                IsIndependentManagedWindow(process, window),
                window.Bounds,
                window.ThreadId)).ToArray();
        return new NativeWindowProjection(
            windows,
            candidates,
            0);
    }

    private bool IsIndependentManagedWindow(
        Process process,
        NativeDesktop.WindowRecord window)
    {
        try
        {
            var root = _automation.FromHandle(window.Handle);
            return RunWithoutUiaCache(() =>
                root.ControlType == ControlType.Window &&
                root.Patterns.Window.PatternOrDefault is not null);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                "[provider] owned HWND classification failed closed for " +
                $"pid={process.Id} hwnd=0x{window.Handle.ToInt64():x}: " +
                $"{error.GetType().Name} HRESULT=0x{error.HResult:x8}");
            return true;
        }
    }

    private IReadOnlyList<AutomationElement> LogicalAutomationRoots(
        Process process,
        NativeDesktop.WindowRecord selectedWindow)
    {
        var projection = ProjectNativeWindows(
            process,
            NativeDesktop.ListWindows(process.Id));
        var logical = projection.LogicalRecord(selectedWindow);
        return LogicalAutomationRoots(process, projection, logical);
    }

    private IReadOnlyList<AutomationElement> LogicalAutomationRoots(
        Process process,
        NativeWindowProjection projection,
        NativeDesktop.WindowRecord logical,
        bool discoverTransients = true,
        bool transientsOnly = false,
        Action<string>? annotation = null,
        Func<TimeSpan>? elapsed = null,
        IReadOnlyCollection<string>? precedingRuntimeIdentities = null,
        UiaDesktopDiscovery? discovery = null,
        bool scanDesktop = true)
    {
        var watch = Stopwatch.StartNew();
        elapsed ??= () => watch.Elapsed;
        var timeout = SnapshotSampleTimeout;
        var projectedMembers = projection.Members(logical.Handle);
        var projectedHandles = projectedMembers
            .Select(member => member.Handle.ToInt64())
            .ToArray();
        var knownNativeHandles = projection.NativeWindows
            .Select(window => window.Handle.ToInt64())
            .ToHashSet();
        var elements = new List<AutomationElement>();
        var candidates = new List<ProjectedUiaRootCandidate>();

        foreach (var member in transientsOnly
                     ? Array.Empty<NativeDesktop.WindowRecord>()
                     : projection.Members(logical.Handle))
        {
            try
            {
                AddCandidate(
                    _automation.FromHandle(member.Handle),
                    member.ProcessId,
                    member.Handle.ToInt64());
            }
            catch when (member.Handle != logical.Handle)
            {
                // A transient member may retire during one bounded observation.
            }
        }

        var desktop = _automation.GetDesktop();
        discovery ??= new UiaDesktopDiscovery(_automation);
        var controlView = discovery.ControlView;
        var rawView = discovery.RawView;
        if (!transientsOnly && scanDesktop)
        {
            AddDesktopRoots(controlView);
            AddDesktopRoots(rawView);
        }
        if (discoverTransients)
        {
            AddDesktopTransientRoots(controlView);
            AddDesktopTransientRoots(rawView);
        }

        var selected = ProjectedUiaRootSelection.SelectIndices(
            candidates,
            process.Id,
            projectedHandles,
            logical.Bounds,
            precedingRuntimeIdentities);
        if (selected.Count == 0 && !transientsOnly)
        {
            throw new InvalidOperationException(
                "No UIA root matched the selected logical window.");
        }
        return selected.Select(index => elements[index]).ToArray();

        void AddDesktopRoots(UiaDiscoveryReader reader)
        {
            var root = reader.Desktop(desktop);
            var result = BoundedTreeDiscovery.Visit(
                () => reader.FirstChild(root),
                reader.FirstChild,
                reader.NextSibling,
                (desktopRoot, _) =>
                {
                    AddCandidate(
                        desktopRoot.Element,
                        desktopRoot.ProcessId,
                        desktopRoot.NativeWindowHandle);
                    return false;
                },
                MaxDesktopRootCandidates, 1, timeout, elapsed);
            RecordDiscoveryLimit(result, "desktop_root");
        }

        void AddDesktopTransientRoots(UiaDiscoveryReader reader)
        {
            var root = reader.Desktop(desktop);
            var result = BoundedTreeDiscovery.Visit(
                () => reader.FirstChild(root),
                reader.FirstChild,
                reader.NextSibling,
                (node, _) =>
                {
                    if (!ProjectedUiaRootSelection.ShouldTraverseTransientHost(
                            node.ProcessId, node.NativeWindowHandle,
                            process.Id, knownNativeHandles))
                    {
                        return false;
                    }
                    if (node.ControlType == ControlType.Menu ||
                        node.ControlType == ControlType.ToolTip)
                    {
                        AddCandidate(
                            node.Element,
                            node.ProcessId,
                            node.NativeWindowHandle,
                            spatialOwnershipEligible: true);
                    }
                    return true;
                },
                MaxDesktopTransientCandidates, MaxDesktopTransientDepth,
                timeout, elapsed);
            RecordDiscoveryLimit(result, "transient_discovery");
        }

        void RecordDiscoveryLimit(BoundedTreeDiscoveryResult result, string scope)
        {
            if (result.StopReason == TreeDiscoveryStopReason.TimeBudget)
            {
                if (annotation is null)
                {
                    throw new ObservationDeadlineExceededException();
                }
                annotation(scope + "_time_budget");
            }
            else if (result.StopReason == TreeDiscoveryStopReason.NodeBudget)
            {
                annotation?.Invoke(scope + "_node_budget");
            }
            // The depth bound is not a gap. Popup hosts sit just below the
            // desktop; the bound cuts window content, which the window
            // traversal covers. Counting it marked ordinary windows such as
            // Notepad partial and made their controls unaddressable.
        }

        void AddCandidate(
            AutomationElement element,
            int rootProcessId,
            long nativeWindowHandle,
            bool spatialOwnershipEligible = false)
        {
            elements.Add(element);
            candidates.Add(
                new ProjectedUiaRootCandidate(
                    rootProcessId,
                    nativeWindowHandle,
                    RuntimeObjectPath(element),
                    Safe(
                        () => element.BoundingRectangle,
                        Rectangle.Empty),
                    Safe(
                        () =>
                            element.ControlType == ControlType.Window &&
                            element.Patterns.Window.PatternOrDefault is not null,
                        true),
                    spatialOwnershipEligible));
        }
    }
}
