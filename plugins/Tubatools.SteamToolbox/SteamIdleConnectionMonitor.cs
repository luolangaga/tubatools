namespace Tubatools.SteamToolbox;

// Independent of the page lifetime: a hidden/background idling session must still be checked.
internal static class SteamIdleConnectionMonitor
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    internal static async Task RunAsync(Func<CancellationToken, Task> probe, Action confirmed,
        Action<Exception> lost, CancellationToken token, TimeSpan? interval = null, TimeSpan? timeout = null)
    {
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                await probe(token).WaitAsync(timeout ?? Timeout, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                confirmed();
                await Task.Delay(interval ?? Interval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (!token.IsCancellationRequested) lost(e);
        }
    }
}
