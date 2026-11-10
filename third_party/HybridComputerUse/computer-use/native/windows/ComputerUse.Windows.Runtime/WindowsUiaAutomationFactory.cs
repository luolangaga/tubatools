using FlaUI.UIA3;

namespace ComputerUse.Windows.Runtime;

internal static class WindowsUiaAutomationFactory
{
    internal static readonly TimeSpan TransactionTimeout =
        TimeSpan.FromSeconds(3);

    // Snapshot reads use a shorter cap per cross-process call. A provider
    // that does not answer within it costs one gap in the snapshot instead of
    // seconds per node; actions keep the longer cap because some providers
    // answer an invoke only after the handler returns.
    internal static readonly TimeSpan SnapshotReadTransactionTimeout =
        TimeSpan.FromMilliseconds(800);

    internal static UIA3Automation Create()
    {
        var automation = new UIA3Automation
        {
            TransactionTimeout = TransactionTimeout,
        };
        return automation;
    }

    internal static IDisposable SnapshotReadTimeout(UIA3Automation automation)
    {
        var previous = automation.TransactionTimeout;
        automation.TransactionTimeout = SnapshotReadTransactionTimeout;
        return new Restore(() => automation.TransactionTimeout = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
