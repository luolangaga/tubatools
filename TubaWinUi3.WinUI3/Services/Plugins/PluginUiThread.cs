using Microsoft.UI.Dispatching;

namespace TubaWinUi3.Services.Plugins;

internal static class PluginUiThread
{
    internal static async Task RunAsync(DispatcherQueue dispatcher, Func<Task> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (dispatcher.HasThreadAccess) { await action(); return; }
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = ct.Register(() => source.TrySetCanceled(ct));
        if (!dispatcher.TryEnqueue(async () =>
        {
            try { ct.ThrowIfCancellationRequested(); await action(); source.TrySetResult(); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { source.TrySetCanceled(ct); }
            catch (Exception ex) { source.TrySetException(ex); }
        })) throw new InvalidOperationException("界面调度器已关闭。");
        await source.Task;
    }
}
