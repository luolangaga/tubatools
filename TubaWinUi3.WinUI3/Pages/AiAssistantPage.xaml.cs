using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Pages;

public sealed partial class AiAssistantPage : Page
{
    private sealed class SessionState
    {
        public string Key { get; } = Guid.NewGuid().ToString("N");
        public string? Id;
        public string Title = L("Untitled", "新会话"), Draft = "", Notice = "", LastPrompt = "", RetryKind = "";
        public AgentPermissionMode Permissions;
        public AgentCanvasMode Mode;
        public int CanvasSent;
        public string CanvasTitle = "";
        public string? ModelKey;
        public bool Error, Attention, Loaded, CustomTitle;
        public int VisibleMessages = 150;
        public CancellationTokenSource? Run;
        public Task? Work;
        public AgentRunProgress? Progress;
        public DateTimeOffset PhaseStarted;
        public IReadOnlyList<AgentMessage> Messages = [];
        public List<AgentOperation> Operations { get; } = [];
        public List<(string Id, string AfterMessageId, AgentQuestionCard View)> Questions { get; } = [];
        public List<AgentAttachment> Attachments { get; } = [];
    }
    private AgentClient? _client;
    private CancellationTokenSource? _lifetime;
    private Window? _owner;
    private bool _preparing, _syncingUi = true, _visible, _closed;
    private int _historyVersion, _focusVersion;
    private readonly AgentSessionList _historyRows = new();
    private readonly DispatcherTimer _draftHistoryTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private SessionState _active = new();
    private readonly Dictionary<string, SessionState> _sessions = [];
    private readonly HashSet<string> _deleting = [];
    private IReadOnlyList<AgentConversation> _history = [];
    private IReadOnlyList<AgentModel> _models = [];
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private static string L(string key, string fallback) => AgentText.L(key, fallback);
    public AiAssistantPage()
    {
        InitializeComponent();
        ConfigureComposerMenu();
        HistoryList.ItemsSource = _historyRows;
        _draftHistoryTimer.Tick += (_, _) => { _draftHistoryTimer.Stop(); if (!_closed) RefreshHistoryView(); };
        _progressTimer.Tick += (_, _) => UpdateRunStatus();
        foreach (var pair in new (UIElement Element, string Key, string Fallback)[] {
            (WorkspaceMenuButton,"WorkspaceMenu","工作区菜单"),(SessionMenuButton,"SessionMenu","会话菜单"),
            (ModelBox,"ModelName","模型"),(PermissionBox,"PermissionName","权限模式"),
            (NewButton,"NewLabel","新建会话"),(CompactNewButton,"NewLabel","新建会话"),(JumpButton,"Jump","跳到最新消息"),
            (ClearAttachmentsButton,"ClearAttachments","清除引用文件"),(AddContextButton,"AddContext","添加上下文"),
            (SendButton,"Send","发送"),(StopButton,"Stop","停止") })
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pair.Element, L(pair.Key,pair.Fallback));
        PermissionBox.Items.Add(L("FullAccess", "全部权限")); PermissionBox.Items.Add(L("AskAccess", "逐次确认"));
        ModeBox.Items.Add(L("ModeChat", "对话")); ModeBox.Items.Add(L("ModeTeach", "教学模式"));
        WorkspacePathText.Text = AgentWorkspace.DirectoryPath;
        ToolTipService.SetToolTip(WorkspacePathText, AgentWorkspace.DirectoryPath);
        _active = NewState(); _syncingUi = false; SyncActive();
    }
    private static SessionState NewState() => new()
    {
        Permissions = AgentPermissionPolicy.Read(),
        Mode = AppSettings.Get("AgentMode") == "teach" ? AgentCanvasMode.Teach : AgentCanvasMode.Chat,
        ModelKey = AppSettings.Get("AgentModel")
    };
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is "plugin")
        {
            CacheDraft(_active); ++_focusVersion; _active = NewState();
            _active.Draft = L("DevelopPrompt", "帮我开发一个图吧工具箱插件：");
            CacheDraft(_active); SyncActive();
        }
        else if (e.Parameter is AgentAssistantFocusRequest request)
        {
            CacheDraft(_active);
            var version = ++_focusVersion;
            _ = FocusInteractionSessionAsync(request.SessionId, version);
        }
    }
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _visible = true;
        var owner = BuiltinToolWindow.FindHostingWindow(this) ?? App.MainWindow;
        if (_owner != owner) { if (_owner is not null) _owner.Closed -= Owner_Closed; _owner = owner; if (_owner is not null) _owner.Closed += Owner_Closed; }
        AttachDropHandler();
        if (_client is null)
        {
            _lifetime = new(); _client = new();
            if (AiComponentService.IsInstalled) _ = PrepareAsync();
            else SetNotice(_active, L("Welcome", "首次使用需要准备 AI 组件。"), attention: true);
        }
        if (_active.Run is not null) _progressTimer.Start();
        RefreshHistoryView(); UpdateButtons();
    }
    private void Page_Unloaded(object sender, RoutedEventArgs e) { _visible = false; DetachDropHandler(); _progressTimer.Stop(); _draftHistoryTimer.Stop(); }
    private void Owner_Closed(object sender, WindowEventArgs e)
    {
        if (e.Handled) return;
        _closed = true; DetachDropHandler(); _progressTimer.Stop(); _draftHistoryTimer.Stop();
        foreach (var state in _sessions.Values.Append(_active).Distinct()) { state.Run?.Cancel(); foreach (var question in state.Questions) question.View.Cancel(); }
        _lifetime?.Cancel(); _client?.Dispose();
        foreach (var bubble in _canvasBubbles.Values)
            try { bubble.Dispose(); } catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        _canvasBubbles.Clear();
        if (_owner is not null) _owner.Closed -= Owner_Closed;
    }
    private bool Ready => !_closed && _client?.IsReady == true && _lifetime?.IsCancellationRequested != true;
    private void UpdateButtons()
    {
        if (_closed) return;
        var running = _active.Run is not null;
        PrepareButton.IsEnabled = !_preparing;
        PrepareButton.Visibility = Ready ? Visibility.Collapsed : Visibility.Visible;
        SendButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        SendButton.IsEnabled = Ready && !_preparing && _pendingImports == 0 && (_active.Id is null || !_deleting.Contains(_active.Id)) && ModelBox.SelectedItem is AgentModel && (!string.IsNullOrWhiteSpace(PromptBox.Text) || _active.Attachments.Count > 0);
        NewButton.IsEnabled = CompactNewButton.IsEnabled = !_preparing;
        HistoryList.IsEnabled = !_preparing;
        ModelBox.IsEnabled = Ready && !_preparing;
        SessionMenuButton.IsEnabled = Ready;
        EmptyState.Visibility = _active.Messages.Count == 0 && !running ? Visibility.Visible : Visibility.Collapsed;
        OlderButton.Visibility = _active.Messages.Count > _active.VisibleMessages ? Visibility.Visible : Visibility.Collapsed;
        RunSpinner.Visibility = running || _preparing ? Visibility.Visible : Visibility.Collapsed;
        RunSpinner.IsActive = (running && _active.Progress?.Phase is not ("question" or "permission")) || _preparing;
        RenderNotice(); UpdateAttachments(); UpdateRunStatus();
    }
    private void SetNotice(SessionState state, string text, bool error = false, bool attention = false, string retry = "")
    {
        state.Notice = text; state.Error = error; state.Attention = attention; state.RetryKind = retry;
        if (ReferenceEquals(state, _active)) RenderNotice();
    }
    private void RenderNotice()
    {
        NoticeBar.Message = _active.Notice;
        NoticeBar.Severity = _active.Error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        NoticeBar.IsOpen = (_active.Error || _active.Attention) && _active.Notice.Length > 0;
        RetryButton.Visibility = _active.Error && _active.RetryKind.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.IsEnabled = Ready && _active.Run is null;
    }
    private async Task PrepareAsync()
    {
        if (_preparing || _client is null || _lifetime is null || _closed) return;
        _preparing = true; UpdateButtons();
        try
        {
            await AiComponentService.InstallAsync(new Progress<string>(text => { if (!_closed) SetNotice(_active, text, attention: true); }), _lifetime.Token);
            SetNotice(_active, L("Starting", "正在连接 AI 组件…"), attention: true);
            await _client.StartAsync(_lifetime.Token);
            _models = await _client.GetModelsAsync(_lifetime.Token);
            ModelBox.ItemsSource = _models;
            SyncActive(); await RefreshHistoryAsync(_lifetime.Token);
            SetNotice(_active, ModelBox.SelectedItem is null ? L("NoFreeModel", "没有可用的免费模型，请选择其他模型。") : "", error: ModelBox.SelectedItem is null);
        }
        catch (Exception ex) { if (!_closed) SetNotice(_active, AgentText.FriendlyError(ex), true, retry: "prepare"); }
        finally { _preparing = false; UpdateButtons(); if (_providersPending && !_providerDialogOpen) await ApplyProvidersAsync(); }
    }
    private async Task RefreshHistoryAsync(CancellationToken ct)
    {
        if (_client is null || _closed) return;
        var version = ++_historyVersion;
        var history = await _client.GetConversationsAsync(ct);
        if (version != _historyVersion || _closed) return;
        _history = history;
        foreach (var item in history) if (_sessions.TryGetValue(item.Id, out var state)) state.Title = item.Title;
        RefreshHistoryView(); SessionTitleText.Text = _active.Title;
    }
    private string DraftId(SessionState state) => "draft:" + state.Key;
    private string DraftTitle(SessionState state)
    {
        if (state.CustomTitle) return state.Title;
        var firstLine = state.Draft.Trim().Split(['\r', '\n'])[0];
        return firstLine.Length == 0 ? L("Untitled", "新会话") : firstLine[..Math.Min(30, firstLine.Length)];
    }
    private void CacheDraft(SessionState state)
    {
        if (state.Id is not null) return;
        if (state.Draft.Length > 0 || state.Attachments.Count > 0 || state.Run is not null || state.CustomTitle) _sessions[DraftId(state)] = state;
        else _sessions.Remove(DraftId(state));
    }
    private IEnumerable<AgentConversation> HistoryRows() => _sessions.Where(p => p.Key.StartsWith("draft:", StringComparison.Ordinal) && p.Value.Id is null)
        .Select(p => new AgentConversation(p.Key, DraftTitle(p.Value) + " · " + L("Draft", "草稿"))).Reverse().Concat(_history);
    private void RefreshHistoryView()
    {
        var old = _syncingUi; _syncingUi = true;
        try
        {
            var rows = HistoryRows().Select(item => _sessions.TryGetValue(item.Id, out var s) && s.Run is not null
                ? item with { Title = item.Title + " · " + (s.Progress?.Phase is "question" or "permission" ? L("WaitingShort", "等待回答") : L("RunningShort", "运行中")) } : item).ToArray();
            var activeId = _active.Id ?? DraftId(_active);
            _historyRows.Merge(rows);
            var selected = _historyRows.FirstOrDefault(s => s.Id == activeId);
            if (!ReferenceEquals(HistoryList.SelectedItem, selected)) HistoryList.SelectedItem = selected;
        }
        finally { _syncingUi = old; }
    }
    private void SyncActive()
    {
        var old = _syncingUi; _syncingUi = true;
        PermissionBox.SelectedIndex = (int)_active.Permissions;
        ModeBox.SelectedIndex = (int)_active.Mode;
        ModelBox.SelectedItem = AgentProtocol.SelectDefault(_models, _active.ModelKey);
        PromptBox.Text = _active.Draft; SessionTitleText.Text = _active.Title;
        _syncingUi = old;
        DetachAllCanvas();
        ClearVisualTimeline(); RenderTimeline(); UpdateCanvas(); UpdateButtons(); RefreshHistoryView();
    }
    private async void History_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi || _client is null || _lifetime is null || HistoryList.SelectedItem is not AgentSessionRow item) return;
        var version = ++_focusVersion;
        if (item.Id.StartsWith("draft:", StringComparison.Ordinal) && _sessions.TryGetValue(item.Id, out var draft)) { _active = draft; SyncActive(); return; }
        if (!_sessions.TryGetValue(item.Id, out var state))
        {
            state = NewState(); state.Id = item.Id; state.Title = _history.FirstOrDefault(h => h.Id == item.Id)?.Title ?? item.Title;
            state.Operations.AddRange(AgentActivityStore.Read(item.Id)); _sessions.Add(item.Id, state);
        }
        _active = state; SyncActive();
        if (state.Loaded || state.Run is not null) return;
        try
        {
            state.Messages = await _client.GetAllMessagesAsync(item.Id, _lifetime.Token); state.Loaded = true;
            if (version == _focusVersion && ReferenceEquals(_active, state)) { RenderTimeline(); UpdateButtons(); JumpToLatest(); }
        }
        catch (Exception ex) { if (!_closed) SetNotice(state, AgentText.FriendlyError(ex), true, retry: "history"); }
    }
    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (_preparing) return;
        CacheDraft(_active); ++_focusVersion; _active = NewState(); SyncActive(); PromptBox.Focus(FocusState.Programmatic);
    }
    private void Prompt_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingUi || SendButton is null) return;
        _active.Draft = PromptBox.Text; CacheDraft(_active); UpdateButtons();
        // Keep draft titles quiet while typing. When typing pauses, only the existing row's
        // Title changes; neither the collection nor other containers are recreated.
        _draftHistoryTimer.Stop(); _draftHistoryTimer.Start();
    }
    private void Model_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi || ModelBox.SelectedItem is not AgentModel model) return;
        _active.ModelKey = model.Key; AppSettings.Set("AgentModel", model.Key); UpdateButtons();
    }
    private void Permission_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi || PermissionBox.SelectedIndex < 0) return;
        _active.Permissions = (AgentPermissionMode)PermissionBox.SelectedIndex;
        AgentPermissionPolicy.Write(_active.Permissions);
    }
    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SidebarColumn is null) return;
        var compact = e.NewSize.Width < 760;
        SidebarColumn.Width = new GridLength(compact ? 0 : 300); Sidebar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactNewButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        ModelBox.MaxWidth = Math.Clamp(e.NewSize.Width - (compact ? 0 : 300) - 280, 100, 340);
        SyncCanvasViewport();
    }
    private static bool IsKeyDown(Windows.System.VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    private void Prompt_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if ((e.Key == Windows.System.VirtualKey.V && IsKeyDown(Windows.System.VirtualKey.Control)) ||
            (e.Key == Windows.System.VirtualKey.Insert && IsKeyDown(Windows.System.VirtualKey.Shift)))
        { e.Handled = true; BeginPaste(); return; }
        if (e.Key != Windows.System.VirtualKey.Enter || IsKeyDown(Windows.System.VirtualKey.Shift)) return;
        e.Handled = true; if (SendButton.IsEnabled) Send_Click(sender, new RoutedEventArgs());
    }
    private void Page_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.N && IsKeyDown(Windows.System.VirtualKey.Control)) { e.Handled = true; New_Click(sender, new RoutedEventArgs()); }
        else if (e.Key == Windows.System.VirtualKey.Escape && _active.Run is not null) { e.Handled = true; Stop_Click(sender, new RoutedEventArgs()); }
        else if (e.Key == Windows.System.VirtualKey.Escape && CloseFullScreenCanvas()) e.Handled = true;
    }
    private async void Prepare_Click(object sender, RoutedEventArgs e) => await PrepareAsync();
    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (_preparing || _sessions.Values.Append(_active).Any(s => s.Run is not null)) return;
        try { _client?.Dispose(); _client = new(); if (File.Exists(AiComponentService.Executable)) File.Delete(AiComponentService.Executable); await PrepareAsync(); }
        catch (Exception ex) { SetNotice(_active, AgentText.FriendlyError(ex), true); }
    }
    private async void Workspace_Click(object sender, RoutedEventArgs e)
    {
        try { AgentWorkspace.Prepare(); await Windows.System.Launcher.LaunchFolderPathAsync(AgentWorkspace.DirectoryPath); }
        catch (Exception ex) { SetNotice(_active, ex.Message, true); }
    }
}
