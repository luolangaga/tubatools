namespace ComputerUse.Windows.Runtime;

internal static class BoundedObservationRetry
{
    private static readonly TimeSpan[] Delays =
    {
        TimeSpan.FromMilliseconds(75),
        TimeSpan.FromMilliseconds(150),
    };

    internal static T Execute<T>(
        Func<T> operation,
        Action<TimeSpan>? delay = null,
        Action<int, Exception>? onFailure = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        delay ??= Thread.Sleep;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (Exception error)
            {
                onFailure?.Invoke(attempt, error);
                if (attempt > Delays.Length)
                {
                    throw;
                }
                delay(Delays[attempt - 1]);
            }
        }
    }
}
