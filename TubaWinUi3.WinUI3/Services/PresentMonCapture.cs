using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Diagnostics.Tracing.Session;

namespace TubaWinUi3.Services;

internal sealed class PresentMonCapture : IDisposable
{
    private const string SessionName = "TubaWinUi3_PresentMon";
    private const string ExpectedHash = "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF";
    private readonly object _gate = new();
    private Process? _process;
    private Task? _reader;
    private PresentMonFrames? _frames;
    private long _lastAttempt;
    private bool _disposed;

    internal PresentMonFrames.Metrics? GetMetrics(int pid)
    {
        lock (_gate)
        {
            if (_disposed || !Environment.Is64BitOperatingSystem) return null;
            long now = Stopwatch.GetTimestamp();
            if (_reader is not { IsCompleted: false } &&
                (_lastAttempt == 0 || Stopwatch.GetElapsedTime(_lastAttempt, now) >= TimeSpan.FromSeconds(10)))
            {
                _lastAttempt = now;
                Stop();
                Start();
            }
            return pid > 0 && _reader is { IsCompleted: false }
                ? _frames?.GetMetrics(pid, Stopwatch.GetTimestamp())
                : null;
        }
    }

    internal List<(int Pid, float Fps)> GetProcesses()
    {
        lock (_gate) return _frames?.GetProcesses(Stopwatch.GetTimestamp()) ?? new();
    }

    internal List<(int Pid, TubaWinUi3.Models.FpsSnapshot Snapshot)> GetSnapshots()
    {
        lock (_gate) return _frames?.GetSnapshots() ?? new();
    }

    private void Start()
    {
        try
        {
            // Packaged installs use the same LocalState tool root as the tool bundle.
            var path = Path.Combine(ToolCatalog.ToolsRoot, "其他工具", "PresentMon", "PresentMon-2.6.0-x64.exe");
            if (!File.Exists(path)) return;
            // This helper starts automatically, including from a downloaded tool bundle.
            using var executable = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (Convert.ToHexString(SHA256.HashData(executable)) != ExpectedHash)
            {
                LogService.Warn(LogService.GameOverlay, "PresentMon checksum mismatch. Using ETW fallback.");
                return;
            }
            var start = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Environment.SystemDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[] { "--output_stdout", "--no_console_stats", "--v1_metrics",
                         "--qpc_time", "--no_track_gpu", "--no_track_input",
                         "--session_name", SessionName, "--stop_existing_session" })
                start.ArgumentList.Add(arg);

            var process = Process.Start(start);
            if (process is null) return;
            _process = process;
            ChildProcessJob.Assign(process);
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                    Debug.WriteLine($"PresentMon: {args.Data}");
            };
            process.BeginErrorReadLine();
            var frames = new PresentMonFrames();
            _frames = frames;
            var output = process.StandardOutput;
            _reader = Task.Run(() => ReadAsync(output, frames));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            Stop();
            LogService.Warn(LogService.GameOverlay, $"PresentMon start failed: {ex.GetType().Name}");
        }
    }

    private static async Task ReadAsync(StreamReader output, PresentMonFrames frames)
    {
        try
        {
            var header = await output.ReadLineAsync().ConfigureAwait(false);
            if (header is null || !frames.ReadHeader(header)) return;
            while (await output.ReadLineAsync().ConfigureAwait(false) is { } line)
                frames.Add(line, Stopwatch.GetTimestamp());
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private void Stop()
    {
        var process = _process;
        _process = null;
        _reader = null;
        _frames = null;
        if (process is null) return;

        try
        {
            StopTraceSession();
            if (!process.WaitForExit(1000))
            {
                process.Kill();
                process.WaitForExit(1000);
                // The child may have created its session while shutdown was starting.
                StopTraceSession();
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            StopTraceSession();
        }
        finally { process.Dispose(); }
    }

    private static void StopTraceSession()
    {
        try
        {
            using var session = TraceEventSession.GetActiveSession(SessionName);
            session?.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PresentMon ETW shutdown: {ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Stop();
        }
    }
}
