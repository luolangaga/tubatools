using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
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
                    RenderBlock(block.Text);
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

    /// <summary>
    /// 一个普通 Markdown block 里可能夹着表格。先把普通段落交给 RichTextBlock，
    /// 表格单独切成真正的 Grid，否则表格只会退化成一条带竖线的长段落。
    /// </summary>
    private void RenderBlock(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var plain = new List<string>();
        void FlushPlain()
        {
            var text = string.Join("\n", plain).Trim();
            plain.Clear();
            if (text.Length == 0) return;
            var rich = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            MarkdownTextService.RenderToRichTextBlock(rich, text);
            _body.Children.Add(rich);
        }

        var i = 0;
        while (i < lines.Length)
        {
            if (!MarkdownTables.IsStart(lines, i))
            {
                plain.Add(lines[i]);
                i++;
                continue;
            }

            var table = MarkdownTables.Parse(lines, i, out var next);
            if (table is null || next <= i)
            {
                plain.Add(lines[i]);
                i++;
                continue;
            }

            FlushPlain();
            _body.Children.Add(BuildTable(table));
            i = next;
        }

        FlushPlain();
    }

    private static FrameworkElement BuildTable(MarkdownTable table)
    {
        var grid = new Grid();
        for (var c = 0; c < table.ColumnCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var r = 0; r < table.Rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var isHeader = r == 0;
            for (var c = 0; c < table.ColumnCount; c++)
            {
                var text = c < table.Rows[r].Length ? table.Rows[r][c] : "";
                var rich = new RichTextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    FontSize = 13,
                    TextAlignment = table.Alignments[c] switch
                    {
                        MarkdownTableAlignment.Center => TextAlignment.Center,
                        MarkdownTableAlignment.Right => TextAlignment.Right,
                        _ => TextAlignment.Left
                    }
                };
                MarkdownTextService.RenderToRichTextBlock(rich, text);
                foreach (var paragraph in rich.Blocks.OfType<Paragraph>())
                {
                    paragraph.Margin = new Thickness(0);
                    if (isHeader) paragraph.FontWeight = FontWeights.SemiBold;
                }

                var cell = new Border
                {
                    Child = rich,
                    Padding = new Thickness(8, 6, 8, 6),
                    Background = isHeader
                        ? (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"]
                        : null,
                    BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(
                        0, 0,
                        c < table.ColumnCount - 1 ? 1 : 0,
                        r < table.Rows.Count - 1 ? 1 : 0)
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        return new Border
        {
            Child = grid,
            Margin = new Thickness(0, 4, 0, 4),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }
}
