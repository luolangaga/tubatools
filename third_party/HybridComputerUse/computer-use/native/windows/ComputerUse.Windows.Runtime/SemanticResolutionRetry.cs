namespace ComputerUse.Windows.Runtime;

internal static class SemanticResolutionRetry
{
    internal const int MaxAttempts = 3;
    internal static readonly TimeSpan RetryDelay =
        TimeSpan.FromMilliseconds(120);

    internal static T? Resolve<T>(
        bool enabled,
        Func<int, T?> sample,
        Action<TimeSpan>? delay = null)
        where T : class
    {
        return ResolveWithContext<int, T>(
            enabled,
            attempt => attempt,
            (attempt, _) => sample(attempt),
            delay);
    }

    internal static TResult? ResolveWithContext<TContext, TResult>(
        bool enabled,
        Func<int, TContext> acquireContext,
        Func<int, TContext, TResult?> sample,
        Action<TimeSpan>? delay = null)
        where TResult : class
    {
        var attempts = enabled ? MaxAttempts : 1;
        var wait = delay ?? Thread.Sleep;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var context = acquireContext(attempt);
            var resolved = sample(attempt, context);
            if (resolved is not null)
            {
                return resolved;
            }
            if (attempt + 1 < attempts)
            {
                wait(RetryDelay);
            }
        }
        return null;
    }
}
