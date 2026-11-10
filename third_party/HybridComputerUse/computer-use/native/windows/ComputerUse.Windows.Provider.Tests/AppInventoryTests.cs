using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class AppInventoryTests
{
    // Window shapes recorded from a Windows 11 desktop with Notepad, a File
    // Explorer folder window and Settings open.
    private const int Shell = 5820;
    private const int Folder = 6800;
    private const int Notepad = 10052;
    private const int FrameHost = 5068;
    private const int Settings = 9148;

    private static readonly Dictionary<int, string> Names = new()
    {
        [Shell] = "explorer",
        [Folder] = "explorer",
        [Notepad] = "Notepad",
        [FrameHost] = "ApplicationFrameHost",
        [Settings] = "SystemSettings",
    };

    private static TopLevelWindow[] Desktop(bool secondFolderProcess = false)
    {
        var windows = new List<TopLevelWindow>
        {
            new(0x100ea, Shell, "", "Shell_TrayWnd", 0x88, false, false),
            new(0x10420, Notepad, "a.txt - Notepad", "Notepad", 0x110, true, false),
            new(0x1043e, Notepad, "Untitled - Notepad", "Notepad", 0x110, false, false),
            new(0x20172, FrameHost, "Settings", "ApplicationFrameWindow", 0x200100, false, false, Settings),
            new(0x401f2, Folder, "Windows - File Explorer", "CabinetWClass", 0x100, false, false),
            new(0x202c8, Shell, "", "ApplicationFrameWindow", 0x200100, false, true),
            new(0x10160, Shell, "", "EdgeUiInputTopWndClass", 0x8200080, false, true),
            new(0x10110, Shell, "Program Manager", "Progman", 0x200080, false, false),
        };
        if (secondFolderProcess)
        {
            windows.Add(new(0x50100, 7300, "Downloads - File Explorer", "CabinetWClass", 0x100, false, false));
        }
        return windows.ToArray();
    }

    private static string? Name(int pid) =>
        Names.TryGetValue(pid, out var name) ? name : pid == 7300 ? "explorer" : null;

    internal static void Run()
    {
        var groups = WindowsAppInventory.Group(Desktop());
        Check(groups.SelectMany(group => group.Windows).All(window => !window.Cloaked),
            "Cloaked shell surfaces must not be listed as windows.");
        var settings = groups.Single(group => group.AppProcessId == Settings);
        Check(settings.OwnerProcessId == FrameHost,
            "A hosted UWP window must keep its host as the process that owns it.");
        var shell = groups.Single(group => group.OwnerProcessId == Shell);
        Check(!shell.Windows.Any(WindowsAppInventory.IsInputTarget),
            "The taskbar and desktop must not be input targets.");
        Check(WindowsAppInventory.IsInputTarget(Desktop()[1]) &&
            WindowsAppInventory.IsInputTarget(Desktop()[3]) &&
            !WindowsAppInventory.IsInputTarget(
                new(1, 1, "menu", "#32768", 0x00000080, false, false)) &&
            WindowsAppInventory.IsInputTarget(
                new(1, 1, "palette", "Palette", 0x00040080, false, false)),
            "Input targets follow the Alt+Tab tool-window rule.");

        var explorer = WindowsAppInventory.MatchName(groups, Name, "explorer");
        Check(explorer.Count == 1 &&
            explorer[0].OwnerProcessId == Folder &&
            explorer[0].Windows.Single().ClassName == "CabinetWClass",
            "explorer must resolve to the folder window, not the taskbar.");

        var systemSettings = WindowsAppInventory.MatchName(groups, Name, "SystemSettings");
        Check(systemSettings.Count == 1 &&
            systemSettings[0].OwnerProcessId == FrameHost &&
            systemSettings[0].Windows.Single().Title == "Settings",
            "SystemSettings must resolve through its ApplicationFrameHost frame.");
        Check(WindowsAppInventory.MatchName(groups, Name, "ApplicationFrameHost").Count == 1,
            "The host name must keep resolving to the hosted window.");

        var notepad = WindowsAppInventory.MatchName(groups, Name, "notepad");
        Check(notepad.Count == 1 && notepad[0].Windows.Count == 2,
            "Several windows of one process are one application.");

        var twoFolders = WindowsAppInventory.MatchName(
            WindowsAppInventory.Group(Desktop(secondFolderProcess: true)), Name, "explorer");
        Check(twoFolders.Count == 2,
            "Two explorer processes with folder windows must stay ambiguous.");

        Check(WindowsAppInventory.MatchName(groups, Name, "Progman").Count == 0 &&
            !WindowsAppInventory.MatchesOnlyInfrastructure(groups, Name, "notepad"),
            "Only process names match.");
        var shellOnly = WindowsAppInventory.Group(
            Desktop().Where(window => window.ProcessId == Shell));
        Check(WindowsAppInventory.MatchName(shellOnly, Name, "explorer").Count == 0 &&
            WindowsAppInventory.MatchesOnlyInfrastructure(shellOnly, Name, "explorer"),
            "A shell-only explorer must be refused with the infrastructure reason.");

        // Default window: the active input window, else the first eligible.
        var shellWindows = Desktop().Where(window => window.ProcessId == Shell).ToArray();
        Check(WindowsAppInventory.DefaultWindow(
                shellWindows, shellWindows.Select(window => window.Handle).ToArray(),
                handle => handle, null) is null,
            "A process without input windows has no default window.");
        var notepadWindows = Desktop().Where(window => window.ProcessId == Notepad).ToArray();
        Check(WindowsAppInventory.DefaultWindow(
                notepadWindows, new long[] { 0x1043e, 0x10420 }, handle => handle, null) == 0x10420,
            "The active window is the default.");
        Check(WindowsAppInventory.DefaultWindow(
                notepadWindows, new long[] { 0x1043e, 0x10420 }, handle => handle,
                new long[] { 0x1043e }) == 0x1043e,
            "A resolved window scope limits the default window.");
        var mixed = new[]
        {
            new TopLevelWindow(0x10, 1, "", "Shell_TrayWnd", 0x88, true, false),
            new TopLevelWindow(0x20, 1, "Folder", "CabinetWClass", 0x100, false, false),
        };
        Check(WindowsAppInventory.DefaultWindow(
                mixed, new long[] { 0x10, 0x20 }, handle => handle, null) == 0x20,
            "An active taskbar must not become the default window of its process.");

        EvidencePolicy();
        Console.WriteLine("App inventory and evidence policy tests passed.");
    }

    private static void EvidencePolicy()
    {
        Check(!ActionEvidencePolicy.KeyboardFocusVerified(Notepad, null) &&
            !ActionEvidencePolicy.KeyboardFocusVerified(Notepad, Shell) &&
            ActionEvidencePolicy.KeyboardFocusVerified(Notepad, Notepad),
            "Keyboard focus is verified only by the foreground owner read after input.");
        Check(ActionEvidencePolicy.VerifiedChange(false) is null &&
            ActionEvidencePolicy.VerifiedChange(true) == true,
            "A readback proves a change only against a different prior value.");

        var emitted = ActionEvidencePolicy.MutationFacts(true, null);
        Check(emitted["input_events_emitted"]!.GetValue<bool>() &&
            !emitted.ContainsKey("desktop_state_mutated"),
            "Emitted input alone must not claim a changed desktop.");
        Check(ActionEvidencePolicy.MutationFacts(true, true)["desktop_state_mutated"]!
                .GetValue<bool>(),
            "A verified change is reported.");
        Check(ActionEvidencePolicy.MutationFacts(false, true)["desktop_state_mutated"]!
                .GetValue<bool>() == false,
            "No input means no change.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
