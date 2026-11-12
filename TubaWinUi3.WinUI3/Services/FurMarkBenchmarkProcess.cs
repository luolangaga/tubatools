using System.Diagnostics;

namespace TubaWinUi3.Services;

internal static class FurMarkBenchmarkProcess
{
    // FurMark keeps its score window open for an arbitrary while and some machines never exit by
    // themselves, so wait for a natural exit and terminate whatever is left once the timeout passes.
    // The exit code is deliberately never inspected: an otherwise perfect run may still report a
    // nonzero code, and treating that as a crash produces false "abnormal exit" failures.
    // Returns true when we had to terminate the process ourselves.
    internal static bool WaitForExit(Process process, TimeSpan timeout, CancellationToken ct,
        Action<TimeSpan>? reportElapsed = null)
    {
        var elapsed = Stopwatch.StartNew();
        TimeSpan nextReport = TimeSpan.Zero;
        while (!process.WaitForExit(250))
        {
            ct.ThrowIfCancellationRequested();
            if (elapsed.Elapsed > timeout)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit();
                ct.ThrowIfCancellationRequested();
                return true;
            }
            if (reportElapsed is not null && elapsed.Elapsed >= nextReport)
            {
                nextReport = elapsed.Elapsed + TimeSpan.FromSeconds(1);
                reportElapsed(elapsed.Elapsed);
            }
        }
        // A cancelled run kills the process, so a natural exit here may still mean "cancelled".
        ct.ThrowIfCancellationRequested();
        return false;
    }
}
