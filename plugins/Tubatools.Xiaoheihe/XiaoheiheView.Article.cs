using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Tubatools.Xiaoheihe.Services;
using Windows.UI.Text;

namespace Tubatools.Xiaoheihe;

internal sealed partial class XiaoheiheView
{
    private static readonly Regex RichTag = new(
        @"<(p|h[1-6]|strong|b|em|i|u|s|a|ul|ol|li|blockquote|img|figure|div|section)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsRichArticle(XhhPost post)
        => !string.IsNullOrWhiteSpace(post.Html) && RichTag.IsMatch(post.Html);

    private FrameworkElement? BuildArticle(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var panel = new StackPanel { Spacing = 12 };
        AppendArticleChildren(panel, document.DocumentNode);
        return panel.Children.Count == 0 ? null : panel;
    }

    private void AppendArticleChildren(StackPanel panel, HtmlNode parent)
    {
        foreach (HtmlNode node in parent.ChildNodes)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                string text = HtmlEntity.DeEntitize(node.InnerText).Trim();
                if (text.Length > 0)
                {
                    TextBlock paragraph = NewArticleTextBlock(14);
                    paragraph.Inlines.Add(new Run { Text = text });
                    panel.Children.Add(paragraph);
                }
                continue;
            }

            string name = node.Name.ToLowerInvariant();
            switch (name)
            {
                case "h1": panel.Children.Add(BuildArticleHeading(node, 26)); break;
                case "h2": panel.Children.Add(BuildArticleHeading(node, 22)); break;
                case "h3": panel.Children.Add(BuildArticleHeading(node, 19)); break;
                case "h4": panel.Children.Add(BuildArticleHeading(node, 17)); break;
                case "h5": panel.Children.Add(BuildArticleHeading(node, 15)); break;
                case "h6": panel.Children.Add(BuildArticleHeading(node, 14)); break;
                case "p":
                {
                    FrameworkElement paragraph = BuildArticleParagraph(node);
                    panel.Children.Add(paragraph);
                    break;
                }
                case "img":
                {
                    FrameworkElement? image = BuildArticleImage(node);
                    if (image is not null) panel.Children.Add(image);
                    break;
                }
                case "ul": panel.Children.Add(BuildArticleList(node, ordered: false)); break;
                case "ol": panel.Children.Add(BuildArticleList(node, ordered: true)); break;
                case "blockquote": panel.Children.Add(BuildArticleQuote(node)); break;
                case "figure": AppendArticleChildren(panel, node); break;
                case "figcaption":
                {
                    string caption = HtmlEntity.DeEntitize(node.InnerText).Trim();
                    if (caption.Length > 0) panel.Children.Add(T(caption, 11, secondary: true, wrap: true));
                    break;
                }
                case "hr": panel.Children.Add(Divider()); break;
                case "video":
                {
                    string source = node.GetAttributeValue("src", "");
                    if (string.IsNullOrWhiteSpace(source)) source = node.GetAttributeValue("data-src", "");
                    if (!string.IsNullOrWhiteSpace(source))
                    {
                        Button play = AccentBtn("播放视频", "\uE768");
                        string videoUrl = source;
                        play.Click += async (_, _) => await RunAsync(() => ShowVideoAsync(videoUrl));
                        panel.Children.Add(play);
                    }
                    break;
                }
                case "br":
                    break;
                case "div":
                case "section":
                case "article":
                case "main":
                    AppendArticleChildren(panel, node);
                    break;
                default:
                    if (node.HasChildNodes) AppendArticleChildren(panel, node);
                    break;
            }
        }
    }

    private TextBlock NewArticleTextBlock(double fontSize)
        => new()
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            LineHeight = fontSize + 9,
            IsTextSelectionEnabled = true
        };

    private FrameworkElement BuildArticleParagraph(HtmlNode node)
    {
        bool hasImage = node.Descendants("img").Any();
        if (!hasImage)
        {
            TextBlock text = NewArticleTextBlock(14);
            AppendArticleInlines(text.Inlines, node, false, false, false, false);
            return text;
        }

        var panel = new StackPanel { Spacing = 10 };
        TextBlock current = NewArticleTextBlock(14);
        foreach (HtmlNode child in node.ChildNodes)
        {
            if (child.Name.Equals("img", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Inlines.Count > 0)
                {
                    panel.Children.Add(current);
                    current = NewArticleTextBlock(14);
                }
                FrameworkElement? image = BuildArticleImage(child);
                if (image is not null) panel.Children.Add(image);
                continue;
            }
            AppendArticleInlineNode(current.Inlines, child, false, false, false, false);
        }
        if (current.Inlines.Count > 0) panel.Children.Add(current);
        return panel;
    }

    private TextBlock BuildArticleHeading(HtmlNode node, double fontSize)
    {
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            LineHeight = fontSize * 1.35,
            Margin = new Thickness(0, 7, 0, 0),
            IsTextSelectionEnabled = true
        };
        AppendArticleInlines(text.Inlines, node, false, false, false, false);
        return text;
    }

    private void AppendArticleInlines(InlineCollection inlines, HtmlNode node,
        bool bold, bool italic, bool underline, bool strike)
    {
        foreach (HtmlNode child in node.ChildNodes)
            AppendArticleInlineNode(inlines, child, bold, italic, underline, strike);
    }

    private void AppendArticleInlineNode(InlineCollection inlines, HtmlNode node,
        bool bold, bool italic, bool underline, bool strike)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            string text = HtmlEntity.DeEntitize(node.InnerText);
            if (text.Length == 0) return;
            var run = new Run { Text = text };
            ApplyArticleRunStyle(run, bold, italic, underline, strike);
            inlines.Add(run);
            return;
        }

        if (node.NodeType != HtmlNodeType.Element) return;
        string name = node.Name.ToLowerInvariant();
        switch (name)
        {
            case "br":
                inlines.Add(new LineBreak());
                return;
            case "strong":
            case "b":
                bold = true;
                break;
            case "em":
            case "i":
                italic = true;
                break;
            case "u":
                underline = true;
                break;
            case "s":
            case "strike":
            case "del":
                strike = true;
                break;
            case "a":
            {
                string href = node.GetAttributeValue("href", "");
                if (TryBuildArticleUri(href, out Uri? uri))
                {
                    var link = new Hyperlink { Foreground = _palette.AccentText };
                    AppendArticleInlines(link.Inlines, node, bold, italic, underline, strike);
                    link.Click += async (_, _) => await _host.Services.OpenUriAsync(uri);
                    inlines.Add(link);
                }
                else
                {
                    AppendArticleInlines(inlines, node, bold, italic, underline, strike);
                }
                return;
            }
            case "code":
            {
                var code = new Run
                {
                    Text = HtmlEntity.DeEntitize(node.InnerText),
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = _palette.AccentText
                };
                ApplyArticleRunStyle(code, bold, italic, underline, strike);
                inlines.Add(code);
                return;
            }
            case "img":
                return;
        }

        foreach (HtmlNode child in node.ChildNodes)
            AppendArticleInlineNode(inlines, child, bold, italic, underline, strike);
    }

    private static void ApplyArticleRunStyle(Run run, bool bold, bool italic, bool underline, bool strike)
    {
        run.FontWeight = bold ? FontWeights.Bold : FontWeights.Normal;
        run.FontStyle = italic ? FontStyle.Italic : FontStyle.Normal;
        TextDecorations decorations = TextDecorations.None;
        if (underline) decorations |= TextDecorations.Underline;
        if (strike) decorations |= TextDecorations.Strikethrough;
        run.TextDecorations = decorations;
    }

    private static bool TryBuildArticleUri(string href, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#')) return false;
        if (href.StartsWith("//", StringComparison.Ordinal)) href = "https:" + href;
        if (Uri.TryCreate(href, UriKind.Absolute, out Uri? absolute))
        {
            if (absolute.Scheme is "http" or "https")
            {
                uri = absolute;
                return true;
            }
            return false;
        }
        if (Uri.TryCreate("https://www.xiaoheihe.cn" + (href.StartsWith('/') ? href : "/" + href),
                UriKind.Absolute, out Uri? relative))
        {
            uri = relative;
            return true;
        }
        return false;
    }

    private FrameworkElement? BuildArticleImage(HtmlNode node)
    {
        string kind = node.GetAttributeValue("data-type", "");
        string css = node.GetAttributeValue("class", "");
        if (kind.Contains("emoji", StringComparison.OrdinalIgnoreCase) ||
            css.Contains("emoji", StringComparison.OrdinalIgnoreCase))
            return null;
        if (int.TryParse(node.GetAttributeValue("width", "0"), out int width) && width > 0 && width < 40)
            return null;

        List<string> urls = XhhParser.ExtractImages(node.OuterHtml);
        string url = urls.FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = NormalizeImageUrl(url);

        var image = new Image
        {
            Source = LoadImage(url, 1600),
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        image.ImageFailed += (_, _) => image.Visibility = Visibility.Collapsed;

        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = _palette.Subtle,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = image,
            MinHeight = 80
        };
        ToolTipService.SetToolTip(border, "点击查看大图");
        border.Tapped += async (_, _) => await RunAsync(() => ShowImageAsync(url));
        return border;
    }

    private FrameworkElement BuildArticleQuote(HtmlNode node)
    {
        var panel = new StackPanel { Spacing = 8 };
        AppendArticleChildren(panel, node);
        return new Border
        {
            BorderBrush = _palette.Accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 2, 0, 2),
            Child = panel
        };
    }

    private FrameworkElement BuildArticleList(HtmlNode list, bool ordered)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(8, 0, 0, 0) };
        int index = 1;
        foreach (HtmlNode item in list.ChildNodes.Where(n =>
                     n.Name.Equals("li", StringComparison.OrdinalIgnoreCase)))
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(T(ordered ? $"{index}." : "•", 14, secondary: true));
            var text = NewArticleTextBlock(14);
            AppendArticleInlines(text.Inlines, item, false, false, false, false);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            panel.Children.Add(row);
            index++;
        }
        return panel;
    }
}
