using ComputerUse.Windows.Runtime;
using System.Drawing;

internal static class TransientDiscoveryBoundaryTests
{
    private sealed class Node(string name, int pid, long handle)
    {
        internal string Name = name;
        internal int Pid = pid;
        internal long Handle = handle;
        internal Node? First;
        internal Node? Next;
    }

    internal static void Run()
    {
        var modal = new Node("selected dialog", 7, 10);
        var selectedPopup = new Node("selected native popup", 7, 11);
        var firstWindow = new Node("first document", 7, 20);
        var secondWindow = new Node("second document", 7, 30);
        var wrapper = new Node("raw wrapper", 0, 0);
        var nestedWindow = new Node("nested native boundary", 7, 40);
        var popup = new Node("unknown popup host", 7, 99);
        var menu = new Node("menu", 7, 0);
        var foreign = new Node("other process", 8, 0);
        var tooltip = new Node("handleless tooltip", 0, 0);
        modal.Next = selectedPopup;
        selectedPopup.Next = firstWindow;
        firstWindow.Next = secondWindow;
        secondWindow.Next = wrapper;
        wrapper.First = nestedWindow;
        nestedWindow.Next = popup;
        popup.First = menu;
        wrapper.Next = foreign;
        foreign.Next = tooltip;
        var nativeHandles = new HashSet<long> { 10, 11, 20, 30, 40 };
        var visited = new List<string>();
        var childReads = new List<string>();
        var result = BoundedTreeDiscovery.Visit(
            () => modal,
            node =>
            {
                // An unrelated document can virtualize a huge tree or block
                // on its first child. Its contents must never be requested.
                if (nativeHandles.Contains(node.Handle) || node.Pid == 8)
                    throw new InvalidOperationException("Read outside transient discovery: " + node.Name);
                childReads.Add(node.Name);
                return node.First;
            },
            node => node.Next,
            (node, _) =>
            {
                visited.Add(node.Name);
                return ProjectedUiaRootSelection.ShouldTraverseTransientHost(
                    node.Pid, node.Handle, 7, nativeHandles);
            },
            20, 8, TimeSpan.FromSeconds(1), () => TimeSpan.Zero);
        if (result.StopReason != TreeDiscoveryStopReason.Completed ||
            !visited.SequenceEqual(new[] { modal.Name, selectedPopup.Name, firstWindow.Name, secondWindow.Name,
                wrapper.Name, nestedWindow.Name, popup.Name, menu.Name, foreign.Name, tooltip.Name }) ||
            !childReads.SequenceEqual(new[] { wrapper.Name, popup.Name, menu.Name, tooltip.Name }))
            throw new InvalidDataException("Native boundaries hid unknown popup siblings or consumed their read budget.");

        // Discovery does not confer ownership. Unknown roots still pass the
        // existing process, geometry and independent-window selection rules.
        var bounds = new Rectangle(100, 100, 300, 200);
        var inside = new Rectangle(120, 120, 60, 40);
        var candidates = new[]
        {
            new ProjectedUiaRootCandidate(7, 99, "menu", inside, SpatialOwnershipEligible: true),
            new ProjectedUiaRootCandidate(0, 0, "tooltip", inside, SpatialOwnershipEligible: true),
            new ProjectedUiaRootCandidate(7, 20, "other window", inside, IndependentWindow: true, SpatialOwnershipEligible: true),
            new ProjectedUiaRootCandidate(8, 0, "foreign menu", inside, SpatialOwnershipEligible: true),
            new ProjectedUiaRootCandidate(0, 0, "outside menu", new Rectangle(500, 500, 20, 20), SpatialOwnershipEligible: true),
            new ProjectedUiaRootCandidate(0, 0, "shared host", inside),
            new ProjectedUiaRootCandidate(7, 10, "selected dialog", bounds, IndependentWindow: true),
            new ProjectedUiaRootCandidate(7, 11, "selected native popup", inside),
        };
        var selected = ProjectedUiaRootSelection.SelectIndices(candidates, 7, new long[] { 10, 11 }, bounds);
        if (!selected.SequenceEqual(new[] { 0, 1, 6, 7 }))
            throw new InvalidDataException("Transient discovery changed logical-window ownership selection.");
    }
}
