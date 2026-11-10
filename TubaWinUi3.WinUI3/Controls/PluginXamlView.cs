using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using TubaWinUi3.PluginSdk;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Plugins;

namespace TubaWinUi3.Controls;

internal sealed class PluginXamlView : UserControl, IDisposable
{
    private readonly PluginXamlTool _tool;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _action;
    private readonly FrameworkElement _root;
    private readonly List<Action> _cleanup = [];
    private readonly HashSet<Control> _boundControls = [];
    private readonly Dictionary<Control, string> _observedValues = [];
    private readonly Dictionary<Control, bool> _restore = [];
    private readonly InfoBar _status = new() { IsOpen = false, IsClosable = true, Visibility = Visibility.Collapsed };
    private readonly Button _stop = new() { Content = L("Stop", "停止"), Visibility = Visibility.Collapsed };
    private bool _initialized, _initializing, _running, _disposed, _closing;
    private int _settingValue;
    private static string L(string key, string fallback) => LocalizationService.L("PluginUi_" + key, fallback);

    internal PluginXamlView(PluginXamlTool tool)
    {
        _tool = tool;
        var execution = CreateExecution(tool, DispatcherQueue, () => XamlRoot, Report, _lifetime.Token);
        _root = (tool.Xaml is { } xaml ? XamlReader.Load(xaml) : tool.Definition.CreateContent!(execution)) as FrameworkElement
            ?? throw new InvalidDataException("插件自定义界面根对象必须是 WinUI FrameworkElement。");
        var grid = new Grid { Padding = new Thickness(24, 0, 24, 24), RowSpacing = 12 };
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.Children.Add(_root); Grid.SetRow(_status, 1); grid.Children.Add(_status);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_status, "PluginStatus");
        _status.Closed += (_, _) => { if (!_status.IsOpen) _status.Visibility = Visibility.Collapsed; };
        _stop.HorizontalAlignment = HorizontalAlignment.Left;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_stop, "PluginStop");
        _stop.Click += (_, _) => _action?.Cancel();
        Grid.SetRow(_stop, 2); grid.Children.Add(_stop); Content = grid;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        if (_tool.Definition.InitializeAsync is not { } initialize) return;
        _initializing = true;
        await RunAsync(_ => initialize(new PluginViewContext
        {
            Host = CreateExecution(_tool, DispatcherQueue, () => XamlRoot, Report, _lifetime.Token), Root = _root,
            FindElement = name => Element(name), BindClick = BindClick, BindValueChanged = BindValueChanged, GetValue = ReadValue,
            SetValue = (name, value) => Post(() => WriteValue(name, value)),
            SetEnabled = (name, value) => Post(() => SetEnabled(name, value)),
            SetVisible = (name, value) => Post(() => Element(name).Visibility = value ? Visibility.Visible : Visibility.Collapsed),
            SetImage = (name, relative) => Post(() =>
            {
                var path = PluginPackageRules.ResolveIcon(_tool.PackageDirectory, relative);
                var source = BuiltinIconService.LoadAsset(path);
                if (Element(name) is Image image) image.Source = source;
                else if (Element(name) is ImageIcon icon) icon.Source = source;
                else throw new ArgumentException("目标不是 Image/ImageIcon。");
            }),
            RunOnUiThreadAsync = action => PluginUiThread.RunAsync(DispatcherQueue, action, _lifetime.Token),
            OnClosed = action => { ObjectDisposedException.ThrowIf(_disposed, this); _cleanup.Add(action); }
        }));
        _initializing = false;
    }
    private FrameworkElement Element(string name)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("读取控件请使用 RunOnUiThreadAsync。");
        return (_root.Name == name ? _root : _root.FindName(name)) as FrameworkElement ?? throw new KeyNotFoundException($"插件 XAML 中找不到控件：{name}");
    }
    private void BindClick(string name, Func<PluginExecutionContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var element = Element(name);
        if (element is ToggleSwitch) { BindValueChanged(name, action); return; }
        var button = element as ButtonBase ?? throw new ArgumentException(string.Format(L("NotButton", "{0} 是 {1}，请对值变化使用 BindValueChanged；BindClick 用于按钮或开关。"), name, element.GetType().Name));
        RegisterControl(button, name);
        RoutedEventHandler handler = async (_, _) => await RunAsync(action);
        button.Click += handler; _cleanup.Add(() => button.Click -= handler);
    }
    private void RegisterControl(Control control, string name)
    {
        if (!_boundControls.Add(control)) throw new InvalidOperationException(string.Format(L("AlreadyBound", "{0} 已绑定操作，请勿重复绑定同一控件。"), name));
        if (_running) { _restore[control] = control.IsEnabled; control.IsEnabled = false; }
    }
    private void BindValueChanged(string name, Func<PluginExecutionContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var element = Element(name);
        if (element is not (ToggleSwitch or ToggleButton or TextBox or ComboBox or NumberBox or Slider))
            throw new ArgumentException(string.Format(L("NotValueControl", "{0} 是 {1}，不支持值变化绑定。"), name, element.GetType().Name));
        RegisterControl((Control)element, name);
        var control = (Control)element; _observedValues[control] = ReadElementValue(element);
        async Task Changed()
        {
            if (_disposed || _closing) return;
            var current = ReadElementValue(element);
            if (_observedValues.TryGetValue(control, out var previous) && previous == current) return;
            _observedValues[control] = current;
            if (_settingValue > 0 || _initializing || _running) return;
            await RunAsync(action);
        }
        switch (element)
        {
            case ToggleSwitch toggle:
                RoutedEventHandler toggled = async (_, _) => await Changed();
                toggle.Toggled += toggled; _cleanup.Add(() => toggle.Toggled -= toggled); break;
            case ToggleButton toggle:
                RoutedEventHandler checkedChanged = async (_, _) => await Changed();
                toggle.Checked += checkedChanged; toggle.Unchecked += checkedChanged; toggle.Indeterminate += checkedChanged;
                _cleanup.Add(() => { toggle.Checked -= checkedChanged; toggle.Unchecked -= checkedChanged; toggle.Indeterminate -= checkedChanged; }); break;
            case TextBox text:
                TextChangedEventHandler textChanged = async (_, _) => await Changed();
                text.TextChanged += textChanged; _cleanup.Add(() => text.TextChanged -= textChanged); break;
            case ComboBox combo:
                SelectionChangedEventHandler selectionChanged = async (_, _) => await Changed();
                combo.SelectionChanged += selectionChanged; _cleanup.Add(() => combo.SelectionChanged -= selectionChanged); break;
            case NumberBox number:
                Windows.Foundation.TypedEventHandler<NumberBox, NumberBoxValueChangedEventArgs> numberChanged = async (_, _) => await Changed();
                number.ValueChanged += numberChanged; _cleanup.Add(() => number.ValueChanged -= numberChanged); break;
            case Slider slider:
                RangeBaseValueChangedEventHandler sliderChanged = async (_, _) => await Changed();
                slider.ValueChanged += sliderChanged; _cleanup.Add(() => slider.ValueChanged -= sliderChanged); break;
        }
    }
    private async Task RunAsync(Func<PluginExecutionContext, Task> action)
    {
        if (_running || _disposed || _closing) return;
        ShowStatus("", InfoBarSeverity.Informational);
        _running = true; _action = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        foreach (var control in _boundControls) { _restore[control] = control.IsEnabled; control.IsEnabled = false; }
        _stop.Visibility = _initializing ? Visibility.Collapsed : Visibility.Visible;
        try { await action(CreateExecution(_tool, DispatcherQueue, () => XamlRoot, Report, _action.Token)); }
        catch (OperationCanceledException) when (_action.IsCancellationRequested) { if (!_disposed) Report(L("Cancelled", "操作已停止。")); }
        catch (Exception ex) { if (!_disposed) ShowStatus(string.IsNullOrWhiteSpace(ex.Message) ? L("Failed", "插件操作失败。") : ex.Message, InfoBarSeverity.Error); }
        finally
        {
            _action.Dispose(); _action = null; _running = false;
            if (!_disposed) { foreach (var (button, enabled) in _restore) button.IsEnabled = enabled; _stop.Visibility = Visibility.Collapsed; }
            _restore.Clear();
        }
    }
    private void SetEnabled(string name, bool value)
    {
        var control = Element(name) as Control ?? throw new ArgumentException("目标不是 Control。");
        if (_running && _boundControls.Contains(control)) _restore[control] = value;
        else control.IsEnabled = value;
    }
    private string ReadValue(string name) => ReadElementValue(Element(name));
    private static string ReadElementValue(FrameworkElement element) => element switch
    {
        TextBox text => text.Text, TextBlock text => text.Text,
        ToggleSwitch toggle => toggle.IsOn ? "true" : "false", ToggleButton toggle => toggle.IsChecked == true ? "true" : "false",
        NumberBox number => number.Value.ToString(CultureInfo.InvariantCulture), Slider number => number.Value.ToString(CultureInfo.InvariantCulture),
        ProgressBar number => number.Value.ToString(CultureInfo.InvariantCulture),
        ComboBox combo => ItemValue(combo.SelectedItem), ContentControl content => content.Content?.ToString() ?? "",
        _ => throw new NotSupportedException("此控件请通过 FindElement 配合 WinUI 类型读取。")
    };
    private static string ItemValue(object? item) => item is ComboBoxItem c ? (c.Tag ?? c.Content)?.ToString() ?? "" : item?.ToString() ?? "";
    private void WriteValue(string name, string value)
    {
        var element = Element(name);
        _settingValue++;
        try
        {
        switch (element)
        {
            case TextBox text: text.Text = value; break;
            case TextBlock text: text.Text = value; break;
            case ToggleSwitch toggle: toggle.IsOn = bool.Parse(value); break;
            case ToggleButton toggle: toggle.IsChecked = bool.Parse(value); break;
            case NumberBox number: number.Value = double.Parse(value, CultureInfo.InvariantCulture); break;
            case Slider number: number.Value = double.Parse(value, CultureInfo.InvariantCulture); break;
            case ProgressBar number: number.Value = double.Parse(value, CultureInfo.InvariantCulture); break;
            case ComboBox combo:
                combo.SelectedItem = combo.Items.FirstOrDefault(i => ItemValue(i) == value) ?? throw new ArgumentException("下拉框中不存在该值。"); break;
            case ContentControl content: content.Content = value; break;
            default: throw new NotSupportedException("此控件请通过 FindElement 配合 WinUI 类型修改。");
        }
        }
        finally
        {
            _settingValue--;
            if (!_disposed && element is Control control && _observedValues.ContainsKey(control)) _observedValues[control] = ReadElementValue(element);
        }
    }
    private void Post(Action action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (DispatcherQueue.HasThreadAccess) action();
        else if (!DispatcherQueue.TryEnqueue(() => { if (!_disposed) { try { action(); } catch (Exception ex) { Report(ex.Message); } } })) throw new InvalidOperationException("界面已关闭。");
    }
    private void Report(string message)
    {
        if (_disposed || _closing) return;
        Post(() => ShowStatus(message, InfoBarSeverity.Informational));
    }
    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        var visible = !string.IsNullOrWhiteSpace(message);
        _status.Message = visible ? message : ""; _status.Severity = severity;
        _status.IsOpen = visible; _status.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
    internal static PluginExecutionContext CreateExecution(PluginXamlTool tool, DispatcherQueue dispatcher,
        Func<XamlRoot?> root, Action<string> report, CancellationToken ct)
    {
        async Task<bool> Dialog(string title, string text, bool confirm)
        {
            var accepted = false;
            await PluginUiThread.RunAsync(dispatcher, async () =>
            {
                var dialog = new FluentContentDialog { XamlRoot = root() ?? throw new InvalidOperationException("工具页面已关闭。"), Title = title,
                    Content = new ScrollViewer { MaxHeight = 400, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } },
                    CloseButtonText = L("Close", "关闭"), DefaultButton = ContentDialogButton.Close, RequestedTheme = ThemeService.CurrentElementTheme };
                if (confirm) { dialog.PrimaryButtonText = L("Confirm", "确认"); dialog.CloseButtonText = L("Cancel", "取消"); }
                using var cancel = ct.Register(() => dispatcher.TryEnqueue(dialog.Hide));
                accepted = await dialog.ShowAsync() == ContentDialogResult.Primary; ct.ThrowIfCancellationRequested();
            }, ct);
            return accepted;
        }
        return new PluginExecutionContext
        {
            CancellationToken = ct, Services = new PluginHostServices(tool.PackageId, tool.Id, tool.PackageDirectory, dispatcher, root, report, tool.FullTrust),
            ShowMessageAsync = async (title, text) => { await Dialog(title, text, false); }, ConfirmAsync = (title, text) => Dialog(title, text, true), ReportProgress = report
        };
    }
    public void Dispose()
    {
        if (_disposed || _closing) return;
        _closing = true; _lifetime.Cancel(); _action?.Cancel(); Loaded -= OnLoaded;
        foreach (var cleanup in _cleanup.AsEnumerable().Reverse()) { try { cleanup(); } catch { } }
        _cleanup.Clear(); _disposed = true; _closing = false; _lifetime.Dispose();
    }
}
