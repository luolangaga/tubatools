using System.Drawing;
using System.Runtime.InteropServices;
using ComputerUse.Windows.Runtime;

if (args.Length != 0) { Console.Error.WriteLine("Unknown arguments"); return 2; }

var window = new Rectangle(100, 100, 800, 600);
var inside = new Rectangle(120, 140, 400, 240);
var outside = new Rectangle(1000, 1000, 400, 240);
var tests = new (string Name, Action Run)[]
{
    ("supported-false-preserved", () => CheckSupported(false)),
    ("supported-true-preserved", () => CheckSupported(true)),
    ("on-screen-without-geometry", () => CheckEffective(false, false, Rectangle.Empty, true)),
    ("focused-offscreen-intersection", () => CheckEffective(true, true, inside, true)),
    ("unfocused-offscreen", () => CheckEffective(true, false, inside, false)),
    ("focused-offscreen-outside", () => CheckEffective(true, true, outside, false)),
    ("focused-offscreen-empty", () => CheckEffective(true, true, Rectangle.Empty, false)),
    ("unsupported-default-false-ignored", () => CheckUnsupported(false)),
    ("unsupported-out-true-ignored", () => CheckUnsupported(true)),
    ("read-error-remains-unknown", () =>
    {
        var read = WindowsVisibilityRead.Read(() =>
            throw new COMException(new string('x', 10000), unchecked((int)0x80004005)));
        Equal(WindowsVisibilityReadState.ReadError, read.State);
        Equal("read_error", read.Reason);
        Equal("System.Runtime.InteropServices.COMException", read.ExceptionType);
        Equal("0x80004005", read.HResult);
        CheckUnknown(read);
    }),
    ("exactly-one-read-no-projection-reread", () =>
    {
        foreach (var outcome in new[] { "supported", "unsupported", "error" })
        {
            var calls = 0;
            var read = WindowsVisibilityRead.Read(() =>
            {
                calls++;
                if (outcome == "error") { throw new InvalidOperationException(); }
                return (outcome == "supported", false);
            });
            _ = read.EffectiveVisibility(false, inside, window);
            _ = read.EffectiveVisibility(true, inside, window);
            Equal(1, calls);
        }
    }),
    ("normalized-unsupported-does-not-invent-error", () =>
    {
        // Models only the callback's observed result, not native normalization.
        var read = WindowsVisibilityRead.Read(() => (false, false));
        Equal(WindowsVisibilityReadState.Unsupported, read.State);
        Equal<string?>(null, read.ExceptionType);
        Equal<string?>(null, read.HResult);
    }),
    ("client-error-not-inner-provider-cause", () =>
    {
        var clientError = new InvalidOperationException(
            "Private client message", new COMException("Private inner message"));
        var read = WindowsVisibilityRead.Read(() => throw clientError);
        Equal(typeof(InvalidOperationException).FullName, read.ExceptionType);
        Equal("0x80131509", read.HResult);
        CheckUnknown(read);
    }),
    ("exception-type-bounded-null-callback-rejected", () =>
    {
        var error = new ExceptionTypeNamesLongEnoughToExerciseTheExplicitMetadataLimit
            .NestedClientExceptionWithAnIntentionallyLongNameForTheBoundCheckBeyondOneHundredTwentyEight();
        var type = error.GetType().FullName!;
        Require(type.Length > 128, "Test exception type must exceed the bound");
        var read = WindowsVisibilityRead.Read(() => throw error);
        Equal(type[..128], read.ExceptionType);
        Equal(128, read.ExceptionType!.Length);
        try { WindowsVisibilityRead.Read(null!); }
        catch (ArgumentNullException) { return; }
        throw new InvalidOperationException("Null callback accepted");
    }),
};

var failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error)
    {
        failures++;
        Console.WriteLine($"FAIL {test.Name}: {error}");
    }
}
Console.WriteLine($"pure-policy tests: passed={tests.Length - failures}/{tests.Length}; no UIA/VM");
return failures == 0 ? 0 : 1;

void CheckSupported(bool value)
{
    var read = WindowsVisibilityRead.Read(() => (true, value));
    Equal(WindowsVisibilityReadState.Supported, read.State);
    Equal<bool?>(value, read.IsOffscreen);
    Equal<string?>(null, read.Reason);
    Equal<string?>(null, read.ExceptionType);
    Equal<string?>(null, read.HResult);
}

void CheckEffective(bool offscreen, bool focused, Rectangle bounds, bool expected)
{
    var read = WindowsVisibilityRead.Read(() => (true, offscreen));
    Equal<bool?>(expected, read.EffectiveVisibility(focused, bounds, window));
    Equal<bool?>(WindowsUiaVisibility.IsEffectivelyVisible(
        offscreen, focused, bounds, window), read.EffectiveVisibility(focused, bounds, window));
    Equal<bool?>(offscreen, read.IsOffscreen);
}

void CheckUnsupported(bool outValue)
{
    var read = WindowsVisibilityRead.Read(() => (false, outValue));
    Equal(WindowsVisibilityReadState.Unsupported, read.State);
    Equal("unsupported", read.Reason);
    CheckUnknown(read);
}

void CheckUnknown(WindowsVisibilityRead read)
{
    Equal<bool?>(null, read.IsOffscreen);
    foreach (var focused in new[] { false, true })
    {
        foreach (var bounds in new[] { inside, outside, Rectangle.Empty })
        {
            Equal<bool?>(null, read.EffectiveVisibility(focused, bounds, window));
        }
    }
}

static void Equal<T>(T expected, T actual)
{
    Require(EqualityComparer<T>.Default.Equals(expected, actual),
        $"Expected {expected}, observed {actual}");
}

static void Require(bool condition, string message)
{
    if (!condition) { throw new InvalidOperationException(message); }
}

internal static class ExceptionTypeNamesLongEnoughToExerciseTheExplicitMetadataLimit
{
    internal sealed class NestedClientExceptionWithAnIntentionallyLongNameForTheBoundCheckBeyondOneHundredTwentyEight : Exception;
}
