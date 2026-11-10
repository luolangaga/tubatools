using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider : IDisposable
{
    private const int MaxDesktopRootCandidates = 512;
    private const int MaxDesktopTransientCandidates = 2048;
    private const int MaxDesktopTransientDepth = 8;

    private const int DefaultNodeLimit = 5000;
    private const int DefaultDepthLimit = 96;
    // One observation's shared sampling deadline: root discovery, ownership
    // probes and traversal all draw from it, and none of them restarts it.
    private static readonly TimeSpan SnapshotSampleTimeout = TimeSpan.FromSeconds(15);
    // Document text read for a snapshot record stops here; a longer document
    // is read on demand by the action that needs it.
    private const int MaxDocumentTextLength = 65536;
    private readonly UIA3Automation _automation =
        WindowsUiaAutomationFactory.Create();
    private readonly WindowLifecycleTracker _windowLifecycle = new();
    private readonly ObservedElementLeaseRegistry<AutomationElement>
        _observedElementLeases = new();
    private readonly string _sessionNonce = Convert.ToHexString(
        RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    private sealed record WindowBinding(
        nint Handle,
        int ProcessId,
        string ProcessGeneration,
        long LifecycleGeneration);
    private sealed record NativeWindowProjection(
        IReadOnlyList<NativeDesktop.WindowRecord> NativeWindows,
        IReadOnlyList<OwnedNativeWindowCandidate> Candidates,
        long PreferredLogicalHandle)
    {
        private Dictionary<long, long>? _logicalHandles;

        internal nint LogicalHandle(nint handle)
        {
            _logicalHandles ??= Candidates.ToDictionary(
                candidate => candidate.Handle,
                candidate => OwnedNativeWindowProjection.ResolveLogicalHandle(
                    Candidates, candidate.Handle));
            return (nint)_logicalHandles.GetValueOrDefault(handle.ToInt64(), handle.ToInt64());
        }

        internal IReadOnlyList<NativeDesktop.WindowRecord> Members(
            nint logicalHandle)
        {
            return NativeWindows
                .Where(window => LogicalHandle(window.Handle) == logicalHandle)
                .OrderByDescending(window => window.Handle == logicalHandle)
                .ToArray();
        }

        internal IReadOnlyList<NativeDesktop.WindowRecord> LogicalWindows()
        {
            return NativeWindows
                .Where(window => LogicalHandle(window.Handle) == window.Handle)
                .Select(LogicalRecord)
                .ToArray();
        }

        internal NativeDesktop.WindowRecord LogicalRecord(
            NativeDesktop.WindowRecord selected)
        {
            var logicalHandle = LogicalHandle(selected.Handle);
            var logical = NativeWindows.FirstOrDefault(
                window => window.Handle == logicalHandle) ?? selected;
            var members = Members(logicalHandle);
            return logical with
            {
                Active = members.Any(window => window.Active),
                Bounds = members
                    .Select(window => window.Bounds)
                    .Aggregate(logical.Bounds, Rectangle.Union),
            };
        }
    }

    private readonly Dictionary<string, WindowBinding> _windowHandles = new(
        StringComparer.Ordinal);

    internal JsonObject ObserveApps()
    {
        using var windowSample = NativeDesktop.ReadOnlyWindowScope();
        // One window enumeration serves the whole listing, so every app is
        // sampled against the same foreground window.
        var desktopWindows = NativeDesktop.ListWindows();
        var groups = WindowsAppInventory.Group(
            desktopWindows.Select(window => window.ToTopLevel()));
        var nativeWindows = desktopWindows
            .GroupBy(window => window.ProcessId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var entries = new List<(string Name, int Pid, string Text, JsonObject App)>();
        var notes = new JsonArray();
        foreach (var group in groups)
        {
            try
            {
                using var process = Process.GetProcessById(group.OwnerProcessId);
                var projection = ProjectNativeWindows(
                    process,
                    nativeWindows.GetValueOrDefault(group.OwnerProcessId) ??
                        Array.Empty<NativeDesktop.WindowRecord>());
                var members = group.Windows
                    .Select(window => (long)projection.LogicalHandle((nint)window.Handle))
                    .ToHashSet();
                var targets = group.Windows
                    .Where(WindowsAppInventory.IsInputTarget)
                    .Select(window => (long)projection.LogicalHandle((nint)window.Handle))
                    .ToHashSet();
                var windows = projection.LogicalWindows()
                    .Where(window => members.Contains(window.Handle.ToInt64()))
                    .ToArray();
                if (windows.Length == 0)
                {
                    continue;
                }
                if (WindowsProcessIdentity.ReadGeneration(() => process.StartTime) is null)
                {
                    notes.Add(
                        $"Process identity is unavailable for {process.ProcessName} " +
                        $"(pid {process.Id}); its windows get no references. Use GUI for them.");
                    continue;
                }
                var generation = ProcessGeneration(process);
                var windowInventory = new JsonArray();
                foreach (var window in windows)
                {
                    var handle = OpaqueWindowHandle(process, window.Handle);
                    windowInventory.Add(new JsonObject
                    {
                        ["title"] = window.Title,
                        ["role"] = "window",
                        ["handle"] = handle,
                        ["providerBusName"] = $"win32:pid:{process.Id}",
                        ["providerObjectPath"] = handle,
                        ["processGeneration"] = generation,
                        ["active"] = window.Active,
                        ["inputTarget"] = targets.Contains(window.Handle.ToInt64()),
                    });
                    if (AccessibilityBridgeNote(process, window) is { } note)
                    {
                        notes.Add(note);
                    }
                }
                var host = group.AppProcessId == group.OwnerProcessId
                    ? string.Empty
                    : $" in {process.ProcessName} pid={process.Id}";
                var app = AppJson(process, group.AppProcessId);
                var name = app["name"]!.GetValue<string>();
                var text =
                    $"{name} (pid={group.AppProcessId}{host}, windows={windows.Length})";
                app["displayText"] = text;
                app["inputCapable"] = targets.Count > 0;
                app["windows"] = windowInventory;
                entries.Add((name, group.AppProcessId, text, app));
            }
            catch
            {
                // A process can terminate between enumeration and inspection.
            }
        }
        var ordered = entries
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Pid)
            .ToArray();
        return new JsonObject
        {
            ["ok"] = true,
            ["text"] = string.Join(
                Environment.NewLine,
                ordered.Select(entry => entry.Text)),
            ["apps"] = new JsonArray(
                ordered.Select(entry => (JsonNode)entry.App).ToArray()),
            ["notes"] = notes,
        };
    }

    // Names a window whose toolkit has not loaded its accessibility bridge;
    // see WindowsAccessibilityBridgeNote. Only VCL frame windows pay for the
    // module and root-children reads.
    private string? AccessibilityBridgeNote(Process process, NativeDesktop.WindowRecord window)
    {
        if (!string.Equals(
                window.ClassName,
                WindowsAccessibilityBridgeNote.VclFrameClass,
                StringComparison.Ordinal))
        {
            return null;
        }
        var children = WindowsAccessibilityBridgeNote.RootChildrenAreTitleBars(() =>
        {
            var walker = _automation.TreeWalkerFactory.GetControlViewWalker();
            var root = _automation.FromHandle(window.Handle);
            return Read();

            IEnumerable<bool> Read()
            {
                for (var child = walker.GetFirstChild(root);
                     child is not null;
                     child = walker.GetNextSibling(child))
                {
                    yield return child.ControlType == ControlType.TitleBar;
                }
            }
        });
        return WindowsAccessibilityBridgeNote.Note(
            window.ClassName,
            window.Title,
            process.Id,
            WindowsAccessibilityBridgeNote.ModuleNames(process),
            children);
    }

    internal JsonObject Observe(JsonObject request)
    {
        using var windowSample = NativeDesktop.ReadOnlyWindowScope();
        var query = RequiredString(request, "app");
        var (process, scope) = ResolveApp(request, query);
        var requestedHandle = OptionalString(request, "window_handle", string.Empty);
        _windowHandles.TryGetValue(requestedHandle, out var expectedBinding);
        try
        {
            var selected = ResolveWindow(
                process,
                null,
                requestedHandle,
                "stale_window_ref",
                scope);
            var observation = request["observation"] as JsonObject;
            // The core's second read after an action whose first read showed
            // no change: after a cell action it reads the sheet in part again.
            var readBack = observation?["recheck_after_action"]?.GetValue<bool>() == true
                ? TryReadSpreadsheetBack(
                    _spreadsheetRead, process, selected, observation, target: null, recheck: true)
                : null;
            var response = new JsonObject
            {
                ["ok"] = true,
                ["snapshot"] = readBack ?? BuildSnapshot(process, selected, observation),
            };
            var notes = new JsonArray();
            if (readBack is not null)
            {
                notes.Add(SpreadsheetReadbackNote);
            }
            if (AccessibilityBridgeNote(process, selected) is { } note)
            {
                notes.Add(note);
            }
            if (notes.Count > 0)
            {
                response["notes"] = notes;
            }
            return response;
        }
        catch (ProviderFailureException error) when (
            error.Code == "stale_window_ref")
        {
            var recovery = StaleWindowRecoveryContext(process, expectedBinding);
            throw WindowsProviderFailures.StaleWindow(
                error.Message,
                recovery,
                factsComplete: recovery["sourceWindowStatus"]?.GetValue<string>() != "unknown");
        }
    }

    internal JsonObject ListWindows(JsonObject request)
    {
        var query = RequiredString(request, "app");
        if (request["app_identity"] is null && !int.TryParse(query, out _))
        {
            return ListWindowsByName(query);
        }
        var process = ResolveProcess(request, query);
        var windows = ProjectNativeWindows(
            process,
            NativeDesktop.ListWindows(process.Id)).LogicalWindows();
        return new JsonObject
        {
            ["ok"] = true,
            ["app_name"] = process.ProcessName,
            ["app_pid"] = process.Id,
            ["window_titles"] = new JsonArray(
                windows.Select(value => JsonValue.Create(value.Title)).ToArray()),
        };
    }

    internal JsonObject ActOnWindow(JsonObject request)
    {
        ForgetSpreadsheetRead();
        var process = ResolveProcess(request, RequiredString(request, "app"));
        var target = request["target"] as JsonObject;
        var requestWindowHandle = OptionalString(
            request,
            "window_handle",
            string.Empty);
        _ = ResolveWindow(
            process,
            target,
            requestWindowHandle);
        var opaqueWindowHandle = OptionalString(
            target,
            "windowHandle",
            requestWindowHandle);
        if (string.IsNullOrEmpty(opaqueWindowHandle) ||
            !_windowHandles.TryGetValue(
                opaqueWindowHandle,
                out var binding))
        {
            throw new ProviderFailureException(
                "window_target_stale",
                "precondition",
                "Window actions require one exact current window reference; no input was emitted.");
        }
        var action = RequiredString(request, "action");
        if (action != "activate" && action != "close")
        {
            throw new ProviderFailureException(
                "unsupported_window_action",
                "precondition",
                $"Unsupported window action {action}.");
        }
        var selectedRoute = "uia.window." + action;
        var commandAttempted = false;
        var commandEmitted = false;
        var app = AppJson(process);
        var appName = process.ProcessName;
        var appPid = process.Id;
        try
        {
            var currentElement = _automation.FromHandle(binding.Handle);
            var windowPattern = currentElement.Patterns.Window.PatternOrDefault ??
                throw new ProviderFailureException(
                    "capability_unsupported",
                    "precondition",
                    "The exact window does not expose the UIA Window pattern.");
            commandAttempted = true;
            if (action == "activate")
            {
                if (windowPattern.WindowVisualState.Value ==
                    WindowVisualState.Minimized)
                {
                    windowPattern.SetWindowVisualState(
                        WindowVisualState.Normal);
                }
                NativeDesktop.ActivateWindow(binding.Handle);
            }
            else if (action == "close")
            {
                windowPattern.Close();
            }
            commandEmitted = true;
            var remaining = action == "close"
                ? WaitForWindowToDisappear(appPid, binding)
                : ProjectNativeWindows(
                    process,
                    NativeDesktop.ListWindows(appPid)).LogicalWindows();
            if (action == "close")
            {
                // Canceled closes, save prompts, hidden and zero-size windows
                // keep the native instance; only its retirement proves close.
                var lifecycle = BoundWindowLifecycle(binding);
                if (lifecycle != WindowLifecycleStatus.Gone)
                {
                    throw new ProviderFailureException(
                        "window_close_not_observed",
                        "postcondition",
                        lifecycle == WindowLifecycleStatus.Alive
                            ? "The close request was emitted, but the exact window remains alive."
                            : "The close request was emitted, but the exact window's retirement could not be verified.");
                }
            }
            var result = new JsonObject
            {
            ["ok"] = true,
            ["app_name"] = appName,
            ["app_pid"] = appPid,
            ["window_titles"] = new JsonArray(
                remaining.Select(value => JsonValue.Create(value.Title)).ToArray()),
            ["mutation"] = MutationFacts(true, false),
            ["action_evidence"] = ActionEvidence(
                selectedRoute,
                false,
                null,
                false,
                null,
                true),
            };
            if (remaining.Count > 0)
            {
                var selected = remaining.FirstOrDefault(value => value.Active) ??
                    remaining[0];
                result["snapshot"] = BuildSnapshot(process, selected, null);
            }
            else
            {
                result["snapshot"] = BuildWindowlessSnapshot(
                    app,
                    opaqueWindowHandle);
            }
            if (action == "close")
            {
                // Keep the identity until readback succeeds, so that a failed
                // readback leaves recovery able to prove retirement.
                _windowHandles.Remove(opaqueWindowHandle);
            }
            return result;
        }
        catch (ProviderFailureException error) when (commandAttempted)
        {
            throw new ProviderFailureException(
                error.Code,
                error.Phase,
                error.Message,
                commandEmitted || error.InputEventsEmitted,
                error.MutationUnknown,
                actionEvidence: ActionEvidence(
                    selectedRoute,
                    false,
                    null,
                    false,
                    null,
                    commandEmitted || error.InputEventsEmitted));
        }
        catch (Exception error) when (commandAttempted)
        {
            throw new ProviderFailureException(
                "post_window_action_observation_failed",
                "postcondition",
                error.Message,
                commandEmitted,
                true,
                actionEvidence: ActionEvidence(
                    selectedRoute,
                    false,
                    null,
                    false,
                    null,
                    commandEmitted));
        }
    }

    private JsonObject StaleWindowRecoveryContext(Process process, WindowBinding? expectedBinding)
        => WindowsWindowReferenceRead.Recover(expectedBinding is not null,
            () => KnownStaleWindowRecoveryContext(process, expectedBinding!));

    private JsonObject KnownStaleWindowRecoveryContext(Process process, WindowBinding expectedBinding)
    {
        var windows = ProjectNativeWindows(
            process,
            NativeDesktop.ListWindows(process.Id)).LogicalWindows();
        var recovery = new JsonObject
        {
            ["sourceAppStatus"] = "alive",
            // A stale token or a window missing from the visible inventory
            // is not retirement; the bound native instance decides.
            ["sourceWindowStatus"] = WindowsWindowIdentityPolicy.RecoveryStatus(
                BoundWindowLifecycle(expectedBinding)),
            ["inventoryComplete"] = true,
        };
        if (windows.Count == 0)
        {
            return recovery;
        }
        var current = windows.FirstOrDefault(value => value.Active) ?? windows[0];
        var handle = OpaqueWindowHandle(process, current.Handle);
        recovery["currentWindow"] = new JsonObject
        {
            ["windowHandle"] = handle,
            ["title"] = current.Title,
            ["role"] = "window",
            ["providerBusName"] = $"win32:pid:{process.Id}",
            ["providerObjectPath"] = handle,
            ["processGeneration"] = ProcessGeneration(process),
        };
        return recovery;
    }

    private WindowLifecycleStatus BoundWindowLifecycle(WindowBinding? binding)
    {
        return WindowsWindowIdentityPolicy.ReadLifecycle(
            binding is not null,
            () => NativeDesktop.WindowExists(binding!.Handle),
            () =>
            {
                if (!NativeDesktop.TryGetWindowProcessId(binding!.Handle, out var currentPid))
                {
                    // The HWND may have retired between samples. A second native
                    // existence check distinguishes that from an unreadable owner.
                    if (!NativeDesktop.WindowExists(binding.Handle)) { return WindowLifecycleStatus.Gone; }
                    throw new InvalidOperationException("The native window owner could not be sampled.");
                }
                if (currentPid != binding.ProcessId) { return WindowLifecycleStatus.Gone; }
                using var currentProcess = Process.GetProcessById(currentPid);
                return WindowsWindowIdentityPolicy.ReadExistingInstance(
                    binding.ProcessId, binding.ProcessGeneration,
                    binding.LifecycleGeneration, currentPid,
                    ProcessGeneration(currentProcess), _windowLifecycle.Generation(binding.Handle));
            });
    }

    public void Dispose()
    {
        _windowLifecycle.Dispose();
        _automation.UnregisterAllEvents();
        _automation.Dispose();
    }
}
