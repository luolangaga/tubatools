using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Pages;

public sealed partial class AiAssistantPage
{
    private async Task FocusInteractionSessionAsync(string sessionId, int version)
    {
        if (_closed || string.IsNullOrWhiteSpace(sessionId)) return;
        if (!_sessions.TryGetValue(sessionId, out var state))
        {
            state = NewState(); state.Id = sessionId;
            state.Title = _history.FirstOrDefault(h => h.Id == sessionId)?.Title ?? L("Untitled", "新会话");
            state.Operations.AddRange(AgentActivityStore.Read(sessionId));
            _sessions[sessionId] = state;
        }
        _active = state; SyncActive();
        if (!state.Loaded && state.Run is null && _client is not null && _lifetime is not null)
        {
            try { state.Messages = await _client.GetAllMessagesAsync(sessionId, _lifetime.Token); state.Loaded = true; }
            catch (Exception ex) { if (!_closed) SetNotice(state, AgentText.FriendlyError(ex), true, retry: "history"); }
        }
        if (_closed || version != _focusVersion || !ReferenceEquals(_active, state)) return;
        RenderTimeline(); JumpToLatest();
        if (state.Questions.Count > 0 && state.Questions[^1].View is { } question)
            DispatcherQueue.TryEnqueue(() => { if (!_closed && ReferenceEquals(_active, state)) question.FocusFirstAnswer(); });
    }

    private void History_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) { args.ItemContainer.ContextFlyout = null; return; }
        if (args.Item is AgentSessionRow item)
        {
            args.ItemContainer.ContextFlyout = BuildSessionMenu(item.Id);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(args.ItemContainer, "AgentSession:" + item.Id);
        }
    }
    private void SessionMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = BuildSessionMenu(_active.Id ?? (_sessions.ContainsKey(DraftId(_active)) ? DraftId(_active) : null));
        menu.ShowAt(SessionMenuButton);
    }
    private MenuFlyout BuildSessionMenu(string? id)
    {
        var menu = new MenuFlyout();
        var fresh = new MenuFlyoutItem { Text = L("NewLabel", "新建会话"), Icon = new FontIcon { Glyph = "\uE70F" } };
        fresh.Click += New_Click; menu.Items.Add(fresh);
        var providers = new MenuFlyoutItem { Text = L("Providers", "AI 提供商"), Icon = new FontIcon { Glyph = "\uE8D7" } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(providers, "AgentProviders"); providers.Click += Providers_Click; menu.Items.Add(providers);
        if (HistoryRows().Any())
        {
            var sessions = new MenuFlyoutSubItem { Text = L("SwitchSession", "切换会话") };
            foreach (var item in HistoryRows())
            {
                var entry = new MenuFlyoutItem { Text = item.Title };
                entry.Click += (_, _) =>
                {
                    if (HistoryList.ItemsSource is IEnumerable<AgentConversation> rows) HistoryList.SelectedItem = rows.FirstOrDefault(r => r.Id == item.Id);
                };
                sessions.Items.Add(entry);
            }
            menu.Items.Add(sessions);
        }
        if (id is null) return menu;
        menu.Items.Add(new MenuFlyoutSeparator());
        var rename = new MenuFlyoutItem { Text = L("Rename", "重命名"), Icon = new FontIcon { Glyph = "\uE70F" } };
        var export = new MenuFlyoutItem { Text = L("Export", "导出 Markdown…"), Icon = new FontIcon { Glyph = "\uE74E" } };
        var stop = new MenuFlyoutItem { Text = L("Stop", "停止"), Icon = new FontIcon { Glyph = "\uE71A" } };
        var delete = new MenuFlyoutItem { Text = L("Delete", "删除会话"), Icon = new FontIcon { Glyph = "\uE74D" } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(rename, "AgentRename");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(export, "AgentExport");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(delete, "AgentDelete");
        rename.Click += async (_, _) => await RenameSessionAsync(id);
        export.Click += async (_, _) => await ExportSessionAsync(id);
        stop.Click += (_, _) =>
        {
            if (!_sessions.TryGetValue(id, out var state)) return;
            state.Queue.Clear(); state.Run?.Cancel();
            if (ReferenceEquals(state, _active)) UpdateQueueView(state);
        };
        delete.Click += async (_, _) => await DeleteSessionAsync(id);
        menu.Items.Add(rename); menu.Items.Add(export); menu.Items.Add(stop); menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(delete);
        menu.Opening += (_, _) =>
        {
            rename.IsEnabled = delete.IsEnabled = Ready && !_deleting.Contains(id);
            export.IsEnabled = Ready || _sessions.TryGetValue(id, out var cached) && (cached.Loaded || cached.Id is null);
            stop.Visibility = _sessions.TryGetValue(id, out var state) && (state.Run is not null || state.Draining) ? Visibility.Visible : Visibility.Collapsed;
        };
        return menu;
    }
    private string TitleFor(string id) => _sessions.TryGetValue(id, out var state) ? state.Id is null ? DraftTitle(state) : state.Title : _history.FirstOrDefault(h => h.Id == id)?.Title ?? L("Untitled", "新会话");
    private async Task RenameSessionAsync(string id)
    {
        if (!Ready || _client is null || _lifetime is null) return;
        var text = new TextBox { Text = TitleFor(id), MaxLength = 160, MinWidth = 280 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(text, "AgentRenameText");
        var dialog = new FluentContentDialog
        {
            XamlRoot = XamlRoot, Title = L("RenameSession", "重命名会话"), Content = text,
            PrimaryButtonText = L("Save", "保存"), CloseButtonText = L("Cancel", "取消"), DefaultButton = ContentDialogButton.Primary
        };
        text.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(text.Text);
        text.Loaded += (_, _) => { text.Focus(FocusState.Programmatic); text.SelectAll(); };
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            if (_sessions.TryGetValue(id, out var state) && state.Id is null) { state.Title = text.Text.Trim(); state.CustomTitle = true; }
            else { await _client.RenameConversationAsync(id, text.Text.Trim(), _lifetime.Token); if (state is not null) state.Title = text.Text.Trim(); }
            await RefreshHistoryAsync(_lifetime.Token);
        }
        catch (Exception ex) { if (!_closed) SetNotice(_active, AgentText.FriendlyError(ex), true); }
    }
    private async Task ExportSessionAsync(string id)
    {
        if (_lifetime is null) return;
        try
        {
            var title = TitleFor(id);
            var fileName = string.Concat(title.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
            if (fileName.Length == 0) fileName = "AI-session";
            var path = Win32Dialogs.PickSave("Markdown\0*.md\0\0", "md", fileName + ".md");
            if (path is null) return;
            var messages = _sessions.TryGetValue(id, out var draft) && draft.Id is null
                ? new AgentMessage[] { new("draft", "user", draft.Draft, "") }
                : Ready && _client is not null ? await _client.GetAllMessagesAsync(id, _lifetime.Token)
                : _sessions.TryGetValue(id, out var state) ? state.Messages : [];
            var markdown = AgentHistory.ToMarkdown(title, messages);
            var operations = _sessions.TryGetValue(id, out var current) ? current.Operations : AgentActivityStore.Read(id);
            if (operations.Count > 0) markdown += "\n" + AgentHistory.ToMarkdown(L("HostActions", "宿主执行记录"),
                [new("host", "assistant", "", "", Content: operations.Select(o => new AgentContent(o.Id, "", o)).ToArray())]);
            await File.WriteAllTextAsync(path, markdown, _lifetime.Token);
        }
        catch (Exception ex) { if (!_closed) SetNotice(_active, ex.Message, true); }
    }
    private async Task DeleteSessionAsync(string id)
    {
        if (!Ready || _client is null || _lifetime is null || !_deleting.Add(id)) return;
        UpdateButtons();
        try
        {
            if (_sessions.TryGetValue(id, out var state))
            {
                state.Discarded = true; state.Queue.Clear();
                state.Run?.Cancel(); if (state.Work is not null) await state.Work;
                foreach (var question in state.Questions) question.View.Cancel();
            }
            if (state?.Id is { } sessionId) { await _client.DeleteConversationAsync(sessionId, _lifetime.Token); AgentActivityStore.Delete(sessionId); }
            else if (!id.StartsWith("draft:", StringComparison.Ordinal)) { await _client.DeleteConversationAsync(id, _lifetime.Token); AgentActivityStore.Delete(id); }
            _sessions.Remove(id);
            if (state is not null) foreach (var key in _sessions.Where(p => ReferenceEquals(p.Value, state)).Select(p => p.Key).ToArray()) _sessions.Remove(key);
            if (_active.Id == id || ReferenceEquals(_active, state)) { _active = NewState(); SyncActive(); }
            await RefreshHistoryAsync(_lifetime.Token);
        }
        catch (Exception ex) { if (!_closed) SetNotice(_active, AgentText.FriendlyError(ex), true); }
        finally { _deleting.Remove(id); UpdateButtons(); }
    }
}
