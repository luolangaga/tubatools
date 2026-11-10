using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Pages;

public sealed partial class AiAssistantPage
{
    /// <summary>Live bubbles, keyed by the canvas document id (one per canvas message).</summary>
    private readonly Dictionary<string, CanvasBubble> _canvasBubbles = [];
    private readonly SemaphoreSlim _canvasQueue = new(1, 1);

    /// <summary>Reads the canvas messages the assistant has sent in this session.</summary>
    private static List<CanvasDocument> CanvasDocuments(SessionState state) =>
        state.Id is null ? [] : AgentCanvasStore.ReadDocuments(state.Id).ToList();

    /// <summary>
    /// Canvas messages are part of the transcript: each one is a bubble anchored right after
    /// the reply that produced it. Nothing is mounted until the assistant actually sends one.
    /// </summary>
    private CanvasBubble CanvasBubbleFor(SessionState state, CanvasDocument document)
    {
        if (state.Id is null) throw new InvalidOperationException("canvas requires a session");
        if (_canvasBubbles.TryGetValue(document.Id, out var existing)) return existing;
        var bubble = new CanvasBubble { OverlayHost = CanvasOverlayHost };
        bubble.SetViewportHeight(ActualHeight > 0 ? ActualHeight : 800);
        bubble.PromptRequested += text => _ = SendFromCanvasAsync(state, text);
        bubble.InsertRequested += InsertFromCanvas;
        bubble.OpenExternalRequested += OpenExternal;
        bubble.InsertImageRequested += () => _ = InsertCanvasImageAsync(state);
        bubble.FullScreenChanged += full =>
        {
            CanvasOverlayHost.Visibility = full || CanvasOverlayHost.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        };
        bubble.Bind(state.Id, document.RelativePath, TitleFor(state, document));
        _canvasBubbles[document.Id] = bubble;
        return bubble;
    }

    private static string TitleFor(SessionState state, CanvasDocument document) =>
        document.Title.Length > 0 ? document.Title : L("CanvasTitle", "教学画布");

    /// <summary>True once the given row is the message a canvas was attached to (or past it).</summary>
    private static bool IsAfterOrAt((string Id, string Role, string Text, AgentOperation? Operation, AgentQuestionCard? Question) row,
        SessionState state, CanvasDocument document)
    {
        if (document.AfterMessageId.Length == 0) return false;
        if (row.Id == document.AfterMessageId) return true;
        if (row.Id.StartsWith(document.AfterMessageId + ":", StringComparison.Ordinal)) return true;
        // Operations/questions carry their own anchor; skip them so the canvas lands after the text.
        return state.Operations.Any(o => o.Id == row.Id && o.AfterMessageId == document.AfterMessageId) ||
               state.Questions.Any(q => q.Id == row.Id && q.AfterMessageId == document.AfterMessageId);
    }

    private static string CanvasSignature(List<CanvasDocument> documents) =>
        "|canvas:" + string.Join(",", documents.Select(d => d.Id + d.RelativePath + d.Hash));

    /// <summary>Inserts (or reuses) the bubble for one document; order is document order.</summary>
    private void MountCanvasDocument(SessionState state, CanvasDocument document, int order)
    {
        if (state.Id is null) return;
        var bubble = CanvasBubbleFor(state, document);
        var id = CanvasRowId(document.Id);
        if (!_nodes.ContainsKey(id))
        {
            _nodes[id] = new TimelineNode();
            _messageOrder.Add(id);
        }
        if (ReferenceEquals(bubble.Parent, Transcript)) return;
        // Place it after the previously mounted canvas (or at the end for the first one) so
        // document order is preserved without re-parenting the WebView2 again.
        var target = Transcript.Children.Count;
        for (var i = order - 1; i >= 0 && i < _canvasAnchors.Count; i--)
        {
            if (_canvasAnchors[i] is { } previous && previous is FrameworkElement element && ReferenceEquals(element.Parent, Transcript))
            {
                target = Transcript.Children.IndexOf(previous) + 1;
                break;
            }
        }
        if (target < Transcript.Children.Count) Transcript.Children.Insert(target, bubble);
        else Transcript.Children.Add(bubble);
        if (order >= 0 && order < _canvasAnchors.Count) _canvasAnchors[order] = bubble;
    }

    private string CanvasRowId(string documentId) => "canvas:" + documentId;

    /// <summary>Anchors recorded while the timeline is rebuilt (index = document order).</summary>
    private readonly List<UIElement?> _canvasAnchors = [];

    /// <summary>Drops bubbles whose document no longer exists so WebView2 instances do not pile up.</summary>
    private void PruneCanvasBubbles(SessionState state)
    {
        var keep = CanvasDocuments(state).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in _canvasBubbles.Where(p => !keep.Contains(p.Key)).ToArray())
        {
            if (pair.Value.Parent is Panel owner) owner.Children.Remove(pair.Value);
            pair.Value.Dispose();
            _canvasBubbles.Remove(pair.Key);
        }
    }

    private void DetachAllCanvas()
    {
        foreach (var bubble in _canvasBubbles.Values)
            if (bubble.Parent is Panel owner) owner.Children.Remove(bubble);
        _canvasAnchors.Clear();
        _lastTranscript = "";
    }

    /// <summary>Esc handled at page level because the WebView2 eats key events while focused.</summary>
    private bool CloseFullScreenCanvas()
    {
        var bubble = _canvasBubbles.Values.FirstOrDefault(b => b.IsFullScreen);
        if (bubble is null) return false;
        bubble.ExitFullScreenPublic();
        return true;
    }

    /// <summary>Keeps each bubble's inline height cap in step with the window.</summary>
    private void SyncCanvasViewport()
    {
        var height = ActualHeight;
        if (height <= 0) return;
        foreach (var bubble in _canvasBubbles.Values) bubble.SetViewportHeight(height);
    }

    private void UpdateCanvas()
    {
        if (_closed) return;
        var state = _active;
        var documents = CanvasDocuments(state);
        CanvasButton.Visibility = documents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CanvasButton.IsEnabled = documents.Count > 0;
        ToolTipService.SetToolTip(CanvasButton, L("CanvasShow", "跳到最新画布"));
        CanvasBanner.IsOpen = state.Mode == AgentCanvasMode.Teach && documents.Count == 0;
        RenderTimeline();
    }

    /// <summary>Jumps to the newest canvas message.</summary>
    private void CanvasToggle_Click(object sender, RoutedEventArgs e)
    {
        var documents = CanvasDocuments(_active);
        if (documents.Count == 0) return;
        var bubble = CanvasBubbleFor(_active, documents[^1]);
        if (bubble.Parent is not null)
        {
            bubble.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });
            return;
        }
        RenderTimeline();
    }

    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingUi || ModeBox.SelectedIndex < 0) return;
        var state = _active;
        state.Mode = (AgentCanvasMode)ModeBox.SelectedIndex;
        AppSettings.Set("AgentMode", state.Mode == AgentCanvasMode.Teach ? "teach" : "chat");
        UpdateCanvas();
    }

    /// <summary>
    /// A canvas button asked for a real turn. Serialized so two fast clicks cannot interleave
    /// runs, and it never touches the composer draft.
    /// </summary>
    private async Task SendFromCanvasAsync(SessionState state, string text)
    {
        var value = text.Trim();
        if (value.Length == 0) return;
        await _canvasQueue.WaitAsync();
        try
        {
            if (state.Work is { } busy) { try { await busy; } catch { } }
            if (_closed || !ReferenceEquals(state, _active)) return;
            if (!Ready || _client is null || _lifetime is null || _preparing || ModelBox.SelectedItem is not AgentModel model) return;
            var title = value.Length > 45 ? value[..45] : value;
            await BeginRunAsync(state, model, title, value, [], fromCanvas: true);
        }
        finally { _canvasQueue.Release(); }
    }

    private void InsertFromCanvas(string text)
    {
        if (_closed || string.IsNullOrEmpty(text)) return;
        var value = text.Length > CanvasBridge.MaxTextLength ? text[..CanvasBridge.MaxTextLength] : text;
        var at = PromptBox.SelectionStart < 0 ? PromptBox.Text.Length : PromptBox.SelectionStart;
        var current = PromptBox.Text;
        PromptBox.Text = current.Insert(Math.Min(at, current.Length), value);
        PromptBox.SelectionStart = Math.Min(at, PromptBox.Text.Length) + value.Length;
        PromptBox.Focus(FocusState.Programmatic);
        _active.Draft = PromptBox.Text; CacheDraft(_active); UpdateButtons();
    }

    private void OpenExternal(string url)
    {
        if (!CanvasBridge.IsAllowedUrl(url)) return;
        try { _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url)); }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException) { }
    }

    private async Task InsertCanvasImageAsync(SessionState state)
    {
        if (state.Id is null) return;
        try
        {
            var paths = Win32Dialogs.PickOpenMultiple(
                L("CanvasImage", "插入图片") + "\0*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.svg;*.avif\0\0",
                L("CanvasImage", "插入图片"));
            if (paths.Count == 0) return;
            var imported = 0;
            foreach (var path in paths)
                if (AgentCanvasStore.ImportImage(state.Id, path) is not null) imported++;
            if (imported == 0) { SetNotice(state, L("CanvasImageFailed", "无法插入该图片，请选择 PNG、JPG、GIF、WEBP、BMP 或 SVG。"), true); return; }
            foreach (var bubble in _canvasBubbles.Values) bubble.PushImages();
            SetNotice(state, string.Format(L("CanvasImageAdded", "已加入 {0} 张图片（assets/）。可以让助手把它们用到这一页。"), imported), attention: true);
        }
        catch (Exception ex) { SetNotice(state, ex.Message, true); }
    }

    /// <summary>
    /// After a turn: archive the page the model just replaced so previous canvas messages stay
    /// intact, then register a new canvas message when the live page actually changed.
    /// </summary>
    private void CommitCanvasTurn(SessionState state)
    {
        if (state.Id is null || _closed) return;
        if (state.Mode != AgentCanvasMode.Teach && state.CanvasSent == 0) return;
        var hash = AgentCanvasStore.HashIndex(state.Id);
        if (hash is null) return;
        var documents = AgentCanvasStore.ReadDocuments(state.Id).ToList();
        var newest = documents.LastOrDefault();
        if (newest is not null && newest.Hash == hash && newest.RelativePath == AgentCanvasStore.IndexName)
        {
            // The model answered without touching the page; attach the reply to the same bubble.
            PushLatestReply(state, newest.Id);
            return;
        }
        if (documents.Count > 0 && documents[^1].RelativePath == AgentCanvasStore.IndexName)
        {
            // Freeze the previous live page before it gets overwritten next turn.
            var archived = AgentCanvasStore.Snapshot(state.Id);
            if (archived is not null)
                documents[^1] = documents[^1] with { RelativePath = archived };
        }
        var document = new CanvasDocument(Guid.NewGuid().ToString("N"), state.Messages.LastOrDefault()?.Id ?? "",
            AgentCanvasStore.IndexName, state.CanvasTitle, hash, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        documents.Add(document);
        AgentCanvasStore.SaveDocuments(state.Id, documents);
        state.CanvasSent = documents.Count;
        if (ReferenceEquals(state, _active)) RenderTimeline();
        PushLatestReply(state, document.Id);
    }

    private void PushLatestReply(SessionState state, string documentId)
    {
        if (!_canvasBubbles.TryGetValue(documentId, out var bubble)) return;
        var last = state.Messages.LastOrDefault(m => m.Role == "assistant" && m.Error is null && m.Text.Length > 0);
        if (last is not null) bubble.PushMessage("assistant", last.Text);
        bubble.PushState(false, "", "");
    }

    private void PushCanvasState()
    {
        if (_closed) return;
        var state = _active;
        if (state.Id is null) return;
        foreach (var bubble in _canvasBubbles.Values)
            bubble.PushState(state.Run is not null, state.Progress?.Phase ?? "", state.Progress?.Detail ?? "");
    }

    /// <summary>Host-side "open this HTML as a canvas message" for any ```html block.</summary>
    private bool OpenHtmlInCanvas(string html, string? title)
    {
        var state = _active;
        if (state.Id is null)
        {
            SetNotice(state, L("CanvasNeedsSession", "先发送一条消息，画布就会出现在对话里。"), attention: true);
            return false;
        }
        var path = AgentCanvasStore.WriteArchive(state.Id, html);
        if (path is null) return false;
        var documents = AgentCanvasStore.ReadDocuments(state.Id).ToList();
        var document = new CanvasDocument(Guid.NewGuid().ToString("N"), state.Messages.LastOrDefault()?.Id ?? "",
            path, title ?? L("CanvasTitle", "教学画布"), AgentCanvasStore.HashText(html), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        documents.Add(document);
        AgentCanvasStore.SaveDocuments(state.Id, documents);
        state.CanvasSent = documents.Count;
        UpdateCanvas();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_canvasBubbles.TryGetValue(document.Id, out var bubble))
                bubble.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true });
        });
        return true;
    }
}
