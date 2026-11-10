using System.Diagnostics;

namespace TubaWinUi3.Services;

internal static class FurMarkBenchmarkProcess
{
    internal static readonly TimeSpan ResultDisplayDuration = TimeSpan.FromSeconds(20);

    // Return true only when we had to terminate the retained result window ourselves.
    internal static bool WaitForExit(Process process, Func<bool> hasCompleteResult,
        Action<TimeSpan> reportRemaining, TimeSpan timeout, CancellationToken ct,
        TimeSpan? displayDuration = null)
    {
        var elapsed = Stopwatch.StartNew();
        Stopwatch? resultDisplay = null;
        TimeSpan hold = displayDuration ?? ResultDisplayDuration;
        while (!process.WaitForExit(250))
        {
            ct.ThrowIfCancellationRequested();
            if (resultDisplay is null && hasCompleteResult()) resultDisplay = Stopwatch.StartNew();
            if (resultDisplay is not null)
            {
                var remaining = hold - resultDisplay.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    // Prefer a normal WM_CLOSE; a modal score box may need the fallback.
                    process.Refresh();
                    if (!process.HasExited && process.CloseMainWindow() && process.WaitForExit(3000)) return false;
                    ct.ThrowIfCancellationRequested();
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit();
                        return true;
                    }
                    return false;
                }
                reportRemaining(remaining);
            }
            else if (elapsed.Elapsed > timeout)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw new TimeoutException("FurMark 测试超时，本次结果无效。");
            }
        }
        ct.ThrowIfCancellationRequested();
        return false;
    }
}
