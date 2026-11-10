using ComputerUse.Windows.Runtime;

var cases = 0;

void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}

foreach (var budget in new[] { 0, 1, 100, 250, 300 })
{
    var now = TimeSpan.Zero;
    var captures = 0;
    var result = DesktopStillness.Observe(budget,
        () => ++captures, (_, _) => true, () => now, value => now += value);
    Check(!result.Quiescent, $"budget {budget} claimed stability");
    Check(result.Sample == captures, "did not retain the latest sample");
    Check(now == TimeSpan.FromMilliseconds(budget), "small budget was exceeded");
    if (budget <= 100)
        Check(captures == (budget == 0 ? 1 : 2), "missing final capture after wait");
    cases++;
}

{
    var now = TimeSpan.Zero;
    var result = DesktopStillness.Observe(100,
        () => now, (a, b) => a == b, () => now, value => now += value);
    Check(result.Sample == TimeSpan.FromMilliseconds(100), "returned a pre-wait screenshot");
    Check(!result.Quiescent, "final screenshot alone claimed stability");
    cases++;
}

foreach (var changingSamples in new[] { 0, 2, 100 })
{
    var now = TimeSpan.Zero;
    var captures = 0;
    var result = DesktopStillness.Observe(800,
        () => (Revision: ++captures, Pixel: Math.Min(captures, changingSamples + 1)),
        (a, b) => a.Pixel == b.Pixel, () => now, value => now += value);
    Check(result.Quiescent == (changingSamples < 100), "wrong stability outcome");
    Check(result.Sample.Revision == captures, "stale final image");
    var expected = changingSamples < 100 ? 300 + changingSamples * 100 : 800;
    Check(now == TimeSpan.FromMilliseconds(expected), "wrong settling duration");
    cases++;
}

foreach (var latency in new[] { 10, 500 })
{
    var now = TimeSpan.Zero;
    var captures = 0;
    var result = DesktopStillness.Observe(800,
        () =>
        {
            captures++;
            now += TimeSpan.FromMilliseconds(latency);
            return captures;
        }, (_, _) => true, () => now, value => now += value);
    Check(result.Quiescent == (latency == 10), "capture overhead ignored");
    Check(result.Sample == captures, "late capture result lost");
    if (latency == 500) Check(captures == 3, "capture started after budget");
    cases++;
}

foreach (var failAt in new[] { 1, 3 })
{
    var now = TimeSpan.Zero;
    var captures = 0;
    var failure = new InvalidOperationException("capture unavailable");
    try
    {
        DesktopStillness.Observe(800, () =>
        {
            if (++captures == failAt) throw failure;
            return captures;
        }, (_, _) => true, () => now, value => now += value);
        throw new Exception("capture failure was swallowed");
    }
    catch (InvalidOperationException error)
    {
        Check(ReferenceEquals(error, failure), "capture evidence replaced");
        Check(captures == failAt, "captured after failure");
    }
    cases++;
}

foreach (var budget in new[] { -1, 3001, int.MaxValue })
{
    try
    {
        DesktopStillness.Observe(budget,
            () => throw new Exception("captured invalid request"),
            (int a, int b) => a == b);
        throw new Exception("invalid budget accepted");
    }
    catch (ArgumentOutOfRangeException)
    {
        cases++;
    }
}

Console.WriteLine($"PASS {cases} desktop stillness cases");
