using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Controls;

/// <summary>A compact, inspectable tool row; the command/output is revealed on demand.</summary>
internal sealed class AgentOperationView : UserControl
{
    private readonly TextBlock _label = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _target = new() { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly FontIcon _icon = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly FontIcon _stateIcon = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressRing _spinner = new() { Width = 14, Height = 14, IsActive = false };
    private readonly TextBlock _added = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _removed = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _body = new() { Spacing = 8 };
    private readonly TextBlock _output = new() { FontSize = 12, FontFamily = new FontFamily("Cascadia Code, Consolas"), IsTextSelectionEnabled = true };
    private readonly ScrollViewer _outputScroll;
    private readonly ContentControl _diff = new();
    private readonly Button _header;
    private AgentOperation? _operation;
    private bool _expanded, _userToggled;
    private readonly DispatcherTimer _collapseTimer = new();
    private DateTimeOffset? _collapseAt;
    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private static string L(string key, string fallback) => AgentText.L(key, fallback);
    public AgentOperationView()
    {
        var grid = new Grid { ColumnSpacing = 8 };
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto }) grid.ColumnDefinitions.Add(new() { Width = width });
        grid.Children.Add(_icon); Grid.SetColumn(_label, 1); grid.Children.Add(_label); Grid.SetColumn(_target, 2); grid.Children.Add(_target);
        Grid.SetColumn(_added, 3); grid.Children.Add(_added); Grid.SetColumn(_removed, 4); grid.Children.Add(_removed);
        var status = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(_spinner); status.Children.Add(_stateIcon); status.Children.Add(_status); Grid.SetColumn(status, 5); grid.Children.Add(status);
        _added.Foreground = Brush("SystemFillColorSuccessBrush"); _removed.Foreground = Brush("SystemFillColorCriticalBrush");
        _target.Foreground = Brush("TextFillColorSecondaryBrush");
        _header = new Button { Content = grid, Padding = new Thickness(0, 4, 0, 4), MinWidth = 0, MinHeight = 28,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = null, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4) };
        _header.Click += (_, _) => { _collapseTimer.Stop(); _collapseAt = null; _expanded = !_expanded; _userToggled = true; UpdateExpansion(); };
        _outputScroll = new ScrollViewer { Content = _output, MaxHeight = 260, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 4, 0, 8) };
        _body.Children.Add(_header); _body.Children.Add(_diff); _body.Children.Add(_outputScroll); Content = _body;
        _collapseTimer.Tick += (_, _) => CollapseDetails();
        Unloaded += (_, _) => _collapseTimer.Stop();
        Loaded += (_, _) => StartCollapseTimer();
    }
    internal void Update(AgentOperation operation)
    {
        if (_operation == operation) return;
        var old = _operation; _operation = operation;
        if (old?.Id != operation.Id) { _collapseTimer.Stop(); _collapseAt = null; _expanded = false; _userToggled = false; }
        _icon.Glyph = operation.Kind switch { "computer" => "\uE7F4", "command" => "\uE756", "read" => "\uE8A5", "edit" => "\uE70F", "skill" => "\uE8F1", "search" => "\uE721", "question" => "\uE9CE", "load" => "\uE8B7", _ => "\uE713" };
        _label.Text = operation.Kind switch { "command" => "Shell", "read" => "Read", "edit" => "Edit", "skill" => "Skill", "search" => "Search", "question" => "Ask", "load" => L("ToolLoad", "加载插件"), _ => operation.Label };
        _target.Text = operation.Target.Length == 0 ? L("PreparingArguments", "正在准备参数…") : operation.Target;
        _status.Text = operation.StatusText;
        _status.Foreground = _stateIcon.Foreground = Brush(operation.Status switch { "error" => "SystemFillColorCriticalBrush", "completed" => "SystemFillColorSuccessBrush", _ => "TextFillColorSecondaryBrush" });
        _spinner.IsActive = operation.Status == "running"; _spinner.Visibility = _spinner.IsActive ? Visibility.Visible : Visibility.Collapsed;
        _stateIcon.Visibility = _spinner.IsActive ? Visibility.Collapsed : Visibility.Visible;
        _stateIcon.Glyph = operation.Status switch { "completed" => "\uE73E", "error" => "\uEA39", "cancelled" => "\uE71A", _ => "\uE916" };
        _added.Text = operation.Additions is { } added ? "+" + added : ""; _removed.Text = operation.Deletions is { } removed ? "-" + removed : "";
        _added.Visibility = operation.Additions.HasValue ? Visibility.Visible : Visibility.Collapsed; _removed.Visibility = operation.Deletions.HasValue ? Visibility.Visible : Visibility.Collapsed;
        ToolTipService.SetToolTip(_header, operation.Command.Length > 0 ? operation.Command : operation.Target);
        if (old?.Diff != operation.Diff || string.Join('|', old?.Files?.Select(f => f.Path) ?? []) != string.Join('|', operation.Files?.Select(f => f.Path) ?? []))
        {
            if (operation.Files is { Count: > 1 } files)
            {
                var changes = new StackPanel { Spacing = 8 };
                foreach (var file in files)
                {
                    changes.Children.Add(new TextBlock { Text = file.Path + (file.Additions is { } a && file.Deletions is { } d ? $"   +{a}  -{d}" : ""), FontSize = 12, TextWrapping = TextWrapping.Wrap });
                    if (file.Diff.Length > 0) changes.Children.Add(Diff(file.Diff));
                }
                _diff.Content = changes;
            }
            else _diff.Content = operation.Kind == "edit" && operation.Diff.Length > 0 ? Diff(operation.Diff) : null;
        }
        _output.Text = operation.Command.Length > 0 ? operation.Command + (operation.Output.Length > 0 ? "\n\n" + operation.Output : "") : operation.Output;
        if (!_userToggled && operation.Kind == "edit" && operation.Diff.Length > 0 && (old?.Diff.Length ?? 0) == 0) _expanded = true;
        if (operation.Status == "error" && !_userToggled) _expanded = true;
        if (operation.Kind == "edit" && operation.Status == "completed" && operation.Diff.Length > 0 &&
            (old?.Status != "completed" || (old?.Diff.Length ?? 0) == 0))
        { _collapseAt = DateTimeOffset.UtcNow.AddSeconds(2); StartCollapseTimer(); }
        else if (operation.Status != "completed") { _collapseAt = null; _collapseTimer.Stop(); }
        UpdateExpansion();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_header, "AgentOperation:" + operation.Id);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_header, _label.Text + " " + _target.Text + " " + _added.Text + " " + _removed.Text + " " + _status.Text);
    }
    private void StartCollapseTimer()
    {
        if (_collapseAt is not { } at) return;
        var remaining = at - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) { CollapseDetails(); return; }
        _collapseTimer.Interval = remaining; _collapseTimer.Start();
    }
    private void CollapseDetails() { _collapseTimer.Stop(); _collapseAt = null; _expanded = false; UpdateExpansion(); }
    private void UpdateExpansion()
    {
        _diff.Visibility = _expanded && _diff.Content is not null ? Visibility.Visible : Visibility.Collapsed;
        _outputScroll.Visibility = _expanded && _output.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private static FrameworkElement Diff(string diff)
    {
        var lines = diff.Replace("\r\n", "\n").Split('\n');
        var view = new StackPanel { Spacing = 1 };
        foreach (var line in lines.Take(140))
        {
            var color = line.StartsWith('+') && !line.StartsWith("+++") ? "SystemFillColorSuccessBrush" :
                line.StartsWith('-') && !line.StartsWith("---") ? "SystemFillColorCriticalBrush" : "TextFillColorPrimaryBrush";
            view.Children.Add(new TextBlock { Text = line, Foreground = Brush(color), FontFamily = new FontFamily("Cascadia Code, Consolas"), FontSize = 12, IsTextSelectionEnabled = true });
        }
        if (lines.Length > 140) view.Children.Add(new TextBlock { Text = L("DiffTruncated", "其余修改已省略，可在工作区查看完整文件。") });
        return new ScrollViewer { Content = view, MaxHeight = 340, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
}
