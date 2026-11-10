using System.Collections.Concurrent;
using System.Threading.Channels;
using TubaWinUi3.Services.AiPlayground;
using Windows.Media.Capture.Frames;

namespace TubaWinUi3.Services;

internal sealed record PhoneCameraInfo(string Id, string Name, int Index);

/// <summary>一次 MJPEG 订阅：读到的都是完整 JPEG 帧；慢客户端只保留最新帧。</summary>
internal sealed class CameraStreamSubscription : IDisposable
{
    private readonly CameraSession _session;

    internal CameraStreamSubscription(CameraSession session, ChannelReader<byte[]> frames)
    {
        _session = session;
        Frames = frames;
    }

    public ChannelReader<byte[]> Frames { get; }

    public void Dispose() => _session.Release(this);
}

/// <summary>
/// PC 摄像头直播：按摄像头共享采集会话，首个订阅启动，最后一个订阅离开 5 秒后关闭。
/// 同一摄像头只允许一个推流订阅，避免多路编码把 CPU 打满。
/// </summary>
internal static class PhoneCameraService
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CameraSession> Sessions = [];
    private static readonly object CacheGate = new();
    private static List<PhoneCameraInfo> _cached = [];
    private static DateTime _cachedAt;
    private static string? _activeCameraName;

    public static bool Enabled => AppSettings.GetBool("PhoneLinkCameraEnabled");

    /// <summary>推流开始/结束/开关变化（可能在后台线程触发）。</summary>
    public static event Action? StreamStateChanged;

    public static string? ActiveCameraName
    {
        get { lock (Gate) return _activeCameraName; }
    }

    public static void SetEnabled(bool enabled)
    {
        AppSettings.Set("PhoneLinkCameraEnabled", enabled);
        if (enabled) return;

        List<CameraSession> sessions;
        lock (Gate)
        {
            sessions = Sessions.Values.ToList();
            Sessions.Clear();
            _activeCameraName = null;
        }
        foreach (var session in sessions)
        {
            // 释放摄像头会等待 MediaFrameReader.StopAsync（最多 1.5 秒），不能卡 UI 线程
            _ = Task.Run(session.Dispose);
        }
        StreamStateChanged?.Invoke();
    }

    public static async Task<IReadOnlyList<PhoneCameraInfo>> ListAsync(bool force = false)
    {
        lock (CacheGate)
        {
            if (!force && DateTime.UtcNow - _cachedAt < TimeSpan.FromSeconds(10)) return _cached;
        }

        var groups = await MediaFrameSourceGroup.FindAllAsync();
        var list = groups
            .Select((group, index) => new PhoneCameraInfo(
                group.Id,
                string.IsNullOrWhiteSpace(group.DisplayName) ? $"摄像头 {index + 1}" : group.DisplayName,
                index))
            .ToList();
        lock (CacheGate)
        {
            _cached = list;
            _cachedAt = DateTime.UtcNow;
        }
        return list;
    }

    public static async Task<CameraStreamSubscription?> SubscribeAsync(
        int index, CameraStreamOptions options, CancellationToken ct)
    {
        var group = await ResolveGroupAsync(index, ct);
        CameraSession session;
        lock (Gate)
        {
            if (Sessions.TryGetValue(group.Id, out var existing))
            {
                if (existing.HasSubscriber) return null;
                session = existing;
            }
            else
            {
                session = new CameraSession(group, options);
                Sessions[group.Id] = session;
            }
        }

        try
        {
            await session.StartAsync().WaitAsync(ct);
        }
        catch
        {
            lock (Gate)
            {
                if (Sessions.TryGetValue(group.Id, out var current) && ReferenceEquals(current, session))
                    Sessions.Remove(group.Id);
            }
            session.Dispose();
            throw;
        }

        var subscription = session.CreateSubscription(options);
        if (subscription is null)
        {
            session.Dispose();
            return null;
        }
        lock (Gate) _activeCameraName = session.CameraName;
        StreamStateChanged?.Invoke();
        return subscription;
    }

    public static async Task<byte[]> SnapshotAsync(int index, int width, int quality, CancellationToken ct)
    {
        var group = await ResolveGroupAsync(index, ct);
        lock (Gate)
        {
            if (Sessions.TryGetValue(group.Id, out var active) &&
                active.TryGetLastJpeg(width, quality, out var cached))
            {
                return cached;
            }
        }

        using var source = new CameraFrameSource();
        await source.InitializeAsync(group);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (source.TryGrab(out var frame)) return CameraJpegEncoder.Encode(frame, width, quality);
            await Task.Delay(30, ct);
        }
        throw new InvalidOperationException("摄像头没有画面，请检查是否被其他程序占用。");
    }

    private static async Task<MediaFrameSourceGroup> ResolveGroupAsync(int index, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        if (index < 0 || index >= groups.Count)
            throw new InvalidOperationException("摄像头不存在，请在电脑上刷新摄像头列表。");
        return groups[index];
    }

    internal static void RemoveSession(CameraSession session)
    {
        lock (Gate)
        {
            if (Sessions.TryGetValue(session.CameraId, out var current) && ReferenceEquals(current, session))
                Sessions.Remove(session.CameraId);
            _activeCameraName = Sessions.Values.FirstOrDefault()?.CameraName;
        }
        StreamStateChanged?.Invoke();
    }
}

internal sealed class CameraSession : IDisposable
{
    private readonly CameraStreamOptions _options;
    private readonly CameraFrameSource _source = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<CameraStreamSubscription, Channel<byte[]>> _subscribers = new();
    private readonly object _startGate = new();
    private readonly object _frameGate = new();
    private FrameBuffer? _lastFrame;
    private Task? _startTask;
    private Task? _loop;
    private long _generation;
    private bool _disposed;
    private DateTime _lastErrorLog;

    public CameraSession(MediaFrameSourceGroup group, CameraStreamOptions options)
    {
        Group = group;
        _options = options;
        CameraName = string.IsNullOrWhiteSpace(group.DisplayName) ? "摄像头" : group.DisplayName;
    }

    public MediaFrameSourceGroup Group { get; }
    public string CameraId => Group.Id;
    public string CameraName { get; }
    public bool HasSubscriber => !_subscribers.IsEmpty;

    public Task StartAsync()
    {
        lock (_startGate) return _startTask ??= StartCoreAsync();
    }

    private async Task StartCoreAsync()
    {
        await _source.InitializeAsync(Group);
        _loop = Task.Run(RunAsync);
    }

    public CameraStreamSubscription? CreateSubscription(CameraStreamOptions options)
    {
        if (_disposed || !options.Equals(_options)) return null;
        var channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        var subscription = new CameraStreamSubscription(this, channel.Reader);
        _subscribers[subscription] = channel;
        return subscription;
    }

    public void Release(CameraStreamSubscription subscription)
    {
        if (_subscribers.TryRemove(subscription, out var channel)) channel.Writer.TryComplete();
        if (!_subscribers.IsEmpty) return;

        var generation = Interlocked.Increment(ref _generation);
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (_disposed || _generation != generation || !_subscribers.IsEmpty) return;
            Dispose();
            PhoneCameraService.RemoveSession(this);
        });
    }

    public bool TryGetLastJpeg(int width, int quality, out byte[] jpeg)
    {
        FrameBuffer? frame;
        lock (_frameGate) frame = _lastFrame;
        if (frame is null)
        {
            jpeg = [];
            return false;
        }
        jpeg = CameraJpegEncoder.Encode(frame, width, quality);
        return true;
    }

    private async Task RunAsync()
    {
        var interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _options.Fps));
        while (!_cts.IsCancellationRequested)
        {
            var startedAt = DateTime.UtcNow;
            try
            {
                if (!_source.TryGrab(out var frame))
                {
                    await Task.Delay(3, _cts.Token);
                    continue;
                }
                lock (_frameGate) _lastFrame = frame;
                if (_subscribers.IsEmpty) continue;

                var jpeg = CameraJpegEncoder.Encode(frame, _options.Width, _options.Quality);
                foreach (var channel in _subscribers.Values) channel.Writer.TryWrite(jpeg);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (DateTime.UtcNow - _lastErrorLog > TimeSpan.FromSeconds(5))
                {
                    _lastErrorLog = DateTime.UtcNow;
                    LogService.Warn(LogService.PhoneLink, $"摄像头采集/编码失败：{ex.Message}");
                }
            }

            var wait = interval - (DateTime.UtcNow - startedAt);
            if (wait <= TimeSpan.Zero) continue;
            try { await Task.Delay(wait, _cts.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        foreach (var channel in _subscribers.Values) channel.Writer.TryComplete();
        _subscribers.Clear();
        try { _source.Dispose(); } catch { }
        try { _cts.Dispose(); } catch { }
        _ = _loop;
    }
}
