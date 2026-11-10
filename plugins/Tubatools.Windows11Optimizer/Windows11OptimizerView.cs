using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.PluginSdk;

namespace Tubatools.Windows11Optimizer;

internal sealed class Windows11OptimizerView : Grid
{
    private readonly PluginExecutionContext _host;
    private readonly Palette _palette = new();
    private readonly TextBox _search = new() { PlaceholderText = "搜索当前页设置", MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly SelectorBar _tabs = new() { Margin = new Thickness(0, 4, 0, 8) };
    private readonly ScrollViewer _scroll = new();
    private readonly StackPanel _surface = new() { Spacing = 16, Padding = new Thickness(0, 4, 8, 24), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly InfoBar _info = new() { IsOpen = false, IsClosable = true, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _recommend;
    private readonly Button _restoreAll;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<CheckBox> _appxChecks = [];
    private readonly Dictionary<string, TextBlock> _appxStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToggleSwitch _removeProvisioned = new()
    {
        Header = "同时移除系统预置副本",
        OffContent = "",
        OnContent = "",
        IsOn = true,
        Margin = new Thickness(0, 8, 0, 0)
    };
    private readonly TextBlock _appxLog = new() { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontSize = 12, Visibility = Visibility.Collapsed };
    private readonly TextBlock _backupInfo = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _wingetState = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };

    private CancellationTokenSource? _appsCts;
    private OptimizerTab _tab = OptimizerTab.Overview;
    private bool _suppress;
    private bool _disposed;

    private Windows11OptimizerView(PluginExecutionContext host)
    {
        _host = host;
        _recommend = AccentButton("应用推荐优化");
        _restoreAll = Btn("恢复全部默认");
        _palette.Apply(IsDark);
        BuildFrame();
        Rebuild();
        ActualThemeChanged += (_, _) =>
        {
            _palette.Apply(IsDark);
            Rebuild();
        };
    }

    public static FrameworkElement Create(PluginExecutionContext host) => new Windows11OptimizerView(host);

    public void Attach(PluginViewContext view) => view.OnClosed(Dispose);

    private bool IsDark => ActualTheme == ElementTheme.Dark ||
        (ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

    private void BuildFrame()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var bar = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 2) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search.TextChanged += (_, _) =>
        {
            if (_tab is OptimizerTab.Overview or OptimizerTab.Apps or OptimizerTab.Tools) return;
            Rebuild();
        };
        bar.Children.Add(_search);
        Grid.SetColumn(_recommend, 1);
        bar.Children.Add(_recommend);
        Grid.SetColumn(_restoreAll, 2);
        bar.Children.Add(_restoreAll);
        _recommend.Click += (_, _) => RunGuarded(ApplyRecommendedAsync);
        _restoreAll.Click += (_, _) => RunGuarded(RestoreAllAsync);

        Children.Add(bar);
        Children.Add(_info);
        Grid.SetRow(_info, 1);

        foreach (var tab in Enum.GetValues<OptimizerTab>())
        {
            var item = new SelectorBarItem { Text = TweakCatalog.Title(tab), Tag = tab };
            if (tab == OptimizerTab.Overview) item.IsSelected = true;
            _tabs.Items.Add(item);
        }
        _tabs.SelectionChanged += (_, _) =>
        {
            if (_tabs.SelectedItem?.Tag is OptimizerTab tab)
            {
                _tab = tab;
                Rebuild();
            }
        };
        Children.Add(_tabs);
        Grid.SetRow(_tabs, 2);

        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _scroll.Content = _surface;
        _scroll.Margin = new Thickness(0, 4, 0, 0);
        Children.Add(_scroll);
        Grid.SetRow(_scroll, 3);
    }

    private void Rebuild()
    {
        _appsCts?.Cancel();
        _appsCts?.Dispose();
        _appsCts = null;
        _appxChecks.Clear();
        _appxStatus.Clear();
        _surface.Children.Clear();

        switch (_tab)
        {
            case OptimizerTab.Overview:
                BuildOverview();
                break;
            case OptimizerTab.Apps:
                BuildApps();
                break;
            case OptimizerTab.Tools:
                BuildTools();
                break;
            default:
                BuildTweakTab(_tab);
                break;
        }
        _scroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    // ── 概览 ─────────────────────────────────────────────────────────────────
    private void BuildOverview()
    {
        var version = Environment.OSVersion.Version;
        var win11 = version.Build >= 22000;
        var admin = TweakEngine.IsAdministrator;

        var statusCard = Card();
        var statusStack = new StackPanel { Spacing = 6 };
        statusStack.Children.Add(Text("系统状态", 18, semiBold: true));
        statusStack.Children.Add(Text(
            $"系统版本：Windows {(win11 ? "11" : "10")}（内部版本 {version.Build}） · " +
            (admin ? "管理员权限正常，系统级设置可以写入。" : "当前不是管理员，系统级设置会失败；请以管理员身份启动工具箱。"),
            13, secondary: true, wrap: true));
        statusStack.Children.Add(Text(
            "所有开关点击后立即写入注册表，关闭开关即可恢复 Windows 默认值；批量操作前会自动备份受管注册表项。",
            13, secondary: true, wrap: true));
        statusCard.Child = statusStack;
        _surface.Children.Add(statusCard);

        var quickCard = Card();
        var quickStack = new StackPanel { Spacing = 0 };
        quickStack.Children.Add(Text("常用快捷操作", 15, semiBold: true));
        quickStack.Children.Add(Text("先处理最常用的两项：快捷方式箭头和 UAC 盾牌。", 12, secondary: true, wrap: true));
        foreach (var id in new[] { "shortcut-arrow", "shortcut-shield" })
        {
            var def = TweakCatalog.Tweaks.FirstOrDefault(t => t.Id == id);
            if (def is null) continue;
            quickStack.Children.Add(Divider());
            quickStack.Children.Add(BuildTweakRow(def));
        }
        quickStack.Children.Add(Divider());
        var quickButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var restart = Btn("重启资源管理器");
        restart.Click += (_, _) => RunGuarded(async () => await RunActionAsync(TweakCatalog.Actions.First(a => a.Id == "restart-explorer")));
        var clearCache = Btn("清理图标缓存");
        clearCache.Click += (_, _) => RunGuarded(async () => await RunActionAsync(TweakCatalog.Actions.First(a => a.Id == "clear-icon-cache")));
        quickButtons.Children.Add(restart);
        quickButtons.Children.Add(clearCache);
        quickStack.Children.Add(quickButtons);
        quickCard.Child = quickStack;
        _surface.Children.Add(quickCard);

        var summaryCard = Card();
        var summaryStack = new StackPanel { Spacing = 6 };
        summaryStack.Children.Add(Text("优化概览", 15, semiBold: true));
        foreach (var tab in new[] { OptimizerTab.Explorer, OptimizerTab.Privacy, OptimizerTab.Performance, OptimizerTab.Update, OptimizerTab.Power, OptimizerTab.Notifications })
        {
            var definitions = TweakCatalog.Tweaks.Where(t => t.Tab == tab && t.Kind == TweakKind.Toggle).ToList();
            var enabled = definitions.Count(d => TweakEngine.ReadToggle(d) == true);
            var row = new Grid { Padding = new Thickness(0, 6, 0, 6), ColumnSpacing = 16 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Text(TweakCatalog.Title(tab), 13));
            var value = Text($"{enabled} / {definitions.Count} 已启用", 13, secondary: true);
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            summaryStack.Children.Add(Divider());
            summaryStack.Children.Add(row);
        }
        summaryCard.Child = summaryStack;
        _surface.Children.Add(summaryCard);

        var helpCard = Card();
        var helpStack = new StackPanel { Spacing = 5 };
        helpStack.Children.Add(Text("使用提示", 15, semiBold: true));
        foreach (var line in new[]
        {
            "• 标记「重启资源管理器后生效」的项目，可在本页或高级工具里一键重启 explorer。",
            "• 「应用推荐优化」只开启经过筛选的隐私、性能和外观项目，并在修改前自动备份。",
            "• 「恢复全部默认」会关闭所有普通开关，并把电源计划恢复为平衡；休眠等无法推断默认值的项目会跳过。",
            "• 预装应用卸载与 winget 安装会调用系统 PowerShell / 应用安装程序，可能需要较长时间。",
            "• 本页功能参考 Winhance / UnattendedWinstall 公开的注册表项独立实现，不包含其源代码。"
        })
        {
            helpStack.Children.Add(Text(line, 12, secondary: true, wrap: true));
        }
        helpCard.Child = helpStack;
        _surface.Children.Add(helpCard);
    }

    // ── 注册表开关页 ─────────────────────────────────────────────────────────
    private void BuildTweakTab(OptimizerTab tab)
    {
        var query = _search.Text.Trim();
        var definitions = TweakCatalog.Tweaks
            .Where(t => t.Tab == tab)
            .Where(t => query.Length == 0 ||
                        t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        t.Group.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (definitions.Count == 0)
        {
            _surface.Children.Add(Text(query.Length == 0 ? "这个分区暂时没有可用项目。" : $"没有找到与“{query}”匹配的设置。", 13, secondary: true, wrap: true));
            return;
        }

        foreach (var group in definitions.GroupBy(t => t.Group))
            _surface.Children.Add(BuildTweakCard(group.Key, group.ToList()));
    }

    private Border BuildTweakCard(string group, IReadOnlyList<TweakDefinition> definitions)
    {
        var card = Card();
        var stack = new StackPanel { Spacing = 0 };
        stack.Children.Add(Text(group, 15, semiBold: true));
        for (var i = 0; i < definitions.Count; i++)
        {
            if (i > 0) stack.Children.Add(Divider());
            stack.Children.Add(BuildTweakRow(definitions[i]));
        }
        card.Child = stack;
        return card;
    }

    private Grid BuildTweakRow(TweakDefinition definition)
    {
        var row = new Grid { ColumnSpacing = 24, Padding = new Thickness(0, 8, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock { Text = definition.Title, FontSize = 14, Foreground = _palette.Text, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            info.Children.Add(new TextBlock
            {
                Text = definition.Description,
                FontSize = 12,
                Foreground = _palette.Secondary,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 760
            });
        }
        var notes = new List<string>();
        if (definition.NeedsExplorerRestart) notes.Add("重启资源管理器后生效");
        if (definition.NeedsReboot) notes.Add("需要重启系统");
        if (definition.Risky) notes.Add("谨慎操作");
        if (notes.Count > 0)
        {
            info.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", notes),
                FontSize = 11,
                Foreground = definition.Risky ? _palette.Danger : _palette.Accent,
                TextWrapping = TextWrapping.Wrap
            });
        }
        row.Children.Add(info);

        FrameworkElement control = definition.Kind == TweakKind.Toggle ? BuildToggle(definition) : BuildChoice(definition);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private ToggleSwitch BuildToggle(TweakDefinition definition)
    {
        var state = TweakEngine.ReadToggle(definition);
        var toggle = new ToggleSwitch
        {
            IsOn = state ?? false,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
            IsEnabled = state is not null,
            Tag = definition
        };
        AutomationProperties.SetName(toggle, definition.Title);
        if (state is null)
        {
            ToolTipService.SetToolTip(toggle, "无法读取当前状态（可能需要管理员权限）");
        }
        else
        {
            toggle.Toggled += async (_, _) => await ToggleChangedAsync(definition, toggle);
        }
        return toggle;
    }

    private ComboBox BuildChoice(TweakDefinition definition)
    {
        var combo = new ComboBox
        {
            MinWidth = 220,
            PlaceholderText = "系统默认（未修改）",
            Tag = definition,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var choice in definition.Choices)
            combo.Items.Add(new ComboBoxItem { Content = choice.Label });

        var applied = TweakEngine.ReadChoiceIndex(definition);
        combo.SelectedIndex = applied;
        AutomationProperties.SetName(combo, definition.Title);
        combo.SelectionChanged += async (_, _) =>
        {
            if (_suppress || combo.SelectedIndex < 0) return;
            var selected = combo.SelectedIndex;
            var previous = applied;
            try
            {
                if (definition.Risky &&
                    !await _host.ConfirmAsync("应用设置", $"{definition.Title}\n\n{definition.Description}\n\n修改后可能需要重启才能完全生效。是否继续？"))
                {
                    SetCombo(combo, previous);
                    return;
                }
                await RunExclusiveAsync(ct => TweakEngine.ApplyChoiceAsync(definition, selected, ct));
                applied = selected;
                SetStatus($"已应用：{definition.Title} → {definition.Choices[selected].Label}", InfoBarSeverity.Success, "设置已应用");
            }
            catch (Exception ex)
            {
                SetCombo(combo, previous);
                SetStatus($"应用失败：{ex.Message}", InfoBarSeverity.Error, "操作失败");
            }
        };
        return combo;
    }

    private async Task ToggleChangedAsync(TweakDefinition definition, ToggleSwitch toggle)
    {
        if (_suppress) return;
        var desired = toggle.IsOn;
        var previous = !desired;
        try
        {
            if (definition.Risky && desired &&
                !await _host.ConfirmAsync("应用设置", $"{definition.Title}\n\n{definition.Description}\n\n此操作可能影响系统安全性或使用体验，是否继续？"))
            {
                Revert(toggle, previous);
                return;
            }

            await RunExclusiveAsync(async ct =>
            {
                SetStatus($"正在应用：{definition.Title}…", InfoBarSeverity.Informational, "正在应用");
                _host.ReportProgress($"正在应用：{definition.Title}");
                try
                {
                    await TweakEngine.ApplyToggleAsync(definition, desired, ct);
                }
                finally
                {
                    _host.ReportProgress("");
                }
            });

            SetStatus(
                $"已{(desired ? "启用" : "恢复")}：{definition.Title}{(definition.NeedsExplorerRestart ? "，重启资源管理器后生效" : "")}",
                InfoBarSeverity.Success, "设置已应用");
        }
        catch (Exception ex)
        {
            Revert(toggle, previous);
            SetStatus($"应用失败：{ex.Message}", InfoBarSeverity.Error, "操作失败");
        }
    }

    private async Task ApplyRecommendedAsync()
    {
        var definitions = TweakCatalog.Tweaks.Where(t => t.Recommended && t.Kind == TweakKind.Toggle).ToList();
        if (definitions.Count == 0) return;
        if (!await _host.ConfirmAsync("应用推荐优化",
                $"将应用 {definitions.Count} 项推荐设置（去箭头/盾牌、隐私遥测、建议广告、后台应用等）。\n\n修改前会自动创建注册表备份，是否继续？"))
            return;

        var applied = 0;
        var failed = 0;
        string? backup = null;
        await RunExclusiveAsync(async ct =>
        {
            backup = await TweakEngine.CreateBackupAsync(_host.Services.DataDirectory, ct);
            for (var i = 0; i < definitions.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var definition = definitions[i];
                try
                {
                    if (TweakEngine.ReadToggle(definition) == true) continue;
                    _host.ReportProgress($"推荐优化 {i + 1}/{definitions.Count}：{definition.Title}");
                    await TweakEngine.ApplyToggleAsync(definition, true, ct);
                    applied++;
                }
                catch
                {
                    failed++;
                }
            }
            _host.ReportProgress("");
        });
        SetStatus($"推荐优化完成：应用 {applied} 项{(failed > 0 ? $"，失败 {failed} 项" : "")}。备份：{Path.GetFileName(backup ?? "")}",
            failed > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, "完成");
        Rebuild();
    }

    private async Task RestoreAllAsync()
    {
        var toggles = TweakCatalog.Tweaks.Where(t => t.Kind == TweakKind.Toggle && t.HasDefault).ToList();
        var choices = TweakCatalog.Tweaks
            .Where(t => t.Kind == TweakKind.Choice && t.Choices.Any(c => c.IsDefault))
            .ToList();
        if (!await _host.ConfirmAsync("恢复全部默认",
                $"将把 {toggles.Count} 个开关和 {choices.Count} 个下拉项恢复为 Windows 默认状态。\n\n电源计划、休眠等无法推断默认值的项目会被跳过；修改前会自动备份。是否继续？"))
            return;

        var restored = 0;
        var failed = 0;
        await RunExclusiveAsync(async ct =>
        {
            await TweakEngine.CreateBackupAsync(_host.Services.DataDirectory, ct);
            foreach (var definition in toggles)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    _host.ReportProgress($"恢复默认：{definition.Title}");
                    await TweakEngine.ApplyToggleAsync(definition, definition.DefaultOn, ct);
                    restored++;
                }
                catch
                {
                    failed++;
                }
            }
            foreach (var definition in choices)
            {
                ct.ThrowIfCancellationRequested();
                var index = -1;
                for (var i = 0; i < definition.Choices.Count; i++)
                {
                    if (definition.Choices[i].IsDefault) { index = i; break; }
                }
                if (index < 0) continue;
                try
                {
                    await TweakEngine.ApplyChoiceAsync(definition, index, ct);
                    restored++;
                }
                catch
                {
                    failed++;
                }
            }
            _host.ReportProgress("");
        });
        SetStatus($"已恢复 {restored} 项默认值{(failed > 0 ? $"，失败 {failed} 项" : "")}。", failed > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, "恢复完成");
        Rebuild();
    }

    // ── 应用管理 ─────────────────────────────────────────────────────────────
    private void BuildApps()
    {
        var appxCard = Card();
        var appxStack = new StackPanel { Spacing = 6 };
        appxStack.Children.Add(Text("Windows 预装应用清理", 15, semiBold: true));
        appxStack.Children.Add(Text(
            "勾选不需要的商店应用后卸载。桌面版内置应用（计算器、记事本、终端等）没有列出。",
            12, secondary: true, wrap: true));
        appxStack.Children.Add(_removeProvisioned);

        var list = new StackPanel { Spacing = 2, Margin = new Thickness(0, 8, 0, 8) };
        foreach (var app in TweakCatalog.AppxApps)
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(4, 4, 4, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var content = new StackPanel { Spacing = 1 };
            content.Children.Add(new TextBlock { Text = app.Title, FontSize = 13, Foreground = _palette.Text });
            content.Children.Add(new TextBlock { Text = app.Description, FontSize = 11, Foreground = _palette.Secondary, TextWrapping = TextWrapping.Wrap });
            var check = new CheckBox { Content = content, Tag = app, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
            AutomationProperties.SetName(check, app.Title);
            _appxChecks.Add(check);
            row.Children.Add(check);

            var status = new TextBlock { Text = "检测中…", FontSize = 11, Foreground = _palette.Secondary, VerticalAlignment = VerticalAlignment.Center };
            _appxStatus[app.PackageName] = status;
            Grid.SetColumn(status, 1);
            row.Children.Add(status);
            list.Children.Add(row);
        }

        var listScroll = new ScrollViewer
        {
            Content = list,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        appxStack.Children.Add(listScroll);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var remove = AccentButton("卸载选中应用");
        remove.Click += (_, _) => RunGuarded(RemoveSelectedAppxAsync);
        var clear = Btn("全不选");
        clear.Click += (_, _) =>
        {
            foreach (var check in _appxChecks) check.IsChecked = false;
        };
        buttons.Children.Add(remove);
        buttons.Children.Add(clear);
        appxStack.Children.Add(buttons);

        _appxLog.Visibility = Visibility.Collapsed;
        _appxLog.Text = "";
        var logBorder = new Border
        {
            Background = _palette.Subtle,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 8, 0, 0),
            Child = _appxLog
        };
        appxStack.Children.Add(logBorder);
        appxCard.Child = appxStack;
        _surface.Children.Add(appxCard);

        var wingetCard = Card();
        var wingetStack = new StackPanel { Spacing = 6 };
        wingetStack.Children.Add(Text("常用软件快速安装（winget）", 15, semiBold: true));
        wingetStack.Children.Add(Text(
            "调用系统自带的 winget 安装指定软件，安装过程会显示在页面状态和宿主进度条中。",
            12, secondary: true, wrap: true));
        _wingetState.Text = "正在检测 winget…";
        _wingetState.Foreground = _palette.Secondary;
        wingetStack.Children.Add(_wingetState);

        var wingetList = new StackPanel { Spacing = 2, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var app in TweakCatalog.WingetApps)
        {
            var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = app.Title, FontSize = 13, Foreground = _palette.Text });
            info.Children.Add(new TextBlock { Text = app.Description, FontSize = 11, Foreground = _palette.Secondary });
            row.Children.Add(info);

            var status = new TextBlock { Text = "", FontSize = 11, Foreground = _palette.Secondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(status, 1);
            row.Children.Add(status);

            var install = Btn("安装");
            install.Tag = (app, status, install);
            install.Click += (_, _) => RunGuarded(() => InstallWingetAsync(app, install, status));
            Grid.SetColumn(install, 2);
            row.Children.Add(install);
            wingetList.Children.Add(row);
        }
        var wingetScroll = new ScrollViewer
        {
            Content = wingetList,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        wingetStack.Children.Add(wingetScroll);
        wingetCard.Child = wingetStack;
        _surface.Children.Add(wingetCard);

        _appsCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = LoadAppxStateAsync(_appsCts.Token);
        _ = LoadWingetStateAsync(_appsCts.Token);
    }

    private async Task LoadAppxStateAsync(CancellationToken ct)
    {
        try
        {
            var installed = await AppManager.GetInstalledAppxAsync(ct);
            if (ct.IsCancellationRequested) return;
            foreach (var (name, label) in _appxStatus)
            {
                var isInstalled = installed.Contains(name);
                label.Text = isInstalled ? "已安装" : "未安装";
                label.Foreground = isInstalled ? _palette.Text : _palette.Secondary;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            foreach (var label in _appxStatus.Values)
            {
                label.Text = "状态未知";
                label.Foreground = _palette.Secondary;
            }
        }
    }

    private async Task LoadWingetStateAsync(CancellationToken ct)
    {
        try
        {
            var available = await AppManager.HasWingetAsync(ct);
            if (ct.IsCancellationRequested) return;
            _wingetState.Text = available
                ? "winget 可用。安装需要联网，部分软件会弹出 UAC 或安装向导。"
                : "未找到 winget。请先在 Microsoft Store 安装/更新「应用安装程序」。";
            _wingetState.Foreground = available ? _palette.Secondary : _palette.Danger;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _wingetState.Text = "检测 winget 失败：" + ex.Message;
            _wingetState.Foreground = _palette.Danger;
        }
    }

    private async Task RemoveSelectedAppxAsync()
    {
        var selected = _appxChecks
            .Where(c => c.IsChecked == true)
            .Select(c => c.Tag as AppxEntry)
            .Where(e => e is not null)
            .Cast<AppxEntry>()
            .ToList();
        if (selected.Count == 0)
        {
            SetStatus("请先在列表中勾选要卸载的应用。", InfoBarSeverity.Warning, "未选择");
            return;
        }

        var names = string.Join("、", selected.Take(6).Select(e => e.Title)) + (selected.Count > 6 ? " 等" : "");
        if (!await _host.ConfirmAsync("卸载预装应用",
                $"将卸载 {selected.Count} 个应用：{names}。\n\n卸载后可以通过 Microsoft Store 重新安装（部分系统应用无法恢复）。是否继续？"))
            return;

        _appxLog.Visibility = Visibility.Collapsed;
        _appxLog.Text = "";
        var progress = new Progress<string>(text => SetStatus(text, InfoBarSeverity.Informational, "正在卸载"));
        await RunExclusiveAsync(async ct =>
        {
            var result = await AppManager.RemoveAppxAsync(selected, _removeProvisioned.IsOn, progress, ct);
            _appxLog.Text = result;
            _appxLog.Visibility = Visibility.Visible;
            var warning = result.Contains("失败", StringComparison.Ordinal) ||
                          result.StartsWith("没有应用被修改", StringComparison.Ordinal);
            SetStatus(result.Split('\n')[0], warning ? InfoBarSeverity.Warning : InfoBarSeverity.Success, "卸载完成");
        });
        await LoadAppxStateAsync(_lifetime.Token);
    }

    private async Task InstallWingetAsync(WingetApp app, Button button, TextBlock status)
    {
        button.IsEnabled = false;
        status.Text = "安装中…";
        try
        {
            await RunExclusiveAsync(async ct =>
            {
                SetStatus($"正在安装 {app.Title}…", InfoBarSeverity.Informational, "winget");
                var progress = new Progress<string>(text => _host.ReportProgress(text));
                var result = await AppManager.InstallWingetAsync(app, progress, ct);
                _host.ReportProgress("");
                var success = result.Contains("安装完成", StringComparison.Ordinal);
                status.Text = success ? "已安装" : "未完成";
                SetStatus(result, success ? InfoBarSeverity.Success : InfoBarSeverity.Warning, app.Title);
            });
        }
        catch (Exception ex)
        {
            _host.ReportProgress("");
            status.Text = "失败";
            SetStatus($"{app.Title} 安装失败：{ex.Message}", InfoBarSeverity.Error, "winget");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    // ── 高级工具 ─────────────────────────────────────────────────────────────
    private void BuildTools()
    {
        foreach (var group in TweakCatalog.Actions.GroupBy(a => a.Group))
        {
            var card = Card();
            var stack = new StackPanel { Spacing = 0 };
            stack.Children.Add(Text(group.Key, 15, semiBold: true));
            foreach (var action in group)
            {
                stack.Children.Add(Divider());
                var row = new Grid { ColumnSpacing = 24, Padding = new Thickness(0, 8, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(new TextBlock { Text = action.Title, FontSize = 14, Foreground = _palette.Text, TextWrapping = TextWrapping.Wrap });
                if (!string.IsNullOrWhiteSpace(action.Description))
                    info.Children.Add(new TextBlock { Text = action.Description, FontSize = 12, Foreground = _palette.Secondary, TextWrapping = TextWrapping.Wrap, MaxWidth = 760 });
                if (action.Risky)
                    info.Children.Add(new TextBlock { Text = "会重启资源管理器 · 谨慎操作", FontSize = 11, Foreground = _palette.Danger });
                row.Children.Add(info);
                var button = Btn(action.ButtonText);
                button.VerticalAlignment = VerticalAlignment.Center;
                button.Click += (_, _) => RunGuarded(() => RunActionAsync(action));
                Grid.SetColumn(button, 1);
                row.Children.Add(button);
                stack.Children.Add(row);
            }
            card.Child = stack;
            _surface.Children.Add(card);
        }

        var backupCard = Card();
        var backupStack = new StackPanel { Spacing = 6 };
        backupStack.Children.Add(Text("注册表备份", 15, semiBold: true));
        backupStack.Children.Add(Text(
            "备份本插件管理的全部注册表值（包括当前未修改的项目），恢复时会写回备份时的状态。",
            12, secondary: true, wrap: true));
        var latest = TweakEngine.FindLatestBackup(_host.Services.DataDirectory);
        _backupInfo.Text = latest is null ? "还没有备份。" : $"最近备份：{Path.GetFileName(latest)}（{File.GetLastWriteTime(latest):yyyy-MM-dd HH:mm}）";
        _backupInfo.Foreground = _palette.Secondary;
        backupStack.Children.Add(_backupInfo);

        var backupButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var create = AccentButton("创建备份");
        create.Click += (_, _) => RunGuarded(async () =>
        {
            await RunExclusiveAsync(async ct =>
            {
                var path = await TweakEngine.CreateBackupAsync(_host.Services.DataDirectory, ct);
                _backupInfo.Text = $"最近备份：{Path.GetFileName(path)}（{DateTime.Now:yyyy-MM-dd HH:mm}）";
                SetStatus($"注册表备份已保存：{path}", InfoBarSeverity.Success, "备份完成");
            });
        });
        var restore = Btn("恢复最近备份");
        restore.Click += (_, _) => RunGuarded(RestoreLatestBackupAsync);
        var open = Btn("打开备份目录");
        open.Click += (_, _) =>
        {
            var directory = Path.Combine(_host.Services.DataDirectory, "backups");
            Directory.CreateDirectory(directory);
            ShellRunner.StartDetached("explorer.exe", $"\"{directory}\"");
        };
        backupButtons.Children.Add(create);
        backupButtons.Children.Add(restore);
        backupButtons.Children.Add(open);
        backupStack.Children.Add(backupButtons);
        backupCard.Child = backupStack;
        _surface.Children.Add(backupCard);
    }

    private async Task RestoreLatestBackupAsync()
    {
        var latest = TweakEngine.FindLatestBackup(_host.Services.DataDirectory);
        if (latest is null)
        {
            SetStatus("还没有可恢复的备份。请先创建一个备份。", InfoBarSeverity.Warning, "没有备份");
            return;
        }
        if (!await _host.ConfirmAsync("恢复注册表备份",
                $"将把 {Path.GetFileName(latest)} 中记录的注册表值写回系统。\n\n当前对这些项目的修改会被覆盖。是否继续？"))
            return;

        await RunExclusiveAsync(async ct =>
        {
            var (restored, skipped) = await TweakEngine.RestoreBackupAsync(latest, ct);
            SetStatus($"已恢复 {restored} 个注册表值{(skipped > 0 ? $"，跳过 {skipped} 个" : "")}。部分项目需要重启资源管理器或系统。",
                skipped > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, "恢复完成");
        });
        Rebuild();
    }

    private async Task RunActionAsync(TweakAction action)
    {
        if (action.Risky &&
            !await _host.ConfirmAsync(action.Title, $"{action.Description}\n\n此操作会结束并重新启动 explorer.exe，未保存的窗口状态可能丢失。是否继续？"))
            return;

        await RunExclusiveAsync(async ct =>
        {
            SetStatus($"正在执行：{action.Title}…", InfoBarSeverity.Informational, "正在执行");
            _host.ReportProgress(action.Title);
            try
            {
                var message = await action.Run(ct);
                SetStatus(message, InfoBarSeverity.Success, action.Title);
            }
            finally
            {
                _host.ReportProgress("");
            }
        });
        Rebuild();
    }

    // ── 基础控件 ─────────────────────────────────────────────────────────────
    private void SetStatus(string message, InfoBarSeverity severity, string title)
    {
        _info.Title = title;
        _info.Message = message;
        _info.Severity = severity;
        _info.IsOpen = true;
    }

    private async Task RunExclusiveAsync(Func<CancellationToken, Task> action)
    {
        if (_disposed) return;
        try
        {
            await _gate.WaitAsync(_lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            await action(_lifetime.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async void RunGuarded(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _host.ReportProgress("");
            SetStatus($"操作失败：{ex.Message}", InfoBarSeverity.Error, "操作失败");
        }
    }

    private void Revert(ToggleSwitch toggle, bool value)
    {
        _suppress = true;
        try
        {
            toggle.IsOn = value;
        }
        finally
        {
            _suppress = false;
        }
    }

    private void SetCombo(ComboBox combo, int index)
    {
        _suppress = true;
        try
        {
            combo.SelectedIndex = index;
        }
        finally
        {
            _suppress = false;
        }
    }

    private Border Card() => new()
    {
        Padding = new Thickness(20, 16, 20, 16),
        CornerRadius = new CornerRadius(8),
        Background = _palette.Card,
        BorderBrush = _palette.CardStroke,
        BorderThickness = new Thickness(1)
    };

    private Border Divider() => new()
    {
        Height = 1,
        Background = _palette.CardStroke,
        Margin = new Thickness(0, 2, 0, 2)
    };

    private TextBlock Text(string text, double size, bool secondary = false, bool semiBold = false, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = semiBold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = secondary ? _palette.Secondary : _palette.Text,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        IsTextSelectionEnabled = true
    };

    private Button Btn(string text) => new() { Content = text };

    private Button AccentButton(string text)
    {
        var button = new Button { Content = text };
        try
        {
            if ((Application.Current.Resources["AccentButtonStyle"] as Style) is { } style) button.Style = style;
        }
        catch
        {
            // The fallback accent colors below are enough when the host resource is unavailable.
        }
        if (button.Style is null)
        {
            button.Background = _palette.Accent;
            button.Foreground = _palette.OnAccent;
        }
        return button;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _lifetime.Cancel(); } catch { }
        try { _appsCts?.Cancel(); } catch { }
        _appsCts?.Dispose();
    }

    private sealed class Palette
    {
        public SolidColorBrush Text { get; } = new();
        public SolidColorBrush Secondary { get; } = new();
        public SolidColorBrush Card { get; } = new();
        public SolidColorBrush CardStroke { get; } = new();
        public SolidColorBrush Subtle { get; } = new();
        public SolidColorBrush Accent { get; } = new();
        public SolidColorBrush OnAccent { get; } = new();
        public SolidColorBrush Danger { get; } = new();

        public void Apply(bool dark)
        {
            Text.Color = dark ? Color(0xFF, 0xFF, 0xFF, 0xFF) : Color(0xFF, 0x1B, 0x1B, 0x1B);
            Secondary.Color = dark ? Color(0xFF, 0xC5, 0xC5, 0xC5) : Color(0xFF, 0x5D, 0x5D, 0x5D);
            Card.Color = dark ? Color(0x1A, 0x2B, 0x2B, 0x2B) : Color(0xD9, 0xFF, 0xFF, 0xFF);
            CardStroke.Color = dark ? Color(0x24, 0xFF, 0xFF, 0xFF) : Color(0x14, 0x00, 0x00, 0x00);
            Subtle.Color = dark ? Color(0x1A, 0xFF, 0xFF, 0xFF) : Color(0x0A, 0x00, 0x00, 0x00);
            Accent.Color = dark ? Color(0xFF, 0x60, 0xCD, 0xFF) : Color(0xFF, 0x00, 0x5F, 0xB8);
            OnAccent.Color = dark ? Color(0xFF, 0x00, 0x1A, 0x26) : Color(0xFF, 0xFF, 0xFF, 0xFF);
            Danger.Color = dark ? Color(0xFF, 0xFF, 0x99, 0xA4) : Color(0xFF, 0xC4, 0x2B, 0x1C);
        }

        private static Windows.UI.Color Color(byte a, byte r, byte g, byte b) => ColorHelper.FromArgb(a, r, g, b);
    }
}
