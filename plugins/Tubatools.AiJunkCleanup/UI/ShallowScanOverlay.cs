using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Tubatools.AiJunkCleanup.Models;
using Windows.Foundation;
using Windows.UI;

namespace Tubatools.AiJunkCleanup.UI;

/// <summary>发送前的浅层扫描遮罩：扫描条动画 + 实时进度，扫描完成后把结果交给 AI。</summary>
internal sealed class ShallowScanOverlay
{
    private const double TrackWidth = 320;
    private const double SweepWidth = 86;

    private readonly Panel _root;
    private readonly Grid _overlay;
    private readonly TextBlock _title;
    private readonly TextBlock _phase;
    private readonly TextBlock _stats;
    private readonly TextBlock _hint;
    private readonly FontIcon _icon;
    private readonly ProgressRing _spinner;
    private readonly Grid _track;
    private readonly ProgressBar _indexBar;
    private readonly Storyboard _storyboard = new();
    private readonly DispatcherQueueTimer _tick;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private long _files;
    private bool _closed;

    internal ShallowScanOverlay(Panel root)
    {
        _root = root;
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _tick = dispatcher.CreateTimer();
        _tick.Interval = TimeSpan.FromSeconds(1);
        _tick.Tick += (_, _) => UpdateStats();
        var accent = ResourceColor("SystemAccentColor", Color.FromArgb(0xFF, 0x4C, 0xC2, 0xA0));

        _icon = new FontIcon
        {
            Glyph = "\uE721",
            FontSize = 20,
            Foreground = new SolidColorBrush(accent)
        };
        _title = new TextBlock
        {
            Text = "正在扫描磁盘浅层占用",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextFillColorPrimaryBrush", Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0))
        };
        _phase = new TextBlock
        {
            Text = "读取 MFT 目录索引",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0))
        };
        _stats = new TextBlock
        {
            Text = "0 个文件 · 0.0 秒",
            FontSize = 11,
            Foreground = ResourceBrush("TextFillColorSecondaryBrush", Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0))
        };
        _hint = new TextBlock
        {
            Text = "只读扫描，不会修改或删除任何文件",
            FontSize = 11,
            Foreground = ResourceBrush("TextFillColorTertiaryBrush", Color.FromArgb(0xFF, 0x80, 0x80, 0x80))
        };

        var iconTile = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(accent) { Opacity = 0.16 },
            Child = _icon,
            VerticalAlignment = VerticalAlignment.Top
        };

        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(_title);
        titles.Children.Add(_phase);
        _spinner = new ProgressRing
        {
            IsActive = true,
            Width = 16,
            Height = 16,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(iconTile);
        header.Children.Add(titles);
        header.Children.Add(_spinner);

        _track = BuildTrack(accent);
        _indexBar = new ProgressBar
        {
            IsIndeterminate = true,
            Width = TrackWidth,
            Height = 3,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(_indexBar, "正在整理深度索引");
        var bars = new Grid { Width = TrackWidth, Height = 6 };
        bars.Children.Add(_track);
        bars.Children.Add(_indexBar);

        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(header);
        body.Children.Add(bars);
        body.Children.Add(_stats);
        body.Children.Add(_hint);

        var card = new Border
        {
            Width = 420,
            Padding = new Thickness(26, 24, 26, 24),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = ResourceBrush("CardStrokeColorDefaultBrush", Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Background = ResourceBrush("SolidBackgroundFillColorBaseBrush", Color.FromArgb(0xFF, 0x20, 0x20, 0x20)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = body
        };

        _overlay = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        if (_root is Grid grid)
        {
            Grid.SetColumnSpan(_overlay, Math.Max(1, grid.ColumnDefinitions.Count));
            Grid.SetRowSpan(_overlay, Math.Max(1, grid.RowDefinitions.Count));
        }
        Canvas.SetZIndex(_overlay, 300);
        _overlay.Children.Add(card);
        _root.Children.Add(_overlay);
        try { _storyboard.Begin(); }
        catch (Exception)
        {
            _root.Children.Remove(_overlay);
            throw;
        }
        _tick.Start();
    }

    private Grid BuildTrack(Color accent)
    {
        var track = new Grid
        {
            Width = TrackWidth,
            Height = 6,
            HorizontalAlignment = HorizontalAlignment.Left,
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, TrackWidth, 6) }
        };
        track.Children.Add(new Border
        {
            Background = ResourceBrush("ControlFillColorSecondaryBrush", Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(3)
        });

        var sweepTransform = new TranslateTransform { X = -SweepWidth };
        var sweep = new Border
        {
            Width = SweepWidth,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = sweepTransform,
            Background = BuildSweepBrush(accent)
        };
        track.Children.Add(sweep);

        var animation = new DoubleAnimation
        {
            From = -SweepWidth,
            To = TrackWidth,
            Duration = new Duration(TimeSpan.FromMilliseconds(1100)),
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, sweepTransform);
        Storyboard.SetTargetProperty(animation, "X");
        _storyboard.Children.Add(animation);
        return track;
    }

    private static LinearGradientBrush BuildSweepBrush(Color accent)
    {
        var transparent = Color.FromArgb(0, accent.R, accent.G, accent.B);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0)
        };
        brush.GradientStops.Add(new GradientStop { Color = transparent, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = accent, Offset = 0.5 });
        brush.GradientStops.Add(new GradientStop { Color = transparent, Offset = 1 });
        return brush;
    }

    internal void Update(ShallowScanProgress progress)
    {
        if (_closed) return;
        if (progress.Files > 0) _files = progress.Files;
        var phase = string.IsNullOrWhiteSpace(progress.Phase) ? "读取 MFT 目录索引" : progress.Phase;
        _phase.Text = progress.Percent is { } percent ? $"{phase} · {percent:0}%" : phase;

        if (progress.Indexing && !progress.Complete && !progress.Failed)
        {
            // 深度索引整理没有可靠的百分比，单独用一条不确定进度条表示。
            _track.Visibility = Visibility.Collapsed;
            _indexBar.Visibility = Visibility.Visible;
        }
        else if (!progress.Complete && !progress.Failed)
        {
            _track.Visibility = Visibility.Visible;
            _indexBar.Visibility = Visibility.Collapsed;
        }

        UpdateStats();
        if (progress.Failed)
        {
            _title.Text = "浅层扫描失败";
            _icon.Glyph = "\uE7BA";
            _icon.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xE8, 0x6C, 0x6C));
            _tick.Stop();
            _elapsed.Stop();
            _storyboard.Stop();
        }
    }

    private void UpdateStats()
    {
        if (_closed) return;
        _stats.Text = $"{_files:N0} 个文件 · {_elapsed.Elapsed.TotalSeconds:0.0} 秒";
    }

    internal void Complete()
    {
        if (_closed) return;
        _tick.Stop();
        _elapsed.Stop();
        _title.Text = "浅层扫描完成";
        _icon.Glyph = "\uE73E";
        _icon.Foreground = new SolidColorBrush(ResourceColor("SystemAccentColor", Color.FromArgb(0xFF, 0x4C, 0xC2, 0xA0)));
        _phase.Text = "正在把结果交给 AI…";
        _stats.Text = $"扫描完成 · {_files:N0} 个文件 · {_elapsed.Elapsed.TotalSeconds:0.0} 秒";
        _hint.Text = "AI 将基于扫描结果生成清理清单";
        _spinner.IsActive = false;
        _storyboard.Stop();
        _track.Visibility = Visibility.Collapsed;
        _indexBar.Visibility = Visibility.Collapsed;
    }

    internal void Fail(string message)
    {
        if (_closed) return;
        _tick.Stop();
        _elapsed.Stop();
        _title.Text = "浅层扫描失败";
        _icon.Glyph = "\uE7BA";
        _icon.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xE8, 0x6C, 0x6C));
        _phase.Text = string.IsNullOrWhiteSpace(message) ? "将把失败信息一起发送给 AI。" : message;
        _stats.Text = $"已停止 · {_files:N0} 个文件 · {_elapsed.Elapsed.TotalSeconds:0.0} 秒";
        _hint.Text = "只读扫描，不会修改或删除任何文件";
        _spinner.IsActive = false;
        _storyboard.Stop();
        _track.Visibility = Visibility.Collapsed;
        _indexBar.Visibility = Visibility.Collapsed;
    }

    internal void Close()
    {
        if (_closed) return;
        _closed = true;
        _tick.Stop();
        _elapsed.Stop();
        _spinner.IsActive = false;
        try { _storyboard.Stop(); } catch (InvalidOperationException) { }
        if (ReferenceEquals(_overlay.Parent, _root)) _root.Children.Remove(_overlay);
    }

    private static Brush ResourceBrush(string key, Color fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(fallback);

    private static Color ResourceColor(string key, Color fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Color color
            ? color
            : fallback;
}
