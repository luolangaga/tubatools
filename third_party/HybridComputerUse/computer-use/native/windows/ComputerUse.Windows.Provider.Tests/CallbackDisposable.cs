namespace ComputerUse.Windows.Runtime;

internal sealed class CallbackDisposable(Action dispose) : IDisposable
{
    public void Dispose()
    {
        dispose();
    }
}
