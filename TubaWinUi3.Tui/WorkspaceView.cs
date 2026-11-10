using System.Runtime.InteropServices;
using Spectre.Console;
using Spectre.Console.Rendering;
using TubaWinUi3.Tui.Services;

namespace TubaWinUi3.Tui;

internal sealed partial class ToolWorkspace
{
    private const int BodyTop = 3;
    private const int ListTop = BodyTop + 2;
    private const int TextCardHeight = 4;
    private const int CardGap = 1;
    private int _cardHeight = TextCardHeight;
    private int CardRowPitch => _cardHeight + CardGap;
    private readonly bool _supportsIcons = AnsiConsole.Profile.Capabilities.Unicode
        && AnsiConsole.Profile.Capabilities.Ansi
        && AnsiConsole.Profile.Supports(ColorSystem.Standard);
    private int _cardColumns = 1;
    private int _visibleCardRows = 1;
    private int _cardWidth;
    private int _cardGutter;
    private static readonly Color Accent = new(128, 178, 234);
    private static readonly Color Outline = new(92, 99, 110);
    private int _navWidth;
    private int _viewWidth;
    private int _viewHeight;
    private int _detailMaxOffset;

    private IRenderable Render(int width, int height)
    {
        _viewWidth = width;
        _viewHeight = height;
        if (width < 47 || height < 14)
        {
            _navWidth = 0;
            return Line("请放大终端窗口（至少 48 列 × 14 行）。Q 退出。", Color.Yellow);
        }

        _navWidth = width >= 84 ? 20 : 16;
        var bodyHeight = height - 7;
        _visibleRows = bodyHeight - 3;
        _navOffset = KeepVisible(_navOffset, _navIndex, _navigation.Count, _visibleRows);
        var rightWidth = width - _navWidth - 1;
        var contentWidth = rightWidth - 4;
        var cardColumns = contentWidth >= 57 ? 2 : 1;
        if (_cardColumns != cardColumns)
            _rowOffset = 0;
        _cardColumns = cardColumns;
        // Give both cards identical widths. The gutter absorbs any odd column.
        _cardGutter = CardGap + (contentWidth - CardGap) % 2;
        _cardWidth = _cardColumns == 1 ? contentWidth : (contentWidth - _cardGutter) / 2;
        _cardHeight = _supportsIcons && _visibleRows >= ToolIconService.RowsPerIcon + 2
            ? ToolIconService.RowsPerIcon + 2 : TextCardHeight;
        _visibleCardRows = Math.Max(1, (_visibleRows + CardGap) / CardRowPitch);
        if (IsHardware)
            _rowOffset = KeepVisible(_rowOffset, _rowIndex, RowCount, _visibleRows);
        else
            _rowOffset = KeepVisible(_rowOffset / _cardColumns, _rowIndex / _cardColumns,
                (RowCount + _cardColumns - 1) / _cardColumns, _visibleCardRows) * _cardColumns;

        var banner = new Rows(
            Line(" 图吧工具箱", Color.Default, Decoration.Bold, width),
            _editingSearch || ActivePage.Page == Page.Search
                ? Line(_editingSearch ? $" 搜索  {_query}_" : $" 搜索  {(_query.Length == 0 ? "按 / 输入名称、路径或标签" : _query)}",
                    Accent, width: width)
                : Line($" TUI 兼容版  /  v{_version}  {RuntimeInformation.ProcessArchitecture}", Color.Silver, width: width),
            Line(""));
        var nav = Pane(RenderNavigation(), _navWidth, bodyHeight);
        var main = Pane(new Rows(RightHeading(contentWidth),
            _details ? RenderDetails(contentWidth, _visibleRows) : RenderList(contentWidth)), rightWidth, bodyHeight);
        var columns = new Grid { Width = width };
        columns.AddColumn(Column(_navWidth));
        columns.AddColumn(Column(1));
        columns.AddColumn(Column(rightWidth));
        columns.AddRow(nav, Line(" "), main);

        var notice = _status;
        if (notice.Length == 0 && IsHardware && _hardwareRequest != null)
            notice = "正在读取本机硬件…";
        if (notice.Length == 0 && SelectedTool is { } tool)
            notice = tool.Description ?? tool.Path;
        if (notice.Length == 0 && !Directory.Exists(ToolCatalog.ToolsRoot))
            notice = "未找到 Tools 目录。请把程序放在便携包根目录。";

        var arrows = _listFocused && !_details && !IsHardware && _cardColumns > 1 ? "↑↓←→" : "↑↓";
        var controls = width >= 76
            ? $" {arrows} 选择   Enter 进入/启动   Esc 回导航   / 搜索   Q 退出"
            : $" {arrows} 选择  Enter 确认  Esc 返回  / 搜索  Q 退出";
        var actions = IsHardware ? " 空格 详情   C 复制分组   F5 刷新"
            : " 空格 详情  F 收藏  O 目录  C 复制  F5 刷新";

        return new Rows(banner, columns, Line($" {notice}", _status.Length > 0 ? Accent : Color.Silver, width: width),
            Line(_editingSearch ? " Enter 确认搜索   Esc 取消" : controls, Color.Silver, width: width),
            Line(_editingSearch ? "" : actions, Color.Silver, width: width));
    }

    private IRenderable RenderNavigation()
    {
        var rows = new List<IRenderable>
        {
            Line(_listFocused ? "导航" : "导航 · 键盘", _listFocused ? Color.Silver : Accent, Decoration.Bold, _navWidth - 4)
        };
        for (var i = _navOffset; i < Math.Min(_navigation.Count, _navOffset + _visibleRows); i++)
        {
            var active = i == _navIndex;
            rows.Add(Choice(_navigation[i].Title, active, active && !_listFocused, _navWidth - 4));
        }
        return new Rows(rows);
    }

    private IRenderable RenderList(int width)
    {
        if (RowCount == 0)
        {
            var message = ActivePage.Page switch
            {
                Page.Search when _query.Length == 0 => "按 / 输入工具名称、路径或标签",
                Page.Search => "没有找到匹配的工具",
                Page.Favorites => "暂无收藏，选中工具后按 F 添加",
                Page.Hardware when _hardwareRequest != null => "正在读取本机硬件…",
                Page.Hardware => "暂无硬件信息，按 F5 重试",
                _ => "此处暂无工具"
            };
            return Line(message, Color.Grey, width: width);
        }

        if (!IsHardware)
            return RenderCards(width);

        var columns = new Grid { Width = width };
        var nameWidth = Math.Min(18, width / 2);
        columns.AddColumn(Column(nameWidth));
        columns.AddColumn(Column(width - nameWidth));
        for (var i = _rowOffset; i < Math.Min(RowCount, _rowOffset + _visibleRows); i++)
        {
            var row = _hardware[i];
            var selected = i == _rowIndex;
            columns.AddRow(Choice(row.Item.Label, selected, selected && _listFocused, nameWidth),
                Line(row.Item.Value ?? "不可用", width: width - nameWidth));
        }
        return columns;
    }

    private IRenderable RenderCards(int width)
    {
        var cards = new Grid { Width = width };
        cards.AddColumn(Column(_cardWidth));
        if (_cardColumns == 2)
        {
            cards.AddColumn(Column(_cardGutter));
            cards.AddColumn(Column(_cardWidth));
        }

        var end = Math.Min(RowCount, _rowOffset + _visibleCardRows * _cardColumns);
        for (var i = _rowOffset; i < end; i += _cardColumns)
        {
            var first = ToolCard(i, _cardWidth);
            if (_cardColumns == 1)
                cards.AddRow(first);
            else
                cards.AddRow(first, Line(" "), i + 1 < end ? ToolCard(i + 1, _cardWidth) : Line(""));

            if (i + _cardColumns < end)
                cards.AddRow(_cardColumns == 1 ? [Line(" ")] : [Line(" "), Line(" "), Line(" ")]);
        }

        return cards;
    }

    private Panel ToolCard(int index, int width)
    {
        var tool = _tools[index];
        var selected = index == _rowIndex;
        var focused = selected && _listFocused && !_editingSearch;
        var installed = File.Exists(tool.Path);
        var favorite = FavoritesService.IsFavorite(tool.Path);
        var status = !installed ? "未安装" : favorite ? "已收藏" : "";
        var metadata = ActivePage.Page == Page.Category
            ? tool.Description ?? tool.Version ?? ""
            : tool.CategoriesText;
        var icon = _cardHeight > TextCardHeight && installed ? ToolIconService.Get(tool.Path) : null;
        var contentWidth = width - 4;
        var textWidth = icon == null ? contentWidth : contentWidth - ToolIconService.Size - CardGap;
        IRenderable caption = Line(metadata, Color.Silver, width: textWidth);
        if (status.Length > 0)
        {
            const int statusWidth = 6; // Three double-width characters.
            var captionGrid = new Grid { Width = textWidth };
            captionGrid.AddColumn(Column(textWidth - statusWidth - 1));
            captionGrid.AddColumn(Column(1));
            captionGrid.AddColumn(Column(statusWidth));
            captionGrid.AddRow(Line(metadata, Color.Silver, width: textWidth - statusWidth - 1), Line(" "),
                Line(status, installed ? Accent : Color.Yellow));
            caption = captionGrid;
        }

        var text = new Rows(
            Line(tool.Name, focused ? Accent : Color.Default, Decoration.Bold, textWidth),
            caption);
        if (icon == null)
            return Pane(text, width, _cardHeight, focused ? Accent : selected ? Color.Silver : Outline);

        var content = new Grid { Width = contentWidth };
        content.AddColumn(Column(ToolIconService.Size));
        content.AddColumn(Column(CardGap));
        content.AddColumn(Column(textWidth));
        content.AddRow(icon, Line(" "), text);
        return Pane(content, width, _cardHeight, focused ? Accent : selected ? Color.Silver : Outline);
    }

    private IRenderable RenderDetails(int width, int height)
    {
        var fields = new List<string>();
        if (IsHardware && RowCount > 0)
        {
            var row = _hardware[_rowIndex];
            fields.Add(row.Item.Label);
            fields.Add(row.Item.Value ?? "不可用");
        }
        else if (SelectedTool is { } tool)
        {
            fields.Add(tool.Name);
            AddField("分类", tool.CategoriesText);
            AddField("架构", tool.PrimaryArch);
            AddField("版本", tool.Version);
            AddField("简介", tool.Description);
            AddField("标签", tool.TagsText);
            AddField("发行者", tool.Publisher);
            AddField("路径", tool.Path);
        }

        var lines = fields.SelectMany(field => field.Replace("\r", "").Split('\n'))
            .SelectMany(line => Segment.SplitOverflow(new Segment(Clean(line)), Overflow.Fold, width))
            .Select(segment => (IRenderable)Line(segment.Text)).ToList();
        _detailMaxOffset = Math.Max(0, lines.Count - height);
        _detailOffset = Math.Clamp(_detailOffset, 0, _detailMaxOffset);
        return new Rows(lines.Skip(_detailOffset).Take(height).DefaultIfEmpty(Line("")));

        void AddField(string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                fields.Add($"{label}  {value}");
        }
    }

    private IRenderable RightHeading(int width)
    {
        var focused = _listFocused && !_editingSearch;
        var title = !_details ? ActivePage.Title : IsHardware ? _hardware[_rowIndex].Section.Title : "工具详情";
        var position = RowCount == 0 || _details ? "" : $"   {_rowIndex + 1}/{RowCount}";
        var suffix = position + (focused ? " · 键盘" : "");
        // Reserve space for the position and focus label before clipping a long category name.
        var suffixWidth = position.Length + (focused ? 7 : 0);
        return Line(Fit(title, width - suffixWidth) + suffix, focused ? Accent : Color.Default, Decoration.Bold, width);
    }

    private static Panel Pane(IRenderable content, int width, int height, Color? borderColor = null) =>
        new(content)
        {
            Width = width,
            Height = height,
            Expand = true,
            Border = BoxBorder.Square,
            BorderStyle = new Style(borderColor ?? Outline),
            Padding = new Padding(1, 0, 1, 0)
        };

    private static GridColumn Column(int width) =>
        new() { Width = width, NoWrap = true, Padding = new Padding(0, 0, 0, 0) };

    private static Text Choice(string value, bool selected, bool focused, int width) =>
        Line((selected ? "> " : "  ") + value,
            focused ? Accent : Color.Default,
            selected ? Decoration.Bold : Decoration.None, width);

    private static Text Line(string value, Color? color = null, Decoration decoration = Decoration.None, int? width = null) =>
        new(width is { } maxWidth ? Fit(value, maxWidth) : Clean(value),
            new Style(color, decoration: decoration)) { Overflow = Overflow.Ellipsis };

    private static string Fit(string value, int width)
    {
        var segment = new Segment(Clean(value));
        var clipped = Segment.Truncate(segment, width);
        return clipped.Text == segment.Text ? segment.Text : Segment.Truncate(segment, width - 1).Text + "…";
    }

    private static string Clean(string value) =>
        string.Concat((value ?? "").Select(ch => char.IsControl(ch) ? ' ' : ch));

    private static int KeepVisible(int offset, int selection, int count, int visible)
    {
        if (selection < offset)
            offset = selection;
        else if (selection >= offset + visible)
            offset = selection - visible + 1;
        return Math.Clamp(offset, 0, Math.Max(0, count - visible));
    }
}
