using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed record ObservedElementLease<T>(
    T Element,
    JsonObject CanonicalRecord);

internal sealed class ObservedElementLeaseRegistry<T>
{
    private int _processId;
    private string _processGeneration = string.Empty;
    private long _windowHandle;
    private Dictionary<string, ObservedElementLease<T>> _entries = new(
        StringComparer.Ordinal);

    internal void Replace(
        int processId,
        string processGeneration,
        long windowHandle,
        IReadOnlyDictionary<string, ObservedElementLease<T>> entries)
    {
        _processId = processId;
        _processGeneration = processGeneration;
        _windowHandle = windowHandle;
        _entries = entries.ToDictionary(
            pair => pair.Key,
            pair => new ObservedElementLease<T>(
                pair.Value.Element,
                (JsonObject)pair.Value.CanonicalRecord.DeepClone()),
            StringComparer.Ordinal);
    }

    internal bool TryGet(
        int processId,
        string processGeneration,
        long windowHandle,
        string providerObjectPath,
        out ObservedElementLease<T>? lease)
    {
        return TryGet(
            processId,
            processGeneration,
            windowHandle,
            providerObjectPath,
            out lease,
            out _);
    }

    internal bool TryGet(
        int processId,
        string processGeneration,
        long windowHandle,
        string providerObjectPath,
        out ObservedElementLease<T>? lease,
        out JsonObject diagnostic)
    {
        var processIdMatches = _processId == processId;
        var processGenerationMatches = string.Equals(
            _processGeneration,
            processGeneration,
            StringComparison.Ordinal);
        var windowHandleMatches = _windowHandle == windowHandle;
        var pathPresent = _entries.TryGetValue(providerObjectPath, out lease);
        diagnostic = new JsonObject
        {
            ["event"] = "uia_observed_element_lease_lookup",
            ["entry_count"] = _entries.Count,
            ["process_id_matches"] = processIdMatches,
            ["process_generation_matches"] = processGenerationMatches,
            ["window_handle_matches"] = windowHandleMatches,
            ["provider_object_path_present"] = pathPresent,
            ["requested_process_id"] = processId,
            ["leased_process_id"] = _processId,
            ["requested_process_generation"] = processGeneration,
            ["leased_process_generation"] = _processGeneration,
            ["requested_window_handle"] = windowHandle,
            ["leased_window_handle"] = _windowHandle,
            ["requested_provider_object_path"] = providerObjectPath,
        };
        if (!processIdMatches ||
            !processGenerationMatches ||
            !windowHandleMatches ||
            !pathPresent)
        {
            lease = null;
            return false;
        }
        return true;
    }
}
