using System.Diagnostics;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class ScriptRunnerWindow : Window
{
    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);
    private static readonly Color AccentRed = Color.FromArgb(255, 248, 113, 113);
    private static readonly Color AccentOrange = Color.FromArgb(255, 251, 191, 36);
    private static readonly Color DimGreen = Color.FromArgb(255, 80, 200, 120);

    private Process? _runningProcess;
    private CancellationTokenSource? _cts;
    private readonly DispatcherQueue _dq;
    private int _lineCount;
    private bool _isRunning;
    private readonly StringBuilder _allOutput = new();
    private string _selectedEncoding = "UTF-8";
    private Stopwatch? _durationStopwatch;
    private bool _closed;
    private DispatcherQueueTimer? _durationTimer;
    private bool _hasProgressLine;
#pragma warning disable CS0414
    private int _progressLineInlineCount;
#pragma warning restore CS0414

    public ScriptRunnerWindow()
    {
        InitializeComponent();
        _dq = DispatcherQueue.GetForCurrentThread();

        AppWindow.Title = "脚本运行";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is not null)
        {
            var screenWidth = displayArea.WorkArea.Width;
            var screenHeight = displayArea.WorkArea.Height;
            var w = (int)(screenWidth * 0.8);
            var h = (int)(screenHeight * 0.8);
            AppWindow.Resize(new SizeInt32(w, h));
            AppWindow.Move(new PointInt32(
                (screenWidth - w) / 2 + displayArea.WorkArea.X,
                (screenHeight - h) / 2 + displayArea.WorkArea.Y));
        }
        else
        {
            AppWindow.Resize(new SizeInt32(1100, 750));
        }

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        if (Content is FrameworkElement root)
            root.RequestedTheme = ThemeService.CurrentElementTheme;

        ApplyTitleBarTheme();

        CmdBadge.Background = new SolidColorBrush(ThemeColors.SubtleBg);
        StatusText.Text = "就绪";

        _durationTimer = _dq.CreateTimer();
        _durationTimer.Interval = TimeSpan.FromMilliseconds(200);
        _durationTimer.Tick += (_, _) => UpdateDurationDisplay();

        AppWindow.Closing += OnAppWindowClosing;
    }

    public static ScriptRunnerWindow Show(string? command = null, string? workingDir = null, string? title = null)
    {
        var window = new ScriptRunnerWindow();
        window.Activate();

        if (!string.IsNullOrEmpty(command))
        {
            window.CommandBox.Text = command;
            window.SubtitleText.Text = command;
        }
        if (!string.IsNullOrEmpty(workingDir))
            window.WorkDirBox.Text = workingDir;
        if (!string.IsNullOrEmpty(title))
        {
            window.TitleText.Text = title;
            window.AppWindow.Title = title;
        }

        return window;
    }

    public static ScriptRunnerWindow ShowAndRun(string command, string? workingDir = null, string? title = null, bool runAsAdmin = false)
    {
        var window = Show(command, workingDir, title);
        if (runAsAdmin)
            window.AdminCheck.IsChecked = true;
        _ = window.ExecuteCommandAsync(command);
        return window;
    }

    public void SetTitle(string title)
    {
        TitleText.Text = title;
        AppWindow.Title = title;
    }

    public void SetSubtitle(string subtitle)
    {
        SubtitleText.Text = subtitle;
    }

    public async Task<ScriptRunResult> RunScriptAsync(
        string fileName,
        string arguments = "",
        string? workingDir = null,
        bool runAsAdmin = false,
        CancellationToken ct = default)
    {
        var request = new ScriptRunRequest
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RunAsAdmin = runAsAdmin,
            OutputEncoding = GetSelectedEncoding()
        };

        SetRunningState(true);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _durationStopwatch = Stopwatch.StartNew();
        _durationTimer?.Start();

        try
        {
            var result = await ScriptRunnerService.RunAsync(
                request,
                onOutput: (line, kind) => AppendOutput(line, kind),
                _cts.Token);

            FinishProgressLine();
            ShowResult(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            FinishProgressLine();
            AppendOutputLine("\n[已取消]", true);
            return new ScriptRunResult { ExitCode = -1, Error = "已取消", Duration = _durationStopwatch?.Elapsed ?? TimeSpan.Zero };
        }
        catch (Exception ex)
        {
            FinishProgressLine();
            AppendOutputLine($"\n[异常] {ex.Message}", true);
            return new ScriptRunResult { ExitCode = -1, Error = ex.Message, Duration = _durationStopwatch?.Elapsed ?? TimeSpan.Zero };
        }
        finally
        {
            _runningProcess = null;
            SetRunningState(false);
        }
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        var command = CommandBox.Text.Trim();
        if (string.IsNullOrEmpty(command)) return;

        SubtitleText.Text = command;
        await ExecuteCommandAsync(command);
    }

    private async void CommandBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && !_isRunning)
        {
            var command = CommandBox.Text.Trim();
            if (string.IsNullOrEmpty(command)) return;

            SubtitleText.Text = command;
            await ExecuteCommandAsync(command);
            e.Handled = true;
        }
    }

    private async Task ExecuteCommandAsync(string command)
    {
        if (_isRunning) return;

        _allOutput.Clear();
        _lineCount = 0;
        _hasProgressLine = false;
        _progressLineInlineCount = 0;
        OutputText.Inlines.Clear();
        ExitCodeBadge.Visibility = Visibility.Collapsed;

        var (fileName, args) = ParseCommand(command);
        var workDir = string.IsNullOrWhiteSpace(WorkDirBox.Text) ? null : WorkDirBox.Text.Trim();
        var runAsAdmin = AdminCheck.IsChecked ?? false;

        AppendOutputLine($"> {command}", false, AccentBlue);
        AppendOutputLine("", false);

        await RunScriptAsync(fileName, args, workDir, runAsAdmin);
    }

    private static (string fileName, string args) ParseCommand(string command)
    {
        var trimmed = command.TrimStart();

        if (trimmed.StartsWith("cmd ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("cmd/", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed[4..].TrimStart();
            if (rest.StartsWith("/c ", StringComparison.OrdinalIgnoreCase))
                return ("cmd.exe", rest);
            if (rest.StartsWith("/k ", StringComparison.OrdinalIgnoreCase))
                return ("cmd.exe", rest);
            return ("cmd.exe", $"/c {rest}");
        }

        if (trimmed.StartsWith("powershell ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed[11..].TrimStart();
            if (rest.StartsWith("-ExecutionPolicy", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith("-File", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith("-f ", StringComparison.OrdinalIgnoreCase))
                return ("powershell.exe", $"-NoProfile {rest}");
            return ("powershell.exe", $"-NoProfile -Command {rest}");
        }

        if (trimmed.StartsWith("pwsh ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = trimmed[5..].TrimStart();
            if (rest.StartsWith("-ExecutionPolicy", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith("-File", StringComparison.OrdinalIgnoreCase) ||
                rest.StartsWith("-f ", StringComparison.OrdinalIgnoreCase))
                return ("pwsh.exe", $"-NoProfile {rest}");
            return ("pwsh.exe", $"-NoProfile -Command {rest}");
        }

        if (trimmed.StartsWith('"'))
        {
            var endIdx = trimmed.IndexOf('"', 1);
            if (endIdx > 0)
            {
                var file = trimmed[1..endIdx];
                var rest = trimmed[(endIdx + 1)..].TrimStart();
                return (file, rest);
            }
        }

        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx < 0)
            return (trimmed, "");

        return (trimmed[..spaceIdx], trimmed[(spaceIdx + 1)..]);
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_runningProcess is not null)
        {
            try
            {
                if (!_runningProcess.HasExited)
                    _runningProcess.Kill();
                AppendOutputLine("\n[进程已终止]", true);
            }
            catch
            {
                _cts?.Cancel();
            }
        }
        else
        {
            _cts?.Cancel();
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _allOutput.Clear();
        _lineCount = 0;
        _hasProgressLine = false;
        _progressLineInlineCount = 0;
        OutputText.Inlines.Clear();
        ExitCodeBadge.Visibility = Visibility.Collapsed;
        DurationText.Text = "";
        LineCountText.Text = "";
        ProcessInfoText.Text = "";
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            SendStdin();
            e.Handled = true;
        }
    }

    private void SendInputButton_Click(object sender, RoutedEventArgs e)
    {
        SendStdin();
    }

    private void SendStdin()
    {
        var text = InputBox.Text;
        if (string.IsNullOrEmpty(text) || _runningProcess is null) return;

        try
        {
            _runningProcess.StandardInput.WriteLine(text);
            AppendOutputLine(text, false, ThemeColors.DimText);
            InputBox.Text = "";
        }
        catch
        {
            ShowToast("发送失败", "无法写入进程标准输入", InfoBarSeverity.Error);
        }
    }

    private void CopyOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var text = _allOutput.ToString();
        if (string.IsNullOrEmpty(text)) return;

        var result = ClipboardService.TrySetText(text);
        if (result.Success)
            ShowToast("已复制", "输出内容已复制到剪贴板", InfoBarSeverity.Success);
        else
            ShowToast("复制失败", "剪贴板被其他程序占用，请稍后重试", InfoBarSeverity.Warning);
    }

    private void ScrollToBottomButton_Click(object sender, RoutedEventArgs e)
    {
        OutputScroll.ChangeView(null, OutputScroll.ExtentHeight, null);
    }

    private void EncMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleMenuFlyoutItem item) return;

        EncUtf8.IsChecked = item == EncUtf8;
        EncGbk.IsChecked = item == EncGbk;
        EncDefault.IsChecked = item == EncDefault;

        _selectedEncoding = item.Text;
        EncodingLabel.Text = _selectedEncoding;
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
            await ConfirmCloseAsync();
        else
            Close();
    }

    private async Task ConfirmCloseAsync()
    {
        var dlg = new ContentDialog
        {
            Title = "脚本正在运行",
            Content = "当前有脚本正在运行，是否强制终止并关闭窗口？",
            PrimaryButtonText = "终止并关闭",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            KillProcessAndCancel();
            Close();
        }
    }

    private void KillProcessAndCancel()
    {
        try { _runningProcess?.Kill(); } catch { }
        _cts?.Cancel();
    }

    private void SetRunningState(bool running)
    {
        _isRunning = running;
        RunButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        CommandBox.IsReadOnly = running;
        AdminCheck.IsEnabled = !running;
        WorkDirBox.IsReadOnly = running;
        InputPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = running ? "运行中..." : "就绪";
        IdleIcon.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        RunningRing.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        if (running)
        {
            StatusText.Foreground = new SolidColorBrush(AccentOrange);
        }
        else
        {
            StatusText.Foreground = new SolidColorBrush(ThemeColors.DimText);
            _durationStopwatch?.Stop();
            _durationTimer?.Stop();
        }
    }

    private void AppendOutput(string line, ScriptOutputKind kind)
    {
        if (kind == ScriptOutputKind.ProgressUpdate)
        {
            _dq.TryEnqueue(() =>
            {
                if (_closed) return;
                UpdateProgressLine(line);
            });
        }
        else
        {
            _dq.TryEnqueue(() =>
            {
                if (_closed) return;
                FinishProgressLine();
                AppendOutputLine(line, kind == ScriptOutputKind.Error);
            });
        }
    }

    private void UpdateProgressLine(string text)
    {
        _allOutput.AppendLine($"\r{text}");

        if (_hasProgressLine)
        {
            var lastIdx = OutputText.Inlines.Count - 1;
            if (lastIdx >= 0 && OutputText.Inlines[lastIdx] is Microsoft.UI.Xaml.Documents.Run run)
            {
                run.Text = FormatProgressText(text);
                return;
            }
        }

        var newRun = new Microsoft.UI.Xaml.Documents.Run
        {
            Text = FormatProgressText(text),
            Foreground = new SolidColorBrush(DimGreen)
        };
        OutputText.Inlines.Add(newRun);
        _hasProgressLine = true;
        _progressLineInlineCount = 1;

        AutoScroll();
        LineCountText.Text = _lineCount > 0 ? $"{_lineCount} 行" : "";
    }

    private void FinishProgressLine()
    {
        if (!_hasProgressLine) return;

        if (OutputText.Inlines.Count > 0)
        {
            var lastIdx = OutputText.Inlines.Count - 1;
            if (OutputText.Inlines[lastIdx] is Microsoft.UI.Xaml.Documents.Run run)
            {
                var current = run.Text;
                if (current.EndsWith('\n'))
                {
                    _hasProgressLine = false;
                    return;
                }

                run.Text = current.TrimEnd() + "\n";
                run.Foreground = new SolidColorBrush(ThemeColors.SecondaryText);
            }
        }

        _hasProgressLine = false;
        _lineCount++;
        LineCountText.Text = _lineCount > 0 ? $"{_lineCount} 行" : "";
    }

    private static string FormatProgressText(string raw)
    {
        if (raw.Contains('%'))
        {
            var bar = RenderProgressBar(raw);
            return $"  {bar} {raw}\n";
        }

        var spinner = SpinnerGlyph();
        return $"  {spinner} {raw}\n";
    }

    private static string RenderProgressBar(string raw)
    {
        var pct = 0.0;
        var match = System.Text.RegularExpressions.Regex.Match(raw, @"(\d+(?:\.\d+)?)\s*%");
        if (match.Success && double.TryParse(match.Groups[1].Value, out var v))
            pct = v;

        var filled = (int)Math.Round(pct / 100 * 20);
        if (filled > 20) filled = 20;
        if (filled < 0) filled = 0;

        var bar = new string('█', filled) + new string('░', 20 - filled);
        return $"[{bar}]";
    }

    private static int _spinnerIdx;
    private static string SpinnerGlyph()
    {
        var glyphs = "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏";
        _spinnerIdx = (_spinnerIdx + 1) % glyphs.Length;
        return glyphs[_spinnerIdx].ToString();
    }

    private void AppendOutputLine(string line, bool isError, Color? color = null)
    {
        _allOutput.AppendLine(line);
        _lineCount++;

        var lineText = $"{line}\n";

        var run = new Microsoft.UI.Xaml.Documents.Run
        {
            Text = lineText
        };

        if (color.HasValue)
            run.Foreground = new SolidColorBrush(color.Value);
        else if (isError)
            run.Foreground = new SolidColorBrush(AccentRed);

        OutputText.Inlines.Add(run);

        TrimBufferIfNeeded();
        AutoScroll();

        LineCountText.Text = _lineCount > 0 ? $"{_lineCount} 行" : "";
    }

    private void TrimBufferIfNeeded()
    {
        var limit = (int)BufferLimitBox.Value;
        if (limit < 100) limit = 100;
        if (_lineCount <= limit + 50) return;

        var toRemove = _lineCount - limit;
        if (toRemove <= 0) return;

        var actualRemove = Math.Min(toRemove, OutputText.Inlines.Count);
        for (var i = 0; i < actualRemove; i++)
            OutputText.Inlines.RemoveAt(0);

        _lineCount -= toRemove;

        if (_lineCount < 0) _lineCount = 0;
    }

    private void AutoScroll()
    {
        if (AutoScrollCheck.IsChecked ?? true)
            OutputScroll.ChangeView(null, OutputScroll.ExtentHeight + 1000, null);
    }

    private void ShowResult(ScriptRunResult result)
    {
        _dq.TryEnqueue(() =>
        {
            ExitCodeBadge.Visibility = Visibility.Visible;

            if (result.Success)
            {
                ExitCodeBadge.Background = new SolidColorBrush(Color.FromArgb(26, AccentGreen.R, AccentGreen.G, AccentGreen.B));
                ExitCodeText.Text = "EXIT 0";
                ExitCodeText.Foreground = new SolidColorBrush(AccentGreen);
                StatusText.Text = "完成";
                StatusText.Foreground = new SolidColorBrush(AccentGreen);
            }
            else
            {
                ExitCodeBadge.Background = new SolidColorBrush(Color.FromArgb(26, AccentRed.R, AccentRed.G, AccentRed.B));
                ExitCodeText.Text = $"EXIT {result.ExitCode}";
                ExitCodeText.Foreground = new SolidColorBrush(AccentRed);
                StatusText.Text = $"失败 (代码 {result.ExitCode})";
                StatusText.Foreground = new SolidColorBrush(AccentRed);
            }

            var duration = result.Duration;
            DurationText.Text = duration.TotalSeconds >= 60
                ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
                : $"{duration.TotalSeconds:F1}s";
            ProcessInfoText.Text = $"耗时 {DurationText.Text}";
        });
    }

    private void UpdateDurationDisplay()
    {
        if (_durationStopwatch is null || !_durationStopwatch.IsRunning) return;

        var elapsed = _durationStopwatch.Elapsed;
        DurationText.Text = elapsed.TotalSeconds >= 60
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s"
            : $"{elapsed.TotalSeconds:F1}s";
    }

    private DispatcherTimer? _toastBarTimer;

    private void ShowToast(string title, string message, InfoBarSeverity severity)
    {
        _dq.TryEnqueue(() =>
        {
            ToastBar.Title = title;
            ToastBar.Message = message;
            ToastBar.Severity = severity;
            ToastBar.IsOpen = true;

            _toastBarTimer?.Stop();
            _toastBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _toastBarTimer.Tick += (s, e) =>
            {
                ToastBar.IsOpen = false;
                ((DispatcherTimer)s!).Stop();
            };
            _toastBarTimer.Start();
        });
    }

    private Encoding GetSelectedEncoding()
    {
        return _selectedEncoding switch
        {
            "GBK" => Encoding.GetEncoding("GBK"),
            "Default" => Encoding.Default,
            _ => Encoding.UTF8
        };
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs e)
    {
        if (_isRunning)
        {
            e.Cancel = true;
            _ = ConfirmCloseAsync();
        }
        else
        {
            _closed = true;
            _durationTimer?.Stop();
        }
    }

    private void ApplyTitleBarTheme()
    {
        var isDark = ThemeService.CurrentElementTheme == ElementTheme.Dark ||
                     (ThemeService.CurrentElementTheme == ElementTheme.Default &&
                      Application.Current.RequestedTheme == ApplicationTheme.Dark);

        // 部分 Windows 10 版本 AppWindow.TitleBar 为 null（microsoft-ui-xaml#6101），
        // 颜色 API 在 Windows 10 上也可能被忽略 —— 失败时保持系统默认配色即可
        var titleBar = SafeTitleBar.Get(this);
        if (titleBar is null) return;

        try
        {
            titleBar.BackgroundColor = isDark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
            titleBar.ForegroundColor = isDark ? Color.FromArgb(255, 210, 210, 210) : Color.FromArgb(255, 30, 30, 30);
            titleBar.InactiveBackgroundColor = titleBar.BackgroundColor;
            titleBar.InactiveForegroundColor = isDark ? Color.FromArgb(255, 100, 100, 100) : Color.FromArgb(255, 160, 160, 160);
            titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonForegroundColor = titleBar.ForegroundColor;
            titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveForegroundColor = isDark ? Color.FromArgb(255, 80, 80, 80) : Color.FromArgb(255, 180, 180, 180);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScriptRunnerWindow] 标题栏配色失败（已忽略）: {ex.Message}");
        }
    }
}
