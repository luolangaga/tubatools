using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace TubaWinUi3.Services;

public static partial class MarkdownTextService
{
    public static void RenderToRichTextBlock(RichTextBlock richTextBlock, string markdown)
    {
        richTextBlock.Blocks.Clear();

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            if (HeadingRegex().IsMatch(line))
            {
                var match = HeadingRegex().Match(line);
                var level = match.Groups[1].Value.Length;
                var text = match.Groups[2].Value.Trim();
                AddHeading(richTextBlock, text, level);
                i++;
            }
            else if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* ") || line.TrimStart().StartsWith("+ "))
            {
                i = AddUnorderedList(richTextBlock, lines, i);
            }
            else if (OrderedListRegex().IsMatch(line.TrimStart()))
            {
                i = AddOrderedList(richTextBlock, lines, i);
            }
            else if (line.Trim() == "---" || line.Trim() == "***" || line.Trim() == "___")
            {
                AddHorizontalRule(richTextBlock);
                i++;
            }
            else if (line.TrimStart().StartsWith("> "))
            {
                i = AddBlockquote(richTextBlock, lines, i);
            }
            else if (line.TrimStart().StartsWith("```"))
            {
                i = AddCodeBlock(richTextBlock, lines, i);
            }
            else
            {
                AddParagraph(richTextBlock, line);
                i++;
            }
        }
    }

    private static void AddHeading(RichTextBlock rtb, string text, int level)
    {
        var para = new Paragraph();
        var fontSize = level switch
        {
            1 => 22,
            2 => 18,
            3 => 15,
            _ => 13
        };
        var fontWeight = level <= 2 ? FontWeights.SemiBold : FontWeights.Normal;

        var run = new Run { Text = text, FontSize = fontSize, FontWeight = fontWeight };
        para.Inlines.Add(run);
        rtb.Blocks.Add(para);
    }

    private static int AddUnorderedList(RichTextBlock rtb, string[] lines, int start)
    {
        var i = start;
        while (i < lines.Length)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("- ") && !trimmed.StartsWith("* ") && !trimmed.StartsWith("+ "))
                break;

            var content = trimmed[2..];
            var para = new Paragraph { TextIndent = 0 };
            var bullet = new Run { Text = "  \u2022  ", Foreground = GetBrush("TextFillColorSecondaryBrush") };
            para.Inlines.Add(bullet);
            AddInlineContent(para, content);
            rtb.Blocks.Add(para);
            i++;
        }
        return i;
    }

    private static int AddOrderedList(RichTextBlock rtb, string[] lines, int start)
    {
        var i = start;
        var number = 1;
        while (i < lines.Length)
        {
            var trimmed = lines[i].TrimStart();
            var match = OrderedListRegex().Match(trimmed);
            if (!match.Success) break;

            var content = trimmed[match.Length..];
            var para = new Paragraph { TextIndent = 0 };
            var numRun = new Run { Text = $"  {number}.  ", Foreground = GetBrush("TextFillColorSecondaryBrush") };
            para.Inlines.Add(numRun);
            AddInlineContent(para, content);
            rtb.Blocks.Add(para);
            number++;
            i++;
        }
        return i;
    }

    private static void AddHorizontalRule(RichTextBlock rtb)
    {
        var para = new Paragraph();
        var run = new Run
        {
            Text = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500",
            Foreground = GetBrush("DividerStrokeColorDefaultBrush"),
            FontSize = 12
        };
        para.Inlines.Add(run);
        rtb.Blocks.Add(para);
    }

    private static int AddBlockquote(RichTextBlock rtb, string[] lines, int start)
    {
        var i = start;
        var sb = new System.Text.StringBuilder();
        while (i < lines.Length)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("> ")) break;
            sb.AppendLine(trimmed[2..]);
            i++;
        }

        var para = new Paragraph();
        var borderRun = new Run { Text = "\u2502 ", Foreground = GetBrush("AccentTextFillColorPrimaryBrush") };
        para.Inlines.Add(borderRun);
        AddInlineContent(para, sb.ToString().TrimEnd());
        rtb.Blocks.Add(para);
        return i;
    }

    private static int AddCodeBlock(RichTextBlock rtb, string[] lines, int start)
    {
        var i = start + 1;
        var sb = new System.Text.StringBuilder();
        while (i < lines.Length)
        {
            if (lines[i].TrimStart().StartsWith("```")) { i++; break; }
            sb.AppendLine(lines[i]);
            i++;
        }

        var para = new Paragraph();
        var codeRun = new Run
        {
            Text = sb.ToString().TrimEnd(),
            FontFamily = new FontFamily("Cascadia Code, Consolas"),
            FontSize = 12,
            Foreground = GetBrush("TextFillColorSecondaryBrush")
        };
        para.Inlines.Add(codeRun);
        rtb.Blocks.Add(para);
        return i;
    }

    private static void AddParagraph(RichTextBlock rtb, string text)
    {
        var para = new Paragraph();
        AddInlineContent(para, text);
        rtb.Blocks.Add(para);
    }

    private static void AddInlineContent(Paragraph para, string text)
    {
        var pos = 0;
        var inlineRegex = InlineRegex();

        while (pos < text.Length)
        {
            var match = inlineRegex.Match(text, pos);
            if (!match.Success || match.Index > pos)
            {
                var plain = match.Success ? text[pos..match.Index] : text[pos..];
                if (!string.IsNullOrEmpty(plain))
                    para.Inlines.Add(new Run { Text = plain });
                if (!match.Success) break;
            }

            if (match.Groups[1].Success)
            {
                var linkText = match.Groups[1].Value;
                var linkUrl = match.Groups[2].Value;
                var hyperlink = new Hyperlink
                {
                    NavigateUri = new Uri(linkUrl),
                    UnderlineStyle = UnderlineStyle.Single
                };
                hyperlink.Inlines.Add(new Run { Text = linkText, Foreground = GetBrush("AccentTextFillColorPrimaryBrush") });
                hyperlink.Click += OnHyperlinkClick;
                para.Inlines.Add(hyperlink);
            }
            else if (match.Groups[3].Success)
            {
                var boldText = match.Groups[3].Value;
                var bold = new Span { FontWeight = FontWeights.SemiBold };
                bold.Inlines.Add(new Run { Text = boldText });
                para.Inlines.Add(bold);
            }
            else if (match.Groups[4].Success)
            {
                var italicText = match.Groups[4].Value;
                var italic = new Span { FontStyle = Windows.UI.Text.FontStyle.Italic };
                italic.Inlines.Add(new Run { Text = italicText });
                para.Inlines.Add(italic);
            }
            else if (match.Groups[5].Success)
            {
                var codeText = match.Groups[5].Value;
                var code = new Span
                {
                    FontFamily = new FontFamily("Cascadia Code, Consolas"),
                    FontSize = 12
                };
                code.Inlines.Add(new Run { Text = codeText, Foreground = GetBrush("AccentTextFillColorPrimaryBrush") });
                para.Inlines.Add(code);
            }
            else if (match.Groups[6].Success)
            {
                var url = match.Groups[6].Value;
                var hyperlink = new Hyperlink
                {
                    NavigateUri = new Uri(url),
                    UnderlineStyle = UnderlineStyle.Single
                };
                hyperlink.Inlines.Add(new Run { Text = url, Foreground = GetBrush("AccentTextFillColorPrimaryBrush") });
                hyperlink.Click += OnHyperlinkClick;
                para.Inlines.Add(hyperlink);
            }

            pos = match.Index + match.Length;
        }
    }

    private static async void OnHyperlinkClick(Hyperlink sender, HyperlinkClickEventArgs args)
    {
        if (sender.NavigateUri is not null)
        {
            try
            {
                await Launcher.LaunchUriAsync(sender.NavigateUri);
            }
            catch
            {
            }
        }
    }

    private static Brush GetBrush(string key)
    {
        return (Brush)Application.Current.Resources[key];
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\d+\.\s")]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"\[(.+?)\]\((.+?)\)|\*\*(.+?)\*\*|\*(.+?)\*|`(.+?)`|<(https?://.+?)>")]
    private static partial Regex InlineRegex();
}
