using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Tubatools.AiXamlCard;

/// <summary>
/// 独立卡片窗口：助手交来内容后立刻渲染，不依赖 AI 助手页的视觉树。
/// 一次提交多张时用标签页并排展示；单张时直接铺满。
/// </summary>
internal sealed class CardWindow
{
    private static CardWindow? _current;

    private readonly Window _window;
    private readonly Grid _root = new();
    private readonly TabView _tabs = new() { IsAddTabButtonVisible = false, CanReorderTabs = false };
    private readonly TextBlock _status = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly List<CardView> _views = [];

    private CardWindow(string title)
    {
        _window = new Window { Title = title };
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.Background = CardPalette.BrushFor("TubaSubtle");
        _root.Children.Add(_tabs);
        _status.Padding = new Thickness(16, 8, 16, 10);
        _status.Foreground = CardPalette.BrushFor("TubaMuted");
        Grid.SetRow(_status, 1);
        _root.Children.Add(_status);
        _window.Content = _root;
        _window.Closed += (_, _) => { Release(); if (ReferenceEquals(_current, this)) _current = null; };
        SizeToWorkArea();
    }

    private void SizeToWorkArea()
    {
        try
        {
            var area = DisplayArea.GetFromWindowId(_window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            var width = Math.Min(820, Math.Max(420, area.Width * 0.52));
            var height = Math.Min(920, Math.Max(420, area.Height * 0.82));
            _window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                area.X + (area.Width - (int)width) / 2,
                area.Y + (area.Height - (int)height) / 2,
                (int)width, (int)height));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { }
    }

    /// <summary>展示一批卡片；已有窗口时整批替换。</summary>
    public static void Show(CardRequest request)
    {
        if (request.Count == 0) return;
        var title = request.Cards[0].Title.Length > 0
            ? request.Cards[0].Title
            : "AI 卡片";
        if (request.Count > 1) title = $"{title} 等 {request.Count} 张卡片";

        if (_current is null) _current = new CardWindow(title);
        _current.Render(request, title);
    }

    private void Render(CardRequest request, string title)
    {
        Release();
        _window.Title = title;
        _tabs.TabItems.Clear();

        var loaded = 0;
        foreach (var content in request.Cards)
        {
            var view = new CardView();
            view.SendRequested += CardStreamHostComposer.Insert;
            var ok = view.Load(content);
            _views.Add(view);
            if (ok) loaded++;

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(16),
                Content = view
            };
            var item = new TabViewItem
            {
                Header = content.Title.Length > 0 ? content.Title : "卡片",
                IsClosable = false,
                Content = scroll
            };
            _tabs.TabItems.Add(item);
        }

        if (_tabs.TabItems.Count > 0) _tabs.SelectedIndex = 0;
        _window.Activate();

        _status.Text = loaded == request.Count
            ? (request.Count == 1
                ? (request.Cards[0].HasCode ? "已渲染：XAML 界面 + 动态编译代码后置" : "已渲染：纯 XAML 界面")
                : $"已渲染 {loaded} 张卡片，用上方标签切换")
            : $"{loaded}/{request.Count} 张渲染成功，失败的行内有原因。";
        _status.Foreground = CardPalette.BrushFor(loaded == request.Count ? "TubaMuted" : "TubaBad");
    }

    private void Release()
    {
        foreach (var view in _views) view.Close();
        _views.Clear();
        _tabs.TabItems.Clear();
    }
}
