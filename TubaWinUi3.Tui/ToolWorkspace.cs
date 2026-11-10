using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Spectre.Console;
using TubaWinUi3.Services;
using TubaWinUi3.Tui.Models;
using TubaWinUi3.Tui.Services;

namespace TubaWinUi3.Tui;

internal sealed partial class ToolWorkspace(string version)
{
    private enum Page { All, Favorites, Search, Hardware, Category }
    private sealed record NavigationItem(Page Page, string Title);
    private sealed record HardwareRow(HardwareInfoSection Section, HardwareInfoItem Item);

    private readonly string _version = version;
    private volatile bool _cancelRequested;
    private int _lastClickedRow = -1;
    private long _lastClickTime;

    private readonly List<NavigationItem> _navigation = [];
    private readonly Dictionary<string, IReadOnlyList<ToolItem>> _categoryTools = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ToolItem> _tools = [];
    private IReadOnlyList<HardwareRow> _hardware = [];
    private Task<IReadOnlyList<HardwareInfoSection>> _hardwareRequest;
    private bool _hardwareLoaded;
    private int _navIndex;
    private int _navOffset;
    private int _rowIndex;
    private int _rowOffset;
    private int _detailOffset;
    private bool _listFocused;
    private bool _details;
    private bool _editingSearch;
    private int _previousNavIndex;
    private string _previousQuery = "";
    private string _query = "";
    private string _status = "";
    private int _visibleRows = 1;

    private NavigationItem ActivePage => _navigation[_navIndex];
    private bool IsHardware => ActivePage.Page == Page.Hardware;
    private int RowCount => IsHardware ? _hardware.Count : _tools.Count;
    private ToolItem SelectedTool => !IsHardware && _tools.Count > 0 ? _tools[_rowIndex] : null;

    public void Run()
    {
        using var input = new ConsoleInput();
        Console.CancelKeyPress += Cancel;
        try
        {
            AnsiConsole.Clear();
            ReloadNavigation();
            AnsiConsole.Live(new Text("正在读取工具库…")).AutoClear(true).Start(context =>
            {
                LoadPage();
                int lastWidth = 0, lastHeight = 0;
                bool redraw = true;
                while (!_cancelRequested)
                {
                    var width = Console.WindowWidth;
                    var height = Console.WindowHeight;
                    redraw |= CompleteHardwareRequest();
                    if (redraw || width != lastWidth || height != lastHeight)
                    {
                        context.UpdateTarget(Render(width - 1, height));
                        context.Refresh();
                        lastWidth = width;
                        lastHeight = height;
                        redraw = false;
                    }

                    if (!input.TryRead(out var key, out var mouse))
                    {
                        Thread.Sleep(35);
                        continue;
                    }

                    if (key is { } keyboard)
                    {
                        _lastClickedRow = -1;
                        if (!HandleKey(keyboard))
                            return;
                        redraw = true;
                    }
                    else if (mouse is { } pointer)
                    {
                        redraw = HandleMouse(pointer);
                    }
                }
            });
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
        }
    }

    private void Cancel(object sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        _cancelRequested = true;
    }

    private bool HandleMouse(ConsoleInput.MouseAction mouse)
    {
        if (_navWidth == 0 || mouse.X < 0 || mouse.X >= _viewWidth || mouse.Y < BodyTop || mouse.Y >= _viewHeight - 4)
            return false;

        if (mouse.X == _navWidth)
            return false;

        var inList = mouse.X > _navWidth;
        if (_editingSearch)
        {
            if (mouse.Wheel != 0)
                return false;
            _editingSearch = false;
        }

        _status = "";
        SetFocus(inList);
        if (mouse.Wheel != 0)
        {
            _lastClickedRow = -1;
            for (var i = 0; i < 3; i++)
                MoveSelection(mouse.Wheel > 0 ? ConsoleKey.UpArrow : ConsoleKey.DownArrow);
            return true;
        }

        if (_details || mouse.Y < ListTop)
            return true;

        var row = mouse.Y - ListTop;
        if (row >= _visibleRows)
            return true;

        if (!inList)
        {
            _lastClickedRow = -1;
            var index = _navOffset + row;
            if (index < _navigation.Count)
            {
                if (_navigation[index].Page == Page.Search)
                    BeginSearch();
                else if (index != _navIndex)
                    SelectPage(index);
            }
            return true;
        }

        var target = _rowOffset + row;
        if (!IsHardware)
        {
            var cardRow = row / CardRowPitch;
            var contentStart = _navWidth + CardGap + 2;
            var columnX = mouse.X - contentStart;
            if (cardRow >= _visibleCardRows || row % CardRowPitch >= _cardHeight
                || columnX < 0 || mouse.X >= _viewWidth - 2)
                return true;
            if (_cardColumns == 2 && columnX >= _cardWidth && columnX < _cardWidth + _cardGutter)
                return true;

            var cardColumn = _cardColumns == 2 && columnX >= _cardWidth + _cardGutter ? 1 : 0;
            target = _rowOffset + cardRow * _cardColumns + cardColumn;
        }
        if (target >= RowCount)
            return true;

        _rowIndex = target;
        var now = Environment.TickCount64;
        var doubleClick = mouse.DoubleClick || (_lastClickedRow == target && now - _lastClickTime <= ConsoleInput.GetDoubleClickTime());
        _lastClickedRow = doubleClick ? -1 : target;
        _lastClickTime = now;
        if (doubleClick)
            HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        return true;
    }

    private bool HandleKey(ConsoleKeyInfo key)
    {
        if (_editingSearch)
        {
            EditSearch(key);
            return true;
        }

        if ((key.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Control)) != 0)
            return true;

        _status = "";
        if (key.Key is ConsoleKey.Q)
            return false;

        if (key.Key is ConsoleKey.F3 || key.KeyChar == '/')
        {
            BeginSearch();
            return true;
        }

        switch (key.Key)
        {
            case ConsoleKey.Tab:
                SetFocus(!_listFocused);
                break;
            case ConsoleKey.Escape:
                SetFocus(false);
                break;
            case ConsoleKey.LeftArrow when _listFocused && !IsHardware && !_details:
            case ConsoleKey.RightArrow when _listFocused && !IsHardware && !_details:
                MoveSelection(key.Key);
                break;
            case ConsoleKey.LeftArrow:
                if (_details)
                    _details = false;
                else
                    SetFocus(false);
                break;
            case ConsoleKey.RightArrow:
                SetFocus(true);
                break;
            case ConsoleKey.UpArrow:
            case ConsoleKey.DownArrow:
            case ConsoleKey.PageUp:
            case ConsoleKey.PageDown:
            case ConsoleKey.Home:
            case ConsoleKey.End:
                MoveSelection(key.Key);
                break;
            case ConsoleKey.F5:
                Refresh();
                break;
            case ConsoleKey.Enter when !_listFocused && ActivePage.Page == Page.Search:
                BeginSearch();
                break;
            case ConsoleKey.Enter when !_listFocused:
                SetFocus(true);
                break;
            case ConsoleKey.Enter when SelectedTool is { } tool:
                RunAction(() => Launch(tool));
                break;
            case ConsoleKey.Enter when IsHardware && RowCount > 0:
            case ConsoleKey.Spacebar when _listFocused && RowCount > 0:
                _details = !_details;
                _detailOffset = 0;
                break;
            case ConsoleKey.F when _listFocused && SelectedTool is { } tool:
                RunAction(() =>
                {
                    _status = FavoritesService.ToggleFavorite(tool.Path) ? "已加入收藏" : "已取消收藏";
                    if (ActivePage.Page == Page.Favorites)
                    {
                        LoadPage();
                        _details &= RowCount > 0;
                    }
                });
                break;
            case ConsoleKey.O when _listFocused && SelectedTool is { } tool:
                RunAction(() => Start(tool.EffectiveWorkingDir, tool.EffectiveWorkingDir));
                break;
            case ConsoleKey.C when _listFocused && SelectedTool is { } tool:
                RunAction(() => CopyText(tool.Path));
                break;
            case ConsoleKey.C when _listFocused && IsHardware && RowCount > 0:
                RunAction(() => CopyText(string.Join(Environment.NewLine, _hardware[_rowIndex].Section.Items
                    .Select(item => $"{item.Label}\t{item.Value}"))));
                break;
        }

        return true;
    }

    private void SetFocus(bool listFocused)
    {
        _listFocused = listFocused;
        if (!listFocused)
            _details = false;
    }

    private void MoveSelection(ConsoleKey key)
    {
        var last = _listFocused ? RowCount - 1 : _navigation.Count - 1;
        var current = _listFocused ? _rowIndex : _navIndex;
        if (_details)
        {
            _detailOffset = Move(_detailOffset, _detailMaxOffset, key, 1, _visibleRows);
            return;
        }

        var cards = _listFocused && !IsHardware;
        var stride = cards ? _cardColumns : 1;
        var pageSize = cards ? _visibleCardRows * _cardColumns : _visibleRows;
        var next = Move(current, Math.Max(0, last), key, stride, pageSize);
        if (_listFocused)
            _rowIndex = next;
        else if (next != current)
            SelectPage(next);
    }

    private static int Move(int index, int last, ConsoleKey key, int stride, int pageSize) => key switch
    {
        ConsoleKey.Home => 0,
        ConsoleKey.End => last,
        ConsoleKey.UpArrow when index >= stride => index - stride,
        ConsoleKey.DownArrow when index / stride < last / stride => Math.Min(last, index + stride),
        ConsoleKey.LeftArrow when index % stride > 0 => index - 1,
        ConsoleKey.RightArrow when index % stride < stride - 1 => Math.Min(last, index + 1),
        ConsoleKey.PageUp => Math.Max(0, index - pageSize),
        ConsoleKey.PageDown => Math.Min(last, index + pageSize),
        _ => index
    };

    private void ReloadNavigation()
    {
        var previous = _navigation.Count > 0 ? ActivePage : null;
        _navigation.Clear();
        _navigation.AddRange([
            new(Page.All, "全部工具"), new(Page.Favorites, "我的收藏"),
            new(Page.Search, "搜索工具"), new(Page.Hardware, "硬件信息")
        ]);
        RunAction(() => _navigation.AddRange(ToolCatalog.GetCategories().Select(category => new NavigationItem(Page.Category, category))));
        _navIndex = Math.Max(0, _navigation.IndexOf(previous));
    }

    private void SelectPage(int index)
    {
        _navIndex = index;
        _rowIndex = _rowOffset = 0;
        _details = false;
        LoadPage();
    }

    private void LoadPage()
    {
        _tools = [];
        RunAction(() =>
        {
            _tools = ActivePage.Page switch
            {
                Page.All => ToolCatalog.GetAllToolsDeduped(),
                Page.Favorites => ToolCatalog.GetAllToolsDeduped().Where(tool => FavoritesService.IsFavorite(tool.Path)).ToArray(),
                Page.Search => ToolCatalog.Search(_query),
                Page.Category => GetCategoryTools(ActivePage.Title),
                _ => []
            };
            if (IsHardware && !_hardwareLoaded)
                ReadHardware(refresh: false);
        });
        _rowIndex = Math.Clamp(_rowIndex, 0, Math.Max(0, RowCount - 1));
    }

    private IReadOnlyList<ToolItem> GetCategoryTools(string category)
    {
        if (!_categoryTools.TryGetValue(category, out var tools))
            _categoryTools[category] = tools = ToolCatalog.GetTools(category);
        return tools;
    }

    private void BeginSearch()
    {
        _previousNavIndex = _navIndex;
        _previousQuery = _query;
        _editingSearch = true;
        SetFocus(true);
        SelectPage(_navigation.FindIndex(item => item.Page == Page.Search));
    }

    private void EditSearch(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                _editingSearch = false;
                return;
            case ConsoleKey.Escape:
                _editingSearch = false;
                _query = _previousQuery;
                SetFocus(false);
                SelectPage(_previousNavIndex);
                return;
            case ConsoleKey.Backspace when _query.Length > 0:
                var count = _query.Length >= 2 && char.IsSurrogatePair(_query[^2], _query[^1]) ? 2 : 1;
                _query = _query[..^count];
                break;
            default:
                if (char.IsControl(key.KeyChar) || _query.Length >= 160)
                    return;
                _query += key.KeyChar;
                break;
        }

        _rowIndex = _rowOffset = 0;
        LoadPage();
    }

    private void Refresh()
    {
        if (IsHardware)
        {
            ReadHardware(refresh: true);
            return;
        }

        ToolMetadataService.InvalidateCache();
        ToolCatalog.InvalidateCache();
        FavoritesService.InvalidateCache();
        ToolIconService.InvalidateCache();
        _categoryTools.Clear();
        _details = false;
        _status = "已刷新工具库";
        ReloadNavigation();
        LoadPage();
    }

    private void ReadHardware(bool refresh)
    {
        _hardwareRequest ??= Task.Run(() => HardwareInfoService.LoadAsync(refresh));
    }

    private bool CompleteHardwareRequest()
    {
        if (_hardwareRequest is not { IsCompleted: true } request)
            return false;

        _hardwareRequest = null;
        if (request.IsCompletedSuccessfully)
        {
            _hardware = request.Result.SelectMany(section => section.Items.Select(item => new HardwareRow(section, item))).ToArray();
            _hardwareLoaded = true;
            if (IsHardware)
                _rowIndex = Math.Clamp(_rowIndex, 0, Math.Max(0, _hardware.Count - 1));
        }
        else
        {
            var error = request.Exception?.GetBaseException().Message;
            if (IsHardware)
                _status = $"读取硬件失败 · {error}";
        }
        return IsHardware;
    }

    private void Launch(ToolItem tool)
    {
        if (!File.Exists(tool.Path))
        {
            _status = "启动文件缺失。可在图形版安装后按 F5 刷新。";
            return;
        }

        var (exe, workingDirectory) = FurMarkLaunchPath.Prepare(tool.Path, tool.EffectiveWorkingDir);
        Start(exe, workingDirectory);
        _status = $"已启动 {tool.Name}";
    }

    private static void Start(string path, string workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = workingDirectory
        });
    }

    private void CopyText(string text) =>
        _status = WindowsClipboard.SetText(text) ? "已复制到剪贴板" : "剪贴板暂不可用";

    private void RunAction(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or JsonException)
        {
            _status = $"操作失败 · {ex.Message}";
        }
    }
}
