using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Tubatools.Xiaoheihe;

internal sealed partial class XiaoheiheView
{
    internal sealed record EmojiEntry(string Code, string Url);

    private static readonly Dictionary<string, string> EmojiMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cube_来财"] = "https://imgheybox.max-c.com/heybox/emoji/cube_142.png",
        ["cube_我方了"] = "https://imgheybox.max-c.com/heybox/emoji/cube_143.png",
        ["cube_时间旅者"] = "https://imgheybox.max-c.com/heybox/emoji/cube_141.png",
        ["cube_猛男微笑"] = "https://imgheybox.max-c.com/heybox/emoji/cube_136.png",
        ["cube_剑星涂鸦"] = "https://imgheybox.max-c.com/heybox/emoji/cube_135.png",
        ["cube_剑星伊芙"] = "https://imgheybox.max-c.com/heybox/emoji/cube_133.png",
        ["cube_悟空"] = "https://imgheybox.max-c.com/heybox/emoji/cube_105.png",
        ["cube_吃鸡啦"] = "https://imgheybox.max-c.com/heybox/emoji/cube_115.png",
        ["cube_鸡毙你"] = "https://imgheybox.max-c.com/heybox/emoji/cube_116.png",
        ["cube_毁灭战士"] = "https://imgheybox.max-c.com/heybox/emoji/cube_131.png",
        ["cube_P的谎言"] = "https://imgheybox.max-c.com/heybox/emoji/cube_132.png",
        ["cube_良民"] = "https://imgheybox.max-c.com/heybox/emoji/cube_111.png",
        ["cube_浪人砍一刀"] = "https://imgheybox.max-c.com/heybox/emoji/cube_114.png",
        ["cube_嬉水女王"] = "https://imgheybox.max-c.com/heybox/emoji/cube_112.png",
        ["cube_欧润吉"] = "https://imgheybox.max-c.com/heybox/emoji/cube_134.png",
        ["cube_摸摸头"] = "https://imgheybox.max-c.com/heybox/emoji/cube_102.png",
        ["cube_电牛"] = "https://imgheybox.max-c.com/heybox/emoji/cube_103.png",
        ["cube_摘墨镜"] = "https://imgheybox.max-c.com/heybox/emoji/cube_104.png",
        ["cube_窝囊"] = "https://imgheybox.max-c.com/heybox/emoji/cube_101.png",
        ["cube_小鸡"] = "https://imgheybox.max-c.com/heybox/emoji/cube_96.png",
        ["cube_僵尸"] = "https://imgheybox.max-c.com/heybox/emoji/cube_95.png",
        ["cube_2025"] = "https://imgheybox.max-c.com/heybox/emoji/cube_113.png",
        ["cube_doge"] = "https://imgheybox.max-c.com/heybox/emoji/cube_13.png",
        ["cube_滑稽"] = "https://imgheybox.max-c.com/heybox/emoji/cube_34.png",
        ["cube_感动"] = "https://imgheybox.max-c.com/heybox/emoji/cube_24.png",
        ["cube_微笑"] = "https://imgheybox.max-c.com/heybox/emoji/cube_62.png",
        ["cube_乖"] = "https://imgheybox.max-c.com/heybox/emoji/cube_31.png",
        ["cube_打脸"] = "https://imgheybox.max-c.com/heybox/emoji/cube_51.png",
        ["cube_闭嘴"] = "https://imgheybox.max-c.com/heybox/emoji/cube_55.png",
        ["cube_晕"] = "https://imgheybox.max-c.com/heybox/emoji/cube_23.png",
        ["cube_笑cry"] = "https://imgheybox.max-c.com/heybox/emoji/cube_32.png",
        ["cube_喜欢"] = "https://imgheybox.max-c.com/heybox/emoji/cube_14.png",
        ["cube_捂脸哭"] = "https://imgheybox.max-c.com/heybox/emoji/cube_22.png",
        ["cube_惊讶"] = "https://imgheybox.max-c.com/heybox/emoji/cube_16.png",
        ["cube_开心"] = "https://imgheybox.max-c.com/heybox/emoji/cube_21.png",
        ["cube_哭泣"] = "https://imgheybox.max-c.com/heybox/emoji/cube_11.png",
        ["cube_酷"] = "https://imgheybox.max-c.com/heybox/emoji/cube_12.png",
        ["cube_困"] = "https://imgheybox.max-c.com/heybox/emoji/cube_54.png",
        ["cube_喷水"] = "https://imgheybox.max-c.com/heybox/emoji/cube_46.png",
        ["cube_赞"] = "https://imgheybox.max-c.com/heybox/emoji/cube_41.png",
        ["cube_学习"] = "https://imgheybox.max-c.com/heybox/emoji/cube_42.png",
        ["cube_生气"] = "https://imgheybox.max-c.com/heybox/emoji/cube_53.png",
        ["cube_睡觉"] = "https://imgheybox.max-c.com/heybox/emoji/cube_66.png",
        ["cube_叹气"] = "https://imgheybox.max-c.com/heybox/emoji/cube_43.png",
        ["cube_摊手"] = "https://imgheybox.max-c.com/heybox/emoji/cube_45.png",
        ["cube_吐"] = "https://imgheybox.max-c.com/heybox/emoji/cube_56.png",
        ["cube_哇"] = "https://imgheybox.max-c.com/heybox/emoji/cube_63.png",
        ["cube_并不简单"] = "https://imgheybox.max-c.com/heybox/emoji/cube_26.png",
        ["cube_委屈"] = "https://imgheybox.max-c.com/heybox/emoji/cube_25.png",
        ["cube_加油"] = "https://imgheybox.max-c.com/heybox/emoji/cube_44.png",
        ["cube_凄凉"] = "https://imgheybox.max-c.com/heybox/emoji/cube_36.png",
        ["cube_沧桑"] = "https://imgheybox.max-c.com/heybox/emoji/cube_35.png",
        ["cube_吓"] = "https://imgheybox.max-c.com/heybox/emoji/cube_65.png",
        ["cube_咕咕"] = "https://imgheybox.max-c.com/heybox/emoji/cube_61.png",
        ["cube_黑人问号"] = "https://imgheybox.max-c.com/heybox/emoji/cube_15.png",
        ["cube_怒"] = "https://imgheybox.max-c.com/heybox/emoji/cube_33.png",
        ["cube_汗"] = "https://imgheybox.max-c.com/heybox/emoji/cube_64.png",
        ["cube_打咩"] = "https://imgheybox.max-c.com/heybox/emoji/cube_93.png",
        ["cube_上学-丧"] = "https://imgheybox.max-c.com/heybox/emoji/cube_92.png",
        ["cube_上学-乐"] = "https://imgheybox.max-c.com/heybox/emoji/cube_91.png",
        ["cube_这是什么鸟"] = "https://imgheybox.max-c.com/heybox/emoji/cube_86.png",
        ["cube_庆祝"] = "https://imgheybox.max-c.com/heybox/emoji/cube_73.png",
        ["cube_圣诞树"] = "https://imgheybox.max-c.com/heybox/emoji/cube_72.png",
        ["cube_H币"] = "https://imgheybox.max-c.com/heybox/emoji/cube_52.png",
        ["cube_我懂你"] = "https://imgheybox.max-c.com/heybox/emoji/cube_76.png",
        ["cube_阳"] = "https://imgheybox.max-c.com/heybox/emoji/cube_81.png",
        ["cube_比心"] = "https://imgheybox.max-c.com/heybox/emoji/cube_82.png",
        ["cube_wota"] = "https://imgheybox.max-c.com/heybox/emoji/cube_83.png",
        ["cube_鹅"] = "https://imgheybox.max-c.com/heybox/emoji/cube_84.png",
        ["cube_握草"] = "https://imgheybox.max-c.com/heybox/emoji/cube_85.png",
        ["cube_超人"] = "https://imgheybox.max-c.com/heybox/emoji/cube_94.png",
        ["cube_庆祝-圣诞"] = "https://imgheybox.max-c.com/heybox/emoji/cube_74.png",
        ["cube_你懂我"] = "https://imgheybox.max-c.com/heybox/emoji/cube_75.png",
        ["cube_2023"] = "https://imgheybox.max-c.com/heybox/emoji/cube_71.png",
        ["cube_2024"] = "https://imgheybox.max-c.com/heybox/emoji/cube_106.png",
        ["cube_吹口哨"] = "https://imgheybox.max-c.com/heybox/emoji/cube_123.png",
        ["cube_太酷啦"] = "https://imgheybox.max-c.com/heybox/emoji/cube_125.png",
        ["cube_蛋糕"] = "https://imgheybox.max-c.com/heybox/emoji/cube_126.png",
        ["heygirl_白嫖怪"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_13.png",
        ["heygirl_敲开心"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_34.png",
        ["heygirl_这…"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_33.png",
        ["heygirl_无语"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_32.png",
        ["heygirl_茄化"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_31.png",
        ["heygirl_痴"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_15.png",
        ["heygirl_惊"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_44.png",
        ["heygirl_记下来"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_45.png",
        ["heygirl_挨刀"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_46.png",
        ["heygirl_偷看"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_42.png",
        ["heygirl_开可乐"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_35.png",
        ["heygirl_滑稽"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_41.png",
        ["heygirl_哈哈"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_36.png",
        ["heygirl_喝奶茶"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_43.png",
        ["heygirl_疑问"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_14.png",
        ["heygirl_耶嘿"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_11.png",
        ["heygirl_哭"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_12.png",
        ["heygirl_害羞"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_22.png",
        ["heygirl_捏脸"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_21.png",
        ["heygirl_喜欢"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_16.png",
        ["heygirl_吃瓜"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_26.png",
        ["heygirl_rua!"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_25.png",
        ["heygirl_秀"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_24.png",
        ["heygirl_苦酒入喉"] = "https://imgheybox.max-c.com/heybox/emoji/heygirl_23.png"
    };

    private static readonly IReadOnlyList<EmojiEntry> EmojiList =
        EmojiMap.Select(kv => new EmojiEntry(kv.Key, kv.Value)).ToList();

    private static readonly Regex EmojiTokenRegex = new(@"\[([A-Za-z][^\]\s]*_[^\]\s]+)\]", RegexOptions.Compiled);

    private RichTextBlock EmojiText(string text, double fontSize, bool wrap = true)
    {
        var block = new RichTextBlock
        {
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            FontSize = fontSize,
            LineHeight = fontSize + 9,
            IsTextSelectionEnabled = true
        };
        var paragraph = new Paragraph();
        AppendEmojiInlines(paragraph.Inlines, text, fontSize, false, false, false, false);
        block.Blocks.Add(paragraph);
        return block;
    }

    private void AppendEmojiInlines(InlineCollection inlines, string text, double fontSize,
        bool bold, bool italic, bool underline, bool strike)
    {
        int position = 0;
        foreach (Match match in EmojiTokenRegex.Matches(text))
        {
            if (match.Index > position)
                AddEmojiRun(inlines, text[position..match.Index], bold, italic, underline, strike);
            string code = match.Groups[1].Value;
            if (EmojiMap.TryGetValue(code, out string? url) && LoadImage(url, 64) is { } source)
            {
                var picture = new Image
                {
                    Source = source,
                    Width = fontSize + 4,
                    Height = fontSize + 4,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 2, 0)
                };
                picture.ImageFailed += (_, _) => picture.Visibility = Visibility.Collapsed;
                try
                {
                    inlines.Add(new InlineUIContainer { Child = picture });
                }
                catch (Exception)
                {
                    AddEmojiRun(inlines, match.Value, bold, italic, underline, strike);
                }
            }
            else
            {
                AddEmojiRun(inlines, match.Value, bold, italic, underline, strike);
            }
            position = match.Index + match.Length;
        }
        if (position < text.Length)
            AddEmojiRun(inlines, text[position..], bold, italic, underline, strike);
    }

    private static void AddEmojiRun(InlineCollection inlines, string text,
        bool bold, bool italic, bool underline, bool strike)
    {
        if (text.Length == 0) return;
        var run = new Run
        {
            Text = text,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal
        };
        TextDecorations decorations = TextDecorations.None;
        if (underline) decorations |= TextDecorations.Underline;
        if (strike) decorations |= TextDecorations.Strikethrough;
        run.TextDecorations = decorations;
        inlines.Add(run);
    }

    private void InsertTextAtCaret(TextBox box, string text)
    {
        string current = box.Text ?? "";
        int start = Math.Clamp(box.SelectionStart, 0, current.Length);
        int length = Math.Clamp(box.SelectionLength, 0, current.Length - start);
        box.Text = current.Remove(start, length).Insert(start, text);
        box.SelectionStart = start + text.Length;
        box.SelectionLength = 0;
        box.Focus(FocusState.Programmatic);
    }

    private void ShowEmojiFlyout(FrameworkElement anchor, Action<string> insert, bool above = true)
    {
        var grid = new GridView
        {
            Width = 360,
            Height = 300,
            IsItemClickEnabled = false,
            SelectionMode = ListViewSelectionMode.None,
            Padding = new Thickness(4)
        };
        var flyout = new Flyout { Placement = above ? FlyoutPlacementMode.Top : FlyoutPlacementMode.Bottom };
        foreach (EmojiEntry emoji in EmojiList)
        {
            var image = new Image
            {
                Source = LoadImage(emoji.Url, 64),
                Width = 28,
                Height = 28,
                Stretch = Stretch.Uniform
            };
            var button = new Button
            {
                Content = image,
                Width = 38,
                Height = 38,
                Padding = new Thickness(3),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            ToolTipService.SetToolTip(button, "[" + emoji.Code + "]");
            button.Click += (_, _) =>
            {
                insert("[" + emoji.Code + "]");
                flyout.Hide();
            };
            grid.Items.Add(button);
        }
        flyout.Content = grid;
        flyout.ShowAt(anchor);
    }
}
