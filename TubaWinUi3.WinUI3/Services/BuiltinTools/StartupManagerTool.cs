using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Markup;
using Microsoft.Win32;
using TubaWinUi3.Pages;
using Windows.UI;

namespace TubaWinUi3.Services;

/// <summary>
/// 启动项管理：封装 Sysinternals Autoruns 命令行版（autorunsc.exe）。
/// 随包的 Tools/其他工具/Autoruns/ 已携带命令行版与图形版；
/// 缺失时（精简版/绿色版）自动从微软官方 live.sysinternals.com 下载命令行版。
/// 界面仿原版 Autoruns：顶部工具栏 + 类别标签页 + 带应用图标的两行列表 + 底部状态栏。
/// 两阶段扫描：先扫登录项/启动执行/Winlogon（秒级完成、立刻展示），
/// 随后后台自动补扫全部 17 类（服务、计划任务、驱动等），完成后整体替换并合并进列表。
/// </summary>
public sealed class StartupManagerTool : IBuiltinTool
{
    static StartupManagerTool()
    {
        // 让旧版 autorunsc 的本地 ANSI 输出（如 GBK 中文）也能正确解码
        try { Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); }
        catch { /* 低版本运行时未内置该提供程序时忽略，走 UTF-8 兜底 */ }
    }

    public string Id => "startup-manager";
    public string Name => LocalizationService.L("Builtin_startup-manager_Name", "启动项管理");
    public string Description => LocalizationService.L("Builtin_startup-manager_Desc", "扫描开机自启动项目（注册表 Run、启动文件夹、计划任务、服务等），隐藏微软条目，快速定位异常自启动。");
    public string Glyph => "\uE823";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    private const string AutorunsDirName = "Autoruns";
    private const string LiveSysinternalsBase = "https://live.sysinternals.com/";
    private const string OfficialZipPage = "https://download.sysinternals.com/files/Autoruns.zip";
    private const string TutorialUrl = "https://tubawinui3.cn/tutorials/autoruns";

    private CancellationTokenSource? _cts;
    private bool _scanning;
    private bool _suppressFilter;
    private string? _selectedKey;
    private List<AutorunsEntry> _allEntries = [];
    private List<DisabledRecord> _disabledRecords = [];
    private readonly Dictionary<string, StartupItemVm> _vmCache = new(StringComparer.Ordinal);
    private string _rawCsv = "";
    private StartupManagerState? _state;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        _cts = new CancellationTokenSource();
        _scanning = false;
        try { _disabledRecords = StartupDisabledStore.Load(); }
        catch { _disabledRecords = []; }

        var rootGrid = new Grid();
        // 列表必须在受限高度里才能滚动 + 虚拟化：宿主唯一一行按星号占满页面高度
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = BuildToolbar();
        var tabs = BuildTabBar();
        var listHost = BuildListHost();
        var statusBar = BuildStatusBar();

        // 整页浅灰卡片：工具栏/标签/列表/状态栏都落在卡片背景上，不再透明
        var host = new Border
        {
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(24, 12, 24, 16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            MaxWidth = 1180
        };
        var inner = new Grid { RowSpacing = 8 };
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        inner.Children.Add(toolbar); Grid.SetRow(toolbar, 0);
        inner.Children.Add(tabs); Grid.SetRow(tabs, 1);
        inner.Children.Add(listHost); Grid.SetRow(listHost, 2);
        inner.Children.Add(statusBar); Grid.SetRow(statusBar, 3);
        host.Child = inner;
        rootGrid.Children.Add(host);

        App.MainWindow?.NavigateToToolPage(typeof(ToolContentPage), new ToolContentPageParam
        {
            Title = "启动项管理",
            Description = "扫描开机自启动项目（登记于注册表、启动文件夹、计划任务等），隐藏微软条目，快速定位异常启动项（基于 Sysinternals Autoruns）",
            Glyph = Glyph,
            Content = rootGrid,
            OnClose = () => _cts?.Cancel()
        });

        var xamlRoot = rootGrid.XamlRoot;
        _ = RunScanAsync(context, xamlRoot ?? context.XamlRoot);
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // UI 构建（仿原版 Autoruns 布局）
    // ------------------------------------------------------------------

    private FrameworkElement BuildToolbar()
    {
        var state = new StartupManagerState();
        _state = state;

        // 工具栏：图标按钮组 + 隐藏微软开关 + 搜索框 + 提示行
        var refreshBtn = MakeIconButton("\uE72C", "重新扫描（先显示登录启动项，随后后台补扫全部类别）");
        refreshBtn.Click += (_, _) => _ = RunScanAsync(MakeContext(), GetXamlRoot());
        state.RefreshBtn = refreshBtn;

        var deleteBtn = MakeIconButton("\uE74D", "删除选中项：注册表项会先备份（可恢复）；文件移入回收站；服务/计划任务不可恢复");
        deleteBtn.Click += (_, _) => _ = DeleteSelectedAsync(state.List.SelectedItem as StartupItemVm);
        deleteBtn.IsEnabled = false;
        state.DeleteBtn = deleteBtn;

        var exportBtn = MakeIconButton("\uE74E", "导出本次扫描结果为 CSV 文件");
        exportBtn.Click += (_, _) => ExportCsv();
        state.ExportBtn = exportBtn;

        var guiBtn = MakeIconButton("\uE90A", "打开图形版 Autoruns（可勾选/取消勾选以禁用自启动项）");
        guiBtn.Click += (_, _) => LaunchGuiAutoruns();
        state.GuiBtn = guiBtn;

        var tutorialBtn = MakeIconButton("\uE8F1", "查看使用教程");
        tutorialBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(TutorialUrl) { UseShellExecute = true }); }
            catch { }
        };

        state.HideMsToggle = new ToggleSwitch
        {
            Header = "隐藏微软条目",
            IsOn = true,
            MinWidth = 150,
            VerticalAlignment = VerticalAlignment.Center
        };
        state.HideMsToggle.Toggled += (_, _) =>
        {
            if (!_scanning)
                _ = RunScanAsync(MakeContext(), GetXamlRoot());
        };

        state.SearchBox = new TextBox
        {
            PlaceholderText = "搜索名称 / 路径 / 描述…",
            MinWidth = 240,
            VerticalAlignment = VerticalAlignment.Center
        };
        state.SearchBox.TextChanged += (_, _) => ApplyFilter();

        var buttonRow = new Grid { ColumnSpacing = 4 };
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonRow.Children.Add(refreshBtn); Grid.SetColumn(refreshBtn, 0);
        buttonRow.Children.Add(deleteBtn); Grid.SetColumn(deleteBtn, 1);
        buttonRow.Children.Add(exportBtn); Grid.SetColumn(exportBtn, 2);
        buttonRow.Children.Add(guiBtn); Grid.SetColumn(guiBtn, 3);
        buttonRow.Children.Add(tutorialBtn); Grid.SetColumn(tutorialBtn, 4);
        buttonRow.Children.Add(state.HideMsToggle); Grid.SetColumn(state.HideMsToggle, 5);
        buttonRow.Children.Add(state.SearchBox); Grid.SetColumn(state.SearchBox, 6);

        var tipText = new TextBlock
        {
            Text = "勾选 = 启用，取消勾选 = 禁用（直接修改注册表/服务/计划任务，可随时恢复）。",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };
        var tipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        tipRow.Children.Add(tipText);

        var root = new StackPanel { Spacing = 8 };
        // 顶部无进度进度条：扫描（含后台补扫）期间显示
        state.TopBar = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 3,
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(state.TopBar);
        root.Children.Add(buttonRow);
        root.Children.Add(tipRow);

        return new Border
        {
            Background = new SolidColorBrush(ThemeColors.HeaderBg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Child = root
        };
    }

    private FrameworkElement BuildTabBar()
    {
        if (_state is null) throw new InvalidOperationException("state 未初始化");
        // 纯原生 TabView，不做任何自定义样式覆盖
        var tabs = new TabView
        {
            IsAddTabButtonVisible = false
        };
        tabs.SelectionChanged += (_, _) =>
        {
            // 重建标签期间 SelectionChanged 会连发，避免每次都全量重建列表
            if (!_suppressFilter) ApplyFilter();
        };
        _state.Tabs = tabs;
        return tabs;
    }

    private FrameworkElement BuildListHost()
    {
        if (_state is null) throw new InvalidOperationException("state 未初始化");
        var state = _state;

        state.List = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = false,
            ItemTemplate = CreateItemTemplate(),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 2, 0, 2),
            MinHeight = 300
        };
        state.List.SelectionChanged += (_, _) =>
        {
            _selectedKey = (state.List.SelectedItem as StartupItemVm)?.Key;
            // 删除按钮仅对“可操作类型”的选中项可用
            if (state.DeleteBtn is not null)
                state.DeleteBtn.IsEnabled = state.List.SelectedItem is StartupItemVm vm && vm.CanOperate;
        };
        state.List.DoubleTapped += (_, _) => ShowDetailDialog();
        return state.List;
    }

    private FrameworkElement BuildStatusBar()
    {
        if (_state is null) throw new InvalidOperationException("state 未初始化");
        var state = _state;
        var make = () => new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        state.TotalText = make();
        state.TotalText.Foreground = new SolidColorBrush(ThemeColors.SecondaryText);
        state.EnabledText = make();
        state.EnabledText.Foreground = new SolidColorBrush(ThemeColors.AccentGreen);
        state.RiskText = make();
        state.RiskText.Foreground = new SolidColorBrush(ThemeColors.AccentRed);
        state.ShownText = make();
        state.ShownText.Foreground = new SolidColorBrush(ThemeColors.DimText);

        var doubleClickHint = make();
        doubleClickHint.Text = "双击条目查看详细信息";
        doubleClickHint.Foreground = new SolidColorBrush(ThemeColors.DimText);

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        left.Children.Add(state.TotalText);
        left.Children.Add(state.EnabledText);
        left.Children.Add(state.RiskText);
        left.Children.Add(state.ShownText);
        left.Children.Add(doubleClickHint);

        state.BusyRing = new ProgressRing { Width = 14, Height = 14, IsActive = false, VerticalAlignment = VerticalAlignment.Center };
        state.StatusText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 460,
            VerticalAlignment = VerticalAlignment.Center
        };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        right.Children.Add(state.StatusText);
        right.Children.Add(state.BusyRing);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        grid.Children.Add(right); Grid.SetColumn(right, 1);

        return new Border
        {
            BorderBrush = new SolidColorBrush(ThemeColors.Separator),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(2, 8, 2, 0),
            Child = grid
        };
    }

    private static Button MakeIconButton(string glyph, string tooltip)
    {
        var btn = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 15 },
            MinWidth = 40,
            MinHeight = 34,
            Padding = new Thickness(8, 4, 8, 4)
        };
        ToolTipService.SetToolTip(btn, tooltip);
        return btn;
    }

    /// <summary>Autoruns 风格行模板：启用勾选框 + 应用图标 + 名称/路径两行 + 描述 + 发布者 + 状态点。</summary>
    private static DataTemplate CreateItemTemplate()
    {
        return (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <Grid Padding="10,7" ColumnSpacing="12" Opacity="{Binding RowOpacity}">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="0.85*"/>
                        <ColumnDefinition Width="0.7*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <CheckBox IsChecked="{Binding EnableChecked, Mode=TwoWay}"
                              IsEnabled="{Binding CanOperate}"
                              ToolTipService.ToolTip="{Binding CheckTip}"
                              VerticalAlignment="Center"/>
                    <Grid Grid.Column="1" VerticalAlignment="Center">
                        <Image Source="{Binding Icon}" Width="30" Height="30"
                               Visibility="{Binding IconVisibility}" Stretch="Uniform"/>
                        <Border Width="30" Height="30" CornerRadius="6"
                                Background="{ThemeResource SubtleFillColorSecondaryBrush}"
                                Visibility="{Binding FallbackVisibility}">
                            <FontIcon Glyph="&#xE8C8;" FontSize="15"
                                      Foreground="{ThemeResource TextFillColorTertiaryBrush}"/>
                        </Border>
                    </Grid>
                    <StackPanel Grid.Column="2" VerticalAlignment="Center" Spacing="2">
                        <TextBlock Text="{Binding Entry}" FontSize="13.5" FontWeight="SemiBold"
                                   TextDecorations="{Binding NameDecorations}"
                                   MaxLines="1" TextTrimming="CharacterEllipsis"/>
                        <TextBlock Text="{Binding PathText}" FontSize="11.5"
                                   TextDecorations="{Binding NameDecorations}"
                                   Foreground="{ThemeResource TextFillColorTertiaryBrush}"
                                   MaxLines="1" TextTrimming="CharacterEllipsis"/>
                    </StackPanel>
                    <TextBlock Grid.Column="3" VerticalAlignment="Center" FontSize="12.5"
                               Text="{Binding Description}" Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                               MaxLines="1" TextTrimming="CharacterEllipsis"/>
                    <TextBlock Grid.Column="4" VerticalAlignment="Center" FontSize="12"
                               Text="{Binding Publisher}" Foreground="{Binding SigningBrush}"
                               MaxLines="1" TextTrimming="CharacterEllipsis"/>
                    <Border Grid.Column="5" Width="10" Height="10" CornerRadius="5"
                            Background="{Binding StatusBrush}" VerticalAlignment="Center"
                            ToolTipService.ToolTip="{Binding StatusTip}"/>
                </Grid>
            </DataTemplate>
            """);
    }

    private BuiltinToolContext MakeContext()
    {
        var xr = _state?.List?.XamlRoot;
        return new BuiltinToolContext { XamlRoot = xr ?? App.MainWindow?.Content.XamlRoot! };
    }

    private XamlRoot? GetXamlRoot() => _state?.List?.XamlRoot;

    // ------------------------------------------------------------------
    // 扫描
    // ------------------------------------------------------------------

    /// <summary>
    /// 两阶段扫描：先扫登录项/启动执行/Winlogon（秒级完成、立刻展示），
    /// 随后在后台自动补扫其余全部类别（服务、计划任务、驱动等），完成后整体替换列表。
    /// </summary>
    private async Task RunScanAsync(BuiltinToolContext context, XamlRoot? xamlRoot)
    {
        var state = _state;
        if (state is null || _scanning) return;
        _scanning = true;
        state.SetBusy(phase1: true);
        try
        {
            var exe = await EnsureAutorunscAsync(context, xamlRoot);
            if (exe is null) return;

            var hideMicrosoft = state.HideMsToggle.IsOn;
            var token = (_cts ??= new CancellationTokenSource()).Token;

            // 阶段 1：登录启动项（秒级）
            var (quick, quickRaw) = await ScanAutorunscAsync(exe, BuildArgs("lbw", hideMicrosoft),
                rows => state.StatusText.Text = $"正在扫描登录启动项…已读取 {rows} 项", token);
            _allEntries = quick;
            _rawCsv = quickRaw;
            RebuildTabs();
            ApplyFilter();
            state.StatusText.Text = $"已显示 {quick.Count} 项登录启动项，正在后台补充扫描其余类别…";

            // 阶段 2：后台扫描全部 17 类（结果包含阶段 1 的类别，完成后整体替换）。
            // 期间列表/搜索/筛选保持可用。
            state.SetBusy(phase1: false, phase2: true);
            var (full, fullRaw) = await ScanAutorunscAsync(exe, BuildArgs("*", hideMicrosoft),
                rows => state.StatusText.Text = $"正在后台扫描其余类别（服务/计划任务/驱动等）…已读取 {rows} 项", token);
            _allEntries = full;
            _rawCsv = fullRaw;
            RebuildTabs();
            ApplyFilter();
            state.StatusText.Text = $"扫描完成，共 {full.Count} 项（含服务、计划任务、驱动等全部类别）。";
        }
        catch (OperationCanceledException)
        {
            if (state.List?.XamlRoot is not null)
                state.StatusText.Text = "扫描已取消。";
        }
        catch (Exception ex)
        {
            if (state.List?.XamlRoot is not null)
            {
                state.StatusText.Text = "扫描失败。";
                await context.ShowError("启动项扫描失败", ex);
            }
        }
        finally
        {
            _scanning = false;
            state.SetBusy(phase1: false, phase2: false);
        }
    }

    private static string BuildArgs(string scope, bool hideMicrosoft)
    {
        // -a 范围：lbw=登录/启动执行/Winlogon（快速），* = 全部 17 类
        var sb = new StringBuilder("-accepteula -s -a ");
        sb.Append(scope);
        if (hideMicrosoft) sb.Append(" -m");
        sb.Append(" -c");
        return sb.ToString();
    }

    /// <summary>
    /// 运行 autorunsc，流式读取 stdout 原始字节，返回解析结果与原始 CSV 文本。
    /// 现代 autorunsc 输出 UTF-16LE（带 BOM），旧版为本地 ANSI 代码页。
    /// </summary>
    private static async Task<(List<AutorunsEntry> Entries, string RawCsv)> ScanAutorunscAsync(
        string exePath, string args, Action<int>? onRows, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = new Process { StartInfo = psi };
        if (!proc.Start())
            throw new InvalidOperationException("无法启动 autorunsc.exe。");

        using var ms = new MemoryStream();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await CopyAsync(proc.StandardOutput.BaseStream, ms, ct);

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(); } catch { /* 进程可能已自行退出 */ }
            throw;
        }

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"autorunsc 退出码 {proc.ExitCode}：{(await stderrTask).Trim()}");

        var text = DecodeOutput(ms.ToArray());
        var entries = StartupCsvParser.Parse(text, onRows);
        if (entries.Count == 0 && !text.Contains("Time,", StringComparison.Ordinal))
            throw new InvalidOperationException("未识别到 autorunsc 的输出（CSV 表头缺失）。");
        return (entries, text);
    }

    private static async Task CopyAsync(Stream source, Stream dest, CancellationToken ct)
    {
        var buffer = new byte[81920];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;
            await dest.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetACP();

    /// <summary>
    /// autorunsc 现代版本输出 UTF-16LE（带 BOM），旧版本为本地 ANSI 代码页；
    /// 按 BOM / 严格 UTF-8 依次尝试，失败回退系统 ANSI 代码页，避免中文路径乱码。
    /// </summary>
    internal static string DecodeOutput(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // 旧版 ANSI 输出：按系统 ANSI 代码页（如 936 中文）解码
            try
            {
                var cp = Encoding.GetEncoding((int)GetACP());
                return cp.GetString(bytes);
            }
            catch
            {
                return Encoding.UTF8.GetString(bytes); // 最后兜底（无效字符替换）
            }
        }
    }

    // ------------------------------------------------------------------
    // autorunsc 定位与下载兜底
    // ------------------------------------------------------------------

    /// <summary>
    /// 按当前进程架构挑选 autorunsc 命令行版；目录里没有对应架构文件时退回任意 autorunsc*.exe。
    /// </summary>
    internal static string? PickAutorunscExe(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        var preferred = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "autorunsc64a.exe",
            Architecture.X86 => "autorunsc.exe",
            _ => "autorunsc64.exe"
        };
        var exact = Path.Combine(dir, preferred);
        if (File.Exists(exact)) return exact;
        return Directory.GetFiles(dir, "autorunsc*.exe", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    internal static string? FindAutorunsc()
    {
        var bundled = Path.Combine(ToolCatalog.ToolsRoot, "其他工具", AutorunsDirName);
        var found = PickAutorunscExe(bundled);
        if (found is not null) return found;

        var dataDir = Path.Combine(ConfigManager.GetDataDir(), "Sysinternals");
        return PickAutorunscExe(dataDir);
    }

    internal static string? FindAutorunsGui()
    {
        var exe = FindAutorunsc();
        if (exe is not null)
        {
            var dir = Path.GetDirectoryName(exe)!;
            var gui = PickByPrefix(dir, "Autoruns");
            if (gui is not null) return gui;
        }
        return PickByPrefix(Path.Combine(ToolCatalog.ToolsRoot, "其他工具", AutorunsDirName), "Autoruns");
    }

    private static string? PickByPrefix(string dir, string prefix)
    {
        if (!Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, $"{prefix}*.exe", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string GetLiveUrl() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => LiveSysinternalsBase + "autorunsc64a.exe",
        Architecture.X86 => LiveSysinternalsBase + "autorunsc.exe",
        _ => LiveSysinternalsBase + "autorunsc64.exe"
    };

    /// <summary>
    /// 确保 autorunsc.exe 可用：随包/数据目录都没有时，从微软官方 live.sysinternals.com
    /// 下载到数据目录（带进度对话框），并做 MZ 魔数校验（杀软可能拦截或损坏文件）。
    /// </summary>
    private async Task<string?> EnsureAutorunscAsync(BuiltinToolContext context, XamlRoot? xamlRoot)
    {
        var existing = FindAutorunsc();
        if (existing is not null && File.Exists(existing)) return existing;

        var dialogRoot = xamlRoot ?? context.XamlRoot;

        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0 };
        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var dlg = new ContentDialog
        {
            Title = "下载 Autoruns 命令行工具",
            Content = new StackPanel
            {
                MinWidth = 360,
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "本机缺少 autorunsc.exe（精简版或绿色版未携带工具包）。首次使用需从微软官方网站下载命令行版（约 2MB）：",
                        TextWrapping = TextWrapping.Wrap
                    },
                    bar,
                    status
                }
            },
            CloseButtonText = "取消",
            XamlRoot = dialogRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var url = GetLiveUrl();
        var destDir = Path.Combine(ConfigManager.GetDataDir(), "Sysinternals");
        var fileName = Path.GetFileName(url);
        var showTask = dlg.ShowAsync();

        try
        {
            var progress = new Progress<ToolDownloadProgress>(p =>
            {
                bar.Value = p.Percentage;
                status.Text = $"正在下载… {p.Percentage:F0}%（{ToolDownloaderService.FormatSize((long)p.BytesReceived)} / {ToolDownloaderService.FormatSize((long)p.TotalBytes)}）";
            });
            var path = await ToolDownloaderService.DownloadToFileAsync(url, destDir, fileName, progress, (_cts ??= new CancellationTokenSource()).Token);

            if (!UpdateService.IsInstallerFileValid(path))
            {
                try { File.Delete(path); } catch { }
                throw new InvalidOperationException(
                    $"下载的文件校验失败（可能被杀毒软件拦截）。请手动下载 {OfficialZipPage}，解压后将 autorunsc*.exe 放到任意目录，或用「图形版 Autoruns」。");
            }

            status.Text = "下载完成。";
            TryHide(dlg);
            return path;
        }
        catch (OperationCanceledException)
        {
            TryHide(dlg);
            return null;
        }
        catch (Exception ex)
        {
            TryHide(dlg);
            await context.ShowError("下载 autorunsc 失败", ex);
            return null;
        }
    }

    private static void TryHide(ContentDialog dlg)
    {
        try { dlg.Hide(); } catch { /* 对话框可能已被关闭 */ }
    }

    // ------------------------------------------------------------------
    // 列表 / 标签 / 状态栏
    // ------------------------------------------------------------------

    /// <summary>
    /// 增量重建标签：已存在的标签对象保留（不重建、不闪烁），只补充新增类别；
    /// 重建期间抑制 SelectionChanged，避免列表被反复重建。
    /// </summary>
    private void RebuildTabs()
    {
        var state = _state;
        if (state is null) return;

        var current = (state.Tabs.SelectedItem as TabViewItem)?.Tag as string;

        // 类别集合（含已禁用记录，它们的类别可能在当前扫描里已没有条目）
        var categoryCounts = _allEntries
            .GroupBy(e => e.CategoryDisplay)
            .Select(g => (Name: g.Key, Count: g.Count()))
            .ToList();
        foreach (var r in _disabledRecords)
        {
            var name = StartupCsvParser.GetCategoryDisplay(r.Category);
            if (!categoryCounts.Any(c => c.Name == name))
                categoryCounts.Add((name, 1));
        }
        var ordered = categoryCounts.OrderByDescending(c => c.Count).ToList();

        _suppressFilter = true;
        try
        {
            var existing = state.Tabs.TabItems.OfType<TabViewItem>().ToList();
            var existingTags = new HashSet<string?>(existing.Select(t => t.Tag as string));

            // 「全部」：首次构建时创建并置顶；已存在时只更新计数（避免整条重建闪烁）
            var allItem = existing.FirstOrDefault(t => t.Tag is null);
            var totalCount = _allEntries.Count + _disabledRecords.Count;
            if (allItem is null)
            {
                allItem = new TabViewItem { Header = MakeTabHeader("全部", totalCount), Tag = null, IsClosable = false };
                state.Tabs.TabItems.Insert(0, allItem);
            }
            else
            {
                allItem.Header = MakeTabHeader("全部", totalCount);
            }

            foreach (var (name, count) in ordered)
            {
                if (name == "全部") continue;
                if (existingTags.Contains(name)) continue;
                state.Tabs.TabItems.Add(new TabViewItem { Header = MakeTabHeader(name, count), Tag = name, IsClosable = false });
            }

            // 默认选中「登录启动」（新手最关心的类别）；补扫重建后保持用户当前选择
            var tabs = state.Tabs.TabItems.OfType<TabViewItem>().ToList();
            TabViewItem? target = null;
            if (current is not null)
                target = tabs.FirstOrDefault(t => t.Tag as string == current);
            target ??= tabs.FirstOrDefault(t => t.Tag as string == "登录启动");
            target ??= tabs.FirstOrDefault(); // 兜底「全部」
            state.Tabs.SelectedItem = target;
        }
        finally
        {
            _suppressFilter = false;
        }
        ApplyFilter();
    }

    private static object MakeTabHeader(string name, int count)
    {
        var countText = new TextBlock
        {
            Text = count.ToString(),
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center
        };
        var nameText = new TextBlock
        {
            Text = name,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        stack.Children.Add(nameText);
        stack.Children.Add(countText);
        return stack;
    }

    private void ApplyFilter()
    {
        var state = _state;
        if (state is null || state.Tabs is null) return;

        var query = state.SearchBox.Text.Trim();
        var selectedCategory = (state.Tabs.SelectedItem as TabViewItem)?.Tag as string;

        // 合并“已由本工具禁用/已删除”的条目：autorunsc 不再枚举它们，用记录补回
        // （禁用 = 变灰；删除 = 变灰 + 删除线，都可就地重新勾选恢复）
        var all = _allEntries.ToList();
        var existingKeys = new HashSet<string>(all.Select(KeyOf), StringComparer.Ordinal);
        foreach (var r in _disabledRecords)
        {
            if (!existingKeys.Contains(KeyOf(r.Location, r.Entry)))
            {
                all.Add(r.ToEntry());
                existingKeys.Add(KeyOf(r.Location, r.Entry));
            }
        }
        var viewSource = all;

        var disabledKeys = new HashSet<string>(_disabledRecords.Select(r => KeyOf(r.Location, r.Entry)), StringComparer.Ordinal);
        var deletedKeys = new HashSet<string>(_disabledRecords.Where(r => r.Deleted).Select(r => KeyOf(r.Location, r.Entry)), StringComparer.Ordinal);

        IEnumerable<AutorunsEntry> view = viewSource;
        if (!string.IsNullOrEmpty(selectedCategory))
            view = view.Where(e => e.CategoryDisplay == selectedCategory);
        if (query.Length > 0)
        {
            view = view.Where(e =>
                e.Entry.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.ImagePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.LaunchString.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        // 复用已有 VM 实例：阶段 2 补扫完成后已显示的条目不重建（图标不闪、滚动不跳）
        var items = new List<StartupItemVm>(view.Count());
        foreach (var e in view)
        {
            var disabledByUs = disabledKeys.Contains(KeyOf(e.EntryLocation, e.Entry));
            var deleted = deletedKeys.Contains(KeyOf(e.EntryLocation, e.Entry));
            var vm = GetOrCreateVm(e, disabledByUs, deleted);
            items.Add(vm);
        }
        state.List.ItemsSource = new ObservableCollection<StartupItemVm>(items);

        // 补扫替换列表后恢复用户正在查看的条目并滚动到它
        if (_selectedKey is not null)
        {
            var match = items.FirstOrDefault(v => v.Key == _selectedKey);
            if (match is not null)
            {
                state.List.SelectedItem = match;
                state.List.ScrollIntoView(match);
            }
        }

        UpdateStatusBar(items);
    }

    private StartupItemVm GetOrCreateVm(AutorunsEntry e, bool disabledByUs, bool deleted)
    {
        var key = KeyOf(e.EntryLocation, e.Entry);
        if (_vmCache.TryGetValue(key, out var vm))
        {
            vm.UpdateData(e, disabledByUs, deleted);
            return vm;
        }
        vm = new StartupItemVm(e, disabledByUs, deleted);
        vm.ToggleRequested += OnToggleRequested;
        _vmCache[key] = vm;
        return vm;
    }

    private static string KeyOf(AutorunsEntry e) => KeyOf(e.EntryLocation, e.Entry);

    internal static string KeyOf(string location, string entry) => location + "\u0001" + entry;

    /// <summary>勾选框变化：请求禁用（value=false）或恢复（value=true）。</summary>
    private void OnToggleRequested(StartupItemVm vm, bool enable)
    {
        _ = ApplyToggleAsync(vm, enable);
    }

    private async Task ApplyToggleAsync(StartupItemVm vm, bool enable)
    {
        var state = _state;
        if (state is null || vm.IsActionInFlight) return;
        vm.IsActionInFlight = true;
        try
        {
            if (!enable)
            {
                if (!await ConfirmDisableAsync(vm))
                {
                    vm.RevertEnableChecked();
                    return;
                }
                var record = await StartupActionService.DisableAsync(vm.E);
                _disabledRecords.Add(record);
                SaveRecords();
                state.StatusText.Text = $"已禁用：{vm.Entry}";
            }
            else
            {
                var record = _disabledRecords.FirstOrDefault(r => KeyOf(r.Location, r.Entry) == vm.Key);
                if (record is null)
                {
                    vm.RevertEnableChecked();
                    state.StatusText.Text = "未找到该条目的禁用记录，刷新后再试。";
                    return;
                }
                await StartupActionService.EnableAsync(record);
                _disabledRecords.Remove(record);
                SaveRecords();
                state.StatusText.Text = $"已恢复：{vm.Entry}";
            }
            vm.MarkEnabled(enable);
        }
        catch (OperationCanceledException)
        {
            vm.RevertEnableChecked();
        }
        catch (Exception ex)
        {
            vm.RevertEnableChecked();
            state.StatusText.Text = "操作失败。";
            await MakeContext().ShowError("启动项操作失败", ex);
        }
        finally
        {
            vm.IsActionInFlight = false;
        }
    }

    private async Task<bool> ConfirmDisableAsync(StartupItemVm vm)
    {
        var dlg = MakeContext().CreateDialog("禁用启动项");
        dlg.Content = new StackPanel
        {
            MinWidth = 360,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = $"确定要禁用「{vm.Entry}」？",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = $"位置：{vm.E.EntryLocation}\n\n禁用后该项将不再随系统启动，可随时重新勾选恢复。",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        dlg.PrimaryButtonText = "禁用";
        dlg.CloseButtonText = "取消";
        dlg.DefaultButton = ContentDialogButton.Close;
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>删除选中项（工具栏按钮 / 详情对话框共用）：注册表项划线保留可恢复，其余类型行消失。重复删除会提示。</summary>
    private async Task DeleteSelectedAsync(StartupItemVm? vm)
    {
        var state = _state;
        if (state is null || vm is null || vm.IsActionInFlight) return;

        // 已删除的行再次点删除：直接提示，不再重复执行
        if (vm.IsDeleted)
        {
            var tipDlg = MakeContext().CreateDialog("提示");
            tipDlg.Content = new TextBlock
            {
                Text = $"「{vm.Entry}」已经删除了（列表已划线）。如需恢复，请重新勾选这一行。",
                TextWrapping = TextWrapping.Wrap
            };
            tipDlg.CloseButtonText = "知道了";
            await tipDlg.ShowAsync();
            return;
        }

        vm.IsActionInFlight = true;
        try
        {
            if (!await ConfirmDeleteAsync(vm)) return;

            // 注册表项：先备份再删除原值，行保留并画删除线（可重新勾选恢复）
            var record = await StartupActionService.DeleteAsync(vm.E);
            if (record is not null)
            {
                _disabledRecords.RemoveAll(r => KeyOf(r.Location, r.Entry) == vm.Key);
                _disabledRecords.Add(record);
                SaveRecords();
                ApplyFilter(); // VM 复用 → 该行直接变灰 + 删除线
                state.StatusText.Text = $"已删除（已划线，重新勾选可恢复）：{vm.Entry}";
            }
            else
            {
                // 文件进回收站、服务/任务真实删除：行消失
                _allEntries.RemoveAll(e => KeyOf(e) == vm.Key);
                SaveRecords();
                ApplyFilter();
                state.DeleteBtn.IsEnabled = false;
                state.StatusText.Text = $"已删除：{vm.Entry}";
            }
        }
        catch (Exception ex)
        {
            state.StatusText.Text = "删除失败。";
            await MakeContext().ShowError("删除失败", ex);
        }
        finally
        {
            vm.IsActionInFlight = false;
        }
    }

    private async Task<bool> ConfirmDeleteAsync(StartupItemVm vm)
    {
        var kind = StartupActionService.DetectKind(vm.E);
        var note = kind switch
        {
            "registry" => "注册表值会先备份到 AutorunsDisabled（与原版 Autoruns 相同的机制），列表中会保留这一行，重新勾选即可恢复。",
            "file" => "文件将移入回收站（不会直接删除），可在回收站中找回。",
            "service" => "将从系统中永久删除该服务/驱动，此操作无法恢复！",
            "task" => "将从任务计划程序中永久删除该计划任务，此操作无法恢复！",
            _ => "此操作无法恢复！"
        };
        var dlg = MakeContext().CreateDialog("删除启动项");
        dlg.Content = new StackPanel
        {
            MinWidth = 380,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = $"确定要删除「{vm.Entry}」？",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = $"位置：{vm.E.EntryLocation}\n\n{note}",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        dlg.PrimaryButtonText = "删除";
        dlg.CloseButtonText = "取消";
        dlg.DefaultButton = ContentDialogButton.Close;
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    private void SaveRecords()
    {
        try { StartupDisabledStore.Save(_disabledRecords); }
        catch (Exception ex)
        {
            if (_state?.StatusText is { } st) st.Text = "记录保存失败：" + ex.Message;
        }
    }

    private void UpdateStatusBar(List<StartupItemVm> items)
    {
        var state = _state;
        if (state is null) return;
        state.TotalText.Text = $"共 {_allEntries.Count} 项";
        state.EnabledText.Text = $"已启用 {items.Count(i => i.IsEnabled)} 项";
        state.RiskText.Text = $"有风险 {items.Count(i => i.IsRisk)} 项";
        state.ShownText.Text = $"当前显示 {items.Count} 项";
    }

    // ------------------------------------------------------------------
    // 详情（双击）/ 导出 / 图形版
    // ------------------------------------------------------------------

    private void ShowDetailDialog()
    {
        var state = _state;
        if (state is null || state.List.SelectedItem is not StartupItemVm vm) return;
        var e = vm.E;

        var stack = new StackPanel
        {
            MinWidth = 420,
            MaxWidth = 560,
            Spacing = 8
        };

        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        chipRow.Children.Add(new TextBlock { Text = e.CategoryDisplay, FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.AccentBlue) });
        chipRow.Children.Add(new TextBlock { Text = vm.SigningText, FontSize = 12, Foreground = vm.SigningBrush });
        if (e.FileMissing)
        {
            chipRow.Children.Add(new TextBlock
            {
                Text = "文件缺失",
                FontSize = 12,
                Foreground = new SolidColorBrush(ThemeColors.AccentRed),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
        }
        stack.Children.Add(chipRow);

        if (e.Description.Length > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = e.Description,
                FontSize = 12.5,
                Foreground = new SolidColorBrush(ThemeColors.SecondaryText),
                TextWrapping = TextWrapping.Wrap
            });
        }

        AddDetailPair(stack, "状态", e.IsEnabled ? "已启用" : "已禁用");
        if (vm.SignerName.Length > 0)
            AddDetailPair(stack, "签名者", vm.SignerName);
        if (e.Company.Length > 0)
            AddDetailPair(stack, "公司", e.Company);
        if (e.Profile.Length > 0)
            AddDetailPair(stack, "用户配置", e.Profile);
        if (e.Version.Length > 0)
            AddDetailPair(stack, "版本", e.Version);
        if (e.ImagePath.Length > 0)
            AddDetailPair(stack, "路径", e.ImagePath, mono: true);
        if (e.LaunchString.Length > 0)
            AddDetailPair(stack, "启动参数", e.LaunchString, mono: true);
        if (e.EntryLocation.Length > 0)
            AddDetailPair(stack, "位置", e.EntryLocation, mono: true);

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };

        var openBtn = new Button { Content = "打开所在文件夹" };
        var targetPath = e.HasImage ? e.OpenablePath : e.FileMissing ? e.MissingPath : null;
        openBtn.IsEnabled = targetPath is not null;
        openBtn.Click += (_, _) => { if (targetPath is not null) OpenInExplorer(targetPath); };
        actionRow.Children.Add(openBtn);

        var copyBtn = new Button { Content = "复制位置" };
        copyBtn.Click += (_, _) =>
        {
            var text = e.EntryLocation.Length > 0 ? e.EntryLocation : e.ImagePath;
            if (text.Length == 0) return;
            // 失败重试与诊断由 ClipboardService 统一负责（剪贴板被占用时不得崩溃）
            var copied = ClipboardService.TrySetText(text).Success;
            if (state.StatusText is { } st)
                st.Text = copied ? "已复制到剪贴板。" : "复制失败：剪贴板被其他程序占用，请稍后重试。";
        };
        actionRow.Children.Add(copyBtn);

        if (vm.CanOperate)
        {
            var deleteBtn = new Button { Content = "删除" };
            deleteBtn.Click += (_, _) => _ = DeleteSelectedAsync(vm);
            actionRow.Children.Add(deleteBtn);
        }
        stack.Children.Add(actionRow);

        var dlg = MakeContext().CreateDialog(e.Entry.Length > 0 ? e.Entry : "（未命名项）");
        dlg.Content = new ScrollViewer { Content = stack, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _ = dlg.ShowAsync();
    }

    private static void AddDetailPair(StackPanel host, string label, string value, bool mono = false)
    {
        var labelBlock = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 12,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap
        };
        if (mono) valueBlock.FontFamily = new FontFamily("Consolas");
        host.Children.Add(labelBlock);
        host.Children.Add(valueBlock);
    }

    private static void OpenInExplorer(string path)
    {
        var p = path.Trim().Trim('"');
        try
        {
            if (File.Exists(p))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{p}\"") { UseShellExecute = true });
                return;
            }
            if (Directory.Exists(p))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", p) { UseShellExecute = true });
                return;
            }
            var parent = Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                Process.Start(new ProcessStartInfo("explorer.exe", parent) { UseShellExecute = true });
        }
        catch { }
    }

    private void ExportCsv()
    {
        var state = _state;
        if (state is null) return;
        if (_rawCsv.Length == 0)
        {
            state.StatusText.Text = "暂无扫描结果，请先扫描。";
            return;
        }

        try
        {
            var dir = Path.Combine(ConfigManager.GetDataDir(), "启动项导出");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"启动项_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            // 原始输出为 UTF-16LE（含 banner），转为 UTF-8 带 BOM 方便 Excel 直接打开
            File.WriteAllText(file, _rawCsv, new UTF8Encoding(true));

            var openBtn = new Button { Content = "打开文件夹" };
            openBtn.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true }); }
                catch { }
                state.ExportDoneDialog?.Hide();
            };
            state.ExportDoneDialog = MakeContext().CreateDialog("导出成功");
            state.ExportDoneDialog.Content = new StackPanel
            {
                Spacing = 10,
                MinWidth = 360,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"已导出 {_allEntries.Count} 项到：\n{file}",
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true
                    },
                    openBtn
                }
            };
            state.StatusText.Text = "已导出 CSV 文件。";
            _ = state.ExportDoneDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            state.StatusText.Text = "导出失败：" + ex.Message;
        }
    }

    private void LaunchGuiAutoruns()
    {
        var state = _state;
        var gui = FindAutorunsGui();
        if (gui is null)
        {
            if (state?.StatusText is { } st) st.Text = "未找到图形版 Autoruns，请从官网下载：" + OfficialZipPage;
            return;
        }
        ToolProcessLauncher.Launch(gui, Path.GetDirectoryName(gui), runAsAdmin: false);
        if (state?.StatusText is { } st2) st2.Text = "已启动图形版 Autoruns。";
    }

    // ------------------------------------------------------------------
    // 列表项视图模型（含应用图标异步加载）
    // ------------------------------------------------------------------

    private sealed class StartupItemVm : INotifyPropertyChanged
    {
        private ImageSource? _icon;
        private bool _enableChecked;
        private bool _lastConfirmed;
        private bool _suspendCheck;
        private bool _isDisabledByUs;
        private bool _isDeleted;

        public StartupItemVm(AutorunsEntry e, bool disabledByUs, bool deleted = false)
        {
            E = e;
            Key = e.EntryLocation + "\u0001" + e.Entry;
            Entry = e.Entry.Length > 0 ? e.Entry : "（未命名项）";
            _isDeleted = deleted;
            _isDisabledByUs = disabledByUs || deleted;
            PathText = ComputePathText();
            CategoryDisplay = e.CategoryDisplay;
            Description = e.Description;
            SigningText = e.Signing.Key switch
            {
                "Verified" => "已验证",
                "NotVerified" => "未验证",
                "Expired" => "签名过期",
                "NotTrusted" => "不受信任",
                "None" => "未签名",
                _ => "未知"
            };
            SignerName = e.Signing.Name;
            Publisher = e.Signing.Name.Length > 0 ? e.Signing.Name : e.Company;

            CanOperate = StartupActionService.DetectKind(e) is not null;
            CheckTip = !CanOperate
                ? "此类型暂不支持在本工具中操作，请使用「图形版 Autoruns」"
                : _isDeleted
                    ? "已删除：重新勾选可从备份恢复"
                    : "勾选 = 启用；取消勾选 = 禁用该启动项（可随时恢复）";
            _lastConfirmed = e.IsEnabled && !disabledByUs;
            _enableChecked = _lastConfirmed;

            SigningBrush = SigningColorBrush(e.Signing.Key);
            IconVisibility = Visibility.Collapsed;
            FallbackVisibility = Visibility.Visible;

            var iconPath = e.ImagePath.Trim().Trim('"');
            if (iconPath.Length > 0 && !e.FileMissing && StartupIconService.TryGetBitmap(iconPath, out var cached) && cached is not null)
                Icon = cached; // 缓存命中 → 同步显示，不闪回退图标
            else
                LoadIconAsync();
        }

        public AutorunsEntry E { get; private set; }
        public string Key { get; }
        public string Entry { get; }
        public string PathText { get; private set; }
        public string CategoryDisplay { get; }
        public string Description { get; private set; }
        public string SigningText { get; private set; }
        public string SignerName { get; private set; }
        public string Publisher { get; private set; }
        public bool CanOperate { get; }
        public string CheckTip { get; }
        public Brush SigningBrush { get; }
        public bool IsEnabled => E.IsEnabled;
        public bool IsRisk => E.Signing.IsRisk;
        public bool IsActionInFlight { get; set; }
        public double RowOpacity => _isDisabledByUs ? 0.55 : 1.0;

        public Brush StatusBrush => new SolidColorBrush(StatusColor());
        public string StatusTip => BuildStatusTip();

        /// <summary>勾选框：取消勾选 = 禁用，重新勾选 = 恢复。</summary>
        public bool EnableChecked
        {
            get => _enableChecked;
            set
            {
                if (value == _enableChecked) return;
                if (_suspendCheck)
                {
                    _enableChecked = value;
                    OnPropertyChanged();
                    return;
                }
                _enableChecked = value;
                OnPropertyChanged();
                ToggleRequested?.Invoke(this, value);
            }
        }

        /// <summary>用户勾选发生变化时触发（value = true 表示请求启用）。</summary>
        public event Action<StartupItemVm, bool>? ToggleRequested;

        /// <summary>操作被取消/失败后，把勾选框还原到操作前的状态。</summary>
        public void RevertEnableChecked()
        {
            _suspendCheck = true;
            EnableChecked = _lastConfirmed;
            _suspendCheck = false;
        }

        /// <summary>禁用/恢复成功后同步勾选框与显示状态。</summary>
        public void MarkEnabled(bool enabled)
        {
            _lastConfirmed = enabled;
            _isDisabledByUs = !enabled;
            if (_enableChecked != enabled)
            {
                _suspendCheck = true;
                EnableChecked = enabled;
                _suspendCheck = false;
            }
            OnPropertyChanged(nameof(RowOpacity));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(StatusTip));
        }

        public Visibility IconVisibility { get; private set; }
        public Visibility FallbackVisibility { get; private set; }

        public ImageSource? Icon
        {
            get => _icon;
            private set
            {
                if (ReferenceEquals(_icon, value)) return;
                _icon = value;
                OnPropertyChanged();
                IconVisibility = value is null ? Visibility.Collapsed : Visibility.Visible;
                FallbackVisibility = value is null ? Visibility.Visible : Visibility.Collapsed;
                OnPropertyChanged(nameof(IconVisibility));
                OnPropertyChanged(nameof(FallbackVisibility));
            }
        }

        /// <summary>扫描数据刷新时更新文本字段（VM 实例复用，避免列表整批重建）。</summary>
        public void UpdateData(AutorunsEntry e, bool disabledByUs, bool deleted = false)
        {
            E = e;
            _isDeleted = deleted;
            _isDisabledByUs = disabledByUs || deleted;
            PathText = ComputePathText();
            Description = e.Description;
            SigningText = e.Signing.Key switch
            {
                "Verified" => "已验证",
                "NotVerified" => "未验证",
                "Expired" => "签名过期",
                "NotTrusted" => "不受信任",
                "None" => "未签名",
                _ => "未知"
            };
            SignerName = e.Signing.Name;
            Publisher = e.Signing.Name.Length > 0 ? e.Signing.Name : e.Company;

            // 同步勾选框：非本工具禁用的条目跟扫描结果走；本工具禁用/删除的保持取消勾选
            var desired = _isDisabledByUs ? false : e.IsEnabled;
            if (_enableChecked != desired)
            {
                _suspendCheck = true;
                EnableChecked = desired;
                _suspendCheck = false;
            }
            if (!_isDisabledByUs)
                _lastConfirmed = e.IsEnabled;

            OnPropertyChanged(nameof(PathText));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(SigningText));
            OnPropertyChanged(nameof(SignerName));
            OnPropertyChanged(nameof(Publisher));
            OnPropertyChanged(nameof(CheckTip));
            OnPropertyChanged(nameof(RowOpacity));
            OnPropertyChanged(nameof(NameDecorations));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(StatusTip));
        }

        public bool IsDeleted => _isDeleted;

        /// <summary>已删除的行，名称与路径画删除线。</summary>
        public Windows.UI.Text.TextDecorations NameDecorations =>
            _isDeleted ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;

        /// <summary>第二行小字：已删除/文件缺失时给出明确说明，避免用户误以为删除无效。</summary>
        private string ComputePathText()
        {
            if (_isDeleted) return "已删除 · 重新勾选可从备份恢复";
            if (E.FileMissing) return $"文件缺失：{E.MissingPath}";
            return E.ImagePath;
        }

        private async void LoadIconAsync()
        {
            try
            {
                var path = E.ImagePath.Trim().Trim('"');
                if (path.Length == 0 || E.FileMissing || !File.Exists(path)) return;
                var bmp = await StartupIconService.GetBitmapAsync(path);
                if (bmp is null) return;
                Icon = bmp;
            }
            catch { /* 图标加载失败时保留兜底图标 */ }
        }

        private Color StatusColor()
        {
            if (_isDeleted || _isDisabledByUs) return ThemeColors.DimText;
            if (E.FileMissing) return ThemeColors.AccentRed;
            if (!E.IsEnabled) return ThemeColors.DimText; // 系统已禁用 → 灰
            return E.Signing.IsRisk ? ThemeColors.AccentOrange : ThemeColors.AccentGreen;
        }

        private string BuildStatusTip()
        {
            if (_isDeleted) return "已删除（重新勾选可从备份恢复）";
            if (_isDisabledByUs) return "已由本工具禁用（可重新勾选恢复）";
            if (E.FileMissing) return "文件缺失";
            if (!E.IsEnabled) return "已禁用";
            if (E.Signing.IsRisk) return "未签名/未验证";
            return "正常";
        }

        private static Brush SigningColorBrush(string key) => key switch
        {
            "Verified" => new SolidColorBrush(ThemeColors.AccentGreen),
            "Expired" or "NotTrusted" => new SolidColorBrush(ThemeColors.AccentRed),
            "None" or "NotVerified" or "Unknown" => new SolidColorBrush(ThemeColors.AccentOrange),
            _ => new SolidColorBrush(ThemeColors.DimText)
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class StartupManagerState
    {
        public TextBlock TotalText = null!;
        public TextBlock EnabledText = null!;
        public TextBlock RiskText = null!;
        public TextBlock ShownText = null!;
        public TextBox SearchBox = null!;
        public ToggleSwitch HideMsToggle = null!;
        public Button RefreshBtn = null!;
        public Button DeleteBtn = null!;
        public Button ExportBtn = null!;
        public Button GuiBtn = null!;
        public ProgressRing BusyRing = null!;
        public TextBlock StatusText = null!;
        public ProgressBar TopBar = null!;
        public TabView Tabs = null!;
        public ListView List = null!;
        public ContentDialog? ExportDoneDialog = null;

        /// <param name="phase1">阶段 1（快速扫描登录项）期间锁定会与扫描冲突的控件。</param>
        /// <param name="phase2">阶段 2（后台补扫其余类别）：列表/搜索/筛选保持可用，仅禁用会触发重扫的控件。</param>
        public void SetBusy(bool phase1, bool phase2 = false)
        {
            var loading = phase1 || phase2;
            TopBar.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            TopBar.IsIndeterminate = loading;
            BusyRing.IsActive = loading;
            RefreshBtn.IsEnabled = !phase1 && !phase2;
            HideMsToggle.IsEnabled = !phase1 && !phase2;
            ExportBtn.IsEnabled = !phase1;
            GuiBtn.IsEnabled = true;
            if (phase1)
                StatusText.Text = "正在扫描登录启动项…";
        }
    }
}

// ----------------------------------------------------------------------
// 应用图标提取（复用 ToolIconService 的 System.Drawing 方案，独立缓存目录）
// ----------------------------------------------------------------------

/// <summary>
/// 为自启动条目的映像文件提取/缓存应用图标（exe/lnk 等），键为路径的 SHA256，
/// 与 ToolIconService 的缓存方案一致；显示时由 BitmapImage 直接加载 PNG。
/// </summary>
internal static class StartupIconService
{
    private static readonly Dictionary<string, string> MemoryCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, BitmapImage> BitmapCache = new(StringComparer.OrdinalIgnoreCase);

    // 软缓存上限：超出即整体清空按需重建（自启动条目数远低于上限，正常不会触发）
    private const int MaxMemoryCacheEntries = 512;
    private const int MaxBitmapCacheEntries = 256;

    private static void CacheIconPath(string path, string iconPath)
    {
        if (MemoryCache.Count >= MaxMemoryCacheEntries) MemoryCache.Clear();
        MemoryCache[path] = iconPath;
    }

    private static void CacheBitmap(string path, BitmapImage bitmap)
    {
        if (BitmapCache.Count >= MaxBitmapCacheEntries) BitmapCache.Clear();
        BitmapCache[path] = bitmap;
    }

    /// <summary>同步查询已加载的 BitmapImage（命中 → 列表重建时图标不闪回退样式）。</summary>
    public static bool TryGetBitmap(string imagePath, out BitmapImage? bitmap)
    {
        bitmap = null;
        var path = imagePath.Trim().Trim('"');
        return path.Length > 0 && BitmapCache.TryGetValue(path, out bitmap);
    }

    public static async Task<BitmapImage?> GetBitmapAsync(string imagePath)
    {
        var path = imagePath.Trim().Trim('"');
        if (path.Length == 0) return null;
        if (TryGetBitmap(path, out var cached)) return cached;

        var png = await GetIconPathAsync(path);
        if (png is null) return null;
        var bmp = new BitmapImage(new Uri(png));
        CacheBitmap(path, bmp);
        return bmp;
    }

    public static async Task<string?> GetIconPathAsync(string imagePath)
    {
        var path = imagePath.Trim().Trim('"');
        if (path.Length == 0 || !File.Exists(path)) return null;

        if (MemoryCache.TryGetValue(path, out var cached) && File.Exists(cached))
            return cached;

        var key = ComputeSha256(path);
        var cacheRoot = Path.Combine(ConfigManager.GetDataDir(), "IconCache", "Startup");
        var iconPath = Path.Combine(cacheRoot, key + ".png");

        // 快速路径：缓存存在且不比源文件旧
        if (File.Exists(iconPath))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(iconPath) >= File.GetLastWriteTimeUtc(path))
                {
                    CacheIconPath(path, iconPath);
                    return iconPath;
                }
            }
            catch { }
        }

        return await Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(cacheRoot);
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is null) return null;
                using var bitmap = System.Drawing.Bitmap.FromHicon(icon.Handle);
                bitmap.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
                CacheIconPath(path, iconPath);
                return iconPath;
            }
            catch
            {
                return null; // 提取失败（非 PE 文件等）→ UI 回退到兜底图标
            }
        });
    }

    private static string ComputeSha256(string path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return Convert.ToHexStringLower(bytes);
    }
}

// ----------------------------------------------------------------------
// 模型与 CSV 解析（internal，便于单元测试）
// ----------------------------------------------------------------------

/// <summary>一条自启动项记录（列来自 autorunsc -c 的 CSV 输出）。</summary>
internal sealed class AutorunsEntry
{
    public string Time { get; init; } = "";
    public string EntryLocation { get; init; } = "";
    public string Entry { get; init; } = "";
    public string EnabledRaw { get; init; } = "";
    public string Category { get; init; } = "";
    public string Profile { get; init; } = "";
    public string Description { get; init; } = "";
    public string Signer { get; init; } = "";
    public string Company { get; init; } = "";
    public string ImagePath { get; init; } = "";
    public string Version { get; init; } = "";
    public string LaunchString { get; init; } = "";

    public bool IsEnabled =>
        EnabledRaw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
        EnabledRaw.Equals("enabled", StringComparison.OrdinalIgnoreCase) ||
        EnabledRaw == "1";

    /// <summary>Image Path 列以 "File not found:" 开头表示文件已不存在（失效启动项）。</summary>
    public bool FileMissing => ImagePath.StartsWith("File not found:", StringComparison.OrdinalIgnoreCase);

    public string MissingPath => FileMissing ? ImagePath["File not found:".Length..].Trim().Trim('"') : "";

    /// <summary>可被资源管理器打开/定位的路径（去除两端的引号）。</summary>
    public string OpenablePath => HasImage ? ImagePath.Trim().Trim('"') : "";

    public bool HasImage => ImagePath.Length > 0 && !FileMissing;

    public SigningInfo Signing => StartupCsvParser.ParseSigner(Signer);

    public string CategoryDisplay => StartupCsvParser.GetCategoryDisplay(Category);
}

internal readonly record struct SigningInfo(string Key, string Name)
{
    public bool IsVerified => Key == "Verified";

    /// <summary>未签名 / 未验证 / 签名异常视为有风险。</summary>
    public bool IsRisk => Key is "None" or "NotVerified" or "Unknown" or "Expired" or "NotTrusted";
}

/// <summary>解析 autorunsc -c 的 CSV 输出：跳过 banner、按表头动态映射列。</summary>
internal static class StartupCsvParser
{
    /// <summary>
    /// 解析整段 CSV 文本。表头行以 "Time," 开头；数据行跳过没有具体启动项的"容器行"。
    /// </summary>
    public static List<AutorunsEntry> Parse(string csvText, Action<int>? onRow = null)
    {
        var entries = new List<AutorunsEntry>();
        using var reader = new StringReader(csvText);
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headerSeen = false;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;

            if (!headerSeen)
            {
                if (!line.StartsWith("Time,", StringComparison.Ordinal)) continue; // 跳过 banner 行
                var headers = ParseCsvLine(line);
                for (var i = 0; i < headers.Count; i++)
                    index[headers[i]] = i;
                headerSeen = true;
                continue;
            }

            var fields = ParseCsvLine(line);
            string Get(string name)
            {
                var col = index.TryGetValue(name, out var c) ? c : -1;
                return col >= 0 && col < fields.Count ? fields[col].Trim() : "";
            }

            var entry = new AutorunsEntry
            {
                Time = Get("Time"),
                EntryLocation = Get("Entry Location"),
                Entry = Get("Entry"),
                EnabledRaw = Get("Enabled"),
                Category = Get("Category"),
                Profile = Get("Profile"),
                Description = Get("Description"),
                Signer = Get("Signer"),
                Company = Get("Company"),
                ImagePath = Get("Image Path"),
                Version = Get("Version"),
                LaunchString = Get("Launch String")
            };

            // 跳过"容器行"（只列目录键、没有具体启动项）
            if (entry.Entry.Length == 0 && entry.ImagePath.Length == 0 && entry.LaunchString.Length == 0)
                continue;

            entries.Add(entry);
            onRow?.Invoke(entries.Count);
        }

        return entries;
    }

    /// <summary>RFC4180 风格行解析：引号包裹、双引号转义、字段内可含逗号。</summary>
    public static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    /// <summary>autorunsc 类别值 → 中文显示名（未知类别保留原值）。</summary>
    public static string GetCategoryDisplay(string category) => category.Trim() switch
    {
        "Logon" => "登录启动",
        "Scheduled Tasks" => "计划任务",
        "Tasks" => "计划任务",
        "Services" => "服务",
        "Boot Execute" => "启动执行",
        "Explorer" => "Explorer 加载项",
        "Internet Explorer" => "IE 加载项",
        "AppInit" => "AppInit DLL",
        "Image Hijacks" => "映像劫持",
        "Known DLLs" => "已知 DLL",
        "Winlogon" => "Winlogon 通知",
        "Winsock" => "Winsock 提供程序",
        "Codecs" => "编解码器",
        "Print Monitors" => "打印监视器",
        "LSA" => "LSA 提供程序",
        "WMI" => "WMI 事件",
        "Sidebar Gadgets" => "边栏小工具",
        _ => category.Trim()
    };

    /// <summary>
    /// 解析 Signer 列（带 -s 时形如 "(Verified) Microsoft Windows"、"File not found: ..."）。
    /// 空值表示未签名。
    /// </summary>
    public static SigningInfo ParseSigner(string signer)
    {
        var s = signer.Trim();
        if (s.Length == 0) return new SigningInfo("None", "");

        var rules = new (string Prefix, string Key)[]
        {
            ("(Verified)", "Verified"),
            ("(Not verified)", "NotVerified"),
            ("(Expired)", "Expired"),
            ("(Not trusted)", "NotTrusted")
        };
        foreach (var (prefix, key) in rules)
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return new SigningInfo(key, s[prefix.Length..].Trim());
        }
        return new SigningInfo("Unknown", s);
    }
}

// ----------------------------------------------------------------------
// 禁用/恢复启动项（Autoruns 式勾选操作）
// ----------------------------------------------------------------------

/// <summary>一条“由本工具禁用”的启动项记录（用于恢复与界面上补回一行）。</summary>
internal sealed class DisabledRecord
{
    public string Location { get; set; } = "";   // Entry Location（注册表路径 / "Task Scheduler" / 服务键）
    public string Entry { get; set; } = "";      // 值名 / 任务路径 / 服务名 / 文件名
    public string Kind { get; set; } = "";       // registry | file | service | task
    public string Category { get; set; } = "";   // 原类别（英文原始值）
    public string Profile { get; set; } = "";
    public string Description { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string LaunchString { get; set; } = "";
    public string Payload { get; set; } = "";    // registry: 注册表值数据；service: 原始启动类型关键字
    public string DisabledAtUtc { get; set; } = "";

    /// <summary>true = 已删除（注册表值已移入镜像备份键，重新勾选可从备份恢复）。</summary>
    public bool Deleted { get; set; }

    public AutorunsEntry ToEntry() => new()
    {
        EntryLocation = Location,
        Entry = Entry,
        EnabledRaw = "disabled",
        Category = Category,
        Profile = Profile,
        Description = Description,
        ImagePath = ImagePath,
        LaunchString = LaunchString
    };

    public static DisabledRecord FromEntry(AutorunsEntry e, string kind) => new()
    {
        Location = e.EntryLocation,
        Entry = e.Entry,
        Kind = kind,
        Category = e.Category,
        Profile = e.Profile,
        Description = e.Description,
        ImagePath = e.ImagePath,
        LaunchString = e.LaunchString,
        DisabledAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
    };
}

/// <summary>禁用记录的持久化（DataDir/启动项管理/disabled.json）。</summary>
internal static class StartupDisabledStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(ConfigManager.GetDataDir(), "启动项管理", "disabled.json");

    public static List<DisabledRecord> Load(string? path = null)
    {
        var file = path ?? DefaultPath;
        if (!File.Exists(file)) return [];
        try
        {
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<List<DisabledRecord>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(List<DisabledRecord> records, string? path = null)
    {
        var file = path ?? DefaultPath;
        var dir = Path.GetDirectoryName(file)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(records, Options);
        File.WriteAllText(file, json, new UTF8Encoding(false));
    }
}

/// <summary>
/// 对启动项执行禁用/恢复（Autoruns 式操作）：
/// 注册表值 → 移到 CurrentVersion\Explorer\AutorunsDisabled 镜像键（可独立恢复）；
/// 启动文件夹文件 → 重命名为 .disabled 后缀；
/// 服务/驱动 → sc config start= disabled，恢复为原启动类型；
/// 计划任务 → schtasks /change /disable|/enable。
/// </summary>
internal static class StartupActionService
{
    private const string TaskSchedulerLocation = "Task Scheduler";
    private const string ServicesLocation = @"HKLM\System\CurrentControlSet\Services";
    private const string AutorunsDisabledMarker = @"\CurrentVersion\Explorer\AutorunsDisabled";

    /// <summary>
    /// 判断某条目支持哪种禁用方式；返回 null 表示不支持（如 Known DLLs、容器行等）。
    /// </summary>
    public static string? DetectKind(AutorunsEntry e)
    {
        var loc = e.EntryLocation.Trim();
        if (loc.Length == 0 || e.Entry.Length == 0) return null;

        if (loc.StartsWith(TaskSchedulerLocation, StringComparison.OrdinalIgnoreCase))
            return "task";
        // 服务/驱动行的 Entry Location 恰好就是 Services 键；更深的子路径是普通注册表值
        if (loc.Equals(ServicesLocation, StringComparison.OrdinalIgnoreCase))
            return "service";
        if (loc.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase) ||
            loc.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) ||
            loc.StartsWith("HKCR\\", StringComparison.OrdinalIgnoreCase) ||
            loc.StartsWith("HKU\\", StringComparison.OrdinalIgnoreCase))
            return "registry";
        // 路径形式的类别（如启动文件夹 "C:\...\Startup"）
        if (loc.Contains(":\\") || loc.StartsWith("\\\\"))
            return "file";
        return null;
    }

    /// <summary>把 "HKLM\SOFTWARE\X" 拆成 (根键名, 子路径)；供注册表操作与测试使用。</summary>
    public static (string Hive, string SubPath) ParseRegistryLocation(string location)
    {
        var sep = location.IndexOf('\\');
        if (sep <= 0) throw new ArgumentException($"不是有效的注册表路径：{location}");
        var hive = location[..sep].ToUpperInvariant();
        if (hive is not ("HKLM" or "HKCU" or "HKCR" or "HKU"))
            throw new NotSupportedException($"不支持的注册表根键：{hive}");
        return (hive, location[(sep + 1)..]);
    }

    private static RegistryKey GetBaseKey(string hive) => hive switch
    {
        "HKLM" => Registry.LocalMachine,
        "HKCU" => Registry.CurrentUser,
        "HKCR" => Registry.ClassesRoot,
        _ => Registry.Users
    };

    /// <summary>镜像键：原 Run 键 → 同层 CurrentVersion\Explorer\AutorunsDisabled\Run。</summary>
    private static string GetMirrorSubPath(string subPath)
    {
        const string marker = @"\CurrentVersion\";
        var idx = subPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return subPath[..idx] + @"\CurrentVersion\Explorer\AutorunsDisabled" + subPath[(idx + marker.Length)..];
        return subPath + @"\AutorunsDisabled";
    }

    public static async Task<DisabledRecord> DisableAsync(AutorunsEntry e, CancellationToken ct = default)
    {
        var kind = DetectKind(e) ?? throw new NotSupportedException("该条目不支持在此禁用，请使用图形版 Autoruns。");
        var record = DisabledRecord.FromEntry(e, kind);
        switch (kind)
        {
            case "registry":
                DisableRegistry(e, record);
                break;
            case "file":
                DisableFile(e, record);
                break;
            case "service":
                record.Payload = await DisableServiceAsync(e.Entry, ct);
                break;
            case "task":
                await RunProcessAsync("schtasks.exe", ["/change", "/tn", e.Entry, "/disable"], ct);
                break;
        }
        return record;
    }

    /// <summary>
    /// 删除启动项（永久移除）。返回非 null 表示该项已备份且可恢复（注册表值）；
    /// 返回 null 表示真实删除（文件进回收站，服务/任务不可恢复）。
    /// </summary>
    public static async Task<DisabledRecord?> DeleteAsync(AutorunsEntry e, CancellationToken ct = default)
    {
        var kind = DetectKind(e) ?? throw new NotSupportedException("该条目不支持在此删除，请使用图形版 Autoruns。");
        switch (kind)
        {
            case "registry":
                // 与禁用相同的“移入镜像键”机制，但记录标记为已删除，重新勾选可从备份恢复
                var record = DisabledRecord.FromEntry(e, "registry");
                DisableRegistry(e, record);
                record.Deleted = true;
                return record;
            case "file":
                DeleteFileToRecycleBin(e.ImagePath);
                return null;
            case "service":
            {
                var (exit, _, stderr) = await RunProcessAsync("sc.exe", ["delete", e.Entry], ct);
                if (exit != 0)
                    throw new InvalidOperationException($"删除服务「{e.Entry}」失败：{(stderr.Length > 0 ? stderr.Trim() : "未知错误")}（服务可能仍在运行，请先在服务管理器中停止）");
                return null;
            }
            case "task":
            {
                var (exit, _, stderr) = await RunProcessAsync("schtasks.exe", ["/delete", "/f", "/tn", e.Entry], ct);
                if (exit != 0)
                    throw new InvalidOperationException($"删除计划任务「{e.Entry}」失败：{(stderr.Length > 0 ? stderr.Trim() : "未知错误")}");
                return null;
            }
            default:
                throw new NotSupportedException($"不支持的删除类型：{kind}");
        }
    }

    public static async Task EnableAsync(DisabledRecord r, CancellationToken ct = default)
    {
        switch (r.Kind)
        {
            case "registry":
                EnableRegistry(r);
                break;
            case "file":
                EnableFile(r);
                break;
            case "service":
                var startType = r.Payload.Length > 0 ? r.Payload : "auto";
                await RunProcessAsync("sc.exe", ["config", r.Entry, "start=", startType], ct);
                break;
            case "task":
                await RunProcessAsync("schtasks.exe", ["/change", "/tn", r.Entry, "/enable"], ct);
                break;
            default:
                throw new NotSupportedException($"未知的禁用类型：{r.Kind}");
        }
    }

    // ---- 注册表 ----

    private static void DisableRegistry(AutorunsEntry e, DisabledRecord record)
    {
        var (hive, subPath) = ParseRegistryLocation(e.EntryLocation);
        var baseKey = GetBaseKey(hive);

        using var key = baseKey.OpenSubKey(subPath, writable: true)
            ?? throw new InvalidOperationException($"无法打开注册表键：{e.EntryLocation}（可能缺少权限）");
        var value = key.GetValue(e.Entry, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (value is null)
            return; // 值已不存在（可能已被其他工具处理），仍记录为已禁用

        var kind = key.GetValueKind(e.Entry);
        if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString))
            throw new NotSupportedException($"「{e.Entry}」的注册表值是 {kind} 类型，暂不支持在此禁用，请使用图形版 Autoruns。");

        var data = (string)value;
        var mirrorSub = GetMirrorSubPath(subPath);
        using (var mirror = baseKey.CreateSubKey(mirrorSub))
        {
            if (mirror.GetValue(e.Entry) is null)
                mirror.SetValue(e.Entry, data, kind);
        }
        key.DeleteValue(e.Entry, throwOnMissingValue: false);
        record.Payload = data;
    }

    private static void EnableRegistry(DisabledRecord r)
    {
        var (hive, subPath) = ParseRegistryLocation(r.Location);
        var baseKey = GetBaseKey(hive);
        var mirrorSub = GetMirrorSubPath(subPath);

        using var mirror = baseKey.OpenSubKey(mirrorSub, writable: true)
            ?? throw new InvalidOperationException($"未找到禁用镜像键：{hive}\\{mirrorSub}，可能已被手动删除。");
        var data = mirror.GetValue(r.Entry);
        if (data is null && r.Payload.Length > 0)
            data = r.Payload;
        if (data is null)
            throw new InvalidOperationException($"未找到「{r.Entry}」的已保存数据，无法恢复。");

        using var key = baseKey.OpenSubKey(subPath, writable: true)
            ?? throw new InvalidOperationException($"无法打开原注册表键：{r.Location}（可能已被删除）");
        if (key.GetValue(r.Entry) is null)
            key.SetValue(r.Entry, data, mirror.GetValueKind(r.Entry));
        try { mirror.DeleteValue(r.Entry, throwOnMissingValue: false); } catch { }
    }

    // ---- 启动文件夹文件 ----

    private static void DisableFile(AutorunsEntry e, DisabledRecord record)
    {
        var src = e.ImagePath.Trim().Trim('"');
        if (src.Length == 0 || !File.Exists(src))
            throw new InvalidOperationException($"文件不存在：{src}");
        var target = src + ".disabled";
        if (File.Exists(target))
            throw new InvalidOperationException($"目标文件已存在：{target}");
        File.Move(src, target);
        record.Payload = target;
    }

    private static void EnableFile(DisabledRecord r)
    {
        var src = r.Payload.Length > 0 ? r.Payload : r.ImagePath.Trim().Trim('"');
        if (src.Length == 0 || !File.Exists(src))
            throw new InvalidOperationException($"文件不存在：{src}");
        var target = src.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
            ? src[..^".disabled".Length]
            : src;
        if (File.Exists(target))
            throw new InvalidOperationException($"原文件已存在：{target}");
        File.Move(src, target);
    }

    // ---- 服务 / 计划任务 ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x40;     // 移入回收站（可恢复）
    private const ushort FOF_NOCONFIRMATION = 0x10;
    private const ushort FOF_SILENT = 0x04;

    /// <summary>把启动文件移入回收站而非直接删除，误删也能找回。</summary>
    internal static void DeleteFileToRecycleBin(string imagePath)
    {
        var path = imagePath.Trim().Trim('"');
        if (path.Length == 0 || !File.Exists(path))
            throw new InvalidOperationException($"文件不存在：{path}");
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT
        };
        var result = SHFileOperation(ref op);
        if (result != 0)
            throw new InvalidOperationException($"移入回收站失败（错误码 0x{result:X8}）。文件可能被占用或权限不足。");
    }

    private static async Task<string> DisableServiceAsync(string serviceName, CancellationToken ct)
    {
        var (exit, stdout, stderr) = await RunProcessAsync("sc.exe", ["qc", serviceName], ct);
        if (exit != 0)
            throw new InvalidOperationException($"读取服务「{serviceName}」信息失败：{(stderr.Length > 0 ? stderr.Trim() : stdout.Trim())}");
        var startType = ParseServiceStartType(stdout)
            ?? throw new InvalidOperationException($"无法解析服务「{serviceName}」的启动类型：{stdout}");

        var (exit2, _, stderr2) = await RunProcessAsync("sc.exe", ["config", serviceName, "start=", "disabled"], ct);
        if (exit2 != 0)
            throw new InvalidOperationException($"禁用服务「{serviceName}」失败：{stderr2.Trim()}");
        return startType;
    }

    /// <summary>解析 "sc qc" 输出中的 START_TYPE 为 sc config 关键字（auto/demand/boot/system/disabled）。</summary>
    internal static string? ParseServiceStartType(string qcOutput)
    {
        foreach (var rawLine in qcOutput.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("START_TYPE", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split([' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries);
            // ["START_TYPE","2","AUTO_START"]
            var keyword = parts.Length >= 3 ? parts[2].Trim().ToUpperInvariant() : "";
            return keyword switch
            {
                "AUTO_START" => "auto",
                "DEMAND_START" => "demand",
                "BOOT_START" => "boot",
                "SYSTEM_START" => "system",
                "DISABLED" => "disabled",
                _ => null
            };
        }
        return null;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(
        string fileName, IEnumerable<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"无法启动 {fileName}");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(); } catch { }
            throw;
        }
        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }
}