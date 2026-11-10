using System.Diagnostics;

namespace ComputerUse.Windows.Runtime;

internal enum FocusPreparationState
{
    RequestAttemptedOutcomeUnknown,
}

internal static class ExactFocusSettlement
{
    internal static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(20);

    internal static void RequestAndVerify(
        Action requestFocus,
        Func<bool> sameObjectHasFocus,
        Func<TimeSpan>? elapsed = null,
        Action<TimeSpan>? delay = null,
        Action? verifyOwnership = null)
    {
        try
        {
            // A throwing request can still have changed or queued focus.
            requestFocus();
            if (Verify(sameObjectHasFocus, elapsed, delay))
            {
                verifyOwnership?.Invoke();
                if (verifyOwnership is null || sameObjectHasFocus()) { return; }
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                "[provider] Exact focus preparation failed: " +
                $"{error.GetType().Name} HRESULT=0x{error.HResult:x8}");
        }
        throw new ProviderFailureException(
            "focus_verification_failed",
            "emission",
            "A focus request was attempted, but exact target focus and live " +
            "window ownership were not confirmed. No requested keys " +
            "were emitted. Focus may already have changed or may change " +
            "later. Stop; do not replay or observe.",
            inputEventsEmitted: false,
            mutationUnknown: true,
            factsComplete: false,
            focusPreparation:
                FocusPreparationState.RequestAttemptedOutcomeUnknown);
    }

    internal static bool Verify(
        Func<bool> sameObjectHasFocus,
        Func<TimeSpan>? elapsed = null,
        Action<TimeSpan>? delay = null)
    {
        var watch = Stopwatch.StartNew();
        elapsed ??= () => watch.Elapsed;
        delay ??= Thread.Sleep;
        // This callback only compares the original object. It must never
        // resolve another target or repeat the preceding focus mutation.
        for (var sample = 0; sample <= 30; sample++)
        {
            if (sample > 0 && elapsed() > Budget)
            {
                return false;
            }
            if (sameObjectHasFocus())
            {
                return elapsed() <= Budget;
            }
            var remaining = Budget - elapsed();
            if (remaining <= TimeSpan.Zero || sample == 30)
            {
                return false;
            }
            delay(remaining < Interval ? remaining : Interval);
        }
        return false;
    }
}
