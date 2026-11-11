using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Pages;

public sealed partial class AiAssistantPage
{
    private readonly Dictionary<string, TimelineNode> _nodes = [];
    private readonly List<string> _messageOrder = [];
    private string _lastTranscript = "";
    private sealed record QueuedTurn(AgentModel Model, string Title, string Prompt,
        IReadOnlyList<AgentAttachment> Attachments, bool FromCanvas, bool RestoreDraftOnFailure);
    private sealed class TimelineNode
    {
        public MarkdownView? Markdown { get; init; }
        public AgentOperationView? Operation { get; init; }
        public string? LastText { get; set; }
    }
    private void ClearVisualTimeline() { Transcript.Children.Clear(); _nodes.Clear(); _messageOrder.Clear(); _lastTranscript = ""; }
    private void RenderTimeline()
    {
        if (_closed) return;
        var state = _active;
        var canvases = CanvasDocuments(state);
        var rows = new List<(string Id, string Role, string Text, AgentOperation? Operation, AgentQuestionCard? Question)>();
        foreach (var message in state.Messages.TakeLast(state.VisibleMessages))
        {
            // Teaching markers stay in the agent prompt but must not clutter the transcript.
            if (message.Role == "user") rows.Add((message.Id, "user", AgentCanvasPrompt.Strip(message.Text), null, null));
            else if (message.Content is { Count: > 0 } parts)
                foreach (var part in parts) rows.Add((part.Id, "assistant", part.Text, part.Operation, null));
            else if (message.Text.Length > 0) rows.Add((message.Id, "assistant", message.Text, null, null));
            rows.AddRange(state.Operations.Where(o => o.AfterMessageId == message.Id).Select(o => (o.Id, "assistant", "", (AgentOperation?)o, (AgentQuestionCard?)null)));
            rows.AddRange(state.Questions.Where(q => q.AfterMessageId == message.Id).Select(q => (q.Id, "assistant", "", (AgentOperation?)null, (AgentQuestionCard?)q.View)));
        }
        rows.AddRange(state.Operations.Where(o => o.AfterMessageId.Length == 0).Select(o => (o.Id, "assistant", "", (AgentOperation?)o, (AgentQuestionCard?)null)));
        rows.AddRange(state.Questions.Where(q => q.AfterMessageId.Length == 0).Select(q => (q.Id, "assistant", "", (AgentOperation?)null, (AgentQuestionCard?)q.View)));
        // Canvas messages are assistant messages: each one sits right after the reply that
        // produced it. Nothing is shown until the assistant actually sends a page.
        _canvasAnchors.Clear();
        if (canvases.Count > 0)
        {
            var ordered = canvases.OrderBy(c => c.CreatedAt).ToList();
            var merged = new List<(string Id, string Role, string Text, AgentOperation? Operation, AgentQuestionCard? Question)>(rows.Count + ordered.Count);
            var next = 0;
            foreach (var row in rows)
            {
                merged.Add(row);
                // Everything up to and including the anchoring message precedes its canvas.
                while (next < ordered.Count && IsAfterOrAt(row, state, ordered[next]))
                {
                    _canvasAnchors.Add(null);
                    merged.Add((CanvasRowId(ordered[next].Id), "canvas", "", null, null));
                    next++;
                }
            }
            while (next < ordered.Count)
            {
                _canvasAnchors.Add(null);
                merged.Add((CanvasRowId(ordered[next].Id), "canvas", "", null, null));
                next++;
            }
            rows = merged;
        }
        var signature = state.Key + CanvasSignature(canvases) + string.Join("\u001f", rows.Select(r => r.Id + r.Text + r.Operation));
        if (_lastTranscript == signature) return;
        var follow = TranscriptScroll.ScrollableHeight - TranscriptScroll.VerticalOffset < 100;
        if (_messageOrder.Count > rows.Count || !_messageOrder.SequenceEqual(rows.Take(_messageOrder.Count).Select(r => r.Id))) ClearVisualTimeline();
        var canvasCursor = 0;
        foreach (var row in rows)
        {
            if (row.Role == "canvas")
            {
                if (canvasCursor < canvases.Count) MountCanvasDocument(state, canvases[canvasCursor], canvasCursor);
                canvasCursor++;
                continue;
            }
            if (!_nodes.TryGetValue(row.Id, out var node))
            {
                if (row.Question is { } card) { node = new(); Transcript.Children.Add(card); }
                else if (row.Operation is not null)
                {
                    var operation = new AgentOperationView(); node = new() { Operation = operation }; Transcript.Children.Add(operation);
                }
                else
                {
                    var markdown = new MarkdownView(); node = new() { Markdown = markdown };
                    var bodyPanel = new StackPanel { Spacing = 8 }; bodyPanel.Children.Add(markdown);
                    if (row.Role == "user" && state.Messages.FirstOrDefault(m => m.Id == row.Id)?.Attachments is { Count: > 0 } attached)
                    {
                        var previews = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                        foreach (var attachment in attached) previews.Children.Add(AttachmentPreview(attachment));
                        bodyPanel.Children.Add(new ScrollViewer { Content = previews, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
                    }
                    var bubble = new Border { Child = bodyPanel };
                    if (row.Role == "user") bubble.Style = (Style)Resources["AgentUserBubble"];
                    var menuNode = node; bubble.ContextFlyout = MessageMenu(() => menuNode.LastText ?? row.Text);
                    Transcript.Children.Add(bubble);
                }
                _nodes.Add(row.Id, node); _messageOrder.Add(row.Id);
            }
            if (row.Operation is { } value) node.Operation?.Update(value);
            else if (node.LastText != row.Text && node.Markdown is { } body) { body.Markdown = row.Text; node.LastText = row.Text; }
        }
        _lastTranscript = signature;
        PruneCanvasBubbles(state);
        if (follow) JumpToLatest();
        UpdateButtons();
    }

    private static string showCanvasSignature(List<CanvasDocument> documents) =>
        "|canvas:" + string.Join(",", documents.Select(d => d.Id + d.RelativePath + d.Hash));
    private MenuFlyout MessageMenu(Func<string> text)
    {
        var menu = new MenuFlyout();
        var copy = new MenuFlyoutItem { Text = L("CopyMessage", "复制消息"), Icon = new FontIcon { Glyph = "\uE8C8" } };
        copy.Click += (_, _) =>
        {
            var result = ClipboardService.TrySetText(text());
            if (!result.Success) SetNotice(_active, result.Error?.Message ?? "剪贴板正被占用，请稍后重试。", true);
        };
        var canvas = new MenuFlyoutItem { Text = L("CanvasOpenHtml", "在画布中打开"), Icon = new FontIcon { Glyph = "\uE7F4" } };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(canvas, "AgentOpenInCanvas");
        canvas.Click += (_, _) =>
        {
            if (AgentCanvasStore.ExtractHtmlBlock(text()) is { } html) OpenHtmlInCanvas(html, null);
            else SetNotice(_active, L("CanvasNoHtml", "这条消息里没有可用的 HTML 页面。"), true);
        };
        menu.Items.Add(copy);
        menu.Items.Add(canvas);
        menu.Opening += (_, _) => canvas.IsEnabled = AgentCanvasStore.ExtractHtmlBlock(text()) is not null && _active.Id is not null;
        return menu;
    }
    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (!Ready || _preparing || _client is null || _lifetime is null || ModelBox.SelectedItem is not AgentModel model) return;
        var state = _active;
        if (_pendingImports > 0) return;
        var text = PromptBox.Text.Trim();
        if (text.Length == 0 && state.Attachments.Count == 0) return;
        if (text.Length == 0) text = L("ReadAttached", "请查看引用的文件。");
        var attachments = state.Attachments.ToArray();
        if (attachments.Any(a => a.IsImage) && !model.SupportsImages)
        { SetNotice(state, L("VisionModelRequired", "当前模型不支持图片，请选择支持图片输入的模型。自定义提供商可在配置中启用图片输入。"), true); return; }
        var title = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (title.Length > 45) title = title[..45];
        var request = new QueuedTurn(model, title, BuildPrompt(text, attachments), attachments, false, true);
        state.Draft = ""; state.Attachments.Clear(); PromptBox.Text = ""; SetNotice(state, "");

        if (state.Run is not null || state.Draining)
        {
            if (_cutIn)
            {
                if (state.Run is not null) state.SuppressDraftRestore = true;
                state.Queue.Insert(0, request);
                state.Run?.Cancel();
                SetNotice(state, L("CutInNotice", "已插队，正在停止当前任务并优先发送。"), attention: true);
            }
            else
            {
                state.Queue.Add(request);
                SetNotice(state, L("QueuedNotice", "已排队，当前任务结束后自动发送。"), attention: true);
            }
            UpdateQueueView(state);
            UpdateButtons();
            return;
        }

        if (state.Queue.Count > 0)
        {
            state.Queue.Add(request);
            UpdateQueueView(state);
            await DrainQueueAsync(state);
            return;
        }
        await DrainQueueAsync(state, request);
    }

    /// <summary>Runs one turn, then continues with queued turns in order.</summary>
    private async Task DrainQueueAsync(SessionState state, QueuedTurn? first = null)
    {
        if (state.Draining)
        {
            if (first is not null) { state.Queue.Add(first); UpdateQueueView(state); }
            return;
        }
        state.Draining = true;
        try
        {
            var current = first;
            while (!_closed && !state.Discarded)
            {
                if (current is null)
                {
                    if (state.Queue.Count == 0) break;
                    current = state.Queue[0];
                    state.Queue.RemoveAt(0);
                    UpdateQueueView(state);
                }
                var item = current;
                current = null;
                await StartRunAsync(state, item);
            }
        }
        finally
        {
            state.Draining = false;
            UpdateQueueView(state);
            if (ReferenceEquals(state, _active)) UpdateButtons();
        }
    }

    private void QueueMode_Click(object sender, RoutedEventArgs e)
    {
        _cutIn = !_cutIn;
        AppSettings.Set("AgentQueueMode", _cutIn ? "cutin" : "queue");
        UpdateButtons();
    }

    private void UpdateQueueView(SessionState? state = null)
    {
        state ??= _active;
        if (!ReferenceEquals(state, _active) || QueueBar is null) return;
        var signature = state.Key + "|" + state.Queue.Count + "|" + string.Join('\u001f', state.Queue.Select(q => q.Title));
        if (signature == _queueSignature) return;
        _queueSignature = signature;
        QueueBar.Visibility = state.Queue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueItems.Children.Clear();
        if (state.Queue.Count == 0) return;
        QueueTitle.Text = string.Format(L("QueueCount", "排队中（{0}）"), state.Queue.Count);
        foreach (var item in state.Queue.ToArray())
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = item.Title + (item.Attachments.Count > 0
                ? string.Format(L("QueueAttachments", "（{0} 个附件）"), item.Attachments.Count) : "");
            row.Children.Add(new TextBlock
            {
                Text = label, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });
            var remove = new Button
            {
                Style = (Style)Resources["AgentIconButton"],
                Content = new FontIcon { Glyph = "\uE711", FontSize = 10 }
            };
            ToolTipService.SetToolTip(remove, L("QueueRemove", "移除排队消息"));
            Grid.SetColumn(remove, 1);
            remove.Click += (_, _) =>
            {
                state.Queue.Remove(item);
                UpdateQueueView(state);
                UpdateButtons();
            };
            row.Children.Add(remove);
            QueueItems.Children.Add(row);
        }
    }

    /// <summary>Non-image attachments become explicit local paths the agent can read.</summary>
    private string BuildPrompt(string text, IReadOnlyList<AgentAttachment> attachments)
    {
        var references = attachments.Where(a => !a.IsImage).ToArray();
        return text + (references.Length == 0 ? "" : "\n\n" + L("ReferencedFiles", "参考文件（本机路径，请使用文件读取工具查看）：") + "\n" +
            string.Join("\n", references.Select(a => "- " + System.Text.Json.JsonSerializer.Serialize(a.Path, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))));
    }

    private async Task StartRunAsync(SessionState state, QueuedTurn turn)
    {
        var run = CancellationTokenSource.CreateLinkedTokenSource(_lifetime!.Token);
        state.Run = run; CacheDraft(state); state.LastPrompt = turn.Prompt; state.Progress = new("waiting");
        state.PhaseStarted = DateTimeOffset.UtcNow;
        if (ReferenceEquals(state, _active)) _progressTimer.Start();
        SetNotice(state, "");
        if (ReferenceEquals(state, _active)) UpdateButtons();
        state.Work = RunTurnAsync(state, turn.Model, turn.Title, turn.Prompt, run, turn.Attachments,
            turn.FromCanvas, turn.RestoreDraftOnFailure);
        await state.Work;
    }

    private async Task RunTurnAsync(SessionState state, AgentModel model, string title, string prompt, CancellationTokenSource run,
        IReadOnlyList<AgentAttachment> attachments, bool fromCanvas, bool restoreDraft)
    {
        var client = _client!;
        var token = run.Token;
        var permissions = state.Permissions;
        var priorUserIds = state.Messages.Where(m => m.Role == "user").Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        try
        {
            if (state.Id is null)
            {
                var sessionTitle = state.CustomTitle ? state.Title : title[..Math.Min(title.Length, 45)];
                state.Id = await client.CreateConversationAsync(model, sessionTitle, token, permissions);
                state.Title = sessionTitle; _sessions.Remove(DraftId(state)); _sessions[state.Id] = state;
                if (ReferenceEquals(state, _active)) SessionTitleText.Text = state.Title;
                await RefreshHistoryAsync(token);
            }
            ComputerControlService.BeginSession(state.Id, () => state.Run?.Cancel());
            AiBrowserControl.BeginTurn();
            AgentWorkspace.RefreshProjectIndex();
            // The canvas only exists once the session id is known; teaching turns get both a
            // starter page and a short marker so AGENTS.md/skill rules stay the source of truth.
            if (state.Mode == AgentCanvasMode.Teach)
            {
                AgentCanvasStore.EnsureDirectory(state.Id);
                if (state.CanvasTitle.Length == 0) state.CanvasTitle = L("CanvasTitle", "教学画布");
                prompt = AgentCanvasPrompt.Prefix(state.Id) + prompt + (fromCanvas ? AgentCanvasPrompt.CanvasTurnHint(state.Id) : "");
            }
            var previous = await client.GetMessagesAsync(state.Id, token);
            var loader = await AgentPluginAutoLoader.CaptureAsync(previous, token);
            void Update(IReadOnlyList<AgentMessage> messages)
            {
                state.Messages = AgentHistory.Merge(state.Messages, messages); state.Loaded = true;
                if (ReferenceEquals(state, _active))
                {
                    RenderTimeline();
                    foreach (var bubble in _canvasBubbles.Values) bubble.RefreshIfChanged();
                }
            }
            void Progress(AgentRunProgress progress)
            {
                if (state.Id is { } progressSessionId) ComputerControlService.UpdateProgress(progressSessionId, progress);
                if (state.Progress != progress) { state.Progress = progress; state.PhaseStarted = DateTimeOffset.UtcNow; RefreshHistoryView(); }
                if (ReferenceEquals(state, _active)) { if (_visible) _progressTimer.Start(); UpdateButtons(); PushCanvasState(); }
            }
            Task<bool> Approve(AgentPermission permission) => ApproveAsync(state, permission, token);
            Task<IReadOnlyDictionary<string, object>?> Ask(AgentQuestion question, CancellationToken ct) => AskAsync(state, question, ct);
            await client.RunAsync(state.Id, model, prompt, Update, Approve, token, Ask, Progress, permissions, attachments);
            var loaded = await loader.LoadAsync(state.Messages, operation => ReportHostOperation(state, operation), token);
            for (var repair = 0; loaded.Any(r => !r.Success) && repair < 2; repair++)
            {
                Progress(new("tool", L("RepairingPlugin", "正在修复插件加载问题")));
                var retryLoader = await AgentPluginAutoLoader.CaptureAsync(state.Messages, token);
                await client.ResumeWithPluginFeedbackAsync(state.Id, model, AgentClient.PluginFeedback(loaded), Update, Approve, token, Ask, Progress, permissions);
                var retried = await retryLoader.LoadAsync(state.Messages, operation => ReportHostOperation(state, operation), token);
                if (retried.Count == 0) break;
                loaded = retried;
            }
            await client.RecordPluginResultsAsync(state.Id, loaded, token);
            AgentWorkspace.RefreshProjectIndex();
            SetNotice(state, loaded.Count > 0 ? string.Join("\n", loaded.Select(r => r.Message)) : L("Done", "任务已完成。"), loaded.Any(r => !r.Success));
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                if (state.Id is not null && _lifetime is { IsCancellationRequested: false })
                {
                    try { using var refresh = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); refresh.CancelAfter(TimeSpan.FromSeconds(5)); state.Messages = AgentHistory.Merge(state.Messages, await client.GetMessagesAsync(state.Id, refresh.Token)); } catch { }
                }
                var accepted = state.Messages.Any(m => m.Role == "user" && !priorUserIds.Contains(m.Id));
                if (!accepted && !fromCanvas && restoreDraft && !state.SuppressDraftRestore)
                {
                    // Canvas-initiated turns never borrow the user's composer: restore nothing,
                    // just report the failure on the page that asked for it.
                    if (state.Draft.Length == 0) state.Draft = title;
                    foreach (var attachment in attachments)
                        if (!state.Attachments.Any(a => a.Path == attachment.Path)) state.Attachments.Add(attachment);
                }
                SetNotice(state, AgentText.FriendlyError(ex), ex is not OperationCanceledException, retry: ex is OperationCanceledException || fromCanvas ? "" : "run");
                if (fromCanvas)
                    foreach (var bubble in _canvasBubbles.Values) bubble.Notice(AgentText.FriendlyError(ex), true);
                if (ReferenceEquals(state, _active)) { if (!fromCanvas && PromptBox.Text.Length == 0) PromptBox.Text = state.Draft; RenderTimeline(); }
            }
        }
        finally
        {
            run.Dispose(); state.Run = null; state.Progress = null; state.SuppressDraftRestore = false;
            if (state.Id is not null) ComputerControlService.EndSession(state.Id);
            if (!_closed)
            {
                if (ReferenceEquals(state, _active)) { _progressTimer.Stop(); UpdateButtons(); PushCanvasState(); }
                RefreshHistoryView();
                try { if (_lifetime is { IsCancellationRequested: false }) await RefreshHistoryAsync(_lifetime.Token); } catch { }
                await ApplyProvidersAsync();
                AfterTurn(state);
            }
        }
    }

    /// <summary>Applies a rewritten canvas page after the turn and announces it as a message.</summary>
    private void AfterTurn(SessionState state)
    {
        if (_closed) return;
        CommitCanvasTurn(state);
        foreach (var bubble in _canvasBubbles.Values) bubble.RefreshIfChanged();
        PushCanvasState();
    }
    private void ReportHostOperation(SessionState state, AgentOperation operation)
    {
        operation = operation with { AfterMessageId = state.Messages.LastOrDefault()?.Id ?? "" };
        var index = state.Operations.FindIndex(o => o.Id == operation.Id);
        if (index < 0) state.Operations.Add(operation); else state.Operations[index] = operation;
        if (state.Id is not null) AgentActivityStore.Save(state.Id, state.Operations);
        if (ReferenceEquals(state, _active)) RenderTimeline();
    }
    private async Task<IReadOnlyDictionary<string, object>?> AskAsync(SessionState state, AgentQuestion question, CancellationToken ct)
    {
        var card = new AgentQuestionCard(question);
        state.Questions.Add(("form:" + question.Id, state.Messages.LastOrDefault()?.Id ?? "", card));
        if (ReferenceEquals(state, _active)) { RenderTimeline(); JumpToLatest(); }
        using var cancel = ct.Register(() => DispatcherQueue.TryEnqueue(card.Cancel));
        return await card.Answer.WaitAsync(ct);
    }
    private async Task<bool> ApproveAsync(SessionState state, AgentPermission permission, CancellationToken ct)
    {
        var field = new AgentQuestionField("decision", "string", AgentProtocol.ToolLabel(permission.Action),
            permission.Message + "\n" + string.Join("\n", permission.Resources), true, false,
            [new("allow", L("AllowOnce", "允许一次"), ""), new("reject", L("Reject", "拒绝"), "")],
            System.Text.Json.JsonSerializer.SerializeToElement(new { key = "decision", type = "string", required = true }));
        var answer = await AskAsync(state, new("permission:" + permission.Id, L("ApproveTitle", "允许这次操作？"), [field]), ct);
        return answer is not null && answer.TryGetValue("decision", out var choice) && choice is "allow";
    }
    private void UpdateRunStatus()
    {
        var progress = _active.Progress;
        RunStatus.Visibility = progress is null ? Visibility.Collapsed : Visibility.Visible;
        if (progress is null) return;
        var text = progress.Phase switch
        {
            "thinking" => L("Thinking", "正在分析与规划"), "responding" => L("Responding", "正在生成回复"),
            "tool" => progress.Detail.Length > 0 ? progress.Detail : L("ExecutingTool", "正在执行工具"), "question" => L("WaitingQuestion", "等待你的回答"),
            "preparing-tool" => L("PreparingTool", "正在准备工具调用"), "compacting" => L("Compacting", "正在整理上下文"),
            "retrying" => progress.Detail.Length > 0 ? progress.Detail : L("RetryingModel", "模型服务正在重试"),
            "reconnecting" => L("Reconnecting", "正在重新连接 AI 组件"),
            "stalled" => L("WaitingLong", "模型尚未返回新进展，仍在等待；可随时停止或稍后切换模型"),
            "permission" => L("WaitingPermission", "等待操作确认"), _ => L("WaitingModel", "等待模型返回下一步")
        };
        RunStatusText.Text = string.Format(L("ProgressElapsed", "{0} · {1} 秒"), text, Math.Max(0, (int)(DateTimeOffset.UtcNow - _active.PhaseStarted).TotalSeconds));
    }
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        var state = _active;
        state.Queue.Clear();
        UpdateQueueView(state);
        state.Run?.Cancel();
    }
    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (_active.Run is not null || _active.Draining) return;
        if (_active.RetryKind == "prepare") { await PrepareAsync(); return; }
        if (!Ready) return;
        if (_active.RetryKind == "history" && _client is not null && _lifetime is not null && _active.Id is { } id)
        {
            var state = _active;
            try { state.Messages = await _client.GetAllMessagesAsync(id, _lifetime.Token); state.Loaded = true; SetNotice(state, ""); if (ReferenceEquals(state,_active)) { RenderTimeline(); UpdateButtons(); } }
            catch (Exception ex) { SetNotice(state, AgentText.FriendlyError(ex), true, retry: "history"); }
            return;
        }
        if (string.IsNullOrWhiteSpace(PromptBox.Text)) PromptBox.Text = L("ContinuePrompt", "继续处理上一条请求。");
        Send_Click(sender, e);
    }
    private void JumpToLatest() => DispatcherQueue.TryEnqueue(() => { if (!_closed) { TranscriptScroll.UpdateLayout(); TranscriptScroll.ChangeView(null, TranscriptScroll.ScrollableHeight, null, true); } });
    private void Jump_Click(object sender, RoutedEventArgs e) => JumpToLatest();
    private void Transcript_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e) =>
        JumpButton.Visibility = TranscriptScroll.ScrollableHeight - TranscriptScroll.VerticalOffset > 120 ? Visibility.Visible : Visibility.Collapsed;
    private void Older_Click(object sender, RoutedEventArgs e) { _active.VisibleMessages += 150; ClearVisualTimeline(); RenderTimeline(); }
}
