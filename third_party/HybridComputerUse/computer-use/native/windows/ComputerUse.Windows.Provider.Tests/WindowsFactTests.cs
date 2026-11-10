using ComputerUse.Windows.Runtime;

internal static class WindowsFactTests
{
    internal static void Run()
    {
        var vcl = new[] { "soffice.bin", "vclplug_winlo.dll", "user32.dll" };
        var bridged = new[] { "soffice.bin", "vclplug_winlo.dll", "winaccessibility.dll" };
        var titleBarOnly = new[] { true };
        Check(WindowsAccessibilityBridgeNote.Note("SALFRAME", "Untitled 1", 42, vcl, titleBarOnly)
                is { } note && note.Contains("SAL_FORCE_IACCESSIBLE2=1") && note.Contains("pid 42"),
            "A VCL frame without the bridge module that exposes only its title bar gets the note.");
        Check(WindowsAccessibilityBridgeNote.Note("SALFRAME", "Untitled 1", 42, bridged, titleBarOnly) is null,
            "A loaded bridge module means the tree shape has another cause.");
        Check(WindowsAccessibilityBridgeNote.Note("SALFRAME", "Untitled 1", 42, vcl, new[] { true, false }) is null,
            "Any content beside the title bar means the bridge is working.");
        Check(WindowsAccessibilityBridgeNote.Note("Notepad", "Untitled", 42, vcl, titleBarOnly) is null,
            "Only VCL frame windows qualify, whatever modules the process loaded.");
        Check(WindowsAccessibilityBridgeNote.Note("SALFRAME", "Untitled 1", 42, new[] { "soffice.bin" }, titleBarOnly) is null,
            "A SALFRAME class without the VCL module is not trusted as VCL.");
        Check(WindowsAccessibilityBridgeNote.Note("SALFRAME", "Untitled 1", 42, null, titleBarOnly) is null &&
            WindowsAccessibilityBridgeNote.Note("SALFRAME", "Untitled 1", 42, vcl, null) is null &&
            WindowsAccessibilityBridgeNote.Note("SALFRAME", "Untitled 1", 42, vcl, Array.Empty<bool>()) is null,
            "Unreadable module or tree facts produce no note.");
        Check(WindowsAccessibilityBridgeNote.RootChildrenAreTitleBars(() => Enumerable.Repeat(true, 40)) is { } many &&
            many.Any(value => !value),
            "An unexpectedly wide root is not reported as decoration-only.");
        Check(WindowsAccessibilityBridgeNote.RootChildrenAreTitleBars(
                () => throw new InvalidOperationException("gone")) is null,
            "A failed root read is an unknown fact.");
        Check(WindowsProcessIdentity.ReadGeneration(() => throw new InvalidOperationException("access denied")) is null &&
            WindowsProcessIdentity.ReadGeneration(() => default) is null &&
            WindowsProcessIdentity.ReadGeneration(() => new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)) ==
                new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "An unreadable start time is no generation; a PID alone cannot tell a replacement process apart.");
        Check(InheritedWindowOwnership.FromParent(true, 0, false) == true &&
            InheritedWindowOwnership.FromParent(false, 0, false) == false,
            "A plain descendant inherits its parent's window ownership.");
        Check(InheritedWindowOwnership.FromParent(true, 0x1234, false) is null &&
            InheritedWindowOwnership.FromParent(true, 0, true) is null &&
            InheritedWindowOwnership.FromParent(null, 0, false) is null,
            "Native windows, semantic windows and roots need the ownership probe.");
        Console.WriteLine("Windows fact tests passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidDataException(message); }
    }
}
