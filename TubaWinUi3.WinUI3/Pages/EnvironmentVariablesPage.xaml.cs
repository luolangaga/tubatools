using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Principal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.EnvVars;

namespace TubaWinUi3.Pages;

/// <summary>
/// 「环境变量」（对标 PowerToys Environment Variables）。
/// 用户 / 系统两个作用域各自编辑（不做系统+用户合并视图），左侧列表 + 右侧编辑区；
/// PATH 这类分号列表变量切成逐条可视化编辑；保存前自动快照，可一键恢复上次备份。
///
/// 写入口径全部在 <see cref="EnvironmentVariableService"/>，本页只负责编排与提示。
/// </summary>
public sealed partial class EnvironmentVariablesPage : Page, ILocalizablePage
{
    private void VariableWorkspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= 840;
        VariableWorkspace.ColumnDefinitions[0].Width = wide ? new GridLength(240) : new GridLength(1, GridUnitType.Star);
        VariableWorkspace.ColumnDefinitions[1].Width = new GridLength(wide ? 1 : 0);
        VariableWorkspace.ColumnDefinitions[2].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        VariableWorkspace.RowDefinitions[0].Height = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(180);
        VariableWorkspace.RowDefinitions[1].Height = wide ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        VariableDivider.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(EditorPanel, wide ? 2 : 0);
        Grid.SetRow(EditorPanel, wide ? 0 : 1);
    }

    private readonly List<EnvVarEntry> _original = [];
    private readonly ObservableCollection<EnvVarEntry> _working = [];
    private readonly ObservableCollection<PathEntryViewModel> _pathEntries = [];

    private readonly bool _isAdmin;

    private EnvScope _scope = EnvScope.User;
    private EnvVarEntry? _selected;
    private bool _dirty;
    private bool _pageAlive = true;
    private bool _initialized;
    private bool _watcherHooked;
    private bool _syncingPathEntries;
    private bool _suppressEditorWrite;
    private bool _suppressScopeChange;
    private bool _pendingExternalNotice;
    private DispatcherTimer? _toastTimer;

    public EnvironmentVariablesPage()
    {
        InitializeComponent();

        _isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

        VarList.ItemsSource = _working;
        PathList.ItemsSource = _pathEntries;

        // 集合本身变了也要重算脏状态：新增/删除变量走这条
        _working.CollectionChanged += (_, _) => UpdateActionState();
        _pathEntries.CollectionChanged += (_, _) => UpdateValueFromPathEntries();

        Loaded += (_, _) =>
        {
            _pageAlive = true;
            HookWatcher();

            if (!_initialized)
            {
                _initialized = true;
                LoadScope(_scope);
            }

            if (_pendingExternalNotice)
            {
                _pendingExternalNotice = false;
                ShowExternalChangeBar();
            }
        };

        Unloaded += (_, _) =>
        {
            _pageAlive = false;
            UnhookWatcher();
        };
    }

    /// <summary>语言切换后重刷代码赋值的文案（打了 Uid 的控件由 WinUI3Localizer 自动刷新）。</summary>
    public void ApplyLocalization()
    {
        if (AdminBar.IsOpen) ApplyAdminBarText();
        if (ExternalChangeBar.IsOpen) ShowExternalChangeBar();
        UpdateKindText(_selected);
        UpdateActionState();
    }

    // ---------- 作用域与加载 ----------

    private async void ScopePivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressScopeChange) return;
        if (ScopePivot.SelectedItem is not PivotItem { Tag: string tag }) return;

        var target = string.Equals(tag, "System", StringComparison.Ordinal) ? EnvScope.System : EnvScope.User;
        if (target == _scope) return;

        if (_dirty && !await ConfirmDiscardAsync())
        {
            // 用户选择继续编辑：把 Pivot 拨回原作用域，别让界面和数据对不上
            _suppressScopeChange = true;
            ScopePivot.SelectedIndex = _scope == EnvScope.User ? 0 : 1;
            _suppressScopeChange = false;
            return;
        }

        _scope = target;
        LoadScope(_scope);
    }

    /// <summary>从注册表重新读入当前作用域，丢弃未保存的编辑。</summary>
    private void LoadScope(EnvScope scope)
    {
        var (entries, failureCode, failureName, _) = EnvironmentVariableService.ReadAll(scope);

        _original.Clear();
        _original.AddRange(entries);

        _working.Clear();
        foreach (var entry in entries)
        {
            var clone = entry.Clone();
            clone.PropertyChanged += OnEntryPropertyChanged;
            _working.Add(clone);
        }

        SetSelected(null);
        UpdateAdminState();
        UpdateActionState();

        if (failureCode != EnvFailureCode.None)
            ShowToast(
                L("EnvVars_LoadFailed", "读取失败"),
                DescribeFailure(failureCode, failureName, EnvRollbackState.NotAttempted),
                InfoBarSeverity.Error);
    }

    // ---------- 列表 / 选择 ----------

    private void VarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => SetSelected(VarList.SelectedItem as EnvVarEntry);

    private void SetSelected(EnvVarEntry? entry)
    {
        _selected = entry;
        EmptyHint.Visibility = entry is null ? Visibility.Visible : Visibility.Collapsed;

        SetEditorText(entry?.Name ?? "", entry?.Value ?? "");
        UpdateKindText(entry);
        ApplyEditorMode(entry);
        UpdateActionState();
    }

    /// <summary>
    /// 把选中项的内容写进编辑框。必须抑制 TextChanged，否则刚载入的文本会被当成用户输入再写回去。
    /// 编辑框一律用显式 TextChanged 回写而不依赖 <c>{Binding}</c>：命令式接线既确定，也贴合本项目其余页面的写法。
    /// </summary>
    private void SetEditorText(string name, string value)
    {
        _suppressEditorWrite = true;
        try
        {
            if (!string.Equals(NameBox.Text, name, StringComparison.Ordinal)) NameBox.Text = name;
            if (!string.Equals(ValueBox.Text, value, StringComparison.Ordinal)) ValueBox.Text = value;
        }
        finally
        {
            _suppressEditorWrite = false;
        }
    }

    // ---------- 编辑框回写 ----------

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditorWrite || _selected is null) return;
        _selected.Name = NameBox.Text;
    }

    private void ValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditorWrite || _selected is null) return;
        _selected.Value = ValueBox.Text;
    }

    private void PathEntryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingPathEntries || sender is not TextBox box) return;
        if (box.DataContext is not PathEntryViewModel viewModel) return;
        viewModel.Value = box.Text;
    }

    /// <summary>按当前变量决定用「单行值文本框」还是「逐条列表编辑器」。</summary>
    private void ApplyEditorMode(EnvVarEntry? entry)
    {
        if (entry is null)
        {
            ListHeaderRow.Visibility = Visibility.Collapsed;
            ValueBox.Visibility = Visibility.Collapsed;
            PathList.Visibility = Visibility.Collapsed;
            ClearPathEntries();
            return;
        }

        var isList = EnvVarRules.IsListVariable(entry.Name);
        ListHeaderRow.Visibility = isList ? Visibility.Visible : Visibility.Collapsed;
        ValueBox.Visibility = isList ? Visibility.Collapsed : Visibility.Visible;
        PathList.Visibility = isList ? Visibility.Visible : Visibility.Collapsed;

        if (isList)
        {
            SyncPathEntriesFromValue(entry.Value);
            return;
        }

        // 从列表型改成普通型（改名导致）时值框里还是旧内容，得按当前值重新载入
        ClearPathEntries();
        if (!string.Equals(ValueBox.Text, entry.Value, StringComparison.Ordinal))
            SetEditorText(entry.Name, entry.Value);
    }

    /// <summary>
    /// 清空逐条列表。必须走抑制开关：<c>ObservableCollection.Clear</c> 即使本来就空也会发 Reset，
    /// 而 CollectionChanged 会触发「把列表合并回变量值」——那会把选中变量的值悄悄抹成空串
    /// （表现为选中任意普通变量后值框变空、并莫名出现「有未保存的更改」，保存下去就是清空该变量）。
    /// </summary>
    private void ClearPathEntries()
    {
        _syncingPathEntries = true;
        try
        {
            _pathEntries.Clear();
        }
        finally
        {
            _syncingPathEntries = false;
        }
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateActionState();

        if (sender is not EnvVarEntry entry || !ReferenceEquals(entry, _selected)) return;

        // 改名可能让它变成（或不再是）列表型变量，编辑方式要跟着换
        if (e.PropertyName == nameof(EnvVarEntry.Name)) ApplyEditorMode(entry);
        else if (e.PropertyName == nameof(EnvVarEntry.Value)) UpdateKindText(entry);
    }

    private void NewBtn_Click(object sender, RoutedEventArgs e)
    {
        var entry = new EnvVarEntry { Name = "", Value = "" };
        entry.PropertyChanged += OnEntryPropertyChanged;
        _working.Add(entry);

        VarList.SelectedItem = entry;
        NameBox.Focus(FocusState.Programmatic);
    }

    private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } entry) return;

        var confirmed = false;
        var dialog = new ContentDialog
        {
            Title = L("EnvVars_Delete", "删除"),
            Content = string.Format(
                L("EnvVars_DeleteConfirmMsg", "确定要删除变量「{0}」吗？保存后才会真正生效，之前可用「放弃更改」取消。"),
                string.IsNullOrWhiteSpace(entry.Name) ? L("EnvVars_Unnamed", "(未命名)") : entry.Name),
            PrimaryButtonText = L("EnvVars_Delete", "删除"),
            CloseButtonText = L("EnvVars_Cancel", "取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        dialog.PrimaryButtonClick += (_, _) => confirmed = true;

        await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(5));
        if (!confirmed || !_pageAlive) return;

        entry.PropertyChanged -= OnEntryPropertyChanged;
        _working.Remove(entry);
        SetSelected(null);
    }

    // ---------- PATH 逐条编辑 ----------

    private void SyncPathEntriesFromValue(string value)
    {
        _syncingPathEntries = true;
        try
        {
            _pathEntries.Clear();
            foreach (var item in EnvVarRules.SplitList(value))
                _pathEntries.Add(CreatePathEntry(item));
        }
        finally
        {
            _syncingPathEntries = false;
        }
    }

    private PathEntryViewModel CreatePathEntry(string value)
    {
        var vm = new PathEntryViewModel { Value = value };
        vm.PropertyChanged += (_, _) => UpdateValueFromPathEntries();
        return vm;
    }

    /// <summary>把逐条编辑的结果合并回变量值（单一数据源，避免两处各说各话）。</summary>
    private void UpdateValueFromPathEntries()
    {
        if (_syncingPathEntries || _selected is null) return;

        // 双保险：只有列表型变量才允许被逐条列表回写
        if (!EnvVarRules.IsListVariable(_selected.Name)) return;

        _selected.Value = EnvVarRules.JoinList(_pathEntries.Select(entry => entry.Value));
    }

    private static PathEntryViewModel? DataContextOf(object sender)
        => (sender as FrameworkElement)?.DataContext as PathEntryViewModel;

    private void PathUp_Click(object sender, RoutedEventArgs e) => MovePathEntry(sender, -1);

    private void PathDown_Click(object sender, RoutedEventArgs e) => MovePathEntry(sender, +1);

    private void MovePathEntry(object sender, int delta)
    {
        if (DataContextOf(sender) is not { } vm) return;

        var index = _pathEntries.IndexOf(vm);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _pathEntries.Count) return;

        _pathEntries.Move(index, target);
    }

    private void PathInsert_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf(sender) is not { } vm) return;

        var index = _pathEntries.IndexOf(vm);
        if (index < 0) return;

        _pathEntries.Insert(index + 1, CreatePathEntry(""));
    }

    private void PathCopy_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf(sender) is not { } vm) return;

        var index = _pathEntries.IndexOf(vm);
        if (index < 0) return;

        _pathEntries.Insert(index + 1, CreatePathEntry(vm.Value));
    }

    private void PathRemove_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf(sender) is not { } vm) return;

        var index = _pathEntries.IndexOf(vm);
        if (index < 0) return;

        // 剩最后一条时不清空列表：没有行也就没有「在其后插入」按钮，用户会卡死在空编辑器里
        if (_pathEntries.Count == 1)
        {
            _pathEntries[0].Value = "";
            UpdateValueFromPathEntries();
            return;
        }

        _pathEntries.RemoveAt(index);
    }

    private void DedupeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;

        var deduped = EnvVarRules.DedupeList([.. _pathEntries.Select(entry => entry.Value)]);
        SyncPathEntriesFromValue(EnvVarRules.JoinList(deduped));
        UpdateValueFromPathEntries();
    }

    // ---------- 保存 / 放弃 / 恢复 ----------

    private async void SaveBtn_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private async Task SaveAsync()
    {
        if (_scope == EnvScope.System && !_isAdmin)
        {
            ApplyAdminBarText();
            AdminBar.IsOpen = true;
            return;
        }

        // 重名（不区分大小写）会让「到底写哪个」不确定，先拦住整批
        var duplicates = _working
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            ShowToast(
                L("EnvVars_ValidationFailed", "校验未通过"),
                string.Format(
                    L("EnvVars_DuplicateNames", "存在重名变量（不区分大小写）：{0}"),
                    string.Join("、", duplicates)),
                InfoBarSeverity.Error);
            return;
        }

        var changes = EnvVarRules.BuildChanges(_original, _working);
        if (changes.Count == 0)
        {
            ShowToast(L("EnvVars_NoChange", "没有需要保存的改动"), "", InfoBarSeverity.Informational);
            return;
        }

        // 与 Service 用同一套规则函数预校验，保证「报错就一个字节都没写」
        foreach (var change in changes)
        {
            if (change.Kind != EnvChangeKind.Set) continue;

            var value = change.Value ?? "";
            var error = EnvVarRules.ValidateName(change.Name)
                        ?? EnvVarRules.ValidateValue(value)
                        ?? EnvVarRules.ValidateTotalLength(change.Name, value);
            if (error is { } code)
            {
                ShowToast(
                    L("EnvVars_ValidationFailed", "校验未通过"),
                    DescribeFailure(code, change.Name, EnvRollbackState.NotAttempted),
                    InfoBarSeverity.Error);
                return;
            }
        }

        var result = EnvironmentVariableService.Apply(_scope, changes);

        if (!result.Succeeded)
        {
            ShowToast(
                L("EnvVars_ApplyFailed", "保存失败"),
                DescribeFailure(result),
                result.NeedsElevation ? InfoBarSeverity.Warning : InfoBarSeverity.Error);
            return;
        }

        ExternalChangeBar.IsOpen = false;
        LoadScope(_scope);

        // 成功路径必然有快照：备份创建失败时 Apply 整批取消、走上面的失败分支
        ShowToast(
            L("EnvVars_SavedTitle", "已保存"),
            string.Format(
                L("EnvVars_SavedMsg", "已保存 {0} 项改动。已打开的程序和终端需要重启才能看到新值。"),
                result.ChangedCount),
            InfoBarSeverity.Success);
        await Task.CompletedTask;
    }

    private async void RevertBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && !await ConfirmDiscardAsync()) return;
        LoadScope(_scope);
    }

    private async void RestoreBtn_Click(object sender, RoutedEventArgs e)
    {
        if (EnvironmentVariableService.LatestBackupPath(_scope) is null)
        {
            ShowToast(
                L("EnvVars_RestoreFailed", "恢复失败"),
                DescribeFailure(EnvFailureCode.RestoreNoBackup, name: null, EnvRollbackState.NotAttempted),
                InfoBarSeverity.Warning);
            return;
        }

        var confirmed = false;
        var dialog = new ContentDialog
        {
            Title = L("EnvVars_RestoreConfirmTitle", "恢复上次备份"),
            Content = string.Format(
                L("EnvVars_RestoreConfirmMsg", "将把「{0}」还原为上次保存前的状态，当前未保存的编辑会丢失。"),
                ScopeDisplayName(_scope)),
            PrimaryButtonText = L("EnvVars_RestoreConfirmPrimary", "恢复"),
            CloseButtonText = L("EnvVars_Cancel", "取消"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        dialog.PrimaryButtonClick += (_, _) => confirmed = true;

        await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(5));
        if (!confirmed || !_pageAlive) return;

        var result = EnvironmentVariableService.RestoreLatest(_scope);
        if (!result.Succeeded)
        {
            ShowToast(L("EnvVars_RestoreFailed", "恢复失败"), DescribeFailure(result), InfoBarSeverity.Error);
            return;
        }

        LoadScope(_scope);
        ShowToast(
            L("EnvVars_RestoreDone", "已恢复"),
            string.Format(L("EnvVars_RestoreDoneMsg", "已还原 {0} 个变量。"), result.ChangedCount),
            InfoBarSeverity.Success);
    }

    // ---------- 返回 / 外部改动 ----------

    private async void OnBackRequested(object? sender, EventArgs e)
    {
        if (_dirty && !await ConfirmDiscardAsync()) return;
        App.MainWindow?.NavigateBack();
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        var discard = false;
        var dialog = new ContentDialog
        {
            Title = L("EnvVars_DirtyConfirmTitle", "未保存的更改"),
            Content = L("EnvVars_DirtyConfirmMsg", "环境变量有未保存的修改，是否放弃这些更改？"),
            PrimaryButtonText = L("EnvVars_Discard", "放弃"),
            CloseButtonText = L("EnvVars_KeepEditing", "继续编辑"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        dialog.PrimaryButtonClick += (_, _) => discard = true;

        await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(5));
        return discard;
    }

    private void HookWatcher()
    {
        if (_watcherHooked) return;
        EnvironmentChangeWatcher.EnvironmentChangedExternally += OnEnvironmentChangedExternally;
        _watcherHooked = true;
    }

    private void UnhookWatcher()
    {
        if (!_watcherHooked) return;
        EnvironmentChangeWatcher.EnvironmentChangedExternally -= OnEnvironmentChangedExternally;
        _watcherHooked = false;
    }

    /// <summary>WndProc 线程回调 → 调度到 UI 线程；页面不在前台时只记标志，回来再提示。</summary>
    private void OnEnvironmentChangedExternally()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_pageAlive) return;

            if (!IsLoaded)
            {
                _pendingExternalNotice = true;
                return;
            }

            ShowExternalChangeBar();
        });
    }

    private void ShowExternalChangeBar()
    {
        ExternalChangeBar.Title = L("EnvVars_ExternalChangeTitle", "检测到环境变量变化");
        ExternalChangeBar.Message = _dirty
            ? L("EnvVars_ExternalChangeDirty", "其他程序修改了环境变量。当前有未保存的编辑，点「重新加载」会丢弃它们。")
            : L("EnvVars_ExternalChangeMsg", "其他程序修改了环境变量，点「重新加载」查看最新内容。");
        ExternalChangeBar.IsOpen = true;
    }

    private async void ReloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && !await ConfirmDiscardAsync()) return;
        ExternalChangeBar.IsOpen = false;
        LoadScope(_scope);
    }

    // ---------- 状态与文案 ----------

    private void UpdateActionState()
    {
        var blocked = _scope == EnvScope.System && !_isAdmin;
        // 不支持的注册表类型：只读展示（编辑/删除都会造成类型转换，一律禁用）
        var unsupported = _selected is { KindUnsupported: true };

        NewBtn.IsEnabled = !blocked;
        DeleteBtn.IsEnabled = !blocked && _selected is not null && !unsupported;
        SetEditorEnabled(!blocked && _selected is not null && !unsupported);

        _dirty = EnvVarRules.BuildChanges(_original, _working).Count > 0;
        SaveBtn.IsEnabled = !blocked && _dirty;
        RevertBtn.IsEnabled = _dirty;
        // 「恢复上次备份」也是写注册表，受限作用域下同样禁用
        RestoreBtn.IsEnabled = !blocked;
        DirtyText.Text = _dirty ? L("EnvVars_DirtyHint", "有未保存的更改") : "";

        UpdateUnsupportedKindBar();
    }

    /// <summary>选中项是「不支持的注册表类型」时亮提示条：编辑与删除都已禁用，避免被无意改成 REG_SZ。</summary>
    private void UpdateUnsupportedKindBar()
    {
        var unsupported = _selected is { KindUnsupported: true };
        UnsupportedKindBar.IsOpen = unsupported;

        if (!unsupported) return;

        UnsupportedKindBar.Title = L("EnvVars_UnsupportedKindTitle", "不支持的注册表类型");
        UnsupportedKindBar.Message = string.Format(
            L("EnvVars_UnsupportedKindMsg", "「{0}」的类型是 {1}，本工具只支持 REG_SZ 与 REG_EXPAND_SZ，已禁用编辑与删除。"),
            _selected!.Name,
            _selected.Kind);
    }

    /// <summary>
    /// Grid 不是 Control、没有 IsEnabled，因此改为停用命中测试 + 逐个停用真正的输入控件
    /// （ListView 停用后，其行内按钮同样收不到输入）。
    /// </summary>
    private void SetEditorEnabled(bool enabled)
    {
        EditorPanel.IsHitTestVisible = enabled;
        EditorPanel.Opacity = enabled ? 1.0 : 0.6;
        NameBox.IsEnabled = enabled;
        ValueBox.IsEnabled = enabled;
        PathList.IsEnabled = enabled;
        DedupeBtn.IsEnabled = enabled;
    }

    /// <summary>系统作用域 + 非管理员时把提示条亮出来（未打包版正常启动即管理员，这条只兜旁路场景）。</summary>
    private void UpdateAdminState()
    {
        ApplyAdminBarText();
        AdminBar.IsOpen = _scope == EnvScope.System && !_isAdmin;
    }

    private void ApplyAdminBarText()
    {
        AdminBar.Title = L("EnvVars_AdminRequiredTitle", "需要管理员权限");
        AdminBar.Message = L("EnvVars_AdminRequired", "当前不是以管理员身份运行，系统变量只能查看，不能修改。");
    }

    private void UpdateKindText(EnvVarEntry? entry)
    {
        if (entry is null)
        {
            KindText.Text = "";
            return;
        }

        // 不支持的注册表类型不参与「将存为」提示：它不可编辑，存成什么类型无从谈起
        KindText.Text = entry.KindUnsupported
            ? ""
            : EnvVarRules.ShouldStoreAsExpandString(entry.Value)
                ? L("EnvVars_KindExpand", "将存为 REG_EXPAND_SZ：值里的 %VAR% 会被使用它的程序展开")
                : L("EnvVars_KindString", "将存为 REG_SZ：值原样保存，不展开 %");
    }

    /// <summary>失败结果 → 当前语言文案（服务层只给代码；文案键与兜底在 EnvFailureText，原始异常只进日志）。</summary>
    private string DescribeFailure(EnvApplyResult result)
        => DescribeFailure(result.FailureCode, result.FailureName, result.Rollback);

    private string DescribeFailure(EnvFailureCode code, string? name, EnvRollbackState rollback)
    {
        var (key, fallback, args) = EnvFailureText.Describe(code, name, rollback, _scope);
        return string.Format(L(key, fallback), args);
    }

    private string ScopeDisplayName(EnvScope scope) => scope == EnvScope.User
        ? L("EnvVars_TabUser", "用户变量")
        : L("EnvVars_TabSystem", "系统变量");

    private static string L(string key, string fallback) => LocalizationService.L(key, fallback);

    private void ShowToast(string title, string message, InfoBarSeverity severity)
    {
        ToastBar.Title = title;
        ToastBar.Message = message;
        ToastBar.Severity = severity;
        ToastBar.IsOpen = true;

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toastTimer.Tick += (s, _) =>
        {
            ToastBar.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _toastTimer.Start();
    }
}

/// <summary>PATH 逐条编辑里的一行。</summary>
public sealed class PathEntryViewModel : INotifyPropertyChanged
{
    private string _value = "";

    public string Value
    {
        get => _value;
        set
        {
            if (string.Equals(_value, value, StringComparison.Ordinal)) return;
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
