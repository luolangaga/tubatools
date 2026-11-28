using System.ComponentModel;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace Tubatools.MinecraftLauncher.Services;

/// <summary>把插件的文件下载/安装任务接入图吧工具箱的统一下载队列。</summary>
internal static class McUnifiedDownloads
{
    public static async Task<string> DownloadFileAsync(string displayName, string url, string destinationFolder,
        string fileName, long size, CancellationToken ct)
    {
        var item = DownloadQueueService.EnqueueMultiFile(
            displayName,
            _ => Task.FromResult(new List<ResolvedDownloadUrl> { new(url, fileName, size) }),
            destinationFolder,
            description: "我的世界启动器",
            glyph: "\uE896",
            tag: "minecraft-launcher");
        await WaitAsync(item, ct);
        return Path.Combine(destinationFolder, fileName);
    }

    public static async Task<T> RunTaskAsync<T>(string displayName, string description, string destinationFolder,
        Func<IProgress<string>, CancellationToken, Task<T>> work, IProgress<string>? status, CancellationToken ct)
    {
        T result = default!;
        var processor = new DelegateProcessor(async (queueStatus, token) =>
        {
            result = await work(queueStatus, token);
        }, status);
        var item = DownloadQueueService.EnqueueMultiFile(
            displayName,
            _ => Task.FromResult(new List<ResolvedDownloadUrl>()),
            destinationFolder,
            processor,
            description,
            "\uE896",
            "minecraft-launcher");
        await WaitAsync(item, ct);
        return result;
    }

    private static async Task WaitAsync(DownloadItem item, CancellationToken ct)
    {
        if (item.State == DownloadItemState.Completed) return;
        if (item.State == DownloadItemState.Failed)
            throw new InvalidOperationException(item.ErrorMessage ?? "统一下载队列任务失败。");
        if (item.State == DownloadItemState.Cancelled) throw new OperationCanceledException(ct);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(DownloadItem.State)) return;
            if (item.State is DownloadItemState.Completed or DownloadItemState.Failed or DownloadItemState.Cancelled)
                completion.TrySetResult();
        }

        item.PropertyChanged += OnChanged;
        using var registration = ct.Register(() =>
        {
            DownloadQueueService.Cancel(item.Id);
            completion.TrySetCanceled(ct);
        });
        try
        {
            OnChanged(item, new PropertyChangedEventArgs(nameof(DownloadItem.State)));
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            item.PropertyChanged -= OnChanged;
        }

        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
        if (item.State == DownloadItemState.Failed)
            throw new InvalidOperationException(item.ErrorMessage ?? "统一下载队列任务失败。");
        if (item.State == DownloadItemState.Cancelled) throw new OperationCanceledException(ct);
    }

    private sealed class DelegateProcessor(
        Func<IProgress<string>, CancellationToken, Task> work,
        IProgress<string>? status) : IDownloadPostProcessor
    {
        public string DisplayName => "我的世界安装任务";

        public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
            IProgress<string>? statusProgress, CancellationToken ct)
        {
            await work(new Progress<string>(message =>
            {
                status?.Report(message);
                statusProgress?.Report(message);
            }), ct);
        }
    }
}
