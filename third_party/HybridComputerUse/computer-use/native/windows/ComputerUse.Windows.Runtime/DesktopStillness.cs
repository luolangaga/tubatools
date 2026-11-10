using System.Diagnostics;

namespace ComputerUse.Windows.Runtime;

internal static class DesktopStillness
{
    internal sealed record Result<T>(T Sample, bool Quiescent);

    internal static Result<T> Observe<T>(
        int settleMilliseconds,
        Func<T> capture,
        Func<T, T, bool> identical,
        Func<TimeSpan>? clock = null,
        Action<TimeSpan>? delay = null)
    {
        if (settleMilliseconds is < 0 or > 3000)
        {
            throw new ArgumentOutOfRangeException(nameof(settleMilliseconds));
        }
        var timer = Stopwatch.StartNew();
        clock ??= () => timer.Elapsed;
        delay ??= Thread.Sleep;
        var previous = capture();
        if (settleMilliseconds == 0)
        {
            return new(previous, false);
        }

        // Match Linux/macOS: four identical samples spanning at least 300ms.
        // Capture overhead is bounded by the provider, not a shortened timeout.
        var stableSince = clock();
        var deadline = stableSince + TimeSpan.FromMilliseconds(settleMilliseconds);
        var stableSamples = 1;
        while (true)
        {
            var remaining = deadline - clock();
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }
            delay(remaining < TimeSpan.FromMilliseconds(100)
                ? remaining : TimeSpan.FromMilliseconds(100));
            if (clock() >= deadline)
            {
                break;
            }
            var current = capture();
            var sampledAt = clock();
            if (identical(previous, current))
            {
                stableSamples++;
            }
            else
            {
                stableSince = sampledAt;
                stableSamples = 1;
            }
            previous = current;
            if (sampledAt >= deadline)
            {
                return new(current, false);
            }
            if (stableSamples >= 4 &&
                sampledAt - stableSince >= TimeSpan.FromMilliseconds(300))
            {
                return new(current, true);
            }
        }
        // A timed wait must end with a fresh image, including budgets too
        // short for a comparison. This final capture does not prove stillness.
        return new(capture(), false);
    }
}
