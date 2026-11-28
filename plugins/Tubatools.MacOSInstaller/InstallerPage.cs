using System.Diagnostics;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.PluginSdk;
using Tubatools.MacOSInstaller.Core;

namespace Tubatools.MacOSInstaller;

internal sealed partial class InstallerPage
{
    private readonly PluginExecutionContext _ctx;
    private readonly DispatcherQueue _dispatcher;
    private readonly Grid _root = new();
    private readonly StackPanel _stepsBar = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly ScrollViewer _scroll = new();
    private readonly StackPanel _body = new() { Spacing = 14, Padding = new Thickness(0, 0, 0, 8) };
    private readonly InfoBar _notice = new() { IsOpen = false, IsClosable = true };
    private readonly TextBlock _statusText = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed, Width = 220 };
    private readonly Button _cancel = new() { Content = "停止", Visibility = Visibility.Collapsed };
    private readonly Border _statusCard;
    private readonly Border _logPanel;
    private readonly TextBox _logBox = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        MinHeight = 180,
        MaxHeight = 260
    };
    private readonly DispatcherQueueTimer _logTimer;
    private readonly StringBuilder _pendingLog = new();
    private readonly object _logGate = new();
    private readonly ProcessRunner _runner;
    private readonly List<(Button Button, int Step)> _stepButtons = [];

    private bool _closed;
    private bool _busy;
    private bool _autoScanStarted;
    private bool _advancedExpanded;
    private bool _usbAutoScanStarted;
    private int _step;
    private HardwareProfile? _hardware;
    private CompatibilityReport? _report;
    private BuildOutcome? _build;
    private MacTarget _target = MacTarget.Sequoia;
    private bool _targetExplicit;
    private bool _suppressTargetSelection;
    private int _audioLayout = 1;
    private bool _verboseBoot = true;
    private bool _forceUnsupported;
    private CancellationTokenSource? _activeCts;
    private Button? _nextButton;

    private InstallerPage(PluginExecutionContext ctx)
    {
        _ctx = ctx;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _runner = new ProcessRunner(Path.Combine(ctx.Services.DataDirectory, "logs"));
        _statusCard = Card(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { _progress, _statusText, _cancel }
        });
        _statusCard.Visibility = Visibility.Collapsed;
        _logPanel = BuildLogPanel();
        _logPanel.Visibility = Visibility.Collapsed;
        _logTimer = _dispatcher.CreateTimer();
        _logTimer.Interval = TimeSpan.FromMilliseconds(200);
        _logTimer.Tick += (_, _) => FlushLog();
        _logTimer.Start();
        _cancel.Click += (_, _) => _activeCts?.Cancel();
        _root.Unloaded += (_, _) =>
        {
            _closed = true;
            _logTimer.Stop();
            _activeCts?.Cancel();
        };
    }

    public static object Create(PluginExecutionContext ctx) => new InstallerPage(ctx).Build();

    private FrameworkElement Build()
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.Padding = new Thickness(24, 18, 24, 12);
        _root.RowSpacing = 10;

        Grid.SetRow(_statusCard, 0);
        _root.Children.Add(_statusCard);

        BuildStepBar();
        var stepRow = new Grid { ColumnSpacing = 8 };
        stepRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stepRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        stepRow.Children.Add(_stepsBar);
        var logLink = new HyperlinkButton { Content = "运行日志", Padding = new Thickness(10, 6, 0, 6), VerticalAlignment = VerticalAlignment.Center };
        logLink.Click += (_, _) =>
        {
            bool show = _logPanel.Visibility != Visibility.Visible;
            _logPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) _logBox.Visibility = Visibility.Visible;
            logLink.Content = show ? "收起日志" : "运行日志";
        };
        Grid.SetColumn(logLink, 1);
        stepRow.Children.Add(logLink);
        Grid.SetRow(stepRow, 1);
        _root.Children.Add(stepRow);

        _scroll.Content = _body;
        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Grid.SetRow(_scroll, 2);
        _root.Children.Add(_scroll);

        Grid.SetRow(_logPanel, 3);
        _root.Children.Add(_logPanel);

        _ = LoadSettingsAsync();
        RenderStep();
        _ = AutoScanAsync();
        return _root;
    }

    private void BuildStepBar()
    {
        string[] titles = ["1 检查电脑", "2 生成 EFI", "3 设置 BIOS", "4 制作 U 盘", "遇到问题"];
        for (int i = 0; i < titles.Length; i++)
        {
            int stepIndex = i;
            var button = new Button
            {
                Content = titles[i],
                Padding = new Thickness(14, 8, 14, 8),
                Tag = stepIndex
            };
            button.Click += (_, _) =>
            {
                _step = stepIndex;
                RenderStep();
            };
            _stepButtons.Add((button, stepIndex));
            _stepsBar.Children.Add(button);
        }
    }

    private Border BuildLogPanel()
    {
        var copy = new Button { Content = "复制", Padding = new Thickness(12, 7, 12, 7) };
        var open = new Button { Content = "打开日志目录", Padding = new Thickness(12, 7, 12, 7) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { copy, open } };
        _logBox.Visibility = Visibility.Visible;
        var panel = new StackPanel { Spacing = 8, Children = { _logBox, bar } };
        copy.Click += async (_, _) => await _ctx.Services.CopyTextAsync(_logBox.Text, CancellationToken.None);
        open.Click += (_, _) => OpenPath(Path.Combine(_ctx.Services.DataDirectory, "logs"));
        return Card(panel);
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            string? t = await _ctx.Services.GetSettingAsync("target");
            _targetExplicit = !string.IsNullOrWhiteSpace(t);
            _target = MacTarget.FromDarwin(t);
            string? audio = await _ctx.Services.GetSettingAsync("audioLayout");
            if (int.TryParse(audio, out int a)) _audioLayout = Math.Clamp(a, 0, 99);
            string? verbose = await _ctx.Services.GetSettingAsync("verbose");
            if (verbose is not null) _verboseBoot = verbose != "0";
            string? force = await _ctx.Services.GetSettingAsync("force");
            _forceUnsupported = force == "1";
            string? proxy = await _ctx.Services.GetSettingAsync("proxy");
            if (proxy is not null) _proxy = proxy.Trim();
            RenderStep();
        }
        catch
        {
        }
    }

    private void RenderStep()
    {
        if (_closed) return;
        _body.Children.Clear();
        foreach ((Button button, int step) in _stepButtons)
        {
            bool active = step == _step;
            button.Background = active
                ? Brush("AccentFillColorDefaultBrush")
                : new SolidColorBrush(Colors.Transparent);
            button.Foreground = active
                ? Brush("TextOnAccentFillColorPrimaryBrush")
                : Brush("TextFillColorPrimaryBrush");
        }

        _nextButton = null;
        switch (_step)
        {
            case 0: RenderCheckStep(); break;
            case 1: RenderBuildStep(); break;
            case 2: RenderBiosStep(); break;
            case 3: RenderUsbStep(); break;
            default: RenderToolsStep(); break;
        }
        _scroll.ChangeView(null, 0, null, true);
    }

    private void RenderCheckStep()
    {
        _body.Children.Add(SectionTitle("第 1 步 · 检查这台电脑", "不用懂配置。程序会自动读取硬件，告诉你这台电脑适不适合装 macOS。"));
        if (_notice.IsOpen) _body.Children.Add(_notice);

        if (_hardware is null && !_busy)
            _body.Children.Add(Hint("正在自动检查，通常几秒钟就好。你也可以点下面的「重新检查」再试一次。"));

        var target = new ComboBox { MinWidth = 260 };
        target.Items.Add(new ComboBoxItem { Content = "macOS 15 Sequoia（兼容性更稳）", Tag = "24" });
        target.Items.Add(new ComboBoxItem { Content = "macOS 26 Tahoe（较新硬件）", Tag = "25" });
        _suppressTargetSelection = true;
        target.SelectedIndex = _target.Darwin == "25" ? 1 : 0;
        _suppressTargetSelection = false;
        target.SelectionChanged += (_, _) =>
        {
            if (_suppressTargetSelection) return;
            _targetExplicit = true;
            string? tag = (target.SelectedItem as ComboBoxItem)?.Tag as string;
            _target = MacTarget.FromDarwin(tag);
            SaveSetting("target", _target.Darwin);
            if (_hardware is not null)
            {
                _report = CompatibilityRules.Evaluate(_hardware, _target, Options());
            }
            _build = null;
            RenderStep();
        };

        var audio = new TextBox { Text = _audioLayout.ToString(), Width = 120 };
        audio.TextChanged += (_, _) =>
        {
            if (int.TryParse(audio.Text, out int value))
            {
                _audioLayout = Math.Clamp(value, 0, 99);
                SaveSetting("audioLayout", _audioLayout.ToString());
                _build = null;
                if (_hardware is not null)
                    _report = CompatibilityRules.Evaluate(_hardware, _target, Options());
                if (_nextButton is not null)
                    _nextButton.IsEnabled = _report?.CanBuild == true;
            }
        };

        var verbose = new CheckBox { Content = "显示详细启动日志（排错用，默认开启）", IsChecked = _verboseBoot };
        verbose.Checked += (_, _) => { _verboseBoot = true; SaveSetting("verbose", "1"); _build = null; };
        verbose.Unchecked += (_, _) => { _verboseBoot = false; SaveSetting("verbose", "0"); _build = null; };

        var force = new CheckBox
        {
            Content = "跳过兼容性检查（不推荐，只在你确定这台电脑能装时使用）",
            IsChecked = _forceUnsupported
        };
        force.Checked += (_, _) =>
        {
            _forceUnsupported = true;
            SaveSetting("force", "1");
            RefreshReport();
            _dispatcher.TryEnqueue(() => RenderStep());
        };
        force.Unchecked += (_, _) =>
        {
            _forceUnsupported = false;
            SaveSetting("force", "0");
            RefreshReport();
            _dispatcher.TryEnqueue(() => RenderStep());
        };

        var advancedContent = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Hint("普通用户不需要改这里。默认值已经按兼容性优先选好。"),
                new TextBlock { Text = "安装版本", Foreground = Brush("TextFillColorSecondaryBrush") },
                target,
                new TextBlock { Text = "音频 layout-id（默认 1；装好后没声音再改，0 = 不加载音频驱动）", Foreground = Brush("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap },
                audio,
                verbose,
                force
            }
        };
        var advanced = new Expander
        {
            Header = "高级选项（一般不用改）",
            IsExpanded = _advancedExpanded,
            Content = advancedContent
        };
        advanced.Expanding += (_, _) => _advancedExpanded = true;
        advanced.Collapsed += (_, _) => _advancedExpanded = false;

        if (_report is not null)
            _body.Children.Add(BuildCompatibilityCard(_report));

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var rescan = new Button { Content = _hardware is null ? "立即检查" : "重新检查", Padding = new Thickness(14, 9, 14, 9) };
        rescan.Click += async (_, _) => await ScanAsync();
        var next = PrimaryButton("下一步：生成 EFI");
        next.IsEnabled = _report?.CanBuild == true;
        _nextButton = next;
        next.Click += (_, _) => { _step = 1; RenderStep(); };
        nav.Children.Add(rescan);
        nav.Children.Add(next);
        _body.Children.Add(nav);

        if (_report is not null && !_report.CanBuild)
        {
            _body.Children.Add(Card(Hint(
                "上面列出的问题会阻止生成可用的 EFI。如果确实想继续尝试，可以在「高级选项」里勾选“跳过兼容性检查”，但生成的 EFI 很可能无法启动。")));
        }

        _body.Children.Add(advanced);

        if (_hardware is not null)
        {
            _body.Children.Add(new Expander
            {
                Header = "这台电脑的硬件配置",
                IsExpanded = false,
                Content = BuildFactPanel(_hardware)
            });
        }
    }

    private void RenderBuildStep()
    {
        _body.Children.Add(SectionTitle("第 2 步 · 生成 EFI", "组件已经内置在插件里，不用装 Python 或下载引擎。点一下就会自动生成，并用 OpenCore 官方工具校验。"));
        _body.Children.Add(_notice);

        if (_hardware is null || _report is null)
        {
            _body.Children.Add(Card(new TextBlock { Text = "请先回到第 1 步，完成硬件检查。", TextWrapping = TextWrapping.Wrap }));
            AddBackButton();
            return;
        }
        if (!_report.CanBuild)
        {
            _body.Children.Add(Card(new TextBlock
            {
                Text = "这台电脑还有兼容性问题，暂时不能生成 EFI：\n" + string.Join("\n", _report.Errors),
                TextWrapping = TextWrapping.Wrap
            }));
            AddBackButton();
            return;
        }

        if (_build is not null)
        {
            var panel = new StackPanel { Spacing = 10 };
            panel.Children.Add(new TextBlock
            {
                Text = _build.Success ? "EFI 生成成功" : "EFI 生成失败",
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Foreground = _build.Success ? Brush("SystemFillColorSuccessBrush") : Brush("SystemFillColorCriticalBrush")
            });
            panel.Children.Add(TextLine(_build.Success
                ? "接下来设置 BIOS，然后制作安装 U 盘。"
                : "请看下面的原因，修正后可以重新生成。"));

            if (_build.Success)
            {
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var nextBios = PrimaryButton("下一步：设置 BIOS");
                nextBios.Click += (_, _) => { _step = 2; RenderStep(); };
                buttons.Children.Add(nextBios);
                var openEfi = new Button { Content = "打开 EFI 文件夹" };
                openEfi.Click += (_, _) => OpenPath(_build.EfiDirectory);
                var export = new Button { Content = "导出 EFI…" };
                export.Click += async (_, _) => await ExportEfiAsync();
                buttons.Children.Add(openEfi);
                buttons.Children.Add(export);
                panel.Children.Add(buttons);
            }

            var details = new StackPanel { Spacing = 6 };
            details.Children.Add(TextLine($"机型：{_build.Model}"));
            details.Children.Add(TextLine($"序列号：{_build.Serial}"));
            details.Children.Add(TextLine($"MLB：{_build.Mlb}"));
            details.Children.Add(TextLine($"EFI：{_build.EfiDirectory}"));
            details.Children.Add(TextLine($"ocvalidate：{(_build.Success ? "通过" : "失败")}"));
            foreach (string error in _build.ValidationErrors)
                details.Children.Add(TextLine("· " + error, critical: true));
            foreach (string warning in _build.Warnings)
                details.Children.Add(TextLine("· " + warning));
            var openConfig = new Button { Content = "查看 config.plist" };
            openConfig.Click += (_, _) => OpenPath(_build.ConfigPath);
            details.Children.Add(openConfig);
            panel.Children.Add(new Expander
            {
                Header = "生成结果详情（序列号 / 校验 / 警告）",
                IsExpanded = !_build.Success,
                Content = details
            });
            _body.Children.Add(Card(panel));
        }

        var summary = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "已经按这台电脑选好", FontSize = 17, FontWeight = FontWeights.SemiBold },
                TextLine($"建议模拟机型：{_report.Model}"),
                TextLine($"安装版本：{_target.DisplayName}"),
                TextLine($"显示输出：{DisplayText(_report)}"),
                Hint("机型、驱动和启动参数都会自动写入 EFI；你不需要手动改文件。")
            }
        };
        var technical = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                TextLine("Kext：" + string.Join("、", _report.Kexts)),
                TextLine("ACPI：" + string.Join("、", _report.AcpiFiles))
            }
        };
        if (!_targetExplicit)
            summary.Children.Add(Hint("安装版本已按硬件自动选择；想换可在第 1 步高级选项里改。"));
        summary.Children.Add(new Expander
        {
            Header = "技术细节（kext / ACPI）",
            IsExpanded = false,
            Content = technical
        });
        _body.Children.Add(Card(summary));

        var buildButton = PrimaryButton(_build?.Success == true ? "重新生成 EFI" : "一键生成 EFI", 240);
        buildButton.Click += async (_, _) => await BuildEfiAsync();
        _body.Children.Add(buildButton);
        _body.Children.Add(Hint("第一次生成会解压内置组件，通常 10–30 秒；以后会使用缓存。"));

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var back = new Button { Content = "上一步" };
        back.Click += (_, _) => { _step = 0; RenderStep(); };
        var next = PrimaryButton("下一步：设置 BIOS");
        next.IsEnabled = _build?.Success == true;
        next.Click += (_, _) => { _step = 2; RenderStep(); };
        nav.Children.Add(back);
        nav.Children.Add(next);
        _body.Children.Add(nav);
    }

    private void RenderBiosStep()
    {
        _body.Children.Add(SectionTitle("第 3 步 · 设置 BIOS", "打开图文指南，照着改几项开关即可。不同品牌 BIOS 的叫法略有不同，找意思相同的选项。"));
        _body.Children.Add(_notice);

        if (_build?.Success != true)
        {
            _body.Children.Add(Card(new TextBlock { Text = "请先在第 2 步生成可用的 EFI。", TextWrapping = TextWrapping.Wrap }));
            AddBackButton();
            return;
        }

        var open = PrimaryButton("打开图文 BIOS 设置指南", 280);
        open.Click += async (_, _) => await OpenBiosGuideAsync();
        _body.Children.Add(Card(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Hint("指南会按你的主板和显卡生成，包含进入 BIOS 的按键、需要关闭的安全选项和启动顺序。"),
                open
            }
        }));

        var list = new StackPanel { Spacing = 6 };
        foreach (string item in new[]
        {
            "关闭 Secure Boot 与 CSM，开启 Above 4G Decoding。",
            "关闭 VT-d / IOMMU；SATA 使用 AHCI；关闭 Intel VMD 与 RAID。",
            "关闭 Fast Boot；如果 BIOS 有 CFG Lock，也一并关闭。",
            "从后置 USB 接口启动 U 盘，选择 OpenCore 菜单里的 Install macOS。",
            "安装完成后把 U 盘里的 EFI 复制到装 macOS 那块 SSD 的 EFI 分区，不要覆盖 Windows 盘。"
        })
            list.Children.Add(TextLine("• " + item));
        _body.Children.Add(new Expander
        {
            Header = "先记住这 5 条（点开看）",
            IsExpanded = false,
            Content = list
        });

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var back = new Button { Content = "上一步" };
        back.Click += (_, _) => { _step = 1; RenderStep(); };
        var next = PrimaryButton("下一步：制作安装 U 盘");
        next.Click += (_, _) => { _step = 3; RenderStep(); };
        nav.Children.Add(back);
        nav.Children.Add(next);
        _body.Children.Add(nav);
    }

    private async Task ScanAsync()
    {
        await RunOperationAsync("正在扫描硬件…", allowCancel: true, async ct =>
        {
            _hardware = await HardwareScanner.ScanAsync(_runner, AppendLog, ct);
            _report = CompatibilityRules.Evaluate(_hardware, _target, Options());
            if (!_targetExplicit && _report.CpuClass == "modern")
            {
                _target = MacTarget.Tahoe;
                _report = CompatibilityRules.Evaluate(_hardware, _target, Options());
            }
            _build = null;
            AppendLog($"硬件：{_hardware.CpuName} / {_hardware.BoardManufacturer} {_hardware.BoardProduct}");
            AppendLog($"显卡：{string.Join("、", _hardware.Gpus.Select(g => g.Name))}");
            _notice.IsOpen = false;
        });
    }

    private async Task BuildEfiAsync()
    {
        if (_report is null) return;
        await RunOperationAsync("正在构建 EFI…", allowCancel: true, async ct =>
        {
            var payload = new PayloadManager(Path.Combine(_ctx.Services.PackageDirectory, "assets", "payload"), _ctx.Services.DataDirectory);
            var outcome = await EfiBuilder.BuildAsync(
                payload, _hardware!, _report, _target, Options(), _runner, _ctx.Services.DataDirectory,
                AppendLog,
                status => _dispatcher.TryEnqueue(() => _statusText.Text = status),
                ct);
            _build = outcome;
            if (!outcome.Success)
            {
                ShowError("EFI 构建未通过", outcome.ValidationErrors.Count > 0
                    ? string.Join("\n", outcome.ValidationErrors.Take(6))
                    : outcome.RawValidation);
            }
        });
    }

    private async Task ExportEfiAsync()
    {
        if (_build?.Success != true) return;
        BuildOutcome build = _build;
        string? folder = await _ctx.Services.PickFolderAsync("选择 EFI 导出位置", CancellationToken.None);
        if (folder is null) return;
        string destination = Path.Combine(folder, "EFI");
        if (Directory.Exists(destination))
        {
            bool ok = await AskConfirmAsync("覆盖已有 EFI？", $"目标目录已存在：\n{destination}\n\n继续会删除该目录后重新写入。");
            if (!ok) return;
            try { Directory.Delete(destination, true); } catch (Exception ex)
            {
                ShowError("无法覆盖目标目录", ex.Message);
                return;
            }
        }

        await RunOperationAsync("正在导出 EFI…", allowCancel: false, async ct =>
        {
            await Task.Run(() => CopyDirectory(build.EfiDirectory, destination), ct);
            AppendLog("EFI 已导出到 " + destination);
            ShowInfo("EFI 已导出", destination);
        });
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (string dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private async Task OpenBiosGuideAsync()
    {
        if (_build is null || _report is null || _hardware is null) return;
        string path = Path.Combine(_ctx.Services.DataDirectory, "build", "BIOS.html");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, BiosGuide.BuildHtml(_hardware, _report, _build, _target), new UTF8Encoding(true));
        OpenPath(path, "BIOS 指南已生成：");
    }

    private BuildOptions Options() => new()
    {
        AudioLayout = _audioLayout,
        ForceUnsupported = _forceUnsupported,
        VerboseBoot = _verboseBoot
    };

    private void RefreshReport()
    {
        _build = null;
        if (_hardware is not null)
            _report = CompatibilityRules.Evaluate(_hardware, _target, Options());
    }

    private void AddBackButton()
    {
        var back = new Button { Content = "返回上一步" };
        back.Click += (_, _) => { _step = Math.Max(0, _step - 1); RenderStep(); };
        _body.Children.Add(back);
    }

    private async Task RunOperationAsync(string status, bool allowCancel, Func<CancellationToken, Task> action)
    {
        if (_busy) return;
        _busy = true;
        _activeCts = new CancellationTokenSource();
        CancellationTokenRegistration registration = default;
        if (allowCancel && !_ctx.CancellationToken.IsCancellationRequested)
            registration = _ctx.CancellationToken.Register(() => _activeCts?.Cancel());

        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = true;
        _statusText.Text = status;
        _cancel.Visibility = allowCancel ? Visibility.Visible : Visibility.Collapsed;
        _statusCard.Visibility = Visibility.Visible;
        _body.IsHitTestVisible = false;
        _stepsBar.IsHitTestVisible = false;
        _ctx.ReportProgress(status);

        try
        {
            await action(_activeCts.Token);
        }
        catch (OperationCanceledException)
        {
            ShowInfo("已取消", "当前操作已停止。");
            AppendLog("! 用户取消了操作。");
        }
        catch (Exception ex)
        {
            ShowError("操作失败", ex.Message);
            AppendLog("! " + ex);
        }
        finally
        {
            registration.Dispose();
            _activeCts.Dispose();
            _activeCts = null;
            _busy = false;
            _progress.Visibility = Visibility.Collapsed;
            _statusText.Text = "";
            _cancel.Visibility = Visibility.Collapsed;
            _statusCard.Visibility = Visibility.Collapsed;
            _body.IsHitTestVisible = true;
            _stepsBar.IsHitTestVisible = true;
            _ctx.ReportProgress("");
            RenderStep();
        }
    }

    private void ShowInfo(string title, string message)
    {
        _notice.Severity = InfoBarSeverity.Informational;
        _notice.Title = title;
        _notice.Message = message;
        _notice.IsOpen = true;
    }

    private void ShowError(string title, string message)
    {
        _notice.Severity = InfoBarSeverity.Error;
        _notice.Title = title;
        _notice.Message = message;
        _notice.IsOpen = true;
    }

    private async Task<bool> AskConfirmAsync(string title, string message)
    {
        try
        {
            return await _ctx.ConfirmAsync(title, message);
        }
        catch (Exception ex)
        {
            AppendLog("! 无法显示确认对话框：" + ex.Message);
            ShowError("需要确认", "宿主没有提供确认对话框，为避免误删数据，操作已停止。");
            return false;
        }
    }

    private void AppendLog(string line)
    {
        lock (_logGate)
        {
            _pendingLog.AppendLine(line);
            if (_pendingLog.Length > 40000)
                _pendingLog.Remove(0, _pendingLog.Length - 30000);
        }
    }

    private void FlushLog()
    {
        if (_closed) return;
        string text;
        lock (_logGate)
        {
            if (_pendingLog.Length == 0) return;
            text = _pendingLog.ToString();
            _pendingLog.Clear();
        }
        string current = _logBox.Text;
        if (current.Length > 20000)
            current = current[^18000..];
        _logBox.Text = current + text;
        _logBox.SelectionStart = _logBox.Text.Length;
    }

    private void SaveSetting(string key, string value) => _ = _ctx.Services.SetSettingAsync(key, value);

    private static TextBlock SectionTitle(string title, string subtitle) => new()
    {
        Text = title + "\n" + subtitle,
        FontSize = 18,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 2)
    };

    private Button PrimaryButton(string text, double minWidth = 0)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(22, 12, 22, 12),
            MinHeight = 44,
            FontSize = 15
        };
        if (minWidth > 0) button.MinWidth = minWidth;
        try
        {
            if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out object value) && value is Style style)
                button.Style = style;
        }
        catch
        {
        }
        return button;
    }

    private TextBlock Hint(string text) => new()
    {
        Text = text,
        Foreground = Brush("TextFillColorSecondaryBrush"),
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12
    };

    private async Task AutoScanAsync()
    {
        if (_autoScanStarted) return;
        _autoScanStarted = true;
        await Task.Delay(600);
        if (_closed || _busy || _hardware is not null) return;
        await ScanAsync();
    }

    private TextBlock TextLine(string text, bool critical = false) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = critical ? Brush("SystemFillColorCriticalBrush") : Brush("TextFillColorPrimaryBrush")
    };

    private Border Card(UIElement child) => new()
    {
        Background = Brush("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = Brush("ControlStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16),
        Child = child
    };

    private Brush Brush(string key)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out object value) && value is Brush brush)
                return brush;
        }
        catch
        {
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    private UIElement BuildFactPanel(HardwareProfile hw)
    {
        var panel = new StackPanel { Spacing = 8 };
        AddFact(panel, "机器", $"{hw.ComputerManufacturer} {hw.ComputerModel}".Trim());
        AddFact(panel, "处理器", $"{hw.CpuName}（{hw.CpuCores} 核 / {hw.CpuThreads} 线程）");
        AddFact(panel, "主板", $"{hw.BoardManufacturer} {hw.BoardProduct} {hw.BoardVersion}".Trim());
        AddFact(panel, "BIOS", $"{hw.BiosVendor} {hw.BiosVersion}".Trim());
        AddFact(panel, "显卡", string.Join("、", hw.Gpus.Select(g => g.Name)) is { Length: > 0 } g ? g : "未识别");
        AddFact(panel, "网卡", string.Join("、", hw.Nics.Select(n => n.Name)) is { Length: > 0 } n ? n : "未识别");
        AddFact(panel, "存储", string.Join("、", hw.Disks.Select(d => $"{d.Model}（{FormatSize(d.Size)}）")) is { Length: > 0 } s ? s : "未识别");
        AddFact(panel, "内存", FormatSize((long)hw.TotalMemory));
        return panel;
    }

    private void AddFact(StackPanel panel, string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = label, Foreground = Brush("TextFillColorSecondaryBrush") };
        var v = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(v, 1);
        grid.Children.Add(l);
        grid.Children.Add(v);
        panel.Children.Add(grid);
    }

    private static string DisplayText(CompatibilityReport r) =>
        r.UseIgpu ? "Intel 核显"
        : r.HasSupportedDgpu ? "AMD 独显"
        : r.BasicDisplayOnly ? "基础显示（无图形加速）"
        : "未识别";

    private UIElement BuildCompatibilityCard(CompatibilityReport r)
    {
        var panel = new StackPanel { Spacing = 8 };
        bool canContinue = r.CanBuild;
        panel.Children.Add(new TextBlock
        {
            Text = canContinue
                ? (r.BasicDisplayOnly ? "可以继续，但只能用基础显示" : r.Warnings.Count > 0 ? "可以继续，但有几项要注意" : "这台电脑可以继续")
                : "这台电脑暂时不能直接安装",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = canContinue ? Brush("SystemFillColorSuccessBrush") : Brush("SystemFillColorCriticalBrush")
        });
        panel.Children.Add(TextLine(canContinue
            ? r.BasicDisplayOnly
                ? "没有图形加速：macOS 只能用固件提供的基础显示，桌面和 App 能打开，但可能低分辨率、没有动画和游戏加速。"
                : "接下来会自动生成 EFI。下面的注意事项不一定会遇到，但提前知道更稳妥。"
            : "先按下面列出的原因处理。如果处理不了，再看看高级选项里的“跳过兼容性检查”。"));

        if (r.BasicDisplayOnly)
        {
            var plan = new StackPanel { Spacing = 4 };
            plan.Children.Add(new TextBlock
            {
                Text = "建议这样装，避免动到 Windows：",
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            plan.Children.Add(TextLine("1. 准备一块 macOS 能识别的外接 SSD，或另一块不在 VMD 后面的内置 SATA/NVMe 硬盘。"));
            plan.Children.Add(TextLine("2. 从 U 盘启动安装，只抹掉这块 SSD；内置硬盘和 Windows 分区不要动。"));
            plan.Children.Add(TextLine("3. 装完继续用 U 盘启动，或把 EFI 放到这块 SSD 的 EFI 分区。"));
            panel.Children.Add(plan);
        }

        if (r.Errors.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "需要先处理的问题",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0)
            });
            foreach (string error in r.Errors)
                panel.Children.Add(TextLine("• " + error, critical: true));
        }

        if (r.Warnings.Count > 0)
        {
            var warnings = new StackPanel { Spacing = 6 };
            foreach (string warning in r.Warnings)
                warnings.Children.Add(TextLine("• " + warning));
            panel.Children.Add(new Expander
            {
                Header = $"注意事项（{r.Warnings.Count} 项，不影响继续）",
                IsExpanded = false,
                Content = warnings
            });
        }

        var details = new StackPanel { Spacing = 6 };
        details.Children.Add(TextLine($"建议模拟机型：{r.Model}"));
        details.Children.Add(TextLine($"主板标识：{r.BoardId}"));
        details.Children.Add(TextLine($"显示输出：{DisplayText(r)}"));
        details.Children.Add(TextLine("Kext：" + string.Join("、", r.Kexts)));
        details.Children.Add(TextLine("ACPI：" + string.Join("、", r.AcpiFiles)));
        foreach (string note in r.Notes)
            details.Children.Add(TextLine("· " + note));
        panel.Children.Add(new Expander
        {
            Header = "技术细节（机型 / kext / ACPI）",
            IsExpanded = false,
            Content = details
        });
        return Card(panel);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "未知";
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.00} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.0} MB";
        return $"{bytes / 1024.0:0.0} KB";
    }

    private static void OpenPath(string path, string? log = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
