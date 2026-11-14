using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;

namespace Tubatools.AiJunkCleanup.UI;

/// <summary>首次进入垃圾清理模式的四步教程。界面全部用 XAML 仿真绘制，并带扫描/高亮动画。</summary>
internal sealed class TutorialDialog : ContentDialog
{
    private const int StepCount = 5;

    private readonly Grid _preview = new();
    private readonly List<Grid> _views = [];
    private readonly List<Storyboard?> _animations = [];
    private readonly List<Border> _dots = [];
    private TextBlock _counter = null!;
    private TextBlock _title = null!;
    private TextBlock _description = null!;
    private int _step;

    internal TutorialDialog(XamlRoot root)
    {
        Title = "垃圾清理 · 使用教程";
        PrimaryButtonText = "下一步";
        SecondaryButtonText = "上一步";
        CloseButtonText = "跳过";
        DefaultButton = ContentDialogButton.Primary;
        XamlRoot = root;
        MaxWidth = 900;
        MinWidth = 700;
        Resources["ContentDialogMaxWidth"] = 900d;
        Resources["ContentDialogMinWidth"] = 700d;
        Content = BuildContent();
        PrimaryButtonClick += OnPrimaryClick;
        SecondaryButtonClick += OnSecondaryClick;
        UpdateStep(animate: false);
    }

    private FrameworkElement BuildContent()
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 660, MaxWidth = 840 };
        _counter = new TextBlock { FontSize = 12, Foreground = Ui.Secondary };
        panel.Children.Add(_counter);

        panel.Children.Add(new Border
        {
            Height = 310,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(12),
            Background = Ui.Card,
            BorderBrush = Ui.Stroke,
            BorderThickness = new Thickness(1),
            Child = _preview
        });

        _title = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold };
        _description = new TextBlock
        {
            FontSize = 13,
            Foreground = Ui.Secondary,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            MinHeight = 42
        };
        panel.Children.Add(_title);
        panel.Children.Add(_description);

        var dots = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 2, 0, 0)
        };
        for (var i = 0; i < StepCount; i++)
        {
            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = Ui.Tertiary
            };
            _dots.Add(dot);
            dots.Children.Add(dot);
        }
        panel.Children.Add(dots);

        BuildViews();
        return panel;
    }

    private void BuildViews()
    {
        AddView(BuildOpenAssistantView());
        AddView(BuildComposerView());
        AddView(BuildScanView());
        AddView(BuildPlanView());
        AddView(BuildDeleteView());
    }

    private void AddView((Grid View, Storyboard? Animation) item)
    {
        item.View.Visibility = Visibility.Collapsed;
        item.View.HorizontalAlignment = HorizontalAlignment.Center;
        item.View.VerticalAlignment = VerticalAlignment.Center;
        _views.Add(item.View);
        _animations.Add(item.Animation);
        _preview.Children.Add(item.View);
    }

    private (Grid View, Storyboard? Animation) BuildOpenAssistantView()
    {
        var view = new Grid { Width = 720, Height = 260 };
        var outer = new StackPanel { Spacing = 10, Width = 620, HorizontalAlignment = HorizontalAlignment.Center };

        var layout = new Grid { ColumnSpacing = 16 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = new StackPanel { Spacing = 5 };
        sidebar.Children.Add(T("图吧工具箱", 12, Ui.Secondary, FontWeights.SemiBold));
        foreach (var label in new[] { "首页", "硬件信息", "内置工具", "社区工具" })
            sidebar.Children.Add(NavRow(label, false));
        var highlight = new Border
        {
            BorderBrush = Ui.Accent,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(1),
            Child = NavRow("AI 助手", true)
        };
        sidebar.Children.Add(highlight);
        sidebar.Children.Add(NavRow("设置", false));
        Grid.SetColumn(sidebar, 0);
        layout.Children.Add(sidebar);

        var main = new StackPanel { Spacing = 10 };
        main.Children.Add(T("AI 助手", 16, Ui.Primary, FontWeights.SemiBold));
        main.Children.Add(new Border
        {
            Background = Ui.Composer,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = T("你好，我可以帮你扫描和清理垃圾文件。", 11, Ui.Secondary)
        });
        main.Children.Add(new Border
        {
            Background = Ui.Card,
            BorderBrush = Ui.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = BuildComposerMock()
        });
        Grid.SetColumn(main, 1);
        layout.Children.Add(main);

        outer.Children.Add(new Border
        {
            Background = Ui.Card,
            BorderBrush = Ui.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = layout
        });
        outer.Children.Add(T("① 在左侧导航打开「AI 助手」", 12, Ui.Accent, FontWeights.SemiBold));
        view.Children.Add(outer);

        var pulse = new DoubleAnimation
        {
            From = 0.35,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(900)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(pulse, highlight);
        Storyboard.SetTargetProperty(pulse, "Opacity");
        storyboard.Children.Add(pulse);
        return (view, storyboard);
    }

    private static Border NavRow(string label, bool active)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon
        {
            Glyph = active ? "\uE99A" : "\uE8A1",
            FontSize = 11,
            Foreground = active ? Ui.Accent : Ui.Tertiary
        });
        content.Children.Add(T(label, 11, active ? Ui.Accent : Ui.Secondary,
            active ? FontWeights.SemiBold : null));
        return new Border
        {
            Background = active ? Ui.AccentSoft : null,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(7, 5, 7, 5),
            Child = content
        };
    }

    private (Grid View, Storyboard? Animation) BuildComposerView()
    {
        var view = new Grid { Width = 720, Height = 260 };
        var stack = new StackPanel { Spacing = 14, Width = 560, HorizontalAlignment = HorizontalAlignment.Center };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(T("AI 助手", 13, Ui.Secondary, FontWeights.SemiBold));

        var modeContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        modeContent.Children.Add(T("垃圾清理", 12, Ui.Accent, FontWeights.SemiBold));
        modeContent.Children.Add(new FontIcon { Glyph = "\uE70D", FontSize = 9, Foreground = Ui.Accent });
        var mode = new Border
        {
            Background = Ui.AccentSoft,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 8, 5),
            Child = modeContent
        };
        var highlight = new Border
        {
            BorderBrush = Ui.Accent,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(1),
            Child = mode
        };
        Grid.SetColumn(highlight, 1);
        header.Children.Add(highlight);
        stack.Children.Add(header);

        var depth = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        depth.Children.Add(Chip("标准", Ui.AccentSoft, Ui.Accent));
        depth.Children.Add(T("扫描深度：标准", 11, Ui.Secondary));
        stack.Children.Add(depth);

        var composer = new Border
        {
            Background = Ui.Composer,
            BorderBrush = Ui.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 10, 10),
            Child = BuildComposerMock()
        };
        stack.Children.Add(composer);
        stack.Children.Add(T("② 在模式下拉框选择「垃圾清理」", 12, Ui.Accent, FontWeights.SemiBold));

        view.Children.Add(new Border
        {
            Background = Ui.Card,
            BorderBrush = Ui.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
            Child = stack
        });

        var pulse = new DoubleAnimation
        {
            From = 0.35,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(900)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(pulse, highlight);
        Storyboard.SetTargetProperty(pulse, "Opacity");
        storyboard.Children.Add(pulse);
        return (view, storyboard);
    }

    private static Grid BuildComposerMock()
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var prompt = T("清理 C 盘里的临时文件和浏览器缓存…", 12, Ui.Secondary);
        prompt.VerticalAlignment = VerticalAlignment.Center;
        prompt.TextTrimming = TextTrimming.CharacterEllipsis;
        grid.Children.Add(prompt);
        var send = new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(15),
            Background = Ui.Accent,
            Child = new FontIcon { Glyph = "\uE74A", FontSize = 12, Foreground = Ui.OnAccent }
        };
        Grid.SetColumn(send, 1);
        grid.Children.Add(send);
        return grid;
    }

    private (Grid View, Storyboard? Animation) BuildScanView()
    {
        var view = new Grid { Width = 720, Height = 260 };
        var stack = new StackPanel { Spacing = 14, Width = 560, HorizontalAlignment = HorizontalAlignment.Center };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(9),
            Background = Ui.AccentSoft,
            Child = new FontIcon { Glyph = "\uE721", FontSize = 19, Foreground = Ui.Accent }
        });
        var titles = new StackPanel { Spacing = 3 };
        titles.Children.Add(T("正在扫描磁盘浅层占用", 16, Ui.Primary, FontWeights.SemiBold));
        titles.Children.Add(T("读取 MFT 文件大小 · 50%", 11, Ui.Secondary));
        header.Children.Add(titles);
        stack.Children.Add(header);

        var track = new Grid
        {
            Width = 520,
            Height = 6,
            HorizontalAlignment = HorizontalAlignment.Left,
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, 520, 6) }
        };
        track.Children.Add(new Border { Background = Ui.Track, CornerRadius = new CornerRadius(3) });
        var sweepTransform = new TranslateTransform { X = -100 };
        track.Children.Add(new Border
        {
            Width = 100,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = Ui.AccentGradient,
            HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = sweepTransform
        });
        stack.Children.Add(track);

        stack.Children.Add(T("1,499,640 个文件 · 0.0 秒", 11, Ui.Secondary));
        stack.Children.Add(Chip("只读扫描，不会修改或删除任何文件", Ui.NeutralBackground, Ui.Secondary));

        view.Children.Add(new Border
        {
            Background = Ui.Card,
            BorderBrush = Ui.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Child = stack
        });

        var animation = new DoubleAnimation
        {
            From = -100,
            To = 520,
            Duration = new Duration(TimeSpan.FromMilliseconds(1100)),
            RepeatBehavior = RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };
        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, sweepTransform);
        Storyboard.SetTargetProperty(animation, "X");
        storyboard.Children.Add(animation);
        return (view, storyboard);
    }

    private (Grid View, Storyboard? Animation) BuildPlanView()
    {
        var view = new Grid { Width = 720, Height = 260 };
        var stack = new StackPanel { Spacing = 10, Width = 580, HorizontalAlignment = HorizontalAlignment.Center };

        var summary = new Grid { ColumnSpacing = 24 };
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summary.Children.Add(Stat("已选 / 总共（项）", "19 / 41"));
        var size = Stat("已选 / 总共（大小）", "16.1 GB / ≥ 109 GB");
        Grid.SetColumn(size, 1);
        summary.Children.Add(size);
        stack.Children.Add(summary);

        stack.Children.Add(PlanRow("cache", "低风险", "193 MB", true));
        stack.Children.Add(PlanRow("logs", "中风险", "271 MB", false));

        var button = new Button
        {
            Content = "查看清理清单 · 41 项",
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            Padding = new Thickness(12, 5, 12, 5)
        };
        stack.Children.Add(button);
        stack.Children.Add(T("④ 低风险默认勾选，中/高风险由你确认", 11, Ui.Accent, FontWeights.SemiBold));

        view.Children.Add(new Border
        {
            Background = Ui.Card,
            BorderBrush = Ui.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
            Child = stack
        });
        return (view, null);
    }

    private (Grid View, Storyboard? Animation) BuildDeleteView()
    {
        var view = new Grid { Width = 720, Height = 260 };
        var stack = new StackPanel { Spacing = 12, Width = 560, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(T("正在删除选中的项目", 16, Ui.Primary, FontWeights.SemiBold));
        stack.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Width = 520, HorizontalAlignment = HorizontalAlignment.Left });
        stack.Children.Add(T("已删除 12 项 · 约释放 2.5 GB", 11, Ui.Secondary));

        var skipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        skipRow.Children.Add(Chip("已跳过 3 项", Ui.CautionBackground, Ui.Caution));
        skipRow.Children.Add(T("被占用或无法删除的文件自动跳过", 11, Ui.Secondary));
        stack.Children.Add(skipRow);

        var skipped = new Border
        {
            Background = Ui.NeutralBackground,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Child = T("C:\\Users\\...\\Temp\\cache.tmp · 文件被占用，已跳过", 11, Ui.Tertiary)
        };
        stack.Children.Add(skipped);
        stack.Children.Add(Chip("直接删除 · 不进入回收站", Ui.AccentSoft, Ui.Accent));

        view.Children.Add(new Border
        {
            Background = Ui.Card,
            BorderBrush = Ui.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Child = stack
        });
        return (view, null);
    }

    private static FrameworkElement PlanRow(string name, string risk, string size, bool isChecked)
    {
        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(10, 7, 10, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new CheckBox
        {
            IsChecked = isChecked,
            IsHitTestVisible = false,
            IsTabStop = false,
            MinWidth = 0,
            Padding = new Thickness(0)
        });
        var chip = Chip(risk, risk == "低风险" ? Ui.SuccessBackground : Ui.CautionBackground,
            risk == "低风险" ? Ui.Success : Ui.Caution);
        Grid.SetColumn(chip, 1);
        grid.Children.Add(chip);
        var label = T(name, 12, Ui.Primary, FontWeights.SemiBold);
        Grid.SetColumn(label, 2);
        grid.Children.Add(label);
        var value = T(size, 11, Ui.Secondary);
        Grid.SetColumn(value, 3);
        grid.Children.Add(value);
        return new Border
        {
            Background = Ui.Card,
            BorderBrush = Ui.Stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private static StackPanel Stat(string label, string value)
    {
        var panel = new StackPanel { Spacing = 1 };
        panel.Children.Add(T(label, 10, Ui.Secondary));
        panel.Children.Add(T(value, 20, Ui.Primary, FontWeights.SemiBold));
        return panel;
    }

    private void UpdateStep(bool animate)
    {
        _counter.Text = $"{_step + 1} / {StepCount}";
        _title.Text = _step switch
        {
            0 => "① 打开 AI 助手",
            1 => "② 选择模式与扫描深度",
            2 => "③ 按下发送，插件先扫描",
            3 => "④ 查看清单，逐项确认",
            _ => "⑤ 直接删除，自动跳过占用"
        };
        _description.Text = _step switch
        {
            0 => "在工具箱左侧导航（或首页）打开「AI 助手」，进入对话页面。",
            1 => "在 AI 助手的模式下拉框选择「垃圾清理」，再选择快速/标准/深度。深度只影响探索范围，不会放宽删除限制。",
            2 => "按下发送后，插件先用宿主 NTFS/MFT 只读扫描根目录下三层，动画里的进度只是近似值；扫描结果会直接附在消息末尾交给 AI。",
            3 => "AI 返回清单后，插件弹出选择列表。低风险默认勾选，中/高风险需要你确认；不存在的路径不会显示。",
            _ => "确认后直接删除，不进入回收站。被占用或无法删除的文件会自动跳过，结果显示跳过项；成功项从清单移除，可以继续分批删除。"
        };

        for (var i = 0; i < _dots.Count; i++)
            _dots[i].Background = i == _step ? Ui.Accent : Ui.Tertiary;

        foreach (var animation in _animations) animation?.Stop();
        for (var i = 0; i < _views.Count; i++)
            _views[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;

        var current = _views[_step];
        current.Opacity = 1;
        if (animate)
        {
            var translate = new TranslateTransform { Y = 8 };
            current.RenderTransform = translate;
            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EnableDependentAnimation = true
            };
            var move = new DoubleAnimation
            {
                From = 8,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EnableDependentAnimation = true
            };
            var storyboard = new Storyboard();
            Storyboard.SetTarget(fade, current);
            Storyboard.SetTargetProperty(fade, "Opacity");
            Storyboard.SetTarget(move, translate);
            Storyboard.SetTargetProperty(move, "Y");
            storyboard.Children.Add(fade);
            storyboard.Children.Add(move);
            storyboard.Begin();
        }
        _animations[_step]?.Begin();

        PrimaryButtonText = _step == StepCount - 1 ? "开始使用" : "下一步";
        IsSecondaryButtonEnabled = _step > 0;
    }

    private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_step >= StepCount - 1) return;
        args.Cancel = true;
        _step++;
        UpdateStep(animate: true);
    }

    private void OnSecondaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (_step <= 0) return;
        _step--;
        UpdateStep(animate: true);
    }

    private static TextBlock T(string text, double size, Brush foreground, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = foreground,
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Border Chip(string text, Brush background, Brush foreground) => new()
    {
        Background = background,
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(8, 3, 8, 3),
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = foreground },
        VerticalAlignment = VerticalAlignment.Center
    };

    private static class Ui
    {
        internal static Brush Card => Resource("CardBackgroundFillColorDefaultBrush", Color.FromArgb(0x14, 0x80, 0x80, 0x80));
        internal static Brush Composer => Resource("ControlFillColorDefaultBrush", Color.FromArgb(0x18, 0x80, 0x80, 0x80));
        internal static Brush Stroke => Resource("CardStrokeColorDefaultBrush", Color.FromArgb(0x2E, 0x80, 0x80, 0x80));
        internal static Brush Track => Resource("ControlFillColorSecondaryBrush", Color.FromArgb(0x24, 0x80, 0x80, 0x80));
        internal static Brush Primary => Resource("TextFillColorPrimaryBrush", Color.FromArgb(0xF0, 0xF0, 0xF0, 0xF0));
        internal static Brush Secondary => Resource("TextFillColorSecondaryBrush", Color.FromArgb(0xB0, 0xA0, 0xA0, 0xA0));
        internal static Brush Tertiary => Resource("TextFillColorTertiaryBrush", Color.FromArgb(0x80, 0x80, 0x80, 0x80));
        internal static Brush Accent => Resource("AccentTextFillColorPrimaryBrush", Color.FromArgb(0xFF, 0x4C, 0xC2, 0xA0));
        internal static Brush OnAccent => Resource("TextOnAccentFillColorPrimaryBrush", Color.FromArgb(0xFF, 0x10, 0x10, 0x10));
        internal static Brush Success => Resource("SystemFillColorSuccessBrush", Color.FromArgb(0xFF, 0x10, 0x7C, 0x10));
        internal static Brush Caution => Resource("SystemFillColorCautionBrush", Color.FromArgb(0xFF, 0x9D, 0x5D, 0x00));
        internal static Brush SuccessBackground => Resource("SystemFillColorSuccessBackgroundBrush", Color.FromArgb(0x24, 0x10, 0x7C, 0x10));
        internal static Brush CautionBackground => Resource("SystemFillColorCautionBackgroundBrush", Color.FromArgb(0x24, 0x9D, 0x5D, 0x00));
        internal static Brush NeutralBackground => Resource("SystemFillColorNeutralBackgroundBrush", Color.FromArgb(0x1A, 0x80, 0x80, 0x80));
        internal static Brush AccentSoft => new SolidColorBrush(Color.FromArgb(0x24, 0x4C, 0xC2, 0xA0));
        internal static Brush AccentGradient => BuildAccentGradient();

        private static Brush BuildAccentGradient()
        {
            var accent = ResourceColor("SystemAccentColor", Color.FromArgb(0xFF, 0x4C, 0xC2, 0xA0));
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

        private static Brush Resource(string key, Color fallback) =>
            Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
                ? brush
                : new SolidColorBrush(fallback);

        private static Color ResourceColor(string key, Color fallback) =>
            Application.Current.Resources.TryGetValue(key, out var value) && value is Color color
                ? color
                : fallback;
    }
}
