using System.Globalization;

namespace ComputerUse.Windows.Runtime;

// A process generation distinguishes a process from a later one that reuses
// its PID. It is the process start time; without it no reference is issued.
internal static class WindowsProcessIdentity
{
    internal static string? ReadGeneration(Func<DateTime> readStartTime)
    {
        try
        {
            var startTime = readStartTime();
            if (startTime.Ticks == 0) { return null; }
            var ticks = startTime.ToUniversalTime().Ticks;
            return ticks > 0 ? ticks.ToString(CultureInfo.InvariantCulture) : null;
        }
        catch
        {
            // A PID alone cannot distinguish a replacement process.
            return null;
        }
    }
}
