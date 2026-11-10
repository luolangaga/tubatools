using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services.AiAgent;
using Primitives = Microsoft.UI.Xaml.Controls.Primitives;

namespace TubaWinUi3.Controls;

/// <summary>Inline, keyboard accessible agent form. Only an explicit submit sends answers.</summary>
public sealed class AgentQuestionCard : UserControl
{
    private readonly AgentQuestion _question;
    private readonly Dictionary<string, Func<object?>> _read = [];
    private readonly Dictionary<string, FrameworkElement> _fields = [];
    private readonly StackPanel _body = new() { Spacing = 16 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, IsTextSelectionEnabled = true };
    private readonly TaskCompletionSource<IReadOnlyDictionary<string, object>?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Button _submit;
    private readonly Button _skip;
    public Task<IReadOnlyDictionary<string, object>?> Answer => _completion.Task;
    private static string L(string key, string fallback) => AgentText.L(key, fallback);
    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];

    public AgentQuestionCard(AgentQuestion question)
    {
        _question = question;
        var border = new Border { Padding = new Thickness(20), CornerRadius = new CornerRadius(8),
            Background = Resource("CardBackgroundFillColorDefaultBrush"), BorderBrush = Resource("CardStrokeColorDefaultBrush"), BorderThickness = new Thickness(1) };
        HorizontalAlignment = HorizontalAlignment.Stretch;
        var root = new StackPanel { Spacing = 16 };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        heading.Children.Add(new FontIcon { Glyph = "\uE9CE", FontSize = 18, Foreground = Resource("AccentTextFillColorPrimaryBrush") });
        heading.Children.Add(new TextBlock { Text = L("QuestionTitle", "需要你的回答"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        root.Children.Add(heading);
        if (question.Title.Length > 0) root.Children.Add(new TextBlock { Text = question.Title, TextWrapping = TextWrapping.Wrap, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var field in question.Fields) BuildField(field);
        root.Children.Add(_body);
        _error.Foreground = Resource("SystemFillColorCriticalBrush"); root.Children.Add(_error); root.Children.Add(_summary);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _submit = new Button { Content = L("SubmitAnswer", "提交回答"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        _skip = new Button { Content = L("SkipQuestion", "跳过") };
        AutomationProperties.SetAutomationId(_submit, "AgentQuestionSubmit");
        AutomationProperties.SetAutomationId(_skip, "AgentQuestionSkip");
        _submit.Click += (_, _) => Submit(); _skip.Click += (_, _) => Complete(null, L("QuestionSkipped", "已跳过此次询问"));
        buttons.Children.Add(_submit); buttons.Children.Add(_skip); root.Children.Add(buttons);
        border.Child = root; Content = border; RefreshVisibility();
    }

    private void BuildField(AgentQuestionField field)
    {
        var panel = new StackPanel { Spacing = 8 };
        _fields[field.Key] = panel; _body.Children.Add(panel);
        if (field.Kind == "external")
        {
            if (Uri.TryCreate(AgentProtocol.Text(field.Schema, "url"), UriKind.Absolute, out var uri) && uri.Scheme == "https")
                panel.Children.Add(new HyperlinkButton { Content = field.Title, NavigateUri = uri });
            return;
        }
        panel.Children.Add(new TextBlock { Text = field.Title + (field.Required ? " *" : ""), TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (field.Description.Length > 0) panel.Children.Add(new TextBlock { Text = field.Description, TextWrapping = TextWrapping.Wrap, Foreground = Resource("TextFillColorSecondaryBrush") });
        if (field.Options.Count > 0)
        {
            var choices = new List<(Primitives.ToggleButton Button, string Value)>();
            var group = "question-" + Guid.NewGuid().ToString("N");
            foreach (var option in field.Options)
            {
                Primitives.ToggleButton choice = field.Kind == "multiselect" ? new CheckBox() : new RadioButton { GroupName = group };
                var label = new StackPanel { Spacing = 3 };
                label.Children.Add(new TextBlock { Text = option.Label.Length > 0 ? option.Label : option.Value, TextWrapping = TextWrapping.Wrap });
                if (option.Description.Length > 0) label.Children.Add(new TextBlock { Text = option.Description, TextWrapping = TextWrapping.Wrap, Foreground = Resource("TextFillColorSecondaryBrush"), FontSize = 12 });
                choice.Content = label; choice.HorizontalAlignment = HorizontalAlignment.Stretch;
                choice.Padding = new Thickness(10, 8, 10, 8);
                AutomationProperties.SetName(choice, option.Label.Length > 0 ? option.Label : option.Value);
                AutomationProperties.SetAutomationId(choice, "AgentQuestion:" + _question.Id + ":" + field.Key + ":" + choices.Count);
                choices.Add((choice, option.Value)); panel.Children.Add(choice);
                choice.Checked += (_, _) => RefreshVisibility(); choice.Unchecked += (_, _) => RefreshVisibility();
            }
            TextBox? custom = null; RadioButton? other = null;
            if (field.Custom)
            {
                custom = new TextBox { PlaceholderText = L("OtherAnswer", "补充你的回答…"), TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MaxHeight = 140 };
                AutomationProperties.SetName(custom, field.Title + " " + L("OtherAnswer", "补充你的回答…"));
                AutomationProperties.SetAutomationId(custom, "AgentQuestion:" + _question.Id + ":" + field.Key + ":custom");
                if (field.Kind != "multiselect")
                {
                    other = new RadioButton { Content = L("CustomAnswer", "其他回答"), GroupName = group };
                    AutomationProperties.SetAutomationId(other, "AgentQuestion:" + _question.Id + ":" + field.Key + ":other");
                    panel.Children.Add(other); other.Checked += (_, _) => RefreshVisibility();
                }
                panel.Children.Add(custom);
                custom.TextChanged += (_, _) => { if (other is not null && custom.Text.Length > 0) other.IsChecked = true; RefreshVisibility(); };
            }
            _read[field.Key] = () => field.Kind == "multiselect"
                ? choices.Where(c => c.Button.IsChecked == true).Select(c => c.Value).Concat(custom is { Text.Length: > 0 } ? new[] { custom.Text.Trim() } : []).Distinct().ToArray()
                : other?.IsChecked == true ? custom?.Text.Trim() : choices.FirstOrDefault(c => c.Button.IsChecked == true).Value;
            // Single-choice cards deliberately start unselected: no implicit consent.
        }
        else if (field.Kind == "boolean")
        {
            var yes = new RadioButton { Content = L("AnswerYes", "是"), GroupName = "bool-" + Guid.NewGuid().ToString("N") };
            var no = new RadioButton { Content = L("AnswerNo", "否"), GroupName = yes.GroupName };
            AutomationProperties.SetAutomationId(yes, "AgentQuestion:" + _question.Id + ":" + field.Key + ":yes");
            AutomationProperties.SetAutomationId(no, "AgentQuestion:" + _question.Id + ":" + field.Key + ":no");
            panel.Children.Add(yes); panel.Children.Add(no);
            _read[field.Key] = () => yes.IsChecked == true ? true : no.IsChecked == true ? false : null;
            yes.Checked += (_, _) => RefreshVisibility(); no.Checked += (_, _) => RefreshVisibility();
        }
        else if (field.Kind is "number" or "integer")
        {
            var number = new NumberBox { SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Value = double.NaN };
            AutomationProperties.SetName(number, field.Title); AutomationProperties.SetAutomationId(number, "AgentQuestion:" + _question.Id + ":" + field.Key); panel.Children.Add(number);
            _read[field.Key] = () => double.IsNaN(number.Value) ? null : number.Value;
            number.ValueChanged += (_, _) => RefreshVisibility();
        }
        else
        {
            var text = new TextBox { PlaceholderText = AgentProtocol.Text(field.Schema, "placeholder"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 180 };
            AutomationProperties.SetName(text, field.Title); AutomationProperties.SetAutomationId(text, "AgentQuestion:" + _question.Id + ":" + field.Key); panel.Children.Add(text);
            _read[field.Key] = () => string.IsNullOrWhiteSpace(text.Text) ? null : text.Text.Trim();
            text.TextChanged += (_, _) => RefreshVisibility();
        }
    }

    private Dictionary<string, object> ReadAll() => _read.Select(p => (p.Key, Value: p.Value())).Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!);
    private void RefreshVisibility()
    {
        var answers = ReadAll();
        foreach (var field in _question.Fields) _fields[field.Key].Visibility = AgentQuestions.Visible(field, answers) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Submit()
    {
        var all = ReadAll();
        var answers = _question.Fields.Where(f => AgentQuestions.Visible(f, all) && all.ContainsKey(f.Key)).ToDictionary(f => f.Key, f => all[f.Key]);
        var error = AgentQuestions.Validate(_question, answers);
        if (error is not null) { _error.Text = error; _error.Visibility = Visibility.Visible; return; }
        Complete(answers, L("AnswerSubmitted", "已提交回答") + "\n" + string.Join("\n", answers.Select(p =>
        {
            var field = _question.Fields.First(f => f.Key == p.Key);
            string Label(string value) => field.Options.FirstOrDefault(o => o.Value == value)?.Label is { Length: > 0 } label ? label : value;
            return field.Title + "：" + (p.Value is string[] list ? string.Join("、", list.Select(Label)) : p.Value is bool b ? b ? L("AnswerYes", "是") : L("AnswerNo", "否") : Label(p.Value.ToString() ?? ""));
        })));
    }
    private void Complete(IReadOnlyDictionary<string, object>? answers, string summary)
    {
        if (_completion.Task.IsCompleted) return;
        _summary.Text = summary; _summary.Visibility = Visibility.Visible;
        _body.Visibility = _error.Visibility = Visibility.Collapsed;
        _submit.Visibility = _skip.Visibility = Visibility.Collapsed;
        _completion.TrySetResult(answers);
    }
    public bool FocusFirstAnswer()
    {
        foreach (var field in _question.Fields)
        {
            if (!_fields.TryGetValue(field.Key, out var fieldElement) || fieldElement is not StackPanel panel || panel.Visibility != Visibility.Visible) continue;
            foreach (var control in panel.Children.OfType<Control>())
                if (control.IsEnabled && control.Visibility == Visibility.Visible && control.Focus(FocusState.Programmatic)) return true;
        }
        return false;
    }
    public void Cancel()
    {
        Complete(null, L("QuestionCancelled", "此次询问已结束"));
    }
}
