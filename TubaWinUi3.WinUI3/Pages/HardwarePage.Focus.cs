using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System.Numerics;
using System.Text.Json;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Foundation;

namespace TubaWinUi3.Pages;

public sealed partial class HardwarePage
{
    private sealed class HardwareFocusState
    {
        public required Button Card;
        public required object OriginalContent;
        public required Rect OriginalBounds;
        public required int SceneRequestId;
        public required string Label;
        public required string Title;
        public required string Summary;
        public required int Index;
        public required bool Left;
        public required Thickness Padding;
        public VerticalAlignment VerticalAlignment;
        public bool Closing;
        public TextBlock? Hint;
        public StackPanel? Rows;
        public List<HardwareInfoItem> Details = [];
    }

    private HardwareFocusState? _hardwareFocus;
    private int _hardwareFocusVersion;
    private Storyboard? _hardwareFocusStoryboard;
    private Task _hardwareFocusTransition = Task.CompletedTask;
    private Task<HardwareDetailData>? _hardwareFocusData;
    private bool _hardwareFocusForceRefresh;
    private TaskCompletionSource<bool>? _hardwareFocusSceneClosed;
    private TaskCompletionSource<bool>? _hardwareFocusSceneStarted;

    private async Task FocusHardwareCardAsync(Button card, string label, string title, string summary, int index)
    {
        if (_hardwareFocus is not null || !UseSettingsStyle || !card.IsHitTestVisible) return;
        var version = ++_hardwareFocusVersion;
        var state = new HardwareFocusState
        {
            Card = card, Label = label, Title = title, Summary = summary, Index = index,
            OriginalContent = card.Content,
            OriginalBounds = new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), card.ActualWidth, card.ActualHeight),
            SceneRequestId = version,
            Left = Canvas.GetLeft(card) + card.ActualWidth / 2 < HardwareDiagram.ActualWidth / 2,
            Padding = card.Padding, VerticalAlignment = card.VerticalContentAlignment
        };
        _hardwareFocus = state;
        _calloutRevealVersion++;
        CancelDiagramCardFlights();
        var clickedVisual = ElementCompositionPreview.GetElementVisual(card);
        clickedVisual.StopAnimation("RotationAngle"); clickedVisual.RotationAngle = 0;
        clickedVisual.StopAnimation("Translation"); clickedVisual.Properties.InsertVector3("Translation", Vector3.Zero);
        clickedVisual.StopAnimation("Scale"); clickedVisual.Scale = Vector3.One;
        HardwareConnectors.Opacity = 0;
        HardwareAutoRotate.IsEnabled = HardwareReplayMenu.IsEnabled = false;
        var sceneStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _hardwareFocusSceneStarted = sceneStarted;
        var brand = label == "处理器" ? HardwareInfoService.DetectCpuBrand(summary) : null;
        _scene?.Post(JsonSerializer.Serialize(new { type = "prepareFocus", label, brand,
            requestId = state.SceneRequestId, side = state.Left ? "left" : "right" }));
        _ = GetHardwareFocusDataAsync();
        try
        {
            var siblings = _diagramCallouts.Where(item => item.Card != card).OrderBy(item => Canvas.GetTop(item.Card)).ToArray();
            for (var i = 0; i < siblings.Length; i++)
            {
                var sibling = siblings[i].Card;
                sibling.IsHitTestVisible = false;
                var visual = ElementCompositionPreview.GetElementVisual(sibling);
                visual.StopAnimation("Translation"); visual.Properties.InsertVector3("Translation", Vector3.Zero);
                visual.StopAnimation("Scale"); visual.Scale = Vector3.One;
                visual.StopAnimation("RotationAngle"); visual.RotationAngle = 0;
                if (ReduceDiagramMotion) { sibling.Opacity = 0; continue; }
                var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(0, 1); fade.InsertKeyFrame(1, 0);
                fade.DelayTime = TimeSpan.FromMilliseconds(i * 24); fade.Duration = TimeSpan.FromMilliseconds(180);
                visual.StartAnimation("Opacity", fade);
                var shrink = visual.Compositor.CreateVector3KeyFrameAnimation();
                shrink.InsertKeyFrame(0, Vector3.One); shrink.InsertKeyFrame(1, new Vector3(.96f));
                shrink.DelayTime = fade.DelayTime; shrink.Duration = fade.Duration;
                visual.StartAnimation("Scale", shrink);
            }
            if (!ReduceDiagramMotion) await Task.Delay(180 + Math.Max(0, siblings.Length - 1) * 24);
            if (version != _hardwareFocusVersion) return;
            foreach (var sibling in siblings)
            {
                sibling.Card.Opacity = 0;
                var visual = ElementCompositionPreview.GetElementVisual(sibling.Card);
                visual.StopAnimation("Opacity"); visual.StopAnimation("Scale"); visual.Scale = Vector3.One;
            }
            var selectedVisual = ElementCompositionPreview.GetElementVisual(card);
            selectedVisual.StopAnimation("Scale"); selectedVisual.Scale = Vector3.One;
            selectedVisual.StopAnimation("RotationAngle"); selectedVisual.RotationAngle = 0;
            selectedVisual.StopAnimation("Translation"); selectedVisual.Properties.InsertVector3("Translation", Vector3.Zero);
            Canvas.SetZIndex(card, 5);
            // The renderer acknowledges only after decoding/uploading the component and warming shaders.
            _scene?.Post(JsonSerializer.Serialize(new { type = "focus", label, brand,
                requestId = state.SceneRequestId, side = state.Left ? "left" : "right" }));
            if (_sceneReady) await sceneStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (version != _hardwareFocusVersion) return;
            var half = HardwareDiagram.ActualWidth / 2;
            var target = new Rect(state.Left ? 12 : half + 12, 12, half - 24, HardwareDiagram.ActualHeight - 24);
            // 变形期间内容顶部对齐并提前用最终内边距：小卡片的标题/数值钉在左上原位，
            // 不随垂直居中被甩到卡片中间，换内容时与大面板标题区位置衔接。
            card.Padding = new Thickness(24);
            card.VerticalContentAlignment = VerticalAlignment.Top;
            await MorphHardwareCardAsync(state, target, 640, version);
            if (version != _hardwareFocusVersion) return;
            card.VerticalContentAlignment = VerticalAlignment.Stretch;
            card.Content = BuildHardwareFocusPanel(state);
            UpdateFocusHint(LocalizationService.L("Hw_FocusHint", "拖动硬件模型旋转 · 点击空白处或按 Esc 返回 · 模型为外观示意"));
            _ = LoadHardwareFocusDetailsAsync(state, version);
        }
        catch (Exception ex)
        {
            if (version != _hardwareFocusVersion) return;
            ResetHardwareFocus();
            ShowStatusBar(LocalizationService.L("Hw_LoadFailed", "硬件信息读取失败"), ex.Message, InfoBarSeverity.Error);
        }
    }

    private Grid BuildHardwareFocusPanel(HardwareFocusState state)
    {
        var root = new Grid { RowSpacing = 18, HorizontalAlignment = HorizontalAlignment.Stretch };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        // 顶行：左侧标题区与小卡片同构（图标 + 标题 + 摘要），切换时原地保留不淡出；
        // 操作按钮放右上角，避免出现在标题上方把标题顶下去。
        var top = new Grid { ColumnSpacing = 16 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Spacing = 8 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new Image { Source = HardwareIconService.Get(HardwareIconService.KeyForLabel(state.Label)),
            Width = 28, Height = 28, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
        titleRow.Children.Add(new TextBlock { Text = state.Title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        heading.Children.Add(titleRow);
        heading.Children.Add(new TextBlock { Text = state.Summary, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        top.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var back = new Button { Content = "← " + LocalizationService.L("Hw_FocusBack", "返回全景"), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetAutomationId(back, "HardwareFocusBack");
        back.Click += (_, _) => _hardwareFocusTransition = RestoreHardwareFocusAsync();
        actions.Children.Add(back);
        var copy = new Button { Content = LocalizationService.L("Hw_CopyParameters", "复制参数") };
        AutomationProperties.SetAutomationId(copy, "HardwareFocusCopy");
        copy.Click += (_, _) => TryCopyToClipboard(state.Title + Environment.NewLine +
            (state.Details.Count == 0 ? state.Summary : string.Join(Environment.NewLine, state.Details.Select(item => $"{item.DisplayLabel}：{item.Value}"))));
        actions.Children.Add(copy);
        Grid.SetColumn(actions, 1); top.Children.Add(actions); root.Children.Add(top);
        // 补充下来的内容：读取提示、模型出处、详细参数——与标题区分离，整体淡入。
        var supplementary = new StackPanel { Spacing = 14 };
        state.Hint = new TextBlock { Text = LocalizationService.L("Hw_FocusLoading", "正在读取详细参数…"), TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        supplementary.Children.Add(state.Hint);
        if (HardwareModelCredit(state.Label, state.Summary) is { } credit)
        {
            var attribution = new TextBlock { TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
            attribution.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = LocalizationService.L("Hw_ModelCredit", "示意模型：") });
            var author = new Microsoft.UI.Xaml.Documents.Hyperlink { NavigateUri = new Uri(credit.Source) };
            author.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = credit.Author });
            attribution.Inlines.Add(author);
            attribution.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " · " });
            var license = new Microsoft.UI.Xaml.Documents.Hyperlink { NavigateUri = new Uri("https://creativecommons.org/licenses/by/4.0/") };
            license.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "CC BY 4.0" });
            attribution.Inlines.Add(license);
            supplementary.Children.Add(attribution);
        }
        state.Rows = new StackPanel { Spacing = 14 };
        supplementary.Children.Add(state.Rows);
        var scroller = new ScrollViewer { Content = supplementary, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(scroller, "HardwareFocusDetails");
        Grid.SetRow(scroller, 1); root.Children.Add(scroller);
        if (!ReduceDiagramMotion)
        {
            var visual = ElementCompositionPreview.GetElementVisual(scroller);
            var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1); fade.Duration = TimeSpan.FromMilliseconds(260);
            visual.StartAnimation("Opacity", fade);
        }
        return root;
    }

    private async Task LoadHardwareFocusDetailsAsync(HardwareFocusState state, int version)
    {
        try
        {
            var data = await GetHardwareFocusDataAsync();
            if (_hardwareFocus != state || version != _hardwareFocusVersion || state.Rows is null) return;
            var rows = HardwareDetailPage.BuildFocusItems(data, state.Label, state.Summary, state.Index);
            state.Details = rows.Where(item => !string.IsNullOrWhiteSpace(item.Label) && !string.IsNullOrWhiteSpace(item.Value)).ToList();
            state.Rows.Children.Clear();
            foreach (var item in state.Details)
            {
                var row = new Grid { ColumnSpacing = 16 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(144) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = item.DisplayLabel, TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
                var value = new TextBlock { Text = item.Value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
                Grid.SetColumn(value, 1); row.Children.Add(value); state.Rows.Children.Add(row);
            }
            state.Hint!.Text = state.Details.Count == 0 ? LocalizationService.L("Hw_FocusNoDetails", "系统未提供更多参数") : "";
        }
        catch (Exception ex)
        {
            if (_hardwareFocus != state || version != _hardwareFocusVersion || state.Rows is null) return;
            state.Hint!.Text = LocalizationService.L("Hw_FocusDetailFailed", "详细参数读取失败，可重试") + "：" + ex.Message;
            var retry = new Button { Content = LocalizationService.L("Hw_ModernRefresh.Content", "刷新") };
            retry.Click += (_, _) => { _hardwareFocusData = null; _hardwareFocusForceRefresh = true; state.Rows.Children.Clear(); _ = LoadHardwareFocusDetailsAsync(state, version); };
            state.Rows.Children.Add(retry);
        }
    }

    private Task<HardwareDetailData> GetHardwareFocusDataAsync()
    {
        if (_hardwareFocusData is not null) return _hardwareFocusData;
        _hardwareFocusData = HardwareInfoService.LoadDetailForDisplayAsync(_hardwareFocusForceRefresh);
        _hardwareFocusForceRefresh = false;
        return _hardwareFocusData;
    }

    private async Task MorphHardwareCardAsync(HardwareFocusState state, Rect target, int duration, int version)
    {
        var card = state.Card;
        var current = new Rect(Canvas.GetLeft(card), Canvas.GetTop(card), card.Width, card.Height);
        _hardwareFocusStoryboard?.Stop();
        SetHardwareCardBounds(card, current);
        if (ReduceDiagramMotion) { SetHardwareCardBounds(card, target); return; }
        var storyboard = new Storyboard();
        void Animate(string property, double from, double to)
        {
            var animation = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(duration),
                EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(animation, card); Storyboard.SetTargetProperty(animation, property); storyboard.Children.Add(animation);
        }
        Animate("(Canvas.Left)", current.X, target.X); Animate("(Canvas.Top)", current.Y, target.Y);
        Animate("Width", current.Width, target.Width); Animate("Height", current.Height, target.Height);
        _hardwareFocusStoryboard = storyboard; storyboard.Begin();
        await Task.Delay(duration + 20);
        if (version != _hardwareFocusVersion) return;
        storyboard.Stop(); _hardwareFocusStoryboard = null; SetHardwareCardBounds(card, target);
    }

    private static void SetHardwareCardBounds(Button card, Rect bounds)
    {
        card.Width = bounds.Width; card.Height = bounds.Height; Canvas.SetLeft(card, bounds.X); Canvas.SetTop(card, bounds.Y);
    }

    private async Task RestoreHardwareFocusAsync()
    {
        var state = _hardwareFocus;
        if (state is null || state.Closing) return;
        state.Closing = true;
        var version = ++_hardwareFocusVersion;
        _hardwareFocusSceneStarted?.TrySetCanceled();
        _hardwareFocusSceneClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _scene?.Post(JsonSerializer.Serialize(new { type = "unfocus", requestId = state.SceneRequestId }));
        try
        {
            // Restore the original content before shrinking, so detailed rows never squash.
            state.Card.Content = state.OriginalContent;
            state.Card.Padding = state.Padding; state.Card.VerticalContentAlignment = state.VerticalAlignment;
            await MorphHardwareCardAsync(state, state.OriginalBounds, 620, version);
            if (version != _hardwareFocusVersion) return;
            if (_sceneReady) await Task.WhenAny(_hardwareFocusSceneClosed.Task, Task.Delay(1100));
            if (version != _hardwareFocusVersion) return;
            Canvas.SetZIndex(state.Card, 0);
            _hardwareFocus = null;
            HardwareAutoRotate.IsEnabled = HardwareReplayMenu.IsEnabled = true;
            UpdateDiagramSource();
            RevealDiagramCallouts(state.Card);
            state.Card.Focus(FocusState.Programmatic);
        }
        catch { if (version == _hardwareFocusVersion) ResetHardwareFocus(); }
    }

    private void ResetHardwareFocus()
    {
        var state = _hardwareFocus;
        if (state is null) return;
        _hardwareFocusVersion++;
        _hardwareFocusStoryboard?.Stop(); _hardwareFocusStoryboard = null;
        _hardwareFocusSceneStarted?.TrySetCanceled();
        _hardwareFocusSceneClosed?.TrySetResult(true);
        _scene?.Post("{\"type\":\"unfocus\",\"immediate\":true}");
        state.Card.Content = state.OriginalContent; state.Card.Padding = state.Padding;
        state.Card.VerticalContentAlignment = state.VerticalAlignment;
        SetHardwareCardBounds(state.Card, state.OriginalBounds); Canvas.SetZIndex(state.Card, 0);
        _hardwareFocus = null;
        HardwareAutoRotate.IsEnabled = HardwareReplayMenu.IsEnabled = true;
        foreach (var (card, _, _, _) in _diagramCallouts)
        {
            var visual = ElementCompositionPreview.GetElementVisual(card);
            visual.StopAnimation("Opacity"); visual.Opacity = 1;
            visual.StopAnimation("Scale"); visual.Scale = Vector3.One;
            visual.StopAnimation("RotationAngle"); visual.RotationAngle = 0;
            visual.StopAnimation("Translation"); visual.Properties.InsertVector3("Translation", Vector3.Zero);
            card.Opacity = 1; card.IsHitTestVisible = true;
        }
        UpdateDiagramSource();
    }

    private void UpdateFocusHint(string message) => SettingsStyleSource.Text = message;

    private static (string Author, string Source)? HardwareModelCredit(string label, string summary) => label switch
    {
        "处理器" when HardwareInfoService.DetectCpuBrand(summary) == "intel" => ("apleesee", "https://sketchfab.com/3d-models/pc-cpu-processor-efb6b95b255c4a37a9661df178ea3bb3"),
        "显卡" => ("_surovic_", "https://sketchfab.com/3d-models/geforce-rtx-3080-graphics-card-8b947ee1bf7a4e3d8ffa1c24893ac160"),
        "内存" => ("Nouraiz", "https://sketchfab.com/3d-models/corsairn-vengeance-rgb-pro-9b163c4d5ad34edca58488500b5f4daf"),
        "硬盘" => ("lime.ball.animations", "https://sketchfab.com/3d-models/m2-nvme-ssd-samsung-990-pro-1tb-3d-model-41b7bfda7eab40f8b13330913fd66fc2"),
        "主板" => ("MUSHROOM_BUILDS", "https://sketchfab.com/3d-models/asus-strix-b-550-f-gaming-motherboard-realistic-3eba5f45bed74fbeb2647de38047000f"),
        _ => null
    };

    private void HardwareFocusOutside_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_hardwareFocus is not { Closing: false } state) return;
        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (ReferenceEquals(source, state.Card)) return;
            source = VisualTreeHelper.GetParent(source);
        }
        _hardwareFocusTransition = RestoreHardwareFocusAsync();
    }

    private void HardwareFocus_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape || _hardwareFocus is null) return;
        e.Handled = true;
        _hardwareFocusTransition = RestoreHardwareFocusAsync();
    }
}
