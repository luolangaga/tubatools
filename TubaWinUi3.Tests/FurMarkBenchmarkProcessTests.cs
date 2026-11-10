using System.Diagnostics;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class FurMarkBenchmarkProcessTests
{
    private static Process StartWaitingProcess() => Process.Start(new ProcessStartInfo(
        "powershell.exe", "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30")
    {
        UseShellExecute = false, CreateNoWindow = true
    })!;

    [Fact]
    public void CompleteResult_IsHeldBeforeAutomaticClose_EvenPastBenchmarkTimeout()
    {
        using var process = StartWaitingProcess();
        var elapsed = Stopwatch.StartNew();
        TimeSpan? firstResultAt = null;
        try
        {
            bool forced = FurMarkBenchmarkProcess.WaitForExit(process, () =>
            {
                // Simulate a summary becoming readable after the benchmark finishes.
                if (elapsed.ElapsedMilliseconds < 500) return false;
                firstResultAt = elapsed.Elapsed;
                return true;
            }, _ => { }, TimeSpan.FromSeconds(1), CancellationToken.None, TimeSpan.FromSeconds(1));
            Assert.True(forced);
            Assert.NotNull(firstResultAt);
            Assert.True(elapsed.Elapsed - firstResultAt.Value >= TimeSpan.FromSeconds(1));
            Assert.True(process.HasExited);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    [Fact]
    public void MissingResult_TimesOutInsteadOfClosingAsSuccess()
    {
        using var process = StartWaitingProcess();
        try
        {
            Assert.Throws<TimeoutException>(() => FurMarkBenchmarkProcess.WaitForExit(process,
                () => false, _ => { }, TimeSpan.FromMilliseconds(400), CancellationToken.None));
            Assert.True(process.WaitForExit(5000));
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    [Fact]
    public void Cancellation_DuringResultDisplay_Propagates()
    {
        using var process = StartWaitingProcess();
        using var cts = new CancellationTokenSource();
        using var cancelProcess = cts.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });
        try
        {
            Assert.Throws<OperationCanceledException>(() => FurMarkBenchmarkProcess.WaitForExit(process,
                () => true, _ => cts.Cancel(), TimeSpan.FromSeconds(30), cts.Token));
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    [Fact]
    public void ExternalExit_DoesNotWaitForResultDisplayOrCountAsAutomaticClose()
    {
        using var process = StartWaitingProcess();
        try
        {
            bool forced = FurMarkBenchmarkProcess.WaitForExit(process, () => true,
                _ => process.Kill(entireProcessTree: true), TimeSpan.FromSeconds(30), CancellationToken.None);
            Assert.False(forced);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
