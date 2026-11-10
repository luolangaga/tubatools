using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider
{
    private string MissingWindowMessage(string opaqueWindowHandle) =>
        WindowsWindowIdentityPolicy.MissingWindowMessage(BoundWindowLifecycle(
            _windowHandles.TryGetValue(opaqueWindowHandle, out var binding) ? binding : null));

    private NativeDesktop.WindowRecord ResolveWindow(
        Process process,
        JsonObject? target,
        string requestWindowHandle,
        string staleFailureCode = "window_target_stale",
        IReadOnlyCollection<long>? scope = null)
    {
        var opaque = OptionalString(target, "windowHandle", requestWindowHandle);
        var projection = WindowsWindowReferenceRead.Project(
            opaque,
            _windowHandles,
            staleFailureCode,
            () => ProjectNativeWindows(process, NativeDesktop.ListWindows(process.Id)),
            out var binding);
        var windows = projection.LogicalWindows();
        if (windows.Count == 0)
        {
            if (!string.IsNullOrEmpty(requestWindowHandle) ||
                !string.IsNullOrEmpty(OptionalString(
                    target,
                    "windowHandle",
                    string.Empty)))
            {
                throw WindowsProviderFailures.StaleWindow(MissingWindowMessage(opaque));
            }
            throw new ProviderFailureException(
                "window_not_found",
                "precondition",
                "The application has no visible top-level window.");
        }
        if (binding is not null)
        {
            if (!WindowsWindowIdentityPolicy.Matches(
                    binding.Handle,
                    binding.ProcessId,
                    binding.ProcessGeneration,
                    binding.LifecycleGeneration,
                    currentHandle: binding.Handle,
                    currentProcessId: process.Id,
                    currentProcessGeneration: ProcessGeneration(process),
                    currentLifecycleGeneration:
                        _windowLifecycle.Generation(binding.Handle)))
            {
                _windowHandles.Remove(opaque);
                throw staleFailureCode == "stale_window_ref"
                    ? WindowsProviderFailures.StaleWindow(
                        "The exact window process identity is stale; no input was emitted.")
                    : new ProviderFailureException(
                        staleFailureCode,
                        "precondition",
                        "The exact window process identity is stale; no input was emitted.");
            }
            var exact = windows.FirstOrDefault(
                value => value.Handle == binding.Handle);
            if (exact is null)
            {
                var message = WindowsWindowIdentityPolicy.MissingWindowMessage(
                    BoundWindowLifecycle(binding));
                throw staleFailureCode == "stale_window_ref"
                    ? WindowsProviderFailures.StaleWindow(message)
                    : new ProviderFailureException(staleFailureCode, "precondition", message);
            }
            return exact;
        }
        var nativeWindows = projection.NativeWindows
            .Select(window => window.ToTopLevel())
            .ToArray();
        var preferred = WindowsAppInventory.DefaultWindow(
            nativeWindows,
            windows.Select(window => window.Handle.ToInt64()).ToArray(),
            handle => projection.LogicalHandle((nint)handle).ToInt64(),
            scope);
        if (preferred is long handle)
        {
            return windows.First(window => window.Handle.ToInt64() == handle);
        }
        if (scope is not null)
        {
            throw new ProviderFailureException(
                "window_not_found",
                "precondition",
                "The application's windows closed while it was resolved.");
        }
        // An identity-bound application that shows no input window (only
        // tool or shell windows) keeps its previous default.
        var activeNative = projection.NativeWindows.FirstOrDefault(
            value => value.Active);
        return activeNative is not null
            ? projection.LogicalRecord(activeNative)
            : windows[0];
    }

    private string OpaqueWindowHandle(Process process, nint handle)
    {
        var processGeneration = ProcessGeneration(process);
        var lifecycleGeneration = _windowLifecycle.Generation(handle);
        foreach (var existing in _windowHandles)
        {
            var binding = existing.Value;
            if (WindowsWindowIdentityPolicy.Matches(
                    binding.Handle,
                    binding.ProcessId,
                    binding.ProcessGeneration,
                    binding.LifecycleGeneration,
                    handle,
                    process.Id,
                    processGeneration,
                    lifecycleGeneration))
            {
                return existing.Key;
            }
        }
        var material =
            $"{_sessionNonce}:{process.Id}:{processGeneration}:{handle}:" +
            $"{lifecycleGeneration}:{Guid.NewGuid():N}";
        var opaque = "w2:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant()[..32];
        _windowHandles[opaque] = new WindowBinding(
            handle,
            process.Id,
            processGeneration,
            lifecycleGeneration);
        return opaque;
    }
}
