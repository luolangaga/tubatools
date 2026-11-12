using System.Diagnostics;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class FurMarkBenchmarkProcessTests
{
    private static Process StartWaitingProcess(int seconds = 30) => Process.Start(new ProcessStartInfo(
        "powershell.exe", $"-NoProfile -NonInteractive -Command Start-Sleep -Seconds {seconds}")
    {
        UseShellExecute = false, CreateNoWindow = true
    })!;

    [Fact]
    public void LingeringScoreWindow_IsTerminatedAtTimeout()
    {
        using var process = StartWaitingProcess();
        var elapsed = Stopwatch.StartNew();
        try
        {
            bool forced = FurMarkBenchmarkProcess.WaitForExit(process, TimeSpan.FromSeconds(1), CancellationToken.None);
            Assert.True(forced);
            Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(1));
            Assert.True(process.HasExited);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    [Fact]
    public void ExternalExit_DoesNotCountAsTermination()
    {
        using var process = StartWaitingProcess(1);
        try
        {
            bool forced = FurMarkBenchmarkProcess.WaitForExit(process, TimeSpan.FromSeconds(30), CancellationToken.None);
            Assert.False(forced);
            Assert.True(process.HasExited);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    [Fact]
    public void Cancellation_Propagates()
    {
        using var process = StartWaitingProcess();
        using var cts = new CancellationTokenSource();
        using var cancelProcess = cts.Token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });
        try
        {
            Assert.Throws<OperationCanceledException>(() => FurMarkBenchmarkProcess.WaitForExit(
                process, TimeSpan.FromSeconds(30), cts.Token, _ => cts.Cancel()));
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
