using System.Diagnostics;
using System.Text;

namespace TubaWinUi3.Services;

internal static class CapturedProcess
{
    internal sealed record Result(int ExitCode, string Output, string Error);
    internal static async Task<Result> RunAsync(ProcessStartInfo start, CancellationToken ct,
        TimeSpan? timeout = null)
    {
        ct.ThrowIfCancellationRequested();
        start.UseShellExecute = false; start.CreateNoWindow = true;
        start.RedirectStandardOutput = start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动外部程序。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout ?? TimeSpan.FromHours(2));
        var output = DrainAsync(process.StandardOutput, deadline.Token);
        var error = DrainAsync(process.StandardError, deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            return new(process.ExitCode, output.Result, error.Result);
        }
        catch
        {
            deadline.Cancel();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
            try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
            throw;
        }
    }

    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken ct)
    {
        const int maxChars = 128 * 1024;
        var text = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            if (text.Length < maxChars) text.Append(buffer, 0, Math.Min(count, maxChars - text.Length));
        return text.ToString(); // Excess output was drained rather than retained in memory.
    }
}
