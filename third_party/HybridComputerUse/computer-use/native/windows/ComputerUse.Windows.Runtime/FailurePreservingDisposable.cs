namespace ComputerUse.Windows.Runtime;

internal sealed class FailurePreservingDisposable : IDisposable
{
    private IDisposable? _inner;
    private readonly Action<Exception> _onFailure;

    internal FailurePreservingDisposable(
        IDisposable inner,
        Action<Exception> onFailure)
    {
        _inner = inner;
        _onFailure = onFailure;
    }

    public void Dispose()
    {
        var inner = Interlocked.Exchange(ref _inner, null);
        if (inner is null)
        {
            return;
        }
        try
        {
            inner.Dispose();
        }
        catch (Exception error)
        {
            try
            {
                _onFailure(error);
            }
            catch
            {
                // Cleanup diagnostics must not replace the primary result.
            }
        }
    }
}
