using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class HardwarePage
{
    private bool UseSettingsStyle => AppSettings.GetBool("HardwareDiagramStyle", false);

    private void ApplyHardwareStyle()
    {
        RootHost.Visibility = UseSettingsStyle ? Visibility.Collapsed : Visibility.Visible;
        SettingsStyleScroll.Visibility = UseSettingsStyle ? Visibility.Visible : Visibility.Collapsed;
        ClassicModeMenu.IsChecked = !UseSettingsStyle;
        DiagramModeMenu.IsChecked = UseSettingsStyle;
        ModernClassicModeMenu.IsChecked = !UseSettingsStyle;
        ModernDiagramModeMenu.IsChecked = UseSettingsStyle;
        SetLoading(_hardwareInfoLoading);
        if (!UseSettingsStyle) ReleaseHardwareScene();
        if (UseSettingsStyle)
        {
            EnsureDiagramAccent();
            SettingsStyleDeviceName.Text = Environment.MachineName;
            if (_currentSections is not null) PopulateSettingsStyle(_currentSections);
            _ = LoadDesktopWallpaperAsync();
        }
    }

    private void PopulateSettingsStyle(IReadOnlyList<HardwareInfoSection> sections)
    {
        ResetHardwareFocus();
        _widthFitBudget = 8;
        _calloutRevealVersion++;
        CancelDiagramCardFlights();
        SettingsStyleDeviceName.Text = Environment.MachineName;
        SettingsStyleModel.Text = ModelText.Text;
        UpdateDiagramSource();

        var hardware = sections[2].Items;
        HardwareCalloutCanvas.Children.Clear();
        _diagramCallouts.Clear();
        HardwareConnectors.Children.Clear();
        _connectorLines.Clear();
        HardwareConnectors.Opacity = 0;
        var all = hardware.Concat(sections[0].Items.Where(item => item.Label == "BIOS"))
            .Concat(sections[1].Items.Where(item => item.Label == "系统")).ToList();
        foreach (var label in new[] { "处理器", "内存", "硬盘", "主板", "BIOS", "声卡" })
            AddDiagramCallout(all.FirstOrDefault(item => item.Label == label), HardwareCalloutCanvas, true);
        foreach (var label in new[] { "显示器", "显卡", "电池", "系统", "NPU", "网卡" })
            AddDiagramCallout(all.FirstOrDefault(item => item.Label == label), HardwareCalloutCanvas, false);
        ArrangeSettingsCards();
        if (_bootCompleted) RevealDiagramCallouts();
    }

    private void UpdateDiagramSource() => SettingsStyleSource.Text =
        LocalizationService.L("Hw_DiagramHint.Text", "拖动旋转 · 点击标注查看参数 · 部件位置为示意") + " · " + HardwareSourceText.Text;

    private bool _diagramLaptop;
    private readonly List<(Button Card, string Label, bool Left, string Key)> _diagramCallouts = [];

    private static bool ReduceDiagramMotion => FastModeService.IsFastModeEnabled()
        || !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    private void HideDiagramCallouts()
    {
        _bootCompleted = false;
        _bootCompletionWaiter.TrySetCanceled();
        _bootCompletionWaiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _calloutRevealVersion++;
        CancelDiagramCardFlights();
        HardwareConnectors.Opacity = 0;
        foreach (var (card, _, _, _) in _diagramCallouts)
        {
            // Translation 只有 SetIsTranslationEnabled 之后才存在于 Visual 上：3D boot 动画没播完
            // （卡片尚未经过 RevealDiagramCallouts）就切回经典视图时，直接 StopAnimation 会抛异常闪退。
            ElementCompositionPreview.SetIsTranslationEnabled(card, true);
            var visual = ElementCompositionPreview.GetElementVisual(card);
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Translation");
            visual.StopAnimation("Scale");
            visual.StopAnimation("RotationAngle");
            visual.RotationAngle = 0;
            visual.Scale = Vector3.One;
            card.Opacity = 0;
            card.IsHitTestVisible = false;
        }
    }

    private void AddDiagramCallout(HardwareInfoItem? item, Canvas host, bool left)
    {
        if (item is null) return;
        var devices = item.DeviceValues.Count > 0 ? item.DeviceValues : new[] { item.Value };
        for (var index = 0; index < devices.Count; index++)
        {
            var value = devices[index];
            var title = item.DisplayLabel + (devices.Count > 1 ? $" {index + 1}" : "");
            var key = $"{item.Label}_{index + 1}";
            var content = new StackPanel { Spacing = 6 };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            heading.Children.Add(new Image { Source = HardwareIconService.Get(HardwareIconService.KeyForLabel(item.Label)),
                Width = 22, Height = 22, Stretch = Stretch.Uniform });
            heading.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            content.Children.Add(heading);
            content.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.None, Style = (Style)Resources["SettingsHardwareCaptionStyle"] });
            var button = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12), MinHeight = 76,
                Style = (Style)Resources["SettingsHardwareCalloutStyle"],
                Opacity = 0, IsHitTestVisible = false };
            var deviceIndex = index;
            button.Click += (_, _) => _hardwareFocusTransition = FocusHardwareCardAsync(button, item.Label, title, value, deviceIndex);
            button.PointerEntered += (_, _) => AnimateCalloutHover(button, true);
            button.PointerExited += (_, _) => AnimateCalloutHover(button, false);
            AutomationProperties.SetAutomationId(button, "HardwareCallout_" + key);
            AutomationProperties.SetName(button, $"{title}: {value}");
            host.Children.Add(button);
            _diagramCallouts.Add((button, item.Label, left, key));
        }
    }

    private void HardwareDiagram_SizeChanged(object sender, SizeChangedEventArgs e) => ArrangeSettingsCards();

    private Windows.UI.ViewManagement.UISettings? _diagramUiSettings;
    private readonly SolidColorBrush _connectorBrush = new();
    private readonly Dictionary<string, Line> _connectorLines = [];

    private void EnsureDiagramAccent()
    {
        if (_diagramUiSettings is null)
        {
            _diagramUiSettings = new Windows.UI.ViewManagement.UISettings();
            _diagramUiSettings.ColorValuesChanged += DiagramAccentChanged;
        }
        _connectorBrush.Color = _diagramUiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
    }

    private void DiagramAccentChanged(Windows.UI.ViewManagement.UISettings sender, object args) =>
        DispatcherQueue.TryEnqueue(() => { if (UseSettingsStyle) EnsureDiagramAccent(); });

    private void DrawHardwareConnectors()
    {
        if (_hardwareFocus is not null || SettingsStyleRoot.ActualWidth < 960 || !UseSettingsStyle || _modelAnchors.Count == 0)
        {
            foreach (var item in _connectorLines.Values) item.Visibility = Visibility.Collapsed;
            return;
        }
        foreach (var (card, label, _, key) in _diagramCallouts)
        {
            if (card.ActualWidth == 0 || !_modelAnchors.TryGetValue(label, out var anchor)) continue;
            var end = HardwareModelViewport.TransformToVisual(HardwareDiagram).TransformPoint(
                new Point(anchor.X * HardwareModelViewport.ActualWidth, anchor.Y * HardwareModelViewport.ActualHeight));
            var center = card.TransformToVisual(HardwareDiagram).TransformPoint(new Point(card.ActualWidth / 2, card.ActualHeight / 2));
            var scale = 1d;
            var opacity = 1d;
            if (TryGetDiagramCardFlight(key, out var flyingCenter, out var flyingScale, out var flyingOpacity))
            {
                center = flyingCenter;
                scale = flyingScale;
                opacity = flyingOpacity;
            }
            var dx = end.X - center.X;
            var dy = end.Y - center.Y;
            var distance = Math.Max(Math.Abs(dx) / (card.ActualWidth * scale / 2), Math.Abs(dy) / (card.ActualHeight * scale / 2));
            var fraction = distance > 0 ? 1 / distance : 0;
            var start = new Point(center.X + dx * fraction, center.Y + dy * fraction);
            if (!_connectorLines.TryGetValue(key, out var line))
            {
                line = new Line { StrokeThickness = 1.8, Opacity = 0.85, Stroke = _connectorBrush };
                _connectorLines[key] = line;
                HardwareConnectors.Children.Add(line);
            }
            // While the tiny card is still inside the computer, no stray line crosses through it.
            line.Visibility = opacity > 0 && distance > 1 ? Visibility.Visible : Visibility.Collapsed;
            line.Opacity = .85 * opacity;
            line.X1 = end.X; line.Y1 = end.Y; line.X2 = start.X; line.Y2 = start.Y;
        }
    }

    private bool _wallpaperLoading;
    private bool _bootCompleted;
    private TaskCompletionSource<bool> _bootCompletionWaiter = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calloutRevealVersion;
    private Task _calloutRevealTask = Task.CompletedTask;
    private bool _sceneReady;
    private bool _sceneInitializing;
    private bool _sceneConfigured;
    private IHardwareScene? _scene;
    private int _sceneLoadVersion;
    private TaskCompletionSource<bool>? _sceneSnapshotWaiter;
    private object? _pendingScene;
    private readonly Dictionary<string, Point> _modelAnchors = [];

    private async Task LoadDesktopWallpaperAsync()
    {
        if (_wallpaperLoading || _pendingScene is not null) return;
        _wallpaperLoading = true;
        try
        {
            _diagramLaptop = await Task.Run(HardwareInfoService.IsLaptop);
            var wallpaper = await DesktopWallpaperService.LoadDataUriAsync();
            if (!UseSettingsStyle) return;
            _pendingScene = new { laptop = _diagramLaptop, wallpaper, embedded = true,
                dark = ActualTheme == ElementTheme.Dark, reducedMotion = ReduceDiagramMotion,
                labels = _diagramCallouts.Select(item => item.Label).Distinct().ToArray(),
                sides = _diagramCallouts.GroupBy(item => item.Label).ToDictionary(group => group.Key, group => group.First().Left),
                connectors = SettingsStyleRoot.ActualWidth >= 960 };
            await EnsureHardwareSceneAsync();
            ConfigureHardwareScene();
        }
        catch (Exception ex) { ShowHardwareModelError(ex.Message); }
        finally { _wallpaperLoading = false; }
    }

    private void ConfigureHardwareScene()
    {
        if (!_sceneReady || _sceneConfigured || _pendingScene is null) return;
        _sceneConfigured = true;
        _scene?.Post(JsonSerializer.Serialize(_pendingScene));
    }

    private async Task EnsureHardwareSceneAsync()
    {
        if (_sceneInitializing || _sceneReady || !UseSettingsStyle) return;
        _sceneInitializing = true;
        var version = ++_sceneLoadVersion;
        var scene = HardwareSceneRuntime.Create();
        _scene = scene;
        HardwareSceneHost.Content = scene.View;
        scene.MessageReceived += HardwareScene_MessageReceived;
        try
        {
            var color = (SettingsStyleScroll.Background as SolidColorBrush)?.Color ?? Windows.UI.Color.FromArgb(0, 0, 0, 0);
            await scene.InitializeAsync(color);
            if (version != _sceneLoadVersion || !UseSettingsStyle) return;
            _ = WatchHardwareSceneAsync(version);
        }
        catch { _sceneInitializing = false; throw; }
    }

    private async Task WatchHardwareSceneAsync(int version)
    {
        await Task.Delay(15000);
        if (version == _sceneLoadVersion && !_sceneReady && IsLoaded && UseSettingsStyle)
            ShowHardwareModelError("3D 场景初始化超时");
    }

    private async void HardwareScene_MessageReceived(string message)
    {
        if (!UseSettingsStyle || _scene is null) return;
        var version = _sceneLoadVersion;
        try
        {
            using var json = JsonDocument.Parse(message);
            var root = json.RootElement;
            var type = root.GetProperty("type").GetString();
            var matchesFocus = _hardwareFocus is { } focus && root.TryGetProperty("requestId", out var requestId)
                && requestId.GetInt32() == focus.SceneRequestId;
            if (type == "focusStarted" && matchesFocus && _hardwareFocus is { Closing: false })
                _hardwareFocusSceneStarted?.TrySetResult(true);
            else if (type == "focusFailed" && matchesFocus)
                _hardwareFocusSceneStarted?.TrySetException(new InvalidOperationException(root.GetProperty("message").GetString()));
            else if (type == "focusClosed" && matchesFocus) _hardwareFocusSceneClosed?.TrySetResult(true);
            else if (type == "dismissFocus" && matchesFocus) _hardwareFocusTransition = RestoreHardwareFocusAsync();
            else if (type == "focusAutoStopped" && matchesFocus)
                UpdateFocusHint(LocalizationService.L("Hw_FocusManual", "已停止自动旋转 · 可拖动模型查看 · 点击空白处或按 Esc 返回"));
            else if (type == "autoStopped") HardwareAutoRotate.IsChecked = false;
            else if (type == "booted")
            {
                _bootCompleted = true;
                RevealDiagramCallouts();
                _bootCompletionWaiter.TrySetResult(true);
            }
            else if (type == "ready")
            {
                ApplySceneBackground();
                _sceneReady = true;
                ConfigureHardwareScene();
            }
            else if (type == "anchors")
            {
                UpdateSceneAnchors(root.GetProperty("anchors"));
                DrawHardwareConnectors();
            }
            else if (type == "rendered")
            {
                if (root.TryGetProperty("image", out var image))
                {
                    var encoded = image.GetString()!;
                    var bytes = Convert.FromBase64String(encoded[(encoded.IndexOf(',') + 1)..]);
                    using var stream = new MemoryStream(bytes);
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
                    if (version != _sceneLoadVersion) return;
                    HardwareModelImage.Source = bitmap;
                    _sceneSnapshotWaiter?.TrySetResult(true);
                }
                UpdateSceneAnchors(root.GetProperty("anchors"));
                HardwareModelError.Visibility = Visibility.Collapsed;
                DispatcherQueue.TryEnqueue(DrawHardwareConnectors);
                _sceneInitializing = false;
            }
            else if (type == "error") ShowHardwareModelError(root.GetProperty("message").GetString());
        }
        catch (Exception ex) { ShowHardwareModelError(ex.Message); }
    }

    private void ShowHardwareModelError(string? message)
    {
        HardwareModelError.Visibility = Visibility.Visible;
        _sceneInitializing = false;
        _bootCompleted = true;
        RevealDiagramCallouts();
        _bootCompletionWaiter.TrySetResult(true);
        System.Diagnostics.Debug.WriteLine($"[Hardware] 3D 预览：{message}");
    }

    private void UpdateSceneAnchors(JsonElement anchors)
    {
        _modelAnchors.Clear();
        foreach (var property in anchors.EnumerateObject())
            _modelAnchors[property.Name] = new Point(property.Value[0].GetDouble(), property.Value[1].GetDouble());
    }

    private async Task PrepareHardwareSceneScreenshotAsync()
    {
        await _hardwareFocusTransition;
        if (!_bootCompleted)
        {
            try { await _bootCompletionWaiter.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (TimeoutException) { throw new TimeoutException("3D 场景尚未就绪，请稍后再截图"); }
        }
        await _calloutRevealTask;
        if (!_sceneReady) return;
        _sceneSnapshotWaiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _scene?.Post("{\"type\":\"snapshot\"}");
        if (await Task.WhenAny(_sceneSnapshotWaiter.Task, Task.Delay(4000)) != _sceneSnapshotWaiter.Task)
            throw new TimeoutException("3D 模型截图超时，请稍后重试");

        HardwareModelImage.Visibility = Visibility.Visible;
        HardwareSceneHost.Visibility = Visibility.Collapsed;
        DrawHardwareConnectors();
        SettingsStyleRoot.UpdateLayout();
        await Task.Delay(50);
    }

    private void RestoreHardwareSceneAfterScreenshot()
    {
        HardwareSceneHost.Visibility = Visibility.Visible;
        HardwareModelImage.Visibility = Visibility.Collapsed;

        _sceneSnapshotWaiter = null;
        if (UseSettingsStyle) DrawHardwareConnectors();
    }

    private void ApplySceneBackground()
    {
        if (SettingsStyleScroll.Background is SolidColorBrush brush)
            _scene?.SetBackground(brush.Color);
    }

    private void HardwareAutoRotate_Click(object sender, RoutedEventArgs e)
    {
        if (_sceneReady) _scene?.Post(JsonSerializer.Serialize(
            new { type = "auto", value = HardwareAutoRotate.IsChecked == true }));
    }

    private void HardwareReplay_Click(object sender, RoutedEventArgs e)
    {
        if (!_sceneReady) return;
        HideDiagramCallouts();
        _scene?.Post("{\"type\":\"replay\"}");
    }

    private void SuspendHardwareScene()
    {
        if (_sceneReady) _scene?.Post("{\"type\":\"pause\",\"value\":true}");
    }

    private void ReleaseHardwareScene()
    {
        ResetHardwareFocus();
        _sceneLoadVersion++;
        _sceneReady = false;
        _sceneConfigured = false;
        _sceneInitializing = false;
        _pendingScene = null;
        if (_diagramUiSettings is not null)
        {
            _diagramUiSettings.ColorValuesChanged -= DiagramAccentChanged;
            _diagramUiSettings = null;
        }
        HideDiagramCallouts();
        HardwareAutoRotate.IsChecked = false;
        var scene = _scene;
        _scene = null;
        if (scene is null) return;
        scene.MessageReceived -= HardwareScene_MessageReceived;
        HardwareSceneHost.Content = null;
        scene.Dispose();
    }

    private void HardwareMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string mode })
            AppSettings.Set("HardwareDiagramStyle", mode == "diagram");
    }

    private void SettingsStyleScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _widthFitBudget = 8;
        ArrangeSettingsCards();
        DispatcherQueue.TryEnqueue(FitSettingsStyleWidth);
    }

    private void SettingsStyleRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ArrangeSettingsCards();
        DispatcherQueue.TryEnqueue(FitSettingsStyleWidth);
    }

    private int _widthFitBudget;

    // 3D 全景要为「一屏放下」整体等比缩放，高度先受限时缩放后的内容比可用宽度窄，
    // 左右就露出留白。这里反推所需逻辑宽度：逻辑宽度 × 缩放比 = 可用宽度。
    // 页面自然高度随宽度单调不增（板高有下限、换行更少），所以迭代几步必然收敛。
    private void FitSettingsStyleWidth()
    {
        if (!UseSettingsStyle || _widthFitBudget <= 0) return;
        var root = SettingsStyleRoot;
        var hostWidth = SettingsStyleScroll.ActualWidth;
        var hostHeight = SettingsStyleScroll.ActualHeight;
        var contentWidth = root.ActualWidth;
        var contentHeight = root.ActualHeight;
        if (hostWidth < 2 || hostHeight < 2 || contentWidth < 2 || contentHeight < 2) return;
        // 与 Viewbox（Uniform + DownOnly）一致的等比缩放比。
        var scale = Math.Min(1, Math.Min(hostWidth / contentWidth, hostHeight / contentHeight));
        if (scale <= 0 || double.IsNaN(scale)) return;
        var target = Math.Clamp(Math.Round(hostWidth / scale), 960, 4096);
        if (!double.IsNaN(root.Width) && Math.Abs(target - root.Width) < 1)
        {
            _widthFitBudget = 0;
            return;
        }
        _widthFitBudget--;
        root.Width = target;
    }

    private bool _arrangingDiagramCards;
    private readonly int _diagramLayoutSeed = Random.Shared.Next();
    private string? _diagramLayoutSignature;
    private HardwareDiagramLayout.Layout? _diagramLayout;

    private void ArrangeSettingsCards()
    {
        if (_arrangingDiagramCards || _hardwareFocus is not null) return;
        _arrangingDiagramCards = true;
        try
        {
            var width = Math.Max(960, SettingsStyleRoot.ActualWidth - 48);
            var measured = new List<HardwareDiagramLayout.Card>();
            foreach (var (card, label, left, key) in _diagramCallouts)
            {
                var cardWidth = HardwareDiagramLayout.CardWidth(key, label);
                card.Width = cardWidth;
                card.Height = double.NaN;
                card.Measure(new Size(cardWidth, double.PositiveInfinity));
                var height = Math.Ceiling(card.DesiredSize.Height);
                card.Height = height;
                measured.Add(new(key, label, left, cardWidth, height));
            }
            var signature = width + ":" + string.Join("|", measured.Select(card => $"{card.Key}:{card.Width}:{card.Height}"));
            if (_diagramLayout is null || signature != _diagramLayoutSignature)
            {
                _diagramLayout = HardwareDiagramLayout.Arrange(measured, width, _diagramLayoutSeed);
                _diagramLayoutSignature = signature;
            }
            var layout = _diagramLayout;
            var cardsByKey = _diagramCallouts.ToDictionary(item => item.Key, item => item.Card);
            foreach (var placement in layout.Cards)
            {
                var card = cardsByKey[placement.Key];
                Canvas.SetLeft(card, placement.X);
                Canvas.SetTop(card, placement.Y);
            }
            if (Math.Abs(HardwareDiagram.Height - layout.Height) > .5) HardwareDiagram.Height = layout.Height;
            // Keep the renderer stretched to the board. Focus moves 3D objects, never the WebView.
            var narrow = SettingsStyleRoot.ActualWidth < 640;
            Grid.SetColumn(SettingsStyleActions, narrow ? 0 : 1);
            Grid.SetRow(SettingsStyleActions, narrow ? 1 : 0);
            DispatcherQueue.TryEnqueue(DrawHardwareConnectors);
        }
        finally { _arrangingDiagramCards = false; }
    }
}
