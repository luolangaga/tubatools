using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider
{
    private static NativeHandleRead ElementNativeHandle(AutomationElement element)
    {
        return NativeHandleRead.Read(() => RunWithoutUiaCache(() =>
        {
            var supported = element.Properties.NativeWindowHandle.TryGetValue(out var value);
            return new NativeHandleRead(supported, supported ? value.ToInt64() : 0);
        }));
    }

    private LogicalWindowBoundary<AutomationElement> CreateLogicalBoundary(
        Process process,
        NativeWindowProjection projection,
        OwnershipProbeBudget budget)
    {
        var ownership = projection.NativeWindows.ToDictionary(
            window => window.Handle.ToInt64(),
            window => OwnedNativeWindowProjection.ResolveLogicalOwner(
                projection.Candidates, window.Handle.ToInt64()));
        var known = ownership.ToDictionary(pair => pair.Key, pair => pair.Value.Handle);
        var nativeOwners = new NativeWindowOwnership(
            ownership, handle => NativeDesktop.RootWindow((nint)handle).ToInt64(),
            ClassifyUnlistedRoot);
        // The desktop is a native sentinel, not a UIA property assumption.
        // Some providers do not support NativeWindowHandle on the desktop node.
        var desktopHandle = NativeDesktop.DesktopWindow().ToInt64();
        var walker = _automation.TreeWalkerFactory.GetRawViewWalker();
        IReadOnlyList<OwnedNativeWindowCandidate>? spatialCandidates = null;
        var semanticRoots = new SemanticWindowRoots<AutomationElement>(
            known, handle => _automation.FromHandle((nint)handle),
            (element, root) => RunWithoutUiaCache(() => _automation.Compare(element, root)),
            budget, handle => ownership[handle].NativeProof);
        var requiresNativeAncestor = new HashSet<AutomationElement>(
            ReferenceEqualityComparer.Instance);
        var inferredOwners = new HashSet<AutomationElement>(
            ReferenceEqualityComparer.Instance);

        return new LogicalWindowBoundary<AutomationElement>(
            element => RunWithoutUiaCache(() =>
            {
                var id = element.Properties.RuntimeId.ValueOrDefault;
                return id is null || id.Length == 0 ? string.Empty : RuntimeObjectPath(id);
            }),
            NativeOwner,
            element => RunWithoutUiaCache(() => walker.GetParent(element)),
            TransientOwner,
            budget,
            requiresNativeAncestor.Contains,
            element => !inferredOwners.Contains(element));

        long? NativeOwner(AutomationElement element)
        {
            return LogicalWindowNativeOwner.Resolve(
                ElementNativeHandle(element), desktopHandle,
                () => RunWithoutUiaCache(() =>
                    UiaSemanticWindowPredicate.Read(
                        () => {
                            var supported = element.Properties.ControlType.TryGetValue(out var value);
                            return (supported, value);
                        },
                        ControlType.Window,
                        () => element.Patterns.Window.PatternOrDefault is not null)),
                () => _automation.Compare(element, _automation.GetDesktop()),
                handle =>
                {
                    var owner = nativeOwners.ReadOwner(handle);
                    if (owner is { NativeProof: false }) { inferredOwners.Add(element); }
                    return owner?.Handle;
                },
                () => semanticRoots.Owner(element),
                () => requiresNativeAncestor.Add(element));
        }

        LogicalWindowOwner? ClassifyUnlistedRoot(long root)
        {
            if (!NativeDesktop.TryGetWindowProcessId((nint)root, out var rootProcess))
            {
                throw new WindowOwnershipUnavailableException("A native ownership root is unreadable.");
            }
            if (rootProcess != process.Id)
            {
                // A foreign top-level surface is not an embedded child.
                return new(root, true);
            }
            // Enumeration can omit a legitimate transient. Do not erase it
            // merely because it was absent from the visible inventory.
            var rootElement = _automation.FromHandle((nint)root);
            var independent = RunWithoutUiaCache(() =>
                rootElement.ControlType == ControlType.Window &&
                rootElement.Patterns.Window.PatternOrDefault is not null);
            if (independent) { return new(root, true); }
            var owner = NativeDesktop.OwnerWindow((nint)root).ToInt64();
            // The native owner is an anchor only when this sample knows its
            // exact top-level identity. Unknown owner chains need UIA evidence.
            return ownership.TryGetValue(owner, out var logical) ? logical : null;
        }

        long? TransientOwner(AutomationElement element)
        {
            return RunWithoutUiaCache(() =>
            {
                var role = element.ControlType;
                if (role != ControlType.Menu && role != ControlType.ToolTip) { return null; }
                if (!element.Properties.ProcessId.TryGetValue(out var pid))
                {
                    throw new WindowOwnershipUnavailableException(
                        "The transient process identity is unsupported.");
                }
                if (pid != 0 && pid != process.Id) { return null; }
                spatialCandidates ??= projection.Candidates.Concat(
                    NativeDesktop.ListWindows()
                        .Where(window => window.ProcessId != process.Id)
                        .Select(window => new OwnedNativeWindowCandidate(
                            window.Handle.ToInt64(), window.OwnerHandle.ToInt64(),
                            window.ProcessId, true, window.Bounds, window.ThreadId)))
                    .ToArray();
                return OwnedNativeWindowProjection.UniqueTransientOwner(
                    spatialCandidates, pid, element.BoundingRectangle);
            });
        }
    }

    private void EnsureTargetLogicalWindow(
        AutomationElement? element,
        Process process,
        NativeDesktop.WindowRecord selectedWindow)
    {
        if (element is null) { return; }
        try
        {
            var budget = new OwnershipProbeBudget(256, TimeSpan.FromSeconds(3));
            var projection = ProjectNativeWindows(
                process, NativeDesktop.ListWindows(process.Id));
            budget.CheckTime();
            var boundary = CreateLogicalBoundary(process, projection, budget);
            if (boundary.Owner(element) == selectedWindow.Handle.ToInt64()) { return; }
        }
        catch (Exception error)
        {
            throw new ProviderFailureException(
                "stale_element_ref", "precondition",
                "The target's live logical-window ownership could not be verified; " +
                $"the ownership read failed ({error.GetType().Name}).");
        }
        throw new ProviderFailureException(
            "stale_element_ref", "precondition",
            "The target does not belong to the bound logical window.");
    }
}
