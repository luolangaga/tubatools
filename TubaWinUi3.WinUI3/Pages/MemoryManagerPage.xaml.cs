using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.MemoryManagement;

namespace TubaWinUi3.Pages;

public sealed partial class MemoryManagerPage : Page
{
    private static string L(string key, string zh, string en) => LocalizationService.L("MS_" + key, zh);
    private CancellationTokenSource? _lifetime;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private PageFileState? _state;
    private bool _busy;
    private sealed record DriveChoice(string Root, string Label, ulong Free) { public override string ToString() => $"{Root}  {Label} · {MemoryText.Bytes(Free)}"; }
    private sealed record SourceChoice(string Path) { public override string ToString() => Path; }
    private static string SystemRoot => Path.GetPathRoot(Environment.SystemDirectory)!;

    public MemoryManagerPage()
    {
        InitializeComponent();
        InitializeCharts();
        Header.Title = LocalizationService.L("Builtin_memory-manager_Name", "内存管理");
        Header.Subtitle = L("Subtitle", "查看内存状态，整理缓存，把分页文件迁移到其他磁盘", "Memory status, cache cleanup and pagefile migration");
        MemoryTitle.Text = L("Memory", "物理内存", "Physical memory");
        RefreshButton.Content = L("Refresh", "刷新", "Refresh");
        CleanButton.Content = L("Clean", "开始清理", "Clean now");
        CleanMode.Header = L("CleanMode", "清理方式", "Cleanup mode");
        CleanMode.Items.Add(L("LightClean", "轻量：低优先级待机缓存", "Light: low-priority standby cache"));
        CleanMode.Items.Add(L("StandardClean", "标准：后台工作集 + 全部待机缓存（推荐）", "Standard: background working sets + all standby cache (recommended)"));
        CleanMode.Items.Add(L("DeepClean", "深度：额外整理系统文件缓存并请求修改页写回", "Deep: also trim system file cache and request modified-page writeback"));
        CleanMode.SelectedIndex = 1;
        Details.Header = L("Details", "查看详细内存分布", "Detailed memory breakdown");
        DetailHint.Text = L("DetailHint", "每项显示实际容量和占比；小项也不会隐藏。缓存、池和提交属于不同口径，不相加。", "Every value remains visible, including small shares. Cache, pools and commit overlap; do not add them together.");
        MigrationTitle.Text = L("Migration", "分页文件迁移", "Move pagefile");
        MigrationHint.Text = L("MigrationHint", "分页文件为系统提供额外的内存提交空间。迁移只修改下次启动配置，不直接移动或删除正在使用的文件。", "Pagefiles increase the system commit budget. Migration changes next-boot settings; it does not move or delete live files.");
        Source.Header = L("Source", "1 · 当前位置", "1 · Current location"); Target.Header = L("Target", "2 · 目标磁盘", "2 · Destination drive");
        Sizing.Header = L("Sizing", "目标分页文件大小", "Destination pagefile size");
        Sizing.Items.Add(L("SystemManaged", "由 Windows 管理（推荐）", "Windows managed (recommended)"));
        Sizing.Items.Add(L("Custom", "自定义大小", "Custom size")); Sizing.SelectedIndex = 0;
        Initial.Header = L("Initial", "初始大小（MiB）", "Initial size (MiB)"); Maximum.Header = L("Maximum", "最大大小（MiB）", "Maximum size (MiB)");
        KeepSystem.Content = L("KeepSystem", "在系统盘保留系统管理的分页文件（推荐，有助于崩溃转储）", "Keep a system-managed pagefile on the system drive (recommended for crash dumps)");
        KeepSystem.Checked += (_, _) => UpdateSummary(); KeepSystem.Unchecked += (_, _) => UpdateSummary();
        Initial.ValueChanged += (_, _) => UpdateSummary(); Maximum.ValueChanged += (_, _) => UpdateSummary();
        MigrateButton.Content = L("Migrate", "检查并迁移", "Review and move");
        RestartHint.Text = L("Restart", "应用前显示完整变更，自动备份；重启电脑后生效。", "Review all changes before applying. Automatic backup; takes effect after restart.");
        Recovery.Header = L("Recovery", "恢复与系统设置", "Recovery and system settings");
        RecoveryHint.Text = L("RecoveryHint", "迁移失败时会尝试恢复原配置，也可以恢复上次备份或让 Windows 自动管理。", "Failures trigger rollback. You can also restore the previous backup or Windows automatic management.");
        RestoreButton.Content = L("Restore", "恢复上次备份", "Restore backup"); AutomaticButton.Content = L("Automatic", "恢复 Windows 自动管理", "Restore Windows automatic management");
        SystemButton.Content = L("SystemSettings", "打开系统设置", "Open system settings"); AdminButton.Content = L("Admin", "以管理员身份重新打开", "Reopen as administrator");
        Loaded += LoadedPage; Unloaded += (_, _) => { _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = null; };
        SizeChanged += (_, _) => { bool narrow = ActualWidth < 680; Grid.SetColumn(Target, narrow ? 0 : 1); Grid.SetColumnSpan(Source, narrow ? 2 : 1); Grid.SetColumnSpan(Target, narrow ? 2 : 1); Grid.SetRow(Target, narrow ? 1 : 0); Grid.SetRow(DriveSpace, narrow ? 2 : 1); };
        DriveGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    }
    private async void LoadedPage(object sender, RoutedEventArgs e)
    {
        if (_lifetime is not null) return;
        _lifetime = new(); var token = _lifetime.Token;
        await Guard(async () => { await RefreshMemory(); await ReadPageFiles(); });
        try { while (!token.IsCancellationRequested) { await Task.Delay(2000, token); await RefreshMemory(); } } catch (OperationCanceledException) { }
    }
    private void Notice(string message, bool error = false) { Feedback.Message = message; Feedback.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational; Feedback.IsOpen = true; }
    private async Task Guard(Func<Task> action) { try { await action(); } catch (Exception ex) { if (_lifetime is not null) Notice(ex.Message, true); } }
    private async Task RefreshMemory()
    {
        if (!await _refresh.WaitAsync(0)) return;
        var token = _lifetime?.Token ?? new CancellationToken(true);
        try
        {
            var s = await Task.Run(MemoryReader.Read); if (token.IsCancellationRequested) return;
            if (PauseGraph.IsChecked != true) AddGraphSample(s);
            MemoryAmount.Text = $"{MemoryText.Bytes(s.Used)} / {MemoryText.Bytes(s.Total)}";
            AvailableText.Text = L("Available", "可用内存", "Available memory") + " " + MemoryText.Bytes(s.Available);
            MemoryBar.Value = 100 - s.AvailablePercent; MemoryPercent.Text = $"{MemoryBar.Value:0.0}%";
            MetricRows.Children.Clear();
            void Row(string label, ulong? value, ulong denominator)
            {
                var grid = new Grid { ColumnSpacing = 12 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
                string percentage = MemoryText.Percentage(value, denominator);
                var amount = new TextBlock { Text = $"{MemoryText.Bytes(value)}   ·   {percentage}", IsTextSelectionEnabled = true };
                Grid.SetColumn(amount, 1); grid.Children.Add(amount); MetricRows.Children.Add(grid);
            }
            Row(L("Installed", "安装容量", "Installed capacity"), s.Installed, s.Installed);
            Row(L("Total", "系统可用总容量", "OS physical capacity"), s.Total, s.Installed);
            Row(L("Reserved", "硬件保留（占安装容量）", "Hardware reserved (of installed capacity)"), s.HardwareReserved, s.Installed);
            Row(L("Occupied", "已占用（含修改页）", "Occupied (includes modified pages)"), s.Used, s.Total);
            Row(L("Available", "可用内存", "Available memory"), s.Available, s.Total);
            Row(L("Free", "空闲", "Free"), s.Free, s.Total); Row(L("Zeroed", "已清零", "Zeroed"), s.Zeroed, s.Total);
            Row(L("Standby", "待机缓存", "Standby cache"), s.Standby, s.Total); Row(L("Modified", "修改页", "Modified pages"), s.Modified, s.Total);
            Row(L("Cache", "系统缓存", "System cache"), s.Cache, s.Total); Row(L("PagedPool", "分页池", "Paged pool"), s.PagedPool, s.Total); Row(L("NonpagedPool", "非分页池", "Nonpaged pool"), s.NonpagedPool, s.Total);
            Row(L("Commit", "已提交（占提交上限）", "Committed (of commit limit)"), s.Commit, s.CommitLimit); Row(L("CommitPeak", "提交峰值（占提交上限）", "Peak commit (of commit limit)"), s.CommitPeak, s.CommitLimit);
            Row(L("CommitLimit", "提交上限", "Commit limit"), s.CommitLimit, s.CommitLimit);
            for (int i = 0; i < 8; i++) Row(L("Priority", "待机优先级", "Standby priority") + " " + i, s.StandbyByPriority?[i], s.Total);
        }
        catch (Exception ex) { if (!token.IsCancellationRequested) Notice(ex.Message, true); }
        finally { _refresh.Release(); }
    }
    private async Task ReadPageFiles()
    {
        var state = await PageFileService.ReadAsync(); if (_lifetime is null) return; _state = state;
        CurrentPageFiles.Text = L("Current", "当前运行：", "Running now:") + "\n" + (state.Usage.Count == 0 ? L("NoFile", "当前未发现分页文件", "No runtime pagefile found") : string.Join("\n", state.Usage.Select(p => $"{p.Path} · {p.UsedMiB:N0} / {p.AllocatedMiB:N0} MiB")))
            + "\n" + L("NextBoot", "已保存配置（下次启动）：", "Saved configuration (next boot):") + " " + Describe(state.Configuration);
        Source.ItemsSource = PageFileMigrationPlanner.EffectiveEntries(state).Select(e => new SourceChoice(e.Path)).ToList(); Source.SelectedIndex = Source.Items.Count > 0 ? 0 : -1;
        var drives = await Task.Run(() => DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).Select(d => new DriveChoice(d.Name, d.VolumeLabel, (ulong)d.AvailableFreeSpace)).ToList());
        Target.ItemsSource = drives; SelectDestination(); UpdateSummary(); RestoreButton.IsEnabled = File.Exists(PageFileService.BackupPath);
    }
    private void SelectDestination()
    {
        string? sourceRoot = (Source.SelectedItem as SourceChoice) is { } source ? Path.GetPathRoot(source.Path) : null;
        Target.SelectedItem = Target.Items.Cast<DriveChoice>().Where(d => !d.Root.Equals(sourceRoot, StringComparison.OrdinalIgnoreCase)).OrderByDescending(d => d.Free).FirstOrDefault();
    }
    private void Source_Changed(object sender, SelectionChangedEventArgs e) { SelectDestination(); UpdateSummary(); }
    private void Target_Changed(object sender, SelectionChangedEventArgs e) => UpdateSummary();
    private void Sizing_Changed(object sender, SelectionChangedEventArgs e) { if (Sizes is null) return; Sizes.Visibility = Sizing.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; UpdateSummary(); }
    private PageFileConfiguration MigrationDraft()
    {
        if (_state is null || Source.SelectedItem is not SourceChoice source || Target.SelectedItem is not DriveChoice target) throw new ArgumentException(L("ChooseDrives", "请选择当前位置和目标磁盘", "Choose source and destination drives"));
        uint Number(double v) => double.IsFinite(v) && v == Math.Truncate(v) && v is >= 16 and <= 4194303 ? (uint)v : throw new ArgumentException(L("Integer", "大小必须是有效的整数 MiB", "Size must be valid integer MiB"));
        return PageFileMigrationPlanner.Build(_state, source.Path, target.Root, SystemRoot, KeepSystem.IsChecked == true, Sizing.SelectedIndex == 1 ? PageFileMode.Custom : PageFileMode.SystemManaged,
            Sizing.SelectedIndex == 1 ? Number(Initial.Value) : 0, Sizing.SelectedIndex == 1 ? Number(Maximum.Value) : 0);
    }
    private void UpdateSummary()
    {
        if (MigrationSummary is null) return;
        try
        {
            var draft = MigrationDraft(); MigrationSummary.Text = L("Preview", "迁移后的配置：", "After migration:") + "\n" + Describe(draft);
            DriveSpace.Text = Target.SelectedItem is DriveChoice d ? L("FreeSpace", "目标磁盘剩余空间：", "Destination free space:") + MemoryText.Bytes(d.Free) : "";
            MigrateButton.IsEnabled = !_busy;
        }
        catch (Exception ex) { MigrationSummary.Text = ex.Message; MigrateButton.IsEnabled = false; }
    }
    private static string Describe(PageFileConfiguration c) => c.Automatic ? L("Automatic", "Windows 自动管理", "Windows automatic management") : string.Join("\n", c.Entries.Where(e => e.Mode != PageFileMode.None).Select(e => e.Path + " · " + (e.Mode == PageFileMode.SystemManaged ? L("Managed", "系统管理", "System managed") : $"{e.InitialMiB:N0}–{e.MaximumMiB:N0} MiB")));
    private async Task Apply(PageFileConfiguration draft)
    {
        if (_state is null) throw new InvalidOperationException(L("ReadFirst", "请先刷新当前配置", "Refresh the current configuration first"));
        await Task.Run(() => PageFileService.Validate(draft));
        var dialog = new TubaWinUi3.Controls.FluentContentDialog { XamlRoot = XamlRoot, Title = L("Confirm", "确认分页文件变更", "Confirm pagefile changes"), Content = new TextBlock { Text = L("Before", "原配置：", "Before:") + "\n" + Describe(_state.Configuration) + "\n\n" + L("After", "新配置：", "After:") + "\n" + Describe(draft) + "\n\n" + L("ConfirmHint", "将备份原配置。更改会影响提交上限和崩溃转储，重启电脑后生效。", "The original configuration will be backed up. Changes affect commit capacity and crash dumps and take effect after restart."), TextWrapping = TextWrapping.Wrap }, PrimaryButtonText = L("Apply", "应用配置", "Apply"), CloseButtonText = L("Cancel", "取消", "Cancel"), DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try { await PageFileService.ApplyAsync(_state.Configuration, draft); }
        catch { await ReadPageFiles(); throw; }
        await ReadPageFiles(); Pending.Message = L("Pending", "配置已保存并验证。请重启电脑完成迁移；当前使用的文件暂时仍在原位置。", "Configuration saved and verified. Restart to finish migration; running files remain in their original locations until then."); Pending.IsOpen = true;
    }
    private async Task Operation(Func<Task> action)
    {
        if (_busy) return; _busy = true; RefreshButton.IsEnabled = CleanButton.IsEnabled = MigrateButton.IsEnabled = RestoreButton.IsEnabled = AutomaticButton.IsEnabled = false;
        try { await Guard(action); } finally { _busy = false; RefreshButton.IsEnabled = CleanButton.IsEnabled = AutomaticButton.IsEnabled = true; RestoreButton.IsEnabled = File.Exists(PageFileService.BackupPath); UpdateSummary(); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await Operation(async () => { await RefreshMemory(); await ReadPageFiles(); });
    private async void Clean_Click(object sender, RoutedEventArgs e) => await Operation(async () =>
    {
        var mode = (MemoryCleanMode)CleanMode.SelectedIndex;
        if (mode != MemoryCleanMode.Light)
        {
            var confirm = new TubaWinUi3.Controls.FluentContentDialog { XamlRoot = XamlRoot, Title = mode == MemoryCleanMode.Deep ? L("DeepConfirm", "深度内存清理", "Deep memory cleanup") : L("StandardConfirm", "标准内存清理", "Standard memory cleanup"),
                Content = new TextBlock { Text = CleanHint.Text, TextWrapping = TextWrapping.Wrap }, PrimaryButtonText = L("Clean", "开始清理", "Clean now"), CloseButtonText = L("Cancel", "取消", "Cancel"), DefaultButton = ContentDialogButton.Close };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        }
        CleanResult.Text = L("Cleaning", "正在清理，完成后再采样三秒…", "Cleaning; measuring again after three seconds…"); CleanResult.Visibility = Visibility.Visible;
        var result = await MemoryCleaner.CleanAsync(mode, _lifetime?.Token ?? default);
        CleanResult.Text = string.Join("\n", result.Steps) + "\n\n" + L("MeasuredClean", "可用内存：清理前 → 完成时 → 三秒后", "Available memory: before → immediate → after three seconds") + "\n"
            + $"{MemoryText.Bytes(result.Before.Available)} → {MemoryText.Bytes(result.Immediate.Available)} → {MemoryText.Bytes(result.After.Available)}\n"
            + L("ActualChange", "三秒后实际变化：", "Measured change after three seconds:") + MemoryText.Signed(MemoryText.Difference(result.After.Available, result.Before.Available)) + "\n"
            + L("StandbyChange", "待机缓存：", "Standby cache:") + MemoryText.Bytes(result.Before.Standby) + " → " + MemoryText.Bytes(result.After.Standby);
        await RefreshMemory();
    });
    private void CleanMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CleanHint is null) return;
        CleanHint.Text = CleanMode.SelectedIndex switch
        {
            1 => L("StandardHint", "整理当前用户的后台进程工作集，再清空待机缓存。跳过前台、自身、系统关键和受保护进程；不关闭应用。程序稍后重新使用这些数据时可能短暂卡顿，私有提交不会释放。", "Trim background working sets in your session, then purge standby. Foreground, self, critical and protected processes are skipped. Apps stay open; reloads may briefly stutter, and private commit remains."),
            2 => L("DeepHint", "包含标准清理，并整理系统文件缓存、请求修改页写回。可能明显增加磁盘读写和重新加载时间。不会关闭程序或删除分页文件，也不能根治应用内存泄漏。", "Includes standard cleanup, file-cache trimming and modified-page writeback requests. Disk I/O and reload time may increase. No apps are closed or pagefiles deleted; application leaks are not fixed."),
            _ => L("LightHint", "仅整理低优先级待机缓存；其他优先级缓存和进程工作集保持原样，收益可能很小。", "Only purge low-priority standby cache; other caches and working sets remain, so gains may be small.")
        };
    }
    private async void Migrate_Click(object sender, RoutedEventArgs e) => await Operation(() => Apply(MigrationDraft()));
    private async void Restore_Click(object sender, RoutedEventArgs e) => await Operation(() => Apply(PageFileService.LoadBackup()));
    private async void Automatic_Click(object sender, RoutedEventArgs e) => await Operation(() => Apply(new(true, Array.Empty<PageFileEntry>())));
    private void System_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("SystemPropertiesAdvanced.exe") { UseShellExecute = true });
    private async void Admin_Click(object sender, RoutedEventArgs e) => await Guard(async () => { var result = await Task.Run(() => { bool ok = App.TryRelaunchElevated(["--open-builtin", "memory-manager"], out var detail); return (ok, detail); }); if (result.ok) App.RequestExit(); else Notice(result.detail, true); });
}
