using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

/// <summary>原生、可选择的 Markdown 视图。只渲染内容，不解析任何旧助手执行指令。</summary>
public sealed class MarkdownView : UserControl
{
    private readonly StackPanel _body = new() { Spacing = 12 };
    public MarkdownView() { Content = _body; }
    public string Markdown
    {
        set
        {
            _body.Children.Clear();
            foreach (var block in MarkdownBlocks.Parse(value ?? ""))
            {
                if (!block.IsCode)
                {
                    var rich = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
                    MarkdownTextService.RenderToRichTextBlock(rich, block.Text);
                    _body.Children.Add(rich);
                    continue;
                }
                var panel = new StackPanel { Spacing = 8 };
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition());
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.Children.Add(new TextBlock { Text = block.Language, VerticalAlignment = VerticalAlignment.Center });
                var copy = new Button { Content = LocalizationService.L("Markdown_Copy", "复制代码"), Padding = new Thickness(8, 4, 8, 4) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(copy, "MarkdownCopyCode");
                copy.Click += (_, _) => ClipboardService.TrySetText(block.Text);
                Grid.SetColumn(copy, 1); header.Children.Add(copy); panel.Children.Add(header);
                panel.Children.Add(new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new TextBlock { Text = block.Text, FontFamily = new FontFamily("Cascadia Code, Consolas"), IsTextSelectionEnabled = true }
                });
                _body.Children.Add(new Border
                {
                    Child = panel, Padding = new Thickness(12), CornerRadius = new CornerRadius(8),
                    Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"]
                });
            }
        }
    }
}
