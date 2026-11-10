namespace ComputerUse.Windows.Runtime;

internal static class ExactFocusSettlementTests
{
    internal static void Run()
    {
        var clock = TimeSpan.Zero;
        var reads = 0;
        var delays = 0;
        void Delay(TimeSpan value) { clock += value; delays++; }
        void Check(bool value, string message)
        {
            if (!value) { throw new InvalidDataException(message); }
        }

        Check(ExactFocusSettlement.Verify(
            () => { reads++; return true; }, () => clock, Delay),
            "Synchronous exact focus must succeed.");
        Check(reads == 1 && delays == 0, "Synchronous focus must not wait.");

        reads = 0;
        Check(ExactFocusSettlement.Verify(
            () => { reads++; return clock >= TimeSpan.FromMilliseconds(50); },
            () => clock, Delay), "Same-object deferred focus must succeed.");
        Check(reads == 4 && clock == TimeSpan.FromMilliseconds(60),
            "Deferred focus must sample without repeating the mutation.");

        clock = TimeSpan.Zero;
        reads = 0;
        delays = 0;
        Check(!ExactFocusSettlement.Verify(
            () => { reads++; return false; }, () => clock, Delay),
            "A replacement or unfocused original must never be accepted.");
        Check(clock == ExactFocusSettlement.Budget && reads == 31 && delays == 30,
            "Failed focus must respect both deadline and sample bound.");

        reads = 0;
        Check(!ExactFocusSettlement.Verify(
            () => { reads++; return false; }, () => TimeSpan.Zero, _ => { }),
            "A stalled test clock must not allow unlimited sampling.");
        Check(reads == 31, "Sampling must have an independent count bound.");

        clock = TimeSpan.Zero;
        reads = 0;
        Check(!ExactFocusSettlement.Verify(
            () => { reads++; return reads > 1; },
            () => clock, _ => clock = TimeSpan.FromSeconds(1)),
            "Do not accept focus obtained after the budget.");
        Check(reads == 1, "Do not sample again after the deadline.");

        clock = TimeSpan.Zero;
        Check(!ExactFocusSettlement.Verify(
            () => { clock = TimeSpan.FromMilliseconds(650); return true; },
            () => clock, Delay),
            "A callback returning success after the budget must be rejected.");

        var expected = new InvalidOperationException("retired original object");
        try
        {
            ExactFocusSettlement.Verify(() => throw expected, () => TimeSpan.Zero, _ => { });
            throw new InvalidDataException("Object access exceptions must fail closed.");
        }
        catch (InvalidOperationException error) when (ReferenceEquals(error, expected))
        {
        }
    }
}
