using System.Text.Json;

namespace TubaWinUi3.Services;

internal sealed class ActivationInbox : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _retry;
    private readonly string _directory;
    private readonly Action<LaunchIntent> _activate;
    private readonly object _gate = new();
    private bool _disposed;

    internal ActivationInbox(string directory, Action<LaunchIntent> activate)
    {
        _directory = directory; _activate = activate;
        Directory.CreateDirectory(directory);
        _watcher = new FileSystemWatcher(directory, "*.json") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        _watcher.Created += (_, _) => Drain();
        _watcher.Changed += (_, _) => Drain();
        _watcher.Renamed += (_, _) => Drain();
        _watcher.Error += (_, _) => Drain();
        // Subscribe before enabling; enable before taking the backlog snapshot.
        _watcher.EnableRaisingEvents = true;
        _retry = new Timer(_ => Drain(), null, 1000, 1000);
        Drain();
    }

    internal void Drain()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                foreach (var file in Directory.GetFiles(_directory, "*.json"))
                {
                    try
                    {
                        // Legacy writers can still be writing. Deny write sharing and retry later.
                        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                        if (stream.Length > 65536) { File.Move(file, file + ".invalid", true); continue; }
                        using var json = JsonDocument.Parse(stream);
                        var root = json.RootElement;
                        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("激活请求必须是对象。");
                        var kind = root.TryGetProperty("Kind", out var k) && k.ValueKind == JsonValueKind.String
                            && Enum.TryParse<LaunchIntentKind>(k.GetString(), true, out var parsed) && Enum.IsDefined(parsed)
                            ? parsed : LaunchIntentKind.Normal;
                        var arg = root.TryGetProperty("Arg", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                        // Claim before dispatch. A failed cleanup must never dispatch the same intent twice.
                        var claimed = file + ".processing";
                        File.Move(file, claimed);
                        try { _activate(new(kind, arg)); }
                        finally { try { File.Delete(claimed); } catch (IOException) { } }
                    }
                    catch (JsonException)
                    { try { File.Move(file, file + ".invalid", true); } catch (IOException) { } }
                    catch (Exception ex)
                    { System.Diagnostics.Debug.WriteLine($"[SingleInstance] 请求稍后重试: {ex.Message}"); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _watcher.Dispose(); _retry.Dispose(); }
    }
}
