using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

/// <summary>Bounded idle deferral and one maintenance job at a time.</summary>
internal static class StartupMaintenance
{
    private static readonly SemaphoreSlim Gate = new(1);
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInput { public uint Size, Tick; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInput info);

    internal static async Task ScheduleAsync(string name, TimeSpan delay, Func<Task> work, bool skipInFastMode = true)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        if (App.IsExiting || (skipInFastMode && FastModeService.IsFastModeEnabled())) return;
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var waiting = Stopwatch.StartNew();
            while (!App.IsExiting && waiting.Elapsed < TimeSpan.FromMinutes(2))
            {
                var input = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
                if (!GetLastInputInfo(ref input) || unchecked((uint)Environment.TickCount - input.Tick) >= 3000) break;
                await Task.Delay(1000).ConfigureAwait(false);
            }
            if (App.IsExiting || (skipInFastMode && FastModeService.IsFastModeEnabled())) return;
            var elapsed = Stopwatch.StartNew();
            await Task.Run(work).ConfigureAwait(false);
            LogService.Info(LogService.Startup, $"{name}: {elapsed.ElapsedMilliseconds} ms");
            await Task.Delay(2000).ConfigureAwait(false);
        }
        catch (Exception ex) { LogService.Warn(LogService.Startup, $"{name}: {ex.GetType().Name}"); }
        finally { Gate.Release(); }
    }
}
