using System.Text;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>User-facing stop gate shared by the app and the browser_use MCP process.</summary>
internal static class AiBrowserControl
{
    internal static event Action? StopRequested;

    private static readonly object Gate = new();
    private static FileSystemWatcher? _watcher;
    private static DateTime _lastRaised;

    internal static bool IsStopped
    {
        get
        {
            try { return File.Exists(AiBrowserPaths.StopFlag); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
    }

    internal static string StopReason
    {
        get
        {
            try { return File.Exists(AiBrowserPaths.StopFlag) ? File.ReadAllText(AiBrowserPaths.StopFlag).Trim() : ""; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
        }
    }

    internal static void Stop(string reason)
    {
        try
        {
            Directory.CreateDirectory(AiBrowserPaths.Root);
            File.WriteAllText(AiBrowserPaths.StopFlag, string.IsNullOrWhiteSpace(reason) ? "用户已停止 AI 浏览器控制" : reason, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Raise();
    }

    internal static void Resume()
    {
        try
        {
            if (File.Exists(AiBrowserPaths.StopFlag)) File.Delete(AiBrowserPaths.StopFlag);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>A new user turn is the explicit permission to use the browser again.</summary>
    internal static void BeginTurn() => Resume();

    internal static void StartWatcher()
    {
        lock (Gate)
        {
            if (_watcher is not null) return;
            try
            {
                Directory.CreateDirectory(AiBrowserPaths.Root);
                var watcher = new FileSystemWatcher(AiBrowserPaths.Root, "stop.flag")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
                };
                watcher.Created += (_, _) => Raise();
                watcher.Changed += (_, _) => Raise();
                watcher.Renamed += (_, _) => Raise();
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
    }

    private static void Raise()
    {
        lock (Gate)
        {
            // The writer raises once and the watcher may see the same write a moment later.
            if (DateTime.UtcNow - _lastRaised < TimeSpan.FromMilliseconds(900)) return;
            _lastRaised = DateTime.UtcNow;
        }
        var handler = StopRequested;
        if (handler is null) return;
        foreach (var subscriber in handler.GetInvocationList())
        {
            try { ((Action)subscriber)(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[AiBrowser] StopRequested: " + ex.Message); }
        }
    }
}
