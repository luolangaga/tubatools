namespace ComputerUse.Windows.Runtime;

// One visible top-level HWND with the shell facts that decide which
// application it belongs to and whether it can take input. HostedProcessId
// is the process of the CoreWindow inside an ApplicationFrameHost frame
// (UWP apps such as Settings); it is 0 for every other window.
internal sealed record TopLevelWindow(
    long Handle,
    int ProcessId,
    string Title,
    string ClassName,
    uint ExtendedStyle,
    bool Active,
    bool Cloaked,
    int HostedProcessId = 0)
{
    // The application the user sees: the hosted UWP process for an
    // ApplicationFrameHost frame, otherwise the process that owns the HWND.
    internal int AppProcessId => HostedProcessId != 0 ? HostedProcessId : ProcessId;
}

// The windows one application shows. Operations run against the HWND owner
// (OwnerProcessId); AppProcessId only names the application.
internal sealed record AppWindowGroup(
    int OwnerProcessId,
    int AppProcessId,
    IReadOnlyList<TopLevelWindow> Windows);

internal static class WindowsAppInventory
{
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExAppWindow = 0x00040000;

    // The desktop, wallpaper host and taskbars belong to explorer.exe but
    // take no application input.
    private static readonly HashSet<string> ShellClasses = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
    };

    // A cloaked window is not shown on the current desktop (another virtual
    // desktop, a suspended UWP frame, shell placeholders).
    internal static bool IsListed(TopLevelWindow window) => !window.Cloaked;

    // The Alt+Tab rule: tool windows (menus, tooltips, the taskbar) are not
    // application windows unless they opt in with WS_EX_APPWINDOW.
    internal static bool IsInputTarget(TopLevelWindow window)
    {
        if (!IsListed(window) || ShellClasses.Contains(window.ClassName))
        {
            return false;
        }
        return (window.ExtendedStyle & WsExAppWindow) != 0 ||
            (window.ExtendedStyle & WsExToolWindow) == 0;
    }

    internal static IReadOnlyList<AppWindowGroup> Group(
        IEnumerable<TopLevelWindow> windows)
    {
        return windows
            .Where(IsListed)
            .GroupBy(window => (window.ProcessId, window.AppProcessId))
            .Select(group => new AppWindowGroup(
                group.Key.ProcessId,
                group.Key.AppProcessId,
                group.ToArray()))
            .ToArray();
    }

    // Applications whose process name equals the query after dropping case
    // and punctuation, as the previous process-name lookup did. A hosted UWP
    // app also answers to its host's name. Only input-target windows count:
    // explorer.exe's taskbar must not make "explorer" resolve to the shell.
    internal static IReadOnlyList<AppWindowGroup> MatchName(
        IReadOnlyList<AppWindowGroup> groups,
        Func<int, string?> processName,
        string query)
    {
        var normalized = Normalize(query);
        if (normalized.Length == 0)
        {
            return Array.Empty<AppWindowGroup>();
        }
        var matches = new List<AppWindowGroup>();
        foreach (var group in groups)
        {
            var targets = group.Windows.Where(IsInputTarget).ToArray();
            if (targets.Length == 0)
            {
                continue;
            }
            if (Matches(processName(group.AppProcessId)) ||
                (group.OwnerProcessId != group.AppProcessId &&
                    Matches(processName(group.OwnerProcessId))))
            {
                matches.Add(group with { Windows = targets });
            }
        }
        return matches;

        bool Matches(string? name) =>
            name is not null && Normalize(name) == normalized;
    }

    // True when the query names an application that shows only shell
    // windows, so the caller can say why it was refused.
    internal static bool MatchesOnlyInfrastructure(
        IReadOnlyList<AppWindowGroup> groups,
        Func<int, string?> processName,
        string query)
    {
        var normalized = Normalize(query);
        return normalized.Length > 0 && groups.Any(group =>
            !group.Windows.Any(IsInputTarget) &&
            processName(group.AppProcessId) is { } name &&
            Normalize(name) == normalized);
    }

    // The window an app-level request without a window reference acts on:
    // the active window, else the first, among the eligible windows. With a
    // scope (the windows a name resolved to) only those are eligible;
    // otherwise only input targets are. Null means nothing is eligible.
    internal static long? DefaultWindow(
        IReadOnlyList<TopLevelWindow> nativeWindows,
        IReadOnlyList<long> logicalWindows,
        Func<long, long> logicalHandle,
        IReadOnlyCollection<long>? scope)
    {
        var eligible = (scope ?? nativeWindows
                .Where(IsInputTarget)
                .Select(window => window.Handle)
                .ToArray())
            .Select(logicalHandle)
            .ToHashSet();
        var active = nativeWindows.FirstOrDefault(window =>
            window.Active && eligible.Contains(logicalHandle(window.Handle)));
        if (active is not null)
        {
            return logicalHandle(active.Handle);
        }
        foreach (var handle in logicalWindows)
        {
            if (eligible.Contains(handle))
            {
                return handle;
            }
        }
        return null;
    }

    private static string Normalize(string value)
    {
        return new string(
            value.Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }
}
