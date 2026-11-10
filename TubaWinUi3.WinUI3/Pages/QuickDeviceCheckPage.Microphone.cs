using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.Capture;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Storage;

namespace TubaWinUi3.Pages;

public sealed partial class QuickDeviceCheckPage
{
    private const int MicRecordingSeconds = 5;
    private CancellationTokenSource? _micRecording;
    private StorageFile? _micClip;
    private MediaSource? _micSource;
    private MediaPlayer? _micPlayer;
    private DispatcherQueueTimer? _micPlaybackTimer;

    private void AddMicrophoneTest(StackPanel panel, Button speakerButton)
    {
        panel.Children.Add(Copy($"麦克风测试 · 使用 Windows 默认输入设备，录音 {MicRecordingSeconds} 秒。", true));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var record = new Button { Content = "开始录音" };
        var play = new Button { Content = "播放录音", IsEnabled = false };
        var status = Copy("点击开始录音后，对着麦克风正常说话。", true);
        var progress = new ProgressBar { Visibility = Visibility.Collapsed };
        AutomationProperties.SetName(progress, "麦克风测试进度");
        buttons.Children.Add(record);
        buttons.Children.Add(play);
        panel.Children.Add(buttons);
        panel.Children.Add(status);
        panel.Children.Add(progress);
        record.Click += async (_, _) => await RecordMicrophoneAsync(record, play, speakerButton, status, progress);
        play.Click += (_, _) => PlayMicrophoneClip(play, speakerButton, status, progress);
    }

    private async Task RecordMicrophoneAsync(Button record, Button play, Button speaker, TextBlock status, ProgressBar progress)
    {
        ReleaseMicrophoneTest();
        _speechSynth?.SpeakAsyncCancelAll();
        using var session = new CancellationTokenSource();
        _micRecording = session;
        record.IsEnabled = play.IsEnabled = speaker.IsEnabled = false;
        progress.Visibility = Visibility.Collapsed;
        StorageFile? clip = null;
        try
        {
            status.Text = "正在打开麦克风…";
            using (var capture = new MediaCapture())
            {
                await capture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = StreamingCaptureMode.Audio,
                    MediaCategory = MediaCategory.Other
                });
                session.Token.ThrowIfCancellationRequested();
                var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetTempPath());
                clip = await folder.CreateFileAsync("TubaWinUi3-Microphone.m4a", CreationCollisionOption.GenerateUniqueName);
                session.Token.ThrowIfCancellationRequested();
                var recording = await capture.PrepareLowLagRecordToStorageFileAsync(
                    MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High), clip);
                try
                {
                    session.Token.ThrowIfCancellationRequested();
                    await recording.StartAsync();
                    session.Token.ThrowIfCancellationRequested();
                    progress.Maximum = MicRecordingSeconds;
                    progress.Visibility = Visibility.Visible;
                    var clock = Stopwatch.StartNew();
                    while (clock.Elapsed.TotalSeconds < MicRecordingSeconds)
                    {
                        var elapsed = clock.Elapsed.TotalSeconds;
                        status.Text = $"正在录音，请说话 · 还剩 {Math.Max(1, Math.Ceiling(MicRecordingSeconds - elapsed)):0} 秒";
                        progress.Value = elapsed;
                        await Task.Delay(100, session.Token);
                    }
                    status.Text = "正在完成录音…";
                    progress.Value = MicRecordingSeconds;
                    await recording.StopAsync();
                }
                finally
                {
                    await recording.FinishAsync();
                }
            }

            session.Token.ThrowIfCancellationRequested();
            var properties = await clip.GetBasicPropertiesAsync();
            session.Token.ThrowIfCancellationRequested();
            if (properties.Size == 0) throw new IOException("录音文件为空。");
            _micClip = clip;
            clip = null;
            status.Text = "录制完成，点击「播放录音」试听。";
            play.IsEnabled = true;
        }
        catch (Exception ex) when (!session.IsCancellationRequested)
        {
            progress.Visibility = Visibility.Collapsed;
            status.Text = $"录音失败，请检查 Windows 麦克风权限和默认输入设备。{ex.Message}";
        }
        catch (Exception) when (session.IsCancellationRequested) { }
        finally
        {
            DeleteMicrophoneClip(clip);
            if (ReferenceEquals(_micRecording, session))
            {
                _micRecording = null;
                record.IsEnabled = speaker.IsEnabled = true;
            }
        }
    }

    private void PlayMicrophoneClip(Button play, Button speaker, TextBlock status, ProgressBar progress)
    {
        if (_micClip is not { } clip) return;
        StopMicrophonePlayback();
        _speechSynth?.SpeakAsyncCancelAll();
        play.IsEnabled = speaker.IsEnabled = false;
        status.Text = "正在打开录音…";
        progress.Value = 0;
        progress.Visibility = Visibility.Visible;

        void Complete(string message)
        {
            StopMicrophonePlayback();
            status.Text = message;
            play.IsEnabled = speaker.IsEnabled = true;
        }

        try
        {
            var player = new MediaPlayer { AutoPlay = true, IsMuted = false, Volume = 1 };
            _micPlayer = player;
            _micSource = MediaSource.CreateFromStorageFile(clip);
            player.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_micPlayer, player)) return;
                progress.Value = progress.Maximum;
                Complete("播放完成，可以再次播放或重新录音。");
            });
            player.MediaFailed += (_, args) =>
            {
                var error = args.ErrorMessage;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ReferenceEquals(_micPlayer, player))
                        Complete($"播放失败，请检查 Windows 的音频输出设备。{error}");
                });
            };

            var timer = DispatcherQueue.CreateTimer();
            _micPlaybackTimer = timer;
            timer.Interval = TimeSpan.FromMilliseconds(100);
            var lastPosition = TimeSpan.Zero;
            var idle = Stopwatch.StartNew();
            timer.Tick += (_, _) =>
            {
                if (!ReferenceEquals(_micPlayer, player)) return;
                var playback = player.PlaybackSession;
                var position = playback.Position;
                if (position != lastPosition)
                {
                    lastPosition = position;
                    idle.Restart();
                }
                if (idle.Elapsed >= TimeSpan.FromSeconds(10))
                {
                    Complete("播放超时，请检查默认音频输出设备后重试。");
                    return;
                }

                var duration = playback.NaturalDuration.TotalSeconds;
                if (duration <= 0) return;
                progress.Maximum = duration;
                progress.Value = Math.Clamp(position.TotalSeconds, 0, duration);
                status.Text = $"播放进度 · {progress.Value:0.0} / {duration:0.0} 秒";
            };
            player.Source = _micSource;
            timer.Start();
        }
        catch (Exception ex) { Complete($"播放失败：{ex.Message}"); }
    }

    private void StopMicrophonePlayback()
    {
        _micPlaybackTimer?.Stop();
        _micPlaybackTimer = null;
        var player = _micPlayer;
        _micPlayer = null;
        player?.Dispose();
        _micSource?.Dispose();
        _micSource = null;
    }

    private void ReleaseMicrophoneTest()
    {
        var session = _micRecording;
        _micRecording = null;
        session?.Cancel();
        StopMicrophonePlayback();
        DeleteMicrophoneClip(_micClip);
        _micClip = null;
    }

    private static void DeleteMicrophoneClip(StorageFile? clip)
    {
        if (clip is null) return;
        try { File.Delete(clip.Path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
