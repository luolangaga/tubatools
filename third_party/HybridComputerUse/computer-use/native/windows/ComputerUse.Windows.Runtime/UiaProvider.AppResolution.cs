using System.Diagnostics;
using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider
{
    // The process an app-level request runs against. A name query resolves
    // through the visible top-level windows, so an ApplicationFrameHost
    // window counts as the UWP app it hosts and explorer.exe's taskbar
    // never counts as an application window.
    private sealed record ResolvedApp(
        Process Process,
        IReadOnlyCollection<long>? Windows);

    private Process ResolveProcess(JsonObject request, string query) =>
        ResolveApp(request, query).Process;

    private ResolvedApp ResolveApp(JsonObject request, string query)
    {
        var identity = request["app_identity"] as JsonObject;
        if (identity is not null)
        {
            var providerBusName = OptionalString(
                identity,
                "providerBusName",
                string.Empty);
            const string prefix = "uia3:pid:";
            if (providerBusName.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(
                    providerBusName[prefix.Length..],
                    out var identityPID))
            {
                Process process;
                try
                {
                    process = Process.GetProcessById(identityPID);
                }
                catch (Exception error)
                {
                    throw WindowsProviderFailures.StaleApp(
                        $"The bound application process no longer exists: {error.Message}");
                }
                var expectedGeneration = OptionalString(
                    identity,
                    "processGeneration",
                    string.Empty);
                if (string.IsNullOrEmpty(expectedGeneration) ||
                    !string.Equals(
                        ProcessGeneration(process),
                        expectedGeneration,
                        StringComparison.Ordinal))
                {
                    process.Dispose();
                    throw WindowsProviderFailures.StaleApp(
                        "The bound application process generation is stale.");
                }
                return new(process, null);
            }
            throw WindowsProviderFailures.StaleApp(
                "The bound Windows application identity is malformed.");
        }
        var names = new Dictionary<int, string?>();
        var groups = WindowsAppInventory.Group(
            NativeDesktop.ListWindows().Select(window => window.ToTopLevel()));
        if (int.TryParse(query, out var pid))
        {
            // The apps listing names a hosted UWP app by its own pid; its
            // windows belong to the host process.
            var hosted = groups.FirstOrDefault(group =>
                group.AppProcessId == pid && group.OwnerProcessId != pid);
            return hosted is null
                ? new(Process.GetProcessById(pid), null)
                : new(
                    Process.GetProcessById(hosted.OwnerProcessId),
                    hosted.Windows.Select(window => window.Handle).ToArray());
        }
        var matches = WindowsAppInventory.MatchName(
            groups,
            id => ProcessName(id, names),
            query);
        if (matches.Count == 0)
        {
            throw new ProviderFailureException(
                "app_not_found",
                "precondition",
                WindowsAppInventory.MatchesOnlyInfrastructure(
                    groups,
                    id => ProcessName(id, names),
                    query)
                    ? $"Application {query} shows only desktop shell windows " +
                        "(taskbar, desktop), which take no application input. " +
                        "List windows with AX {\"mode\":\"apps\"}."
                    : $"Application {query} was not found.");
        }
        if (matches.Count > 1)
        {
            // An exact window reference names one of the processes.
            var bound = _windowHandles.TryGetValue(
                    OptionalString(request, "window_handle", string.Empty),
                    out var binding)
                ? matches.Where(match => match.OwnerProcessId == binding.ProcessId).ToArray()
                : Array.Empty<AppWindowGroup>();
            if (bound.Length != 1)
            {
                throw AmbiguousApp(query, matches, names);
            }
            matches = bound;
        }
        var match = matches[0];
        try
        {
            return new(
                Process.GetProcessById(match.OwnerProcessId),
                match.Windows.Select(window => window.Handle).ToArray());
        }
        catch (ArgumentException)
        {
            throw new ProviderFailureException(
                "app_not_found",
                "precondition",
                $"Application {query} exited while it was resolved.");
        }
    }

    // Several application processes show input windows under one name.
    // Picking the foreground one would guess; the candidates let the core
    // list each process's windows so the caller can pass one wN.
    private static ProviderFailureException AmbiguousApp(
        string query,
        IReadOnlyList<AppWindowGroup> matches,
        Dictionary<int, string?> names)
    {
        var candidates = new JsonArray();
        var labels = new List<string>();
        foreach (var match in matches)
        {
            var name = ProcessName(match.AppProcessId, names) ?? query;
            labels.Add($"{name} ({match.Windows.Count} window{(match.Windows.Count == 1 ? "" : "s")})");
            try
            {
                using var owner = Process.GetProcessById(match.OwnerProcessId);
                candidates.Add(new JsonObject
                {
                    ["name"] = name,
                    ["providerBusName"] = $"uia3:pid:{owner.Id}",
                    ["processGeneration"] = ProcessGeneration(owner),
                });
            }
            catch (ArgumentException)
            {
                // An exited candidate cannot be addressed; it is still named.
            }
        }
        return new ProviderFailureException(
            "ax_app_ambiguous",
            "precondition",
            $"appTargetAmbiguous(\"{query}\"): {matches.Count} application " +
            $"processes with input windows match this query: {string.Join(", ", labels)}. " +
            "No action was attempted. Pass the wN of one window as window_ref; " +
            "AX {\"mode\":\"apps\"} lists the windows.",
            factsComplete: true,
            subject: "app",
            candidates: candidates);
    }

    // A title wait by application name asks whether any application of that
    // name shows such a window, so it lists every matching process; no match
    // is an empty listing, not a failure.
    private static JsonObject ListWindowsByName(string query)
    {
        var names = new Dictionary<int, string?>();
        var matches = WindowsAppInventory.MatchName(
            WindowsAppInventory.Group(
                NativeDesktop.ListWindows().Select(window => window.ToTopLevel())),
            id => ProcessName(id, names),
            query);
        var appNames = matches
            .Select(match => ProcessName(match.AppProcessId, names) ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new JsonObject
        {
            ["ok"] = true,
            ["app_name"] = appNames.Length == 1 ? appNames[0] : string.Empty,
            ["app_pid"] = matches.Count == 1 ? matches[0].AppProcessId : 0,
            ["window_titles"] = new JsonArray(
                matches
                    .SelectMany(match => match.Windows)
                    .Select(window => window.Title)
                    .Where(title => title.Length > 0)
                    .Select(title => (JsonNode)JsonValue.Create(title)!)
                    .ToArray()),
        };
    }

    private static string? ProcessName(int processId, Dictionary<int, string?> cache)
    {
        if (!cache.TryGetValue(processId, out var name))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                name = process.ProcessName;
            }
            catch (Exception)
            {
                name = null;
            }
            cache[processId] = name;
        }
        return name;
    }
}
