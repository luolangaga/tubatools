namespace ComputerUse.Windows.Runtime;

internal static class DesktopObservationRetry
{
    internal static T Execute<T>(
        Func<T> capture,
        Action<TimeSpan>? delay = null,
        Action<int, Exception>? onFailure = null)
    {
        return BoundedObservationRetry.Execute(
            capture,
            delay,
            onFailure ?? LogFailure);
    }

    private static void LogFailure(int attempt, Exception error)
    {
        Console.Error.WriteLine(
            "[provider] post-action desktop snapshot attempt " +
            $"{attempt} failed: {error.GetType().Name} " +
            $"HRESULT=0x{error.HResult:x8} message={error.Message}");
    }
}
