using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// 摄像头帧源：MediaCapture + MediaFrameReader（CPU 内存模式），只保留最新帧（丢旧帧）。
/// 需要在调用前初始化（<see cref="InitializeAsync"/>）；Dispose 释放摄像头。
/// </summary>
public sealed class CameraFrameSource : IFrameSource
{
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private readonly object _gate = new();
    private byte[]? _latest;
    private int _w, _h, _stride;
    private bool _hasFrame;
    private bool _disposed;

    public string DisplayName => "Camera";
    public (int Width, int Height) Size => (_w, _h);

    /// <summary>初始化摄像头并开始取帧。失败抛异常（调用方提示用户检查设备/隐私权限）。</summary>
    public async Task InitializeAsync()
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        var group = groups.FirstOrDefault()
            ?? throw new InvalidOperationException("未找到摄像头设备。");

        _capture = new MediaCapture();
        await _capture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            SourceGroup = group,
            SharingMode = MediaCaptureSharingMode.SharedReadOnly,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
        });

        var frameSource = _capture.FrameSources.Values
            .FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
            ?? throw new InvalidOperationException("摄像头没有可用的彩色帧源。");

        _reader = await _capture.CreateFrameReaderAsync(frameSource, MediaEncodingSubtypes.Bgra8);
        _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
        _reader.FrameArrived += OnFrameArrived;
        await _reader.StartAsync();
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null) return;

            using var converted = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                ? null
                : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var src = converted ?? bitmap;

            int w = src.PixelWidth, h = src.PixelHeight;
            var buffer = new byte[w * h * 4];
            src.CopyToBuffer(buffer.AsBuffer());
            var stride = w * 4;

            lock (_gate)
            {
                _latest = buffer;
                _w = w;
                _h = h;
                _stride = stride;
                _hasFrame = true;
            }
        }
        catch { }
    }

    public bool TryGrab(out FrameBuffer frame)
    {
        frame = null!;
        if (_disposed) return false;
        lock (_gate)
        {
            if (!_hasFrame || _latest is null) return false;
            _hasFrame = false;   // 已消费，等待下一帧
            frame = new FrameBuffer { Bgra = _latest, Width = _w, Height = _h, Stride = _stride };
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_reader is not null)
            {
                _reader.FrameArrived -= OnFrameArrived;
                try { _reader.StopAsync().AsTask().Wait(1500); } catch { }
                _reader.Dispose();
            }
        }
        catch { }
        try { _capture?.Dispose(); } catch { }
        _reader = null;
        _capture = null;
    }
}

/// <summary>把 byte[] 适配为 WinRT IBuffer。</summary>
internal static class BufferExtensions
{
    public static Windows.Storage.Streams.IBuffer AsBuffer(this byte[] data) =>
        System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(data);
}
