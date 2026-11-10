using System.Diagnostics;

namespace ComputerUse.Windows.Runtime;

// LibreOffice draws its controls with its own toolkit, VCL. Its Windows
// accessibility bridge (winaccessibility.dll) is a separate module that some
// releases load only when the process starts with SAL_FORCE_IACCESSIBLE2=1.
// Without it a window exposes only the title bar that Windows draws. The
// provider cannot change a running process's environment, so it reports the
// condition and the restart that fixes it.
internal static class WindowsAccessibilityBridgeNote
{
    internal const string VclFrameClass = "SALFRAME";
    // The Windows backend of VCL; the core library is merged into another module.
    private const string VclModule = "vclplug_winlo.dll";
    private const string BridgeModule = "winaccessibility.dll";
    private const int MaxRootChildren = 16;

    // The note needs all three facts: a VCL frame window, a VCL process
    // without the bridge module, and a root whose children are all title
    // bars. Unreadable facts produce no note.
    internal static string? Note(
        string windowClass,
        string title,
        int processId,
        IReadOnlyCollection<string>? moduleNames,
        IReadOnlyCollection<bool>? rootChildrenAreTitleBars)
    {
        if (!string.Equals(windowClass, VclFrameClass, StringComparison.Ordinal) ||
            moduleNames is null ||
            rootChildrenAreTitleBars is null ||
            rootChildrenAreTitleBars.Count == 0 ||
            rootChildrenAreTitleBars.Any(isTitleBar => !isTitleBar))
        {
            return null;
        }
        var modules = new HashSet<string>(moduleNames, StringComparer.OrdinalIgnoreCase);
        if (!modules.Contains(VclModule) || modules.Contains(BridgeModule))
        {
            return null;
        }
        return $"Accessibility bridge not loaded: \"{title}\" (pid {processId}, " +
            "LibreOffice VCL) exposes only its title bar. Restart the application " +
            "with SAL_FORCE_IACCESSIBLE2=1 in its environment. Until then only GUI " +
            "can read and operate its contents.";
    }

    internal static IReadOnlyCollection<string>? ModuleNames(Process process)
    {
        try
        {
            return process.Modules.Cast<ProcessModule>()
                .Select(module => module.ModuleName)
                .ToArray();
        }
        catch
        {
            return null;
        }
    }

    internal static IReadOnlyCollection<bool>? RootChildrenAreTitleBars(
        Func<IEnumerable<bool>> readChildren)
    {
        try
        {
            var children = readChildren().Take(MaxRootChildren + 1).ToArray();
            return children.Length > MaxRootChildren ? new[] { false } : children;
        }
        catch
        {
            return null;
        }
    }
}
