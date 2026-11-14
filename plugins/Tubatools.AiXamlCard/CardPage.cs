using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Tubatools.AiXamlCard;

/// <summary>
/// 卡片工作台：左边是 AI 写过的卡片，右边按当前主题实时渲染选中的那张。
/// 卡片来自 AI 工作区的 <c>cards/&lt;slug&gt;/card.xaml</c>（+ 可选 <c>card.cs</c>），
/// 由模型用普通文件工具写入，插件只负责加载与执行。
/// </summary>
internal sealed class CardPage
{
    private readonly CardStore _store;
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, MinWidth = 220 };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = CardPalette.BrushFor("TubaMuted") };
    private readonly TextBlock _empty = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = CardPalette.BrushFor("TubaMuted"), Margin = new Thickness(4) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    private CardView? _current;
    private IReadOnlyList<CardFiles> _cards = [];
    private string _lastSignature = "";

    public CardPage(CardStore store)
    {
        _store = store;
        _timer.Tick += (_, _) => Poll();
    }

    public FrameworkElement Build()
    {
        var root = new Grid { ColumnSpacing = 16 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(BuildSidebar());
        var main = BuildMain();
        Grid.SetColumn(main, 1);
        root.Children.Add(main);
        Grid.SetRow(_status, 1);
        Grid.SetColumnSpan(_status, 2);
        root.Children.Add(_status);
        return root;
    }

    private FrameworkElement BuildSidebar()
    {
        var panel = new Grid { RowSpacing = 8 };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var refresh = new Button { Content = "刷新卡片", HorizontalAlignment = HorizontalAlignment.Stretch };
        refresh.Click += (_, _) => { Refresh(force: true); Notice(""); };
        panel.Children.Add(refresh);

        Grid.SetRow(_list, 1);
        _list.SelectionChanged += (_, _) => ShowSelected();
        panel.Children.Add(_list);

        var buttons = new StackPanel { Spacing = 6 };
        var sample = new Button { Content = "生成示例卡片", HorizontalAlignment = HorizontalAlignment.Stretch };
        sample.Click += (_, _) => Caretaker.WriteSample(_store, Notice);
        var folder = new Button { Content = "打开卡片文件夹", HorizontalAlignment = HorizontalAlignment.Stretch };
        folder.Click += (_, _) => OpenFolder();
        buttons.Children.Add(sample);
        buttons.Children.Add(folder);
        Grid.SetRow(buttons, 2);
        panel.Children.Add(buttons);
        return panel;
    }

    private FrameworkElement BuildMain()
    {
        var panel = new Grid();
        _empty.Text = "还没有卡片。\n\n" +
                      "在 AI 助手里让它写一张卡片即可：它会把 XAML（和需要时的 C# 代码后置）写到工作区的 cards/ 目录，这里会自动出现并实时渲染。";
        panel.Children.Add(_empty);
        panel.Children.Add(_scroll);
        return panel;
    }

    public void Activate()
    {
        Refresh(force: true);
        _timer.Start();
    }

    public void Deactivate()
    {
        _timer.Stop();
        Release();
    }

    /// <summary>卡片文件变了就重新加载；签名没变时什么都不做，避免每秒重建界面。</summary>
    private void Poll()
    {
        var cards = _store.List();
        var signature = string.Join("|", cards.Select(c => c.Slug + c.ChangedAt.UtcTicks));
        if (signature == _lastSignature) return;
        Refresh(force: false);
        Notice("检测到卡片变化，已重新加载。");
    }

    private void Refresh(bool force)
    {
        _cards = _store.List();
        _lastSignature = string.Join("|", _cards.Select(c => c.Slug + c.ChangedAt.UtcTicks));
        var selected = (_list.SelectedItem as CardRow)?.Card.Slug;
        _list.Items.Clear();
        foreach (var card in _cards) _list.Items.Add(new CardRow(card));
        if (_list.Items.Count == 0)
        {
            Release();
            _empty.Visibility = Visibility.Visible;
            _scroll.Visibility = Visibility.Collapsed;
            return;
        }
        _empty.Visibility = Visibility.Collapsed;
        _scroll.Visibility = Visibility.Visible;
        var index = _list.Items.Cast<CardRow>().ToList().FindIndex(r => r.Card.Slug == selected);
        _list.SelectedIndex = index >= 0 ? index : _list.Items.Count - 1;
        if (force) ShowSelected();
    }

    private void ShowSelected()
    {
        if (_list.SelectedItem is not CardRow row) return;
        Release();
        var view = new CardView();
        view.SendRequested += text => CardStreamHostComposer.Insert(text);
        if (!view.Load(row.Card))
        {
            _scroll.Content = view;
            return;
        }
        view.MarkLoaded(row.Card.ChangedAt);
        _current = view;
        _scroll.Content = view;
        Notice($"已加载「{row.Card.Slug}」" + (row.Card.HasCode ? "（含动态编译的代码后置）" : "（纯 XAML）"));
    }

    private void Release()
    {
        _current?.Close();
        _current = null;
        _scroll.Content = null;
    }

    private void OpenFolder()
    {
        _store.EnsureRoot();
        Notice("卡片目录：" + _store.Root);
    }

    internal void Notice(string message)
    {
        _status.Text = message;
        _status.Foreground = CardPalette.BrushFor("TubaMuted");
    }

    private sealed record CardRow(CardFiles Card)
    {
        public override string ToString() => Card.Slug + (Card.HasCode ? "  ⚙" : "");
    }
}

/// <summary>页面上的小动作；拆出来是为了让页面本身只关心渲染。</summary>
internal static class Caretaker
{
    public static void WriteSample(CardStore store, Action<string> notice)
    {
        var card = store.WriteSample("sample-" + DateTime.Now.ToString("HHmmss"), "使用时长换算");
        notice(card is null ? "示例卡片写入失败，请检查工作区权限。" : "已写入示例卡片：" + card.Slug);
    }
}
