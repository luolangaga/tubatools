namespace ComputerUse.Windows.Runtime;

internal static class LiveValueReadback
{
    private const double RangeTolerance = 1e-9;

    // The application applies input asynchronously. Readback samples at
    // once and then every PollInterval until the value matches or Timeout
    // passes, instead of sleeping a fixed delay before one sample.
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);
    internal static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1000);

    private static bool Poll(Func<bool> matches, Action<TimeSpan>? delay)
    {
        delay ??= Thread.Sleep;
        var waited = TimeSpan.Zero;
        while (true)
        {
            bool matched;
            try
            {
                matched = matches();
            }
            catch
            {
                matched = false;
            }
            if (matched) { return true; }
            if (waited >= Timeout) { return false; }
            delay(PollInterval);
            waited += PollInterval;
        }
    }

    internal static bool Verify(
        string expected,
        Func<string?> readFreshValue,
        Action<TimeSpan>? delay = null)
    {
        return Poll(
            () => string.Equals(readFreshValue(), expected, StringComparison.Ordinal),
            delay);
    }

    internal static bool VerifyTextValue(
        string expected,
        Func<string?> readFreshValue,
        Func<string?> readFreshDocumentText,
        Action<TimeSpan>? delay = null)
    {
        return Poll(() =>
        {
            if (!HasLineBreak(expected) &&
                string.Equals(readFreshValue(), expected, StringComparison.Ordinal))
            {
                return true;
            }
            // A document's text is its content. Rich edit controls end it
            // with one paragraph mark, and after a typed replacement their
            // Value property can lag behind the text.
            var documentText = readFreshDocumentText();
            if (documentText is null)
            {
                return false;
            }
            var actual = NormalizeLineEndings(documentText);
            var wanted = NormalizeLineEndings(expected);
            return string.Equals(actual, wanted, StringComparison.Ordinal) ||
                actual.EndsWith('\n') &&
                string.Equals(actual[..^1], wanted, StringComparison.Ordinal);
        }, delay);
    }

    internal static string CanonicalTextValue(
        string valuePatternValue,
        string? documentText)
    {
        return documentText is not null && HasLineBreak(documentText)
            ? documentText
            : valuePatternValue;
    }

    internal static bool TextEquals(string actual, string expected) =>
        string.Equals(
            NormalizeLineEndings(actual),
            NormalizeLineEndings(expected),
            StringComparison.Ordinal);

    internal static bool RangeEquals(double actual, double expected) =>
        double.IsFinite(actual) &&
        Math.Abs(actual - expected) <= RangeTolerance;

    private static bool HasLineBreak(string value)
    {
        return value.Contains('\r') || value.Contains('\n');
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    internal static bool VerifyRange(
        double expected,
        Func<double?> readFreshValue,
        Action<TimeSpan>? delay = null)
    {
        return Poll(() =>
        {
            var actual = readFreshValue();
            return actual is not null && RangeEquals(actual.Value, expected);
        }, delay);
    }
}
