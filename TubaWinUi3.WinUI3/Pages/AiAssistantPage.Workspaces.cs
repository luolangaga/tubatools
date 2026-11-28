using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Pages;

public sealed partial class AiAssistantPage
{
    private string WorkspaceDisplayName(AgentWorkspaceInfo workspace) =>
        workspace.IsDefault ? L("DefaultWorkspace", "默认工作区") : workspace.Name;

    private void UpdateWorkspaceHeader()
    {
        WorkspaceNameText.Text = WorkspaceDisplayName(_workspace);
        WorkspacePathText.Text = _workspace.Path;
        ToolTipService.SetToolTip(WorkspaceNameText, _workspace.Path);
        ToolTipService.SetToolTip(WorkspacePathText, _workspace.Path);
    }

    private bool HasRunningSessions() =>
        _sessions.Values.Append(_active).Any(state => state.Run is not null || state.Draining);

    private void WorkspaceMenu_Opening(object? sender, object e)
    {
        WorkspaceSwitchMenu.Items.Clear();
        foreach (var workspace in AgentWorkspaceStore.List())
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = WorkspaceDisplayName(workspace),
                GroupName = "AgentWorkspaces",
                IsChecked = string.Equals(workspace.Id, _workspace.Id, StringComparison.OrdinalIgnoreCase)
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, "AgentWorkspace:" + workspace.Id);
            item.Click += async (_, _) => await SwitchWorkspaceAsync(workspace);
            WorkspaceSwitchMenu.Items.Add(item);
        }
        NewWorkspaceItem.IsEnabled = !_preparing && !_closed;
        RenameWorkspaceItem.Visibility = _workspace.IsDefault ? Visibility.Collapsed : Visibility.Visible;
        RenameWorkspaceItem.IsEnabled = !_preparing && !_closed;
    }

    private async void NewWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _preparing) return;
        if (HasRunningSessions())
        {
            SetNotice(_active, L("WorkspaceBusy", "请先停止正在运行的任务，再新建或切换工作区。"), true);
            return;
        }
        var draft = await AskWorkspaceNameAsync(L("NewWorkspace", "新建工作区"), "", null, L("Create", "创建"), withDirectory: true);
        if (draft is null || _closed) return;
        AgentWorkspaceInfo workspace;
        try { workspace = AgentWorkspaceStore.Create(draft.Name, draft.Directory); }
        catch (Exception ex)
        {
            SetNotice(_active, L("WorkspaceCreateFailed", "无法创建工作区。") + "\n" + ex.Message, true);
            return;
        }
        await SwitchWorkspaceAsync(workspace);
    }

    private async void RenameWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_closed || _preparing || _workspace.IsDefault) return;
        var draft = await AskWorkspaceNameAsync(L("RenameWorkspace", "重命名工作区"), _workspace.Name, _workspace.Id, L("Save", "保存"), withDirectory: false);
        if (draft is null || _closed || string.Equals(draft.Name, _workspace.Name, StringComparison.Ordinal)) return;
        try
        {
            _workspace = AgentWorkspaceStore.Rename(_workspace, draft.Name);
            UpdateWorkspaceHeader();
            SetNotice(_active, string.Format(L("WorkspaceRenamed", "已重命名为「{0}」。"), _workspace.Name), attention: true);
        }
        catch (Exception ex)
        {
            SetNotice(_active, L("WorkspaceRenameFailed", "无法重命名工作区，请检查数据目录权限。") + "\n" + ex.Message, true);
        }
    }

    /// <summary>切换工作区：换目录 → 清空当前工作区会话 → 重启 AI 组件（工作目录与技能随目录变化）。</summary>
    private async Task SwitchWorkspaceAsync(AgentWorkspaceInfo workspace)
    {
        if (_closed || _preparing) return;
        if (HasRunningSessions())
        {
            SetNotice(_active, L("WorkspaceBusy", "请先停止正在运行的任务，再新建或切换工作区。"), true);
            return;
        }
        if (string.Equals(workspace.Path, _workspace.Path, StringComparison.OrdinalIgnoreCase)) return;
        _preparing = true;
        UpdateButtons();
        try
        {
            AgentWorkspace.Prepare(workspace.Path);
            AgentWorkspaceStore.Select(workspace);
            CloseFullScreenCanvas();
            ResetWorkspaceSessions();
            _workspace = workspace;
            UpdateWorkspaceHeader();
            SyncActive();
        }
        catch (Exception ex)
        {
            SetNotice(_active, L("WorkspaceSwitchFailed", "无法切换工作区。") + "\n" + ex.Message, true);
            return;
        }
        finally
        {
            _preparing = false;
            UpdateButtons();
        }
        if (_closed) return;
        if (AiComponentService.IsInstalled) await PrepareAsync();
        else SetNotice(_active, L("Welcome", "首次使用需要准备 AI 组件。"), attention: true);
    }

    private void ResetWorkspaceSessions()
    {
        ++_focusVersion;
        _progressTimer.Stop();
        _draftHistoryTimer.Stop();
        foreach (var state in _sessions.Values.Append(_active).Distinct())
        {
            state.Discarded = true;
            state.Queue.Clear();
            foreach (var question in state.Questions) question.View.Cancel();
        }
        _sessions.Clear();
        _deleting.Clear();
        _history = [];
        ++_historyVersion;
        _historyRows.Merge([]);
        _models = [];
        ModelBox.ItemsSource = null;
        _client?.Dispose();
        // PrepareAsync 要求存在 client；切换后要重启组件，所以这里立即建好新的实例。
        _client = new();
        _active = NewState();
        CanvasOverlayHost.Visibility = Visibility.Collapsed;
    }

    private sealed record WorkspaceDraft(string Name, string? Directory);

    private async Task<WorkspaceDraft?> AskWorkspaceNameAsync(string title, string initial, string? exceptId, string primaryText, bool withDirectory)
    {
        var text = new TextBox
        {
            Text = initial,
            MaxLength = AgentWorkspaceStore.MaxNameLength,
            MinWidth = 320,
            Header = L("WorkspaceName", "工作区名称"),
            PlaceholderText = L("WorkspaceNamePlaceholder", "例如：网络工具插件")
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(text, "AgentWorkspaceNameInput");
        TextBox? directory = null;
        Grid? directoryRow = null;
        if (withDirectory)
        {
            var box = new TextBox
            {
                MinWidth = 240,
                Header = L("WorkspaceFolder", "工作区目录"),
                PlaceholderText = L("WorkspaceFolderPlaceholder", "留空则在数据目录下自动创建")
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(box, "AgentWorkspaceFolderInput");
            directory = box;
            var browse = new Button
            {
                Content = L("ChooseFolder", "选择…"),
                Height = 32,
                MinWidth = 80,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(browse, "AgentWorkspaceFolderBrowse");
            var pickerTitle = L("ChooseWorkspaceFolder", "选择工作区目录");
            browse.Click += async (_, _) =>
            {
                browse.IsEnabled = false;
                try
                {
                    var picked = await Win32Dialogs.PickFolderAsync(pickerTitle);
                    if (picked is null || string.IsNullOrWhiteSpace(picked)) return;
                    box.Text = picked;
                    if (string.IsNullOrWhiteSpace(text.Text))
                    {
                        var leaf = Path.GetFileName(picked.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (!string.IsNullOrWhiteSpace(leaf))
                            text.Text = leaf.Length > AgentWorkspaceStore.MaxNameLength
                                ? leaf[..AgentWorkspaceStore.MaxNameLength] : leaf;
                    }
                }
                finally { browse.IsEnabled = true; }
            };
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(box);
            Grid.SetColumn(browse, 1);
            row.Children.Add(browse);
            directoryRow = row;
        }
        var error = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = ThemeBrush("SystemFillColorCriticalBrush", Microsoft.UI.Colors.Red)
        };
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(text);
        if (directoryRow is not null) panel.Children.Add(directoryRow);
        panel.Children.Add(error);
        var dialog = new FluentContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = panel,
            PrimaryButtonText = primaryText,
            CloseButtonText = L("Cancel", "取消"),
            DefaultButton = ContentDialogButton.Primary
        };
        void Validate()
        {
            var nameIssue = AgentWorkspaceStore.ValidateName(text.Text, exceptId);
            var dirIssue = withDirectory ? AgentWorkspaceStore.ValidateDirectory(directory?.Text) : AgentWorkspaceDirectoryIssue.None;
            var message = nameIssue != AgentWorkspaceNameIssue.None ? WorkspaceNameIssueText(nameIssue)
                : dirIssue != AgentWorkspaceDirectoryIssue.None ? WorkspaceDirectoryIssueText(dirIssue) : "";
            error.Text = message;
            error.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            dialog.IsPrimaryButtonEnabled = message.Length == 0;
        }
        text.TextChanged += (_, _) => Validate();
        if (directory is not null) directory.TextChanged += (_, _) => Validate();
        dialog.Opened += (_, _) => { text.Focus(FocusState.Programmatic); text.SelectAll(); };
        Validate();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var selected = directory is { } box2 && !string.IsNullOrWhiteSpace(box2.Text) ? box2.Text.Trim() : null;
        return new WorkspaceDraft(text.Text.Trim(), selected);
    }

    private string WorkspaceNameIssueText(AgentWorkspaceNameIssue issue) => issue switch
    {
        AgentWorkspaceNameIssue.Empty => L("WorkspaceNameEmpty", "请输入工作区名称。"),
        AgentWorkspaceNameIssue.TooLong => string.Format(L("WorkspaceNameTooLong", "名称最多 {0} 个字符。"), AgentWorkspaceStore.MaxNameLength),
        AgentWorkspaceNameIssue.Invalid => L("WorkspaceNameInvalid", "名称不能包含 \\ / : * ? \" < > | 等字符。"),
        AgentWorkspaceNameIssue.Duplicate => L("WorkspaceNameDuplicate", "已有同名工作区。"),
        _ => ""
    };

    private string WorkspaceDirectoryIssueText(AgentWorkspaceDirectoryIssue issue) => issue switch
    {
        AgentWorkspaceDirectoryIssue.Invalid => L("WorkspaceFolderInvalid", "目录无效。请选择普通文件夹，不能是盘符根目录或保留位置。"),
        AgentWorkspaceDirectoryIssue.Duplicate => L("WorkspaceFolderDuplicate", "已有工作区使用这个目录。"),
        AgentWorkspaceDirectoryIssue.Overlapping => L("WorkspaceFolderOverlap", "该目录与现有工作区目录重叠，请选择其他位置。"),
        AgentWorkspaceDirectoryIssue.NotDirectory => L("WorkspaceFolderNotDirectory", "所选路径是文件，请选择文件夹。"),
        _ => ""
    };

    private static Brush ThemeBrush(string key, Windows.UI.Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush) return brush;
        return new SolidColorBrush(fallback);
    }
}
