using System.Diagnostics;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>实时检测循环：从帧源取最新帧 → 推理 → 回调结果。丢旧帧，只跑最新。</summary>
public sealed class DetectionLoop : IDisposable
{
    private readonly IFrameSource _source;
    private readonly Func<FrameBuffer, List<DetectionItem>> _detect;
    private readonly Action<List<DetectionItem>, FrameBuffer> _onResult;
    private CancellationTokenSource? _cts;
    private Task? _task;
    private readonly Stopwatch _fpsClock = Stopwatch.StartNew();
    private int _frames;
    private bool _disposed;

    /// <summary>最近一次实际处理（含推理）的毫秒数。</summary>
    public double LastInferenceMs { get; private set; }
    /// <summary>累计处理帧数。</summary>
    public int ProcessedFrames => _frames;
    public bool IsRunning => _task is { IsCompleted: false };

    public DetectionLoop(
        IFrameSource source,
        Func<FrameBuffer, List<DetectionItem>> detect,
        Action<List<DetectionItem>, FrameBuffer> onResult)
    {
        _source = source;
        _detect = detect;
        _onResult = onResult;
    }

    public void Start()
    {
        if (_disposed || IsRunning) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _task = Task.Run(async () =>
        {
            var sw = new Stopwatch();
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!_source.TryGrab(out var frame) || frame is null)
                    {
                        await Task.Delay(15, token).ConfigureAwait(false);
                        continue;
                    }

                    sw.Restart();
                    var items = _detect(frame);
                    sw.Stop();
                    LastInferenceMs = sw.Elapsed.TotalMilliseconds;
                    Interlocked.Increment(ref _frames);
                    _onResult(items, frame);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // 单帧失败不影响循环
                    await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }, token);
    }

    /// <summary>实时 FPS（调用方按时间间隔自行相除）。</summary>
    public double AverageFps
    {
        get
        {
            var secs = _fpsClock.Elapsed.TotalSeconds;
            return secs <= 0 ? 0 : _frames / secs;
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _task?.Wait(2000); } catch { }
        _cts?.Dispose();
        _cts = null;
        _task = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _source.Dispose();
    }
}
