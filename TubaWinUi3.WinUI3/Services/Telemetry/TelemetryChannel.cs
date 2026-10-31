using System.Collections.Concurrent;
using Microsoft.ApplicationInsights.Channel;
using TubaWinUi3.Services.Agent;

namespace TubaWinUi3.Services.Telemetry;

/// <summary>
/// 遥测通道：内存队列 + 后台批量上传 + 失败落盘续传。
/// 桌面应用没有常驻服务端，网络失败（尤其跨境采集链路）
/// 与进程随时退出都是常态，因此：
/// <list type="bullet">
/// <item>Send 永不阻塞、永不抛出，队列有上限，满了丢弃新条目（保内存）。</item>
/// <item>上传失败（临时故障）时把已序列化的批次落盘，下次启动或下一轮继续补传。</item>
/// <item>落盘目录同样有上限，超出丢最旧，避免长期离线把磁盘写满。</item>
/// </list>
/// </summary>
internal sealed class TelemetryChannel : ITelemetryChannel
{
    private const int MaxInMemoryItems = 2000;
    private const int MaxBatchItems = 100;
    private const int MaxSpoolFiles = 20;
    private const int MaxSpoolDrainPerCycle = 3;
    private const long MaxSpoolBytes = 4 * 1024 * 1024;

    private static readonly TimeSpan SendInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FlushWait = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentQueue<ITelemetry> _queue = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly TelemetryTransport _transport;
    private readonly string _spoolDirectory;
    private readonly Uri _trackUri;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    private int _queuedCount;
    private int _disposed;
    private long _droppedCount;
    private int _uploading;
    private int _discardPending;
    private DateTime _retryNotBeforeUtc;

    public TelemetryChannel(Uri trackUri, TelemetryTransport transport, string spoolDirectory)
    {
        EndpointAddress = trackUri.ToString();
        _transport = transport;
        _spoolDirectory = spoolDirectory;
        _trackUri = trackUri;
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public bool? DeveloperMode { get; set; }

    public string EndpointAddress { get; set; }

    /// <summary>因队列/落盘上限被丢弃的条目数（诊断用）。</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public void Send(ITelemetry item)
    {
        if (item is null || Volatile.Read(ref _disposed) != 0) return;

        if (Interlocked.Increment(ref _queuedCount) > MaxInMemoryItems)
        {
            Interlocked.Decrement(ref _queuedCount);
            Interlocked.Increment(ref _droppedCount);
            return;
        }

        _queue.Enqueue(item);
        Signal();
    }

    /// <summary>尽力发送完当前队列（有界等待），供退出前调用。</summary>
    public void Flush()
    {
        Signal();
        var deadline = DateTime.UtcNow + FlushWait;
        while ((Volatile.Read(ref _queuedCount) > 0 || Volatile.Read(ref _uploading) != 0)
               && DateTime.UtcNow < deadline)
            Thread.Sleep(50);
    }

    public void Dispose() => Stop(discardPending: false);

    internal void Stop(bool discardPending)
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (discardPending) Interlocked.Exchange(ref _discardPending, 1);
        else try { Flush(); } catch { }
        try { _cts.Cancel(); } catch { }
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch { }
        try { _transport.Dispose(); } catch { }
        try { _cts.Dispose(); } catch { }
        try { _wake.Dispose(); } catch { }
    }

    private void Signal()
    {
        try { _wake.Release(); } catch (SemaphoreFullException) { } catch (ObjectDisposedException) { }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _wake.WaitAsync(SendInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                try
                {
                    if (DateTime.UtcNow < _retryNotBeforeUtc) continue;
                    Interlocked.Exchange(ref _uploading, 1);
                    await DrainSpoolAsync(ct).ConfigureAwait(false);

                    var batch = DequeueBatch();
                    if (batch.Count > 0)
                        await UploadAsync(batch, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AgentDebugLog.Error("[Telemetry] 发送循环异常", ex);
                }
                finally { Interlocked.Exchange(ref _uploading, 0); }
            }
        }
        finally
        {
            // 正常退出取消上传时，也要持久化尚在内存中的条目。
            while (DequeueBatch() is { Count: > 0 } pending)
            {
                try { Spool(new Transmission(_trackUri, pending, UploadTimeout).Content); }
                catch { }
            }
        }
    }

    private List<ITelemetry> DequeueBatch()
    {
        var batch = new List<ITelemetry>(MaxBatchItems);
        while (batch.Count < MaxBatchItems && _queue.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _queuedCount);
            batch.Add(item);
        }
        return batch;
    }

    private async Task UploadAsync(List<ITelemetry> batch, CancellationToken ct)
    {
        try
        {
            var transmission = new Transmission(_trackUri, batch, UploadTimeout);
            var result = await _transport
                .PostAsync(transmission.Content, transmission.ContentEncoding, transmission.ContentType, ct)
                .ConfigureAwait(false);

            if (result == TelemetrySendResult.RetryLater)
            {
                Spool(transmission.Content);
                _retryNotBeforeUtc = DateTime.UtcNow.AddMinutes(1);
            }
        }
        catch (Exception ex)
        {
            AgentDebugLog.Error("[Telemetry] 序列化/上传失败", ex);
        }
    }

    /// <summary>把已序列化的批次写入落盘目录，等待后续补传。</summary>
    private void Spool(byte[] payload)
    {
        if (Volatile.Read(ref _discardPending) != 0) return;
        try
        {
            Directory.CreateDirectory(_spoolDirectory);
            TrimSpool();
            var name = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.bin";
            File.WriteAllBytes(Path.Combine(_spoolDirectory, name), payload);
        }
        catch { }
    }

    private async Task DrainSpoolAsync(CancellationToken ct)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(_spoolDirectory)) return;
            files = Directory.GetFiles(_spoolDirectory, "*.bin");
            // 文件名以时间戳开头，字典序即时间序
            Array.Sort(files, StringComparer.Ordinal);
        }
        catch
        {
            return;
        }

        var drained = 0;
        foreach (var file in files)
        {
            if (drained >= MaxSpoolDrainPerCycle || ct.IsCancellationRequested) return;

            byte[] payload;
            try { payload = File.ReadAllBytes(file); }
            catch { continue; }

            var result = await _transport.PostAsync(payload, "gzip", "application/x-json-stream", ct).ConfigureAwait(false);
            if (result == TelemetrySendResult.RetryLater)
            {
                _retryNotBeforeUtc = DateTime.UtcNow.AddMinutes(1);
                return; // 网络仍不通：本轮停止，保留文件待下次
            }

            TryDelete(file);
            drained++;
        }
    }

    private void TrimSpool()
    {
        var files = Directory.GetFiles(_spoolDirectory, "*.bin");
        if (files.Length == 0) return;
        Array.Sort(files, StringComparer.Ordinal);

        long total = 0;
        foreach (var file in files)
        {
            try { total += new FileInfo(file).Length; } catch { }
        }

        // 超量或超容：从最旧的开始删（新数据更有诊断价值）
        var index = 0;
        while (index < files.Length - 1
               && (files.Length - index > MaxSpoolFiles || total > MaxSpoolBytes))
        {
            try
            {
                total -= new FileInfo(files[index]).Length;
                File.Delete(files[index]);
                Interlocked.Increment(ref _droppedCount);
            }
            catch { }
            index++;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
