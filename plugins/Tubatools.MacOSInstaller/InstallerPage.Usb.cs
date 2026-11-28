using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.PluginSdk;
using Tubatools.MacOSInstaller.Core;

namespace Tubatools.MacOSInstaller;

internal sealed partial class InstallerPage
{
    private readonly List<UsbDisk> _disks = [];
    private UsbDisk? _selectedDisk;
    private bool _withRecovery = true;
    private string _proxy = "";
    private string _writtenVolume = "";
    private readonly List<string> _diagLogs = [];
    private DiagnosticResult? _diagResult;
    private string _efiCheckResult = "";
    private string _efiCheckPath = "";

    private void RenderUsbStep()
    {
        _body.Children.Add(SectionTitle("第 4 步 · 制作安装 U 盘", "准备一个至少 2GB 的 U 盘（建议 8GB 以上）。制作会清空它，请先把里面的文件备份到电脑。"));
        _body.Children.Add(_notice);

        if (_build?.Success != true)
        {
            _body.Children.Add(Card(new TextBlock { Text = "请先在第 2 步生成可用的 EFI。", TextWrapping = TextWrapping.Wrap }));
            AddBackButton();
            return;
        }

        var refresh = new Button { Content = "重新查找 U 盘", Padding = new Thickness(14, 8, 14, 8) };
        refresh.Click += async (_, _) => await RefreshDisksAsync();

        var primaryRefresh = PrimaryButton("插入好了，重新查找 U 盘", 260);
        primaryRefresh.Click += async (_, _) => await RefreshDisksAsync();

        var diskCombo = new ComboBox { MinWidth = 420, PlaceholderText = "选择要制作的 U 盘" };
        foreach (UsbDisk disk in _disks)
        {
            bool enoughForEfi = disk.Size >= 2UL * 1024 * 1024 * 1024;
            diskCombo.Items.Add(new ComboBoxItem
            {
                Content = disk.Display + (enoughForEfi ? "" : " · 容量不足"),
                Tag = disk.Number,
                IsEnabled = enoughForEfi && !(disk.IsBoot || disk.IsSystem || disk.IsReadOnly || disk.IsOffline)
            });
        }
        if (_selectedDisk is not null)
        {
            for (int i = 0; i < diskCombo.Items.Count; i++)
            {
                if ((int?)(diskCombo.Items[i] as ComboBoxItem)?.Tag == _selectedDisk.Number)
                {
                    diskCombo.SelectedIndex = i;
                    break;
                }
            }
        }
        diskCombo.SelectionChanged += (_, _) =>
        {
            int? number = (diskCombo.SelectedItem as ComboBoxItem)?.Tag as int?;
            _selectedDisk = _disks.FirstOrDefault(d => d.Number == number);
            RenderStep();
        };

        var recovery = new CheckBox
        {
            Content = "同时下载并写入 macOS 安装镜像（推荐）",
            IsChecked = _withRecovery
        };
        recovery.Checked += (_, _) => _withRecovery = true;
        recovery.Unchecked += (_, _) => _withRecovery = false;

        var write = PrimaryButton("开始制作安装 U 盘", 280);
        write.IsEnabled = _selectedDisk is not null;
        write.Click += async (_, _) => await WriteUsbAsync();

        var prepare = new StackPanel { Spacing = 10 };
        prepare.Children.Add(new TextBlock
        {
            Text = _disks.Count == 0 ? "还没有找到 U 盘" : "已找到 U 盘",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold
        });
        if (_disks.Count == 0)
        {
            prepare.Children.Add(Hint("把 U 盘插到电脑的 USB 接口，等几秒让系统识别，再点下面的按钮。程序只会列出 U 盘，不会动你的硬盘。"));
            prepare.Children.Add(primaryRefresh);
        }
        else
        {
            prepare.Children.Add(diskCombo);
            prepare.Children.Add(refresh);
            prepare.Children.Add(recovery);
            prepare.Children.Add(Hint("安装镜像约 0.8～1.5 GB，需要联网下载；下载失败可以在下面的高级选项里填代理。"));
            if (_selectedDisk is not null)
                prepare.Children.Add(Hint($"将写入：{_selectedDisk.Display}（{_selectedDisk.SizeText}）。这块盘上的文件会被全部删除。"));
            prepare.Children.Add(write);
            prepare.Children.Add(Hint("制作过程大约 3–10 分钟，期间不要拔 U 盘，也不要关闭工具箱。"));
        }
        _body.Children.Add(Card(prepare));

        var proxyBox = new TextBox
        {
            Text = _proxy,
            PlaceholderText = "可选，例如 http://127.0.0.1:7890；留空则直连",
            MinWidth = 420
        };
        proxyBox.TextChanged += (_, _) =>
        {
            _proxy = proxyBox.Text.Trim();
            SaveSetting("proxy", _proxy);
        };

        var advancedContent = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "下载代理（可选）", Foreground = Brush("TextFillColorSecondaryBrush") },
                proxyBox,
                Hint("只有 Apple 恢复镜像下载失败时才需要填。普通网络可以先留空。")
            }
        };
        if (_selectedDisk is not null)
        {
            advancedContent.Children.Add(TextLine($"磁盘 {_selectedDisk.Number}：{_selectedDisk.Name}"));
            advancedContent.Children.Add(TextLine($"序列号：{_selectedDisk.Serial}"));
            advancedContent.Children.Add(TextLine($"唯一 ID：{_selectedDisk.UniqueId}"));
            advancedContent.Children.Add(TextLine("写入前会重新核对容量和序列号；如果这块盘被换掉，程序会停止。"));
        }
        var advanced = new Expander
        {
            Header = "高级选项（代理 / 磁盘校验）",
            IsExpanded = _advancedExpanded,
            Content = advancedContent
        };
        advanced.Expanding += (_, _) => _advancedExpanded = true;
        advanced.Collapsed += (_, _) => _advancedExpanded = false;
        _body.Children.Add(advanced);

        if (_writtenVolume.Length > 0)
        {
            var done = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "U 盘制作完成", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("SystemFillColorSuccessBrush") },
                    TextLine($"盘符：{_writtenVolume}"),
                    TextLine("把 U 盘插到要安装 macOS 的电脑上，按 BIOS 指南从 U 盘启动，再选择 OpenCore 菜单里的 Install macOS。"),
                    TextLine("安装时只选准备装 macOS 的那块 SSD，不要抹掉内置硬盘和 Windows 分区。"),
                    TextLine("装完把 U 盘里的 EFI 放到这块 SSD 的 EFI 分区，或继续用 U 盘启动。")
                }
            };
            var open = new Button { Content = "打开 U 盘" };
            open.Click += (_, _) => OpenPath(_writtenVolume);
            var guide = new Button { Content = "再看一遍 BIOS 指南" };
            guide.Click += async (_, _) => await OpenBiosGuideAsync();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { open, guide } };
            done.Children.Add(buttons);
            _body.Children.Add(Card(done));
        }

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var back = new Button { Content = "上一步" };
        back.Click += (_, _) => { _step = 2; RenderStep(); };
        var tools = new Button { Content = "启动失败？打开诊断工具" };
        tools.Click += (_, _) => { _step = 4; RenderStep(); };
        nav.Children.Add(back);
        nav.Children.Add(tools);
        _body.Children.Add(nav);

        if (!_usbAutoScanStarted)
        {
            _usbAutoScanStarted = true;
            _dispatcher.TryEnqueue(async () =>
            {
                await Task.Delay(500);
                if (!_closed && !_busy && _disks.Count == 0)
                    await RefreshDisksAsync();
            });
        }
    }

    private async Task RefreshDisksAsync()
    {
        await RunOperationAsync("正在枚举 USB 磁盘…", allowCancel: false, async ct =>
        {
            List<UsbDisk> list = await UsbService.ListAsync(_runner, AppendLog, ct);
            _disks.Clear();
            _disks.AddRange(list);
            _selectedDisk = list.FirstOrDefault(d => d.Size >= 2UL * 1024 * 1024 * 1024
                && !(d.IsBoot || d.IsSystem || d.IsReadOnly || d.IsOffline));
            AppendLog($"找到 {list.Count} 块 USB 磁盘。");
            _notice.IsOpen = false;
        });
    }

    private async Task WriteUsbAsync()
    {
        if (_selectedDisk is null || _build is null) return;
        UsbDisk disk = _selectedDisk;

        bool ok = await AskConfirmAsync(
            "要清空这个 U 盘吗？",
            $"已选择：磁盘 {disk.Number}：{disk.Name}（{disk.SizeText}）\n\n" +
            "制作安装 U 盘会删除这块盘上的所有文件，并重新格式化。\n" +
            "请确认它是 U 盘，不是移动硬盘、系统盘或你还要用的存储盘。");
        if (!ok) return;

        ok = await AskConfirmAsync(
            "开始制作？",
            "点「确定」后开始写入，过程中请不要拔 U 盘、不要关闭工具箱。\n" +
            "制作需要几分钟，完成后会提示你。");
        if (!ok) return;

        string recoveryDir = Path.Combine(_ctx.Services.DataDirectory, "recovery", _target.Darwin);
        string recoveryPath = "";

        if (_withRecovery)
        {
            bool cacheValid = false;
            bool checkedCache = false;
            await RunOperationAsync("正在检查恢复镜像缓存…", allowCancel: true, async ct =>
            {
                cacheValid = await RecoveryImageService.VerifyCacheAsync(_target, recoveryDir, ct);
                checkedCache = true;
            });
            if (!checkedCache) return;

            if (!cacheValid)
            {
                bool? downloaded = await RunOperationWithResultAsync("正在下载 macOS 恢复镜像…", allowCancel: true, async ct =>
                {
                    RecoveryDownloadResult result = await RecoveryImageService.DownloadAsync(
                        _target, recoveryDir, AppendLog, DispatchStatus, _proxy, ct);
                    if (!result.Success) throw new InvalidOperationException(result.Error);
                    return true;
                });
                if (downloaded != true) return;
            }
            else
            {
                AppendLog("使用已缓存且校验通过的恢复镜像：" + recoveryDir);
            }
            recoveryPath = recoveryDir;
        }

        await RunOperationAsync("正在写入安装 U 盘…", allowCancel: false, async ct =>
        {
            _cancel.Visibility = Visibility.Collapsed;
            UsbWriteResult result = await UsbService.WriteAsync(
                _runner, disk, _build.EfiDirectory, recoveryPath, _ctx.Services.DataDirectory,
                AppendLog, DispatchStatus, ct);
            if (!result.Success) throw new InvalidOperationException(result.Error);
            _writtenVolume = result.VolumeRoot;
            ShowInfo("U 盘已制作完成", $"盘符 {result.VolumeRoot}。现在可以按 BIOS 指南从 U 盘启动。");
        });
    }

    private void RenderToolsStep()
    {
        _body.Children.Add(SectionTitle("遇到问题？", "如果卡在苹果 Logo、黑屏、重启或出现禁止符号，先按下面的顺序排查；大多数启动失败都能找到原因。"));
        _body.Children.Add(_notice);

        var diagCard = new StackPanel { Spacing = 8 };
        diagCard.Children.Add(new TextBlock { Text = "启动失败诊断", FontSize = 17, FontWeight = FontWeights.SemiBold });
        diagCard.Children.Add(new TextBlock
        {
            Text = "把制作好的 U 盘插回这台电脑，点「查找 U 盘日志」；程序会读取 opencore-*.txt、panic-*.txt，并结合屏幕最后一行给出修复步骤。",
            Foreground = Brush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        var find = new Button { Content = "查找 U 盘日志" };
        find.Click += async (_, _) => await FindLogsAsync();
        var pickFile = new Button { Content = "选择日志文件" };
        pickFile.Click += async (_, _) => await PickLogFileAsync();
        var pickFolder = new Button { Content = "选择日志文件夹" };
        pickFolder.Click += async (_, _) => await PickLogFolderAsync();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { find, pickFile, pickFolder } };
        diagCard.Children.Add(buttons);
        diagCard.Children.Add(TextLine(_diagLogs.Count > 0 ? "已找到：" + string.Join("、", _diagLogs.Select(Path.GetFileName)) : "还没有选择日志；不选日志也可以，只填屏幕最后一行就能分析。"));

        var stopped = new TextBox { PlaceholderText = "屏幕最后一行文字，例如 EXITBS:START 或 Still waiting for root device" };
        diagCard.Children.Add(stopped);
        var analyze = PrimaryButton("开始分析");
        analyze.HorizontalAlignment = HorizontalAlignment.Left;
        analyze.Click += async (_, _) =>
        {
            string stoppedLine = stopped.Text;
            await RunOperationAsync("正在分析启动日志…", allowCancel: false, async ct =>
            {
                _diagResult = await Task.Run(
                    () => Diagnostics.Analyze(_diagLogs, stoppedLine, _hardware?.CpuName ?? ""), ct);
                AppendLog($"诊断完成，共 {_diagResult.Findings.Count} 项。");
            });
        };
        diagCard.Children.Add(analyze);
        _body.Children.Add(Card(diagCard));

        if (_diagResult is not null)
        {
            var results = new StackPanel { Spacing = 10 };
            results.Children.Add(new TextBlock
            {
                Text = $"诊断结果：{_diagResult.Findings.Count} 项",
                FontSize = 17,
                FontWeight = FontWeights.SemiBold
            });
            foreach (DiagnosticFinding finding in _diagResult.Findings)
            {
                var panel = new StackPanel { Spacing = 6 };
                panel.Children.Add(TextLine(finding.Title, critical: true));
                panel.Children.Add(TextLine(finding.Cause));
                for (int i = 0; i < finding.Steps.Count; i++)
                    panel.Children.Add(TextLine($"{i + 1}. {finding.Steps[i]}"));
                results.Children.Add(Card(panel));
            }
            foreach (string note in _diagResult.Notes)
                results.Children.Add(TextLine(note));
            _body.Children.Add(results);
        }

        var efiCard = new StackPanel { Spacing = 8 };
        efiCard.Children.Add(new TextBlock { Text = "EFI 体检", FontSize = 17, FontWeight = FontWeights.SemiBold });
        efiCard.Children.Add(new TextBlock
        {
            Text = "选择别人做的 EFI 文件夹，用 OpenCore 官方的 ocvalidate 检查 config.plist。",
            Foreground = Brush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        var check = new Button { Content = "选择 EFI 并校验" };
        check.Click += async (_, _) => await CheckEfiAsync();
        efiCard.Children.Add(check);
        if (_efiCheckResult.Length > 0)
            efiCard.Children.Add(Card(new TextBlock { Text = _efiCheckResult, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas") }));
        _body.Children.Add(Card(efiCard));

        var dataCard = new StackPanel { Spacing = 8 };
        dataCard.Children.Add(new TextBlock { Text = "数据与组件", FontSize = 17, FontWeight = FontWeights.SemiBold });
        dataCard.Children.Add(TextLine("数据目录：" + _ctx.Services.DataDirectory));
        dataCard.Children.Add(TextLine("内置组件：OpenCorePkg、Lilu、VirtualSMC、WhateverGreen、AppleALC、NVMeFix、IntelMausi、RealtekRTL8111、RestrictEvents、VoodooPS2Controller。"));
        dataCard.Children.Add(TextLine("这些组件分别遵循其上游许可证（BSD-3-Clause / MIT 等）；本插件不包含任何 Apple 系统文件，恢复镜像在制作时从 Apple 服务器下载。"));
        var openData = new Button { Content = "打开数据目录" };
        openData.Click += (_, _) => OpenPath(_ctx.Services.DataDirectory);
        var openLogs = new Button { Content = "打开日志目录" };
        openLogs.Click += (_, _) => OpenPath(Path.Combine(_ctx.Services.DataDirectory, "logs"));
        var clean = new Button { Content = "清理构建与恢复镜像" };
        clean.Click += async (_, _) => await CleanAsync();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { openData, openLogs, clean } };
        dataCard.Children.Add(row);

        var about = new StackPanel { Spacing = 6 };
        about.Children.Add(new TextBlock { Text = "关于", FontSize = 17, FontWeight = FontWeights.SemiBold });
        about.Children.Add(TextLine("macOS 安装助手是图吧工具箱的原生 C# 插件：界面、流程、下载校验与磁盘操作都在插件内完成，不依赖外部安装器。"));
        about.Children.Add(TextLine("OpenCore 官方文档：https://dortania.github.io/OpenCore-Install-Guide/"));
        var link = new Button { Content = "打开 OpenCore 安装指南" };
        link.Click += async (_, _) => await _ctx.Services.OpenUriAsync(new Uri("https://dortania.github.io/OpenCore-Install-Guide/"));
        about.Children.Add(link);

        var more = new StackPanel { Spacing = 12, Children = { dataCard, about } };
        _body.Children.Add(new Expander
        {
            Header = "更多信息（数据目录 / 组件 / 关于）",
            IsExpanded = false,
            Content = Card(more)
        });
    }

    private async Task FindLogsAsync()
    {
        await RunOperationAsync("正在查找启动日志…", allowCancel: false, async ct =>
        {
            _diagLogs.Clear();
            _diagLogs.AddRange(await Task.Run(Diagnostics.FindLogs, ct));
            AppendLog($"找到 {_diagLogs.Count} 个日志文件。");
            if (_diagLogs.Count == 0)
                ShowError("没有找到日志", "请确认 U 盘还插在电脑上，或手动选择日志文件/文件夹。");
        });
    }

    private async Task PickLogFileAsync()
    {
        string? file = await _ctx.Services.PickOpenFileAsync("选择 opencore 或 panic 日志", CancellationToken.None);
        if (file is null) return;
        if (!_diagLogs.Contains(file)) _diagLogs.Add(file);
        RenderStep();
    }

    private async Task PickLogFolderAsync()
    {
        string? folder = await _ctx.Services.PickFolderAsync("选择包含启动日志的文件夹", CancellationToken.None);
        if (folder is null) return;
        try
        {
            foreach (string pattern in new[] { "opencore-*.txt", "panic-*.txt" })
                foreach (string file in Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly))
                    if (!_diagLogs.Contains(file)) _diagLogs.Add(file);
        }
        catch (Exception ex)
        {
            ShowError("读取文件夹失败", ex.Message);
        }
        RenderStep();
    }

    private async Task CheckEfiAsync()
    {
        string? folder = await _ctx.Services.PickFolderAsync("选择 EFI 文件夹", CancellationToken.None);
        if (folder is null) return;

        await RunOperationAsync("正在校验 EFI…", allowCancel: true, async ct =>
        {
            string? config = await Task.Run(() => FindConfig(folder), ct);
            if (config is null) throw new InvalidOperationException("没有找到 config.plist。请选择 EFI 文件夹或 OC 文件夹。");
            _efiCheckPath = folder;
            var payload = new PayloadManager(Path.Combine(_ctx.Services.PackageDirectory, "assets", "payload"), _ctx.Services.DataDirectory);
            await payload.EnsureAsync(AppendLog, ct);
            var result = await _runner.RunAsync(payload.Ocvalidate, [config], payload.OpenCoreRoot, "ocvalidate", false, true, AppendLog, ct);
            _efiCheckResult = (result.Output + "\n" + result.StdErr).Trim();
            if (string.IsNullOrWhiteSpace(_efiCheckResult))
                _efiCheckResult = result.Success ? "ocvalidate：没有发现问题。" : "ocvalidate 返回失败。";
            if (!result.Success) ShowError("EFI 校验发现问题", _efiCheckResult);
            else ShowInfo("EFI 校验通过", "ocvalidate 没有发现问题。");
        });
    }

    private static string? FindConfig(string folder)
    {
        string direct = Path.Combine(folder, "config.plist");
        if (File.Exists(direct)) return direct;
        string oc = Path.Combine(folder, "OC", "config.plist");
        if (File.Exists(oc)) return oc;
        string efiOc = Path.Combine(folder, "EFI", "OC", "config.plist");
        if (File.Exists(efiOc)) return efiOc;
        try
        {
            return Directory.EnumerateFiles(folder, "config.plist", SearchOption.AllDirectories)
                .FirstOrDefault(p => p.Contains(Path.Combine("OC", "config.plist"), StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private async Task CleanAsync()
    {
        bool ok = await AskConfirmAsync("清理构建产物？", "将删除已生成的 EFI、BIOS 页面和已下载的恢复镜像。内置组件和解压缓存会保留。");
        if (!ok) return;
        foreach (string dir in new[]
        {
            Path.Combine(_ctx.Services.DataDirectory, "build"),
            Path.Combine(_ctx.Services.DataDirectory, "recovery")
        })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception ex) { AppendLog("! " + ex.Message); }
        }
        _build = null;
        _writtenVolume = "";
        ShowInfo("已清理", "构建产物与恢复镜像已删除。");
        RenderStep();
    }

    private async Task<T?> RunOperationWithResultAsync<T>(string status, bool allowCancel, Func<CancellationToken, Task<T>> action)
    {
        T? result = default;
        await RunOperationAsync(status, allowCancel, async ct => result = await action(ct));
        return result;
    }

    private void DispatchStatus(string text, double? fraction)
    {
        _dispatcher.TryEnqueue(() =>
        {
            _statusText.Text = text;
            _progress.Visibility = Visibility.Visible;
            _progress.IsIndeterminate = fraction is null;
            _progress.Value = (fraction ?? 0) * 100;
        });
    }
}
