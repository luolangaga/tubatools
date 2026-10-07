using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TubaWinUi3.Controls.AgentChat;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Agent;
using TubaWinUi3.Services.Ai;

namespace TubaWinUi3.Pages;

/// <summary>
/// 一条助手消息的构建状态（流式文本 / 思考指示 / 内容宿主）。
/// </summary>
internal sealed class AssistantBubble
{
    public required Border Root { get; set; }
    public required TextBlock StreamingText { get; init; }
    public required StackPanel StreamingRow { get; init; }
    public required StackPanel ThinkingRow { get; init; }
    public required TextBlock StatusText { get; init; }
    public required ReasoningDisclosureControl Reasoning { get; init; }
    public required ContentControl ContentHost { get; init; }
}

/// <summary>
/// AI 智能代理完整页面：Agent 循环 + 代码构建消息气泡（项目已验证的可靠模式）
/// + 步骤链可视化（运行中展开、完成后自动折叠为摘要）+ 危险操作确认卡片。
/// 引擎为 AgentSession（多轮工具调用循环）。
/// </summary>
public sealed partial class AiAgentPage : UserControl
{
    private readonly DispatcherQueue _dq;

    private AgentSession? _session;
    private AssistantBubble? _streamingBubble;
    private StepChainControl? _activeChain;
    private Control? _activeConfirmationCard;
    private bool _isProcessing;
    private bool _awaitingConfirmation;
    private bool _suppressToggleEvent;
    private bool _syncingCombos;
    private string? _lastUserText;

    /// <summary>API 请求发出后超过该时长仍无首个 chunk，视为排队中。</summary>
    private static readonly TimeSpan QueueThreshold = TimeSpan.FromSeconds(10);
    private DispatcherTimer? _queueTimer;
    private DispatcherTimer? _dotsTimer;
    private int _dots;
    private bool _roundHasText;
    private bool _followLatest = true;

    private static readonly (string Text, string Glyph)[] QuickQuestions =
    [
        ("新电脑怎么验机", "\uE950"),
        ("电脑卡顿怎么办", "\uE7E8"),
        ("内存占用过高怎么优化", "\uE8F1"),
        ("查看系统配置", "\uE770"),
    ];

    public AiAgentPage() : this(compact: false)
    {
    }

    internal AiAgentPage(bool compact)
    {
        InitializeComponent();
        if (compact)
        {
            HeaderTokenText.Visibility = Visibility.Collapsed;
            FullAccessToggle.Visibility = Visibility.Collapsed;
            SkillsButton.Visibility = Visibility.Collapsed;
            AiSettingsButton.Visibility = Visibility.Collapsed;
            MsgPanel.MaxWidth = 760;
            InputBox.PlaceholderText = "输入问题（Enter 发送，Shift+Enter 换行）";
        }
        _dq = DispatcherQueue.GetForCurrentThread();

        // 输入法组合跟踪：组合中按 Enter 是确认候选词，不发送也不换行
        InputBox.TextCompositionStarted += (_, _) => _isComposing = true;
        InputBox.TextCompositionEnded += (_, _) => _isComposing = false;

        MsgPanel.ChildrenTransitions = new TransitionCollection
        {
            new EntranceThemeTransition { FromVerticalOffset = 16, IsStaggeringEnabled = true },
            new RepositionThemeTransition()
        };

        _suppressToggleEvent = true;
        FullAccessToggle.IsOn = AgentToolContext.IsFullAccess;
        _suppressToggleEvent = false;
        UpdateTokenUsage();
        UpdateRunState();
        BuildQuickPills();
        RefreshProviderCombos();
        LoadLatestConversation();
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // 页面淡入
        var sb = new Storyboard();
        var opacity = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(300)) };
        Storyboard.SetTarget(opacity, this);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        sb.Children.Add(opacity);
        sb.Begin();
    }

    /// <summary>页面卸载（内置工具关闭时调用）：取消后台运行、保存并释放会话。</summary>
    public void Unload()
    {
        StopQueueWatch();
        var session = _session;
        _session = null;
        session?.Dispose();
    }

    // ---------- 会话 ----------

    private AgentSession CreateSession()
    {
        var session = AgentSession.CreateNew();
        HookSession(session);
        return session;
    }

    private void HookSession(AgentSession session)
    {
        session.TextChunk += chunk => _dq.TryEnqueue(() => SafeInvoke(() => AppendChunk(chunk)));
        session.ReasoningChunk += chunk => _dq.TryEnqueue(() => SafeInvoke(() => AppendReasoningChunk(chunk)));
        session.StepStarted += step => _dq.TryEnqueue(() => SafeInvoke(() => OnStepStarted(step)));
        session.StepCompleted += step => _dq.TryEnqueue(() => SafeInvoke(() => OnStepCompleted(step)));
        session.ConfirmationsRequested += requests => _dq.TryEnqueue(() => SafeInvoke(() => OnConfirmations(requests)));
        session.Error += error => _dq.TryEnqueue(() => SafeInvoke(() => OnError(error)));
        session.RunCompleted += () => _dq.TryEnqueue(() => SafeInvoke(() => { StopQueueWatch(); FinalizeStreaming(); }));
        session.RoundStarted += () => _dq.TryEnqueue(() => SafeInvoke(StartQueueWatch));
        session.StepGroupCompleted += summary => _dq.TryEnqueue(() => SafeInvoke(() => OnStepGroupCompleted(summary)));
    }

    /// <summary>
    /// UI 线程事件回调的安全壳：任何异常都转为错误气泡，
    /// 防止 marshal 回调中的未处理异常导致 XAML 崩溃（0xc000027b）。
    /// </summary>
    private void SafeInvoke(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            AgentDebugLog.Error("UI 回调异常", ex);
            try { AddErrorBubble($"界面处理出错：{ex.Message}"); } catch { }
        }
    }

    // ---------- 排队检测（API 请求发出后久无响应 → 「正在排队」+ 动画） ----------

    /// <summary>新一轮 API 请求开始：重置状态为「正在思考…」，并启动排队定时器。</summary>
    private void StartQueueWatch()
    {
        StopQueueWatch();
        _roundHasText = false;
        // 多轮运行中上一轮的气泡可能已被定稿（_streamingBubble = null），
        // 这里只复位仍在使用的气泡；迟到的排队提示会在 QueueTimer_Tick 里自建气泡。
        if (_streamingBubble is not null)
        {
            _streamingBubble.ThinkingRow.Visibility = Visibility.Visible;
            SetStreamingStatus("正在思考…");
        }

        _queueTimer = new DispatcherTimer { Interval = QueueThreshold };
        _queueTimer.Tick += QueueTimer_Tick;
        _queueTimer.Start();
    }

    /// <summary>超过阈值仍无首个 chunk → 切换为「正在排队等待响应」+ 点号动画。</summary>
    private void QueueTimer_Tick(object? sender, object e)
    {
        _queueTimer?.Stop();
        if (_roundHasText) return;

        // 气泡可能尚未创建（如工具步骤后的新一轮）→ 先建气泡再显示排队状态
        if (_streamingBubble is null)
            _streamingBubble = BeginAssistantBubble();
        _streamingBubble.ThinkingRow.Visibility = Visibility.Visible;

        _dots = 0;
        SetStreamingStatus("正在排队等待响应");
        _dotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _dotsTimer.Tick += (_, _) =>
        {
            _dots = (_dots + 1) % 4;
            SetStreamingStatus("正在排队等待响应" + new string('·', _dots));
        };
        _dotsTimer.Start();
    }

    private void StopQueueWatch()
    {
        _queueTimer?.Stop();
        _queueTimer = null;
        _dotsTimer?.Stop();
        _dotsTimer = null;
    }

    private void SetStreamingStatus(string text)
    {
        if (_streamingBubble is null) return;
        _streamingBubble.StatusText.Text = text;
    }

    // ---------- 发送 / 停止 ----------

    private async void SendButton_Click(object sender, RoutedEventArgs e)
        => await SendAsync(InputBox.Text);

    private bool _isComposing; // 输入法组合中（Enter 用于确认候选词）

    private void InputBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.OriginalKey != Windows.System.VirtualKey.Enter || _isComposing) return;

        // Shift+Enter 换行；单独 Enter 发送（不用 e.KeyStatus.IsMenuKeyDown：
        // CorePhysicalKeyStatus 在部分键盘驱动/输入法/远程桌面下误报，且 Alt+Enter 无实际用途）
        if (!Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            _ = SendAsync(InputBox.Text);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        StopButton.IsEnabled = false;
        RunStateText.Text = "正在停止…";
        _session.Cancel();
        if (_awaitingConfirmation)
        {
            _awaitingConfirmation = false;
            _activeConfirmationCard?.IsEnabled = false;
            AddSystemBubble("已取消待确认操作");
            UpdateInputState();
        }
    }

    private void FullAccessToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        AgentToolContext.IsFullAccess = FullAccessToggle.IsOn;
        UpdateRunState();
        if (FullAccessToggle.IsOn)
            AddSystemBubble("⚠️ 已开启完全访问模式：AI 可直接执行命令、修改注册表等操作，不再逐项确认。");
    }

    // ---------- 技能（Skills）菜单 ----------

    private void SkillsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshSkillsPanel();
        if (Resources["SkillsFlyout"] is Flyout flyout)
            flyout.ShowAt(SkillsButton);
    }

    /// <summary>按当前会话的技能状态重建菜单项（技能默认全部加载）。</summary>
    private void RefreshSkillsPanel()
    {
        _session ??= CreateSession();
        if (Resources["SkillsFlyout"] is not Flyout { Content: StackPanel panel }) return;
        panel.Children.Clear();

        // 顶部说明：消除"技能默认关闭"的误解
        panel.Children.Add(new TextBlock
        {
            Text = "技能默认全部启用；取消勾选即禁用（勾选/取消后立即生效，下一条消息按新状态执行）",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(8, 2, 8, 8),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        foreach (var skill in AgentSkillRegistry.All)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new FontIcon
            {
                Glyph = skill.Glyph,
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            var texts = new StackPanel { Spacing = 1 };
            texts.Children.Add(new TextBlock
            {
                Text = skill.DisplayName,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            texts.Children.Add(new TextBlock
            {
                Text = skill.Description,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 230,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            });
            content.Children.Add(texts);

            var check = new CheckBox
            {
                Content = content,
                IsChecked = _session.ActiveSkillIds.Contains(skill.Id),
                Tag = skill.Id,
                MinWidth = 0,
                Padding = new Thickness(0, 6, 0, 6)
            };
            check.Checked += SkillToggle_Changed;
            check.Unchecked += SkillToggle_Changed;
            panel.Children.Add(check);
        }
    }

    private void SkillToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string id } cb) return;
        if (_session is null) return;
        var skill = AgentSkillRegistry.Find(id);
        if (skill is null) return;

        var on = cb.IsChecked == true;
        _session.SetSkillEnabled(id, on);
        _session.Save();
        AddSystemBubble(on
            ? $"✅ 已启用技能：{skill.DisplayName}（{skill.Description}）"
            : $"已禁用技能：{skill.DisplayName}（恢复默认状态可重新勾选）");
    }

    private async Task SendAsync(string text)
    {
        if (_isProcessing || _awaitingConfirmation) return;
        text = text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _session ??= CreateSession();
        _lastUserText = text;

        AddUserBubble(text);

        _streamingBubble = BeginAssistantBubble();

        _isProcessing = true;
        UpdateInputState();
        InputBox.Text = "";
        SmartScroll();

        try
        {
            await Task.Run(() => _session.SendAsync(text));
        }
        catch (OperationCanceledException)
        {
            FinalizeStreaming(); // 清理排队/思考中的空气泡
            AddSystemBubble("已取消");
        }
        catch (Exception ex)
        {
            AddErrorBubble(AgentErrorPolicy.FormatApiError(ex));
        }
        finally
        {
            StopQueueWatch();
            _isProcessing = false;
            UpdateInputState();
            // 页面已关闭（Unload 已置空 _session）时跳过，避免空引用
            if (_session is { } session)
            {
                session.Save();
                TitleText.Text = session.Title;
            }
            UpdateTokenUsage();
            SmartScroll();
        }
    }

    // ---------- 确认流 ----------

    private void Confirmation_Resolved(object? sender, ConfirmationResolvedEventArgs e)
        => _ = ResumeAfterConfirmationAsync(e.Decisions);

    private void Plan_Resolved(object? sender, PlanResolvedEventArgs e)
    {
        if (_session is null || !_awaitingConfirmation) return;
        if (_pendingPlanRequest is not { } request) return;

        _ = ResumeAfterConfirmationAsync(
            [new AgentConfirmationDecision { Request = request, Confirmed = e.Approved }]);
    }

    private AgentConfirmationRequest? _pendingPlanRequest;

    private async Task ResumeAfterConfirmationAsync(IReadOnlyList<AgentConfirmationDecision> decisions)
    {
        if (_session is null || !_awaitingConfirmation || decisions.Count == 0) return;
        _awaitingConfirmation = false;
        _activeConfirmationCard = null;
        _isProcessing = true;
        UpdateInputState();

        try
        {
            await Task.Run(() => _session.ResumeConfirmationsAsync(decisions));
        }
        catch (OperationCanceledException)
        {
            FinalizeStreaming(); // 清理排队/思考中的空气泡
            AddSystemBubble("已取消");
        }
        catch (Exception ex)
        {
            AddErrorBubble(AgentErrorPolicy.FormatApiError(ex));
        }
        finally
        {
            StopQueueWatch();
            _isProcessing = false;
            UpdateInputState();
            // 页面已关闭（Unload 已置空 _session）时跳过，避免空引用
            if (_session is { } session)
                session.Save();
            UpdateTokenUsage();
            SmartScroll();
        }
    }

    // ---------- 会话事件 ----------

    private void AppendReasoningChunk(string chunk)
    {
        if (_streamingBubble is null)
            _streamingBubble = BeginAssistantBubble();

        _roundHasText = true;
        StopQueueWatch();
        _streamingBubble.ThinkingRow.Visibility = Visibility.Collapsed;
        _streamingBubble.Reasoning.Visibility = Visibility.Visible;
        _streamingBubble.Reasoning.Append(chunk);
        SmartScroll();
    }

    private void AppendChunk(string chunk)
    {
        // 步骤链之后的新文本块 → 新的助手气泡（文本与步骤按顺序交错展示）
        if (_streamingBubble is null)
        {
            _streamingBubble = BeginAssistantBubble();
        }

        // 首个 chunk 到达：本轮未排队，停止排队检测并收起思考指示
        if (!_roundHasText)
        {
            _roundHasText = true;
            StopQueueWatch();
            if (_streamingBubble.ThinkingRow is { } row)
                row.Visibility = Visibility.Collapsed;
        }

        _streamingBubble.StreamingText.Text += chunk;
        SmartScroll();
    }

    /// <summary>
    /// 首个工具步骤：定稿当前文本气泡，并在其后的消息流位置插入
    /// 独立的步骤链节点（执行中展开，整链完成后自动折叠为摘要）。
    /// </summary>
    private void OnStepStarted(AgentStep step)
    {
        if (_activeChain is null)
        {
            FinalizeStreaming();
            _activeChain = CreateStepChainNode();
        }
        _activeChain.RunVm.AddStep(new StepRowVm(step));
        SmartScroll();
    }

    private void OnStepCompleted(AgentStep step)
    {
        if (_activeChain is null) return;
        var row = _activeChain.RunVm.FindByCallId(step.CallId ?? "");
        row?.Update(step);
        SmartScroll();
    }

    private void OnStepGroupCompleted(AgentStepGroupSummary summary)
    {
        FinalizeStreaming();
        _activeChain?.RunVm.Complete(summary);
        _activeChain = null;
        UpdateTokenUsage();
        SmartScroll();
    }

    private void OnConfirmations(IReadOnlyList<AgentConfirmationRequest> requests)
    {
        StopQueueWatch();
        FinalizeStreaming();
        _awaitingConfirmation = true;
        UpdateInputState();

        if (requests.Count == 1 && requests[0].Kind == "plan")
        {
            _pendingPlanRequest = requests[0];
            var planCard = new PlanCardControl
            {
                Request = requests[0],
                Margin = new Thickness(38, 0, 0, 10)
            };
            planCard.Resolved += Plan_Resolved;
            _activeConfirmationCard = planCard;
            MsgPanel.Children.Add(planCard);
        }
        else
        {
            var card = new ConfirmationCardControl
            {
                Requests = requests,
                Margin = new Thickness(38, 0, 0, 10)
            };
            card.Resolved += Confirmation_Resolved;
            _activeConfirmationCard = card;
            MsgPanel.Children.Add(card);
        }
        SmartScroll();
    }

    private void OnError(string error)
    {
        StopQueueWatch();
        FinalizeStreaming();
        AddErrorBubble(error);
        SmartScroll();
    }

    /// <summary>流式气泡定稿：文本渲染为 markdown 内容；无文本的气泡（纯工具轮次）移除。</summary>
    private void FinalizeStreaming()
    {
        if (_streamingBubble is null) return;
        var bubble = _streamingBubble;
        _streamingBubble = null;

        var content = bubble.StreamingText.Text;
        if (!string.IsNullOrWhiteSpace(content))
        {
            var rendered = AiMarkdownRenderer.Render(content);
            rendered.MaxWidth = 860;
            bubble.ContentHost.Content = rendered;
        }
        bubble.Reasoning.Complete(collapse: true);
        if (string.IsNullOrWhiteSpace(content) && !bubble.Reasoning.HasContent && MsgPanel.Children.Contains(bubble.Root))
            MsgPanel.Children.Remove(bubble.Root);
        bubble.StreamingRow.Visibility = Visibility.Collapsed;
    }

    /// <summary>创建独立的步骤链节点（与文本气泡按事件顺序交错排列）。</summary>
    private StepChainControl CreateStepChainNode()
    {
        var chain = new StepChainControl
        {
            Margin = new Thickness(38, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        chain.RunVm = new RunVm();
        MsgPanel.Children.Add(chain);
        return chain;
    }

    // ---------- 历史 / 新对话 ----------

    private void LoadLatestConversation()
    {
        var conversations = AiAssistantService.ListConversations();
        if (conversations.Count > 0)
        {
            LoadConversation(conversations[0]);
            // 历史会话也展示当前技能状态，避免"技能没加载"的误解
            ShowActiveSkillsBubble();
        }
        else
            Welcome();
    }

    private void Welcome()
    {
        if (AiService.IsUsingDefaultModel)
        {
            AddSystemBubble("⚠️ 自带模型可能出现排队/限额满速，质量低下等问题。推荐使用 DeepSeek V4 Pro。\n\n你可以在 设置 → AI 服务 中配置自己的 API Key 和模型。");
        }
        AddSystemBubble("你好！我是图吧助手（智能代理版），可以帮你诊断系统问题、优化配置、执行操作、搜索最新资讯。\n\n我可以：\n- 诊断问题并**自动执行**修复操作（危险操作会先请你确认）\n- 读写文件、执行命令、下载文件\n- 联网搜索最新硬件评测、驱动、价格\n- **配电脑/装机时自动上京东查实时价格**（「电脑选购」技能，可在顶部「技能」菜单开关）\n- 制定多步任务计划并逐项执行\n- 记住你的偏好与任务进度（会话记忆）\n\n试试下面的快捷问题，或直接输入你的需求！");
        ShowActiveSkillsBubble();
    }

    /// <summary>展示「已加载技能」可见提示（技能菜单开关会即时生效）。</summary>
    private void ShowActiveSkillsBubble()
    {
        var skills = AgentSkillRegistry.All;
        if (skills.Count == 0) return;

        var parts = skills.Select(s => $"{s.DisplayName}（{s.Description}）");
        AddSystemBubble($"🔧 已加载技能：{string.Join("、", parts)}\n可在顶部「技能」菜单随时开关，咨询对应场景时我将按技能要求执行。");
    }

    private void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        ResetToNewChat();
    }

    /// <summary>重置为新对话：释放当前会话并清空界面（删除当前会话时复用）。</summary>
    private void ResetToNewChat()
    {
        _session?.Dispose();
        _session = null;
        _streamingBubble = null;
        _activeChain = null;
        _awaitingConfirmation = false;
        _pendingPlanRequest = null;
        MsgPanel.Children.Clear();
        TitleText.Text = "新对话";
        Welcome();
        UpdateInputState();
        SmartScroll();
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;
        _session?.Save();

        var flyout = AiHistoryMenu.Build(
            AiAssistantService.ListConversations(),
            onOpen: LoadConversation,
            onRename: OnRenameConversation,
            onDelete: OnDeleteConversation);

        flyout.ShowAt(sender as FrameworkElement);
    }

    /// <summary>重命名会话：当前打开的会话同步更新内存标题，并立即持久化到 meta.json。</summary>
    private async void OnRenameConversation(ConversationMeta conv)
    {
        var newTitle = await AiHistoryMenu.PromptRenameAsync(XamlRoot, conv.Title);
        if (newTitle is null) return;

        if (_session?.Id == conv.Id)
        {
            _session.Rename(newTitle);
            TitleText.Text = newTitle;
        }
        AiAssistantService.RenameConversation(conv.Id, newTitle);
    }

    /// <summary>删除会话：确认后移除全部关联文件；删的是当前会话则回到新对话。</summary>
    private async void OnDeleteConversation(ConversationMeta conv)
    {
        if (!await AiHistoryMenu.ConfirmDeleteAsync(XamlRoot, conv.Title)) return;

        AiAssistantService.DeleteConversation(conv.Id);
        if (_session?.Id == conv.Id)
            ResetToNewChat();
    }

    private void LoadConversation(ConversationMeta meta)
    {
        if (_isProcessing) return;

        _session?.Dispose();
        _session = AgentSession.Load(meta);
        HookSession(_session);
        _streamingBubble = null;
        _activeChain = null;
        _awaitingConfirmation = false;
        _pendingPlanRequest = null;
        MsgPanel.Children.Clear();

        // 展示记录（文本 + 步骤链按原始顺序恢复）；旧会话无记录时回退到协议消息
        var display = AiAssistantService.LoadConversationDisplay(meta.Id);
        var protocolMessages = AiAssistantService.LoadConversation(meta.Id);
        var assistantMessages = new Queue<AiChatMessage>(protocolMessages
            .Where(m => m.Role == "assistant" && (m.ToolCalls is null || m.ToolCalls.Count == 0)));
        if (display.Any(i => i.Type is "text" or "steps"))
        {
            foreach (var item in display)
            {
                if (item.Type == "meta") continue; // token 统计条目，不渲染
                if (item.Type == "steps")
                {
                    AddPersistedStepChain(item);
                }
                else if (item.Role == "user")
                {
                    AddUserBubble(item.Content);
                }
                else if (item.Role == "assistant")
                {
                    var protocol = assistantMessages.Count > 0 ? assistantMessages.Dequeue() : null;
                    var reasoning = !string.IsNullOrWhiteSpace(item.ReasoningContent)
                        ? item.ReasoningContent
                        : protocol?.ReasoningContent ?? "";
                    AddRestoredAssistant(item.Content, reasoning);
                }
            }
        }
        else
        {
            foreach (var msg in AiAssistantService.LoadConversation(meta.Id))
            {
                if (msg.Role == "system") continue;
                if (msg.Role == "user")
                {
                    if (msg.Content.StartsWith("[ACTION_CONFIRMED]", StringComparison.OrdinalIgnoreCase)) continue;
                    if (msg.Content.StartsWith("[TOOL_RESULT]", StringComparison.OrdinalIgnoreCase)) continue;
                    AddUserBubble(msg.Content);
                }
                else if (msg.Role == "assistant" &&
                         (!string.IsNullOrWhiteSpace(msg.Content) || !string.IsNullOrWhiteSpace(msg.ReasoningContent)))
                {
                    AddRestoredAssistant(msg.Content, msg.ReasoningContent ?? "");
                }
            }
        }

        TitleText.Text = meta.Title;
        UpdateTokenUsage();
        UpdateInputState();
        SmartScroll();
    }

    /// <summary>从展示记录恢复步骤链节点（折叠态，可点击展开查看每步详情）。</summary>
    private void AddPersistedStepChain(ConversationDisplayItem item)
    {
        var chain = new StepChainControl
        {
            Margin = new Thickness(38, 0, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var run = new RunVm();
        foreach (var snap in item.Steps)
            run.Steps.Add(new StepRowVm(snap.ToAgentStep()));
        run.SummaryText = string.IsNullOrWhiteSpace(item.SummaryText)
            ? $"{item.Steps.Count} 步完成"
            : item.SummaryText;
        run.IsExpanded = false;
        chain.RunVm = run;
        chain.ShowCollapsed();
        MsgPanel.Children.Add(chain);
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastUserText is { Length: > 0 } text)
            await SendAsync(text);
    }

    private void AddRestoredAssistant(string content, string reasoning)
    {
        var bubble = BeginAssistantBubble(streaming: false);
        if (!string.IsNullOrWhiteSpace(reasoning))
        {
            bubble.Reasoning.Visibility = Visibility.Visible;
            bubble.Reasoning.SetContent(reasoning);
        }
        if (!string.IsNullOrWhiteSpace(content))
        {
            var rendered = AiMarkdownRenderer.Render(content);
            rendered.MaxWidth = 860;
            bubble.ContentHost.Content = rendered;
        }
        bubble.StreamingRow.Visibility = Visibility.Collapsed;
    }

    // ---------- 气泡构建 ----------

    private void AddUserBubble(string text)
    {
        var bubble = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(12, 12, 4, 12),
            Padding = new Thickness(14, 9, 14, 9),
            MaxWidth = 520,
            Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"],
                IsTextSelectionEnabled = true
            }
        };
        MsgPanel.Children.Add(bubble);
        AnimateMessageIn(bubble, fromX: 18);
    }

    private AssistantBubble BeginAssistantBubble(bool streaming = true)
    {
        // 头像
        var avatar = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(12),
            VerticalAlignment = VerticalAlignment.Top,
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Child = new FontIcon
            {
                Glyph = "\uE946",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"]
            }
        };

        // 名称行
        var nameRow = new TextBlock
        {
            Text = "图吧助手",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        // 流式文本 + 思考指示（ProgressRing + 状态文字；排队时文字变「正在排队等待响应…」）
        var streamingText = new TextBlock
        {
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        var statusText = new TextBlock
        {
            Text = streaming ? "正在思考…" : "",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };
        var thinkingRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new ProgressRing { Width = 14, Height = 14, IsActive = true },
                statusText
            }
        };
        var streamingRow = new StackPanel { Spacing = 4, Children = { streamingText, thinkingRow } };

        var reasoning = new ReasoningDisclosureControl
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 2, 0, 4)
        };

        // 最终内容宿主
        var contentHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };

        var bubble = new AssistantBubble
        {
            Root = null!,
            StreamingText = streamingText,
            StreamingRow = streamingRow,
            ThinkingRow = thinkingRow,
            StatusText = statusText,
            Reasoning = reasoning,
            ContentHost = contentHost
        };

        var body = new StackPanel
        {
            Spacing = 8,
            Children = { nameRow, reasoning, streamingRow, contentHost }
        };

        var grid = new Grid
        {
            ColumnSpacing = 10,
            Margin = new Thickness(0, 0, 0, 12)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(body, 1);
        grid.Children.Add(avatar);
        grid.Children.Add(body);

        var root = new Border
        {
            Child = grid,
            Margin = new Thickness(0, 0, 0, 2)
        };
        bubble.Root = root;

        MsgPanel.Children.Add(root);
        AnimateMessageIn(root, fromX: -18);
        return bubble;
    }

    private void AddSystemBubble(string text)
    {
        var bubble = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10, 16, 10),
            MaxWidth = 700,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            }
        };
        MsgPanel.Children.Add(bubble);
        AnimateMessageIn(bubble, fromY: 10);
    }

    private void AddErrorBubble(string text)
    {
        var retryBtn = new Button
        {
            Content = "重试",
            FontSize = 12,
            Padding = new Thickness(12, 4, 12, 4),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        retryBtn.Click += RetryButton_Click;

        var bubble = new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 10, 16, 10),
            MaxWidth = 700,
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new FontIcon
                            {
                                Glyph = "\uE783",
                                FontSize = 14,
                                Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                            },
                            new TextBlock
                            {
                                Text = "出错了",
                                FontSize = 12,
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                            }
                        }
                    },
                    // 详细错误（异常链）可滚动查看，不占大块版面
                    new ScrollViewer
                    {
                        MaxHeight = 240,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new TextBlock
                        {
                            Text = text,
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap,
                            IsTextSelectionEnabled = true,
                            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                        }
                    },
                    retryBtn
                }
            }
        };
        MsgPanel.Children.Add(bubble);
        AnimateMessageIn(bubble, fromY: 10);
    }

    private static void AnimateMessageIn(UIElement element, double fromX = 0, double fromY = 0)
    {
        element.Opacity = 0;
        element.RenderTransform = new TranslateTransform { X = fromX, Y = fromY };

        var sb = new Storyboard();
        var opacity = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacity, element);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        sb.Children.Add(opacity);

        if (Math.Abs(fromX) > 0.1)
        {
            var translateX = new DoubleAnimation
            {
                From = fromX,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(240)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(translateX, element);
            Storyboard.SetTargetProperty(translateX, "(UIElement.RenderTransform).(TranslateTransform.X)");
            sb.Children.Add(translateX);
        }

        if (Math.Abs(fromY) > 0.1)
        {
            var translateY = new DoubleAnimation
            {
                From = fromY,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(240)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(translateY, element);
            Storyboard.SetTargetProperty(translateY, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            sb.Children.Add(translateY);
        }

        sb.Begin();
    }

    // ---------- UI 辅助 ----------

    private void BuildQuickPills()
    {
        var index = 0;
        foreach (var (text, glyph) in QuickQuestions)
        {
            var btn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 5,
                    Children =
                    {
                        new FontIcon { Glyph = glyph, FontSize = 12 },
                        new TextBlock { Text = text, FontSize = 12 }
                    }
                },
                Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 5, 12, 5),
                Tag = text
            };
            btn.Click += (_, _) => _ = SendAsync(text);
            QuickPillPanel.Children.Add(btn);

            // 错落入场动画（间隔 50ms）
            btn.RenderTransform = new TranslateTransform();
            var sb = new Storyboard { BeginTime = TimeSpan.FromMilliseconds(index++ * 50) };
            var opacity = new DoubleAnimation
            {
                From = 0, To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(opacity, btn);
            Storyboard.SetTargetProperty(opacity, "Opacity");
            var translate = new DoubleAnimation
            {
                From = 10, To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(250)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(translate, btn);
            Storyboard.SetTargetProperty(translate, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            sb.Children.Add(opacity);
            sb.Children.Add(translate);
            sb.Begin();
        }
    }

    // ---------- 提供商 / 模型切换 ----------

    /// <summary>同步顶栏提供商/模型下拉框与当前选中状态（选中状态全局持久化）。</summary>
    private void RefreshProviderCombos()
    {
        _syncingCombos = true;
        try
        {
            AiProviderStore.SyncLocalProviderModels();
            var providers = AiProviderStore.GetProviders();
            var selectedId = AiProviderStore.SelectedProviderId;
            // 传副本（见 SettingsPage.RefreshAiProviderList：活列表原地修改会导致
            // ItemsSourceView 快照过期，同步设置 SelectedItem 抛 E_INVALIDARG）
            ProviderCombo.ItemsSource = providers.ToList();
            ProviderCombo.SelectedItem = providers.FirstOrDefault(p => p.Id == selectedId) ?? providers.FirstOrDefault();

            var provider = AiProviderStore.SelectedProvider;
            var modelId = AiProviderStore.SelectedModelId;
            ModelCombo.ItemsSource = provider.Models.ToList();
            ModelCombo.SelectedItem = provider.Models.FirstOrDefault(m => m.Id.Equals(modelId, StringComparison.OrdinalIgnoreCase))
                                      ?? provider.Models.FirstOrDefault();
        }
        finally
        {
            _syncingCombos = false;
        }
        UpdateServiceStatus();
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombos || _isProcessing) return;
        if (ProviderCombo.SelectedItem is not AiProvider provider) return;

        var prevId = AiProviderStore.SelectedProviderId;
        if (provider.Id == prevId) return;

        AiProviderStore.SetSelected(provider.Id);
        RefreshProviderCombos();
        NotifyModelSwitch(provider.Name, AiProviderStore.SelectedModelId);
    }

    private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombos || _isProcessing) return;
        if (ProviderCombo.SelectedItem is not AiProvider provider) return;
        if (ModelCombo.SelectedItem is not AiModelOption model) return;

        var prev = AiProviderStore.SelectedModelId;
        if (model.Id.Equals(prev, StringComparison.OrdinalIgnoreCase)) return;

        AiProviderStore.SetSelected(provider.Id, model.Id);
        UpdateServiceStatus();
        NotifyModelSwitch(provider.Name, model.Id);
    }

    /// <summary>跳转设置页 AI 服务配置（提供商 / API Key / 模型）。</summary>
    private void AiSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.NavigateToSettings("AiApiEndpoint");
    }

    /// <summary>
    /// 提供商管理弹窗：列出全部提供商，可改自定义提供商名称、删除、新建。
    /// 预设提供商（含「本地模型」）不可改名/删除。
    /// </summary>
    private async void ManageProvidersButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isProcessing) return;

        var panel = new StackPanel { Spacing = 8, MinWidth = 360 };

        void Rebuild()
        {
            panel.Children.Clear();

            foreach (var provider in AiProviderStore.GetProviders().ToList())
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                if (provider.IsPreset)
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = provider.Name,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                    if (provider.Kind == ProviderKind.Local)
                    {
                        var badge = new TextBlock
                        {
                            Text = "本地",
                            FontSize = 11,
                            Margin = new Thickness(8, 0, 0, 0),
                            VerticalAlignment = VerticalAlignment.Center,
                            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                        };
                        Grid.SetColumn(badge, 1);
                        row.Children.Add(badge);
                    }
                }
                else
                {
                    var box = new TextBox
                    {
                        Text = provider.Name,
                        PlaceholderText = "提供商名称",
                    };
                    var id = provider.Id;
                    box.LostFocus += (_, _) => AiProviderStore.RenameProvider(id, box.Text);
                    row.Children.Add(box);

                    var del = new Button { Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 }, Padding = new Thickness(8, 4, 8, 4) };
                    del.Click += (_, _) =>
                    {
                        AiProviderStore.DeleteProvider(id);
                        Rebuild();
                    };
                    Grid.SetColumn(del, 1);
                    row.Children.Add(del);
                }

                panel.Children.Add(row);
            }

            var add = new Button { Content = "新建自定义提供商", HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += (_, _) =>
            {
                AiProviderStore.AddCustomProvider();
                Rebuild();
            };
            panel.Children.Add(add);
        }

        Rebuild();

        var dialog = new ContentDialog
        {
            Title = "管理 AI 提供商",
            Content = new ScrollViewer { Content = panel, MaxHeight = 400 },
            CloseButtonText = "完成",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };

        await dialog.ShowAsync();

        // 改名/删除/新建在操作时即已持久化；关闭后刷新顶栏下拉
        AiProviderStore.Save();
        RefreshProviderCombos();
    }

    /// <summary>会话已有内容时，切换后提示新模型从下一条消息生效。</summary>
    private void NotifyModelSwitch(string providerName, string modelId)
    {
        if (_session is null || _session.TotalTokens <= 0) return;
        AddSystemBubble($"已切换到 {providerName} · {modelId}，后续消息将使用该模型。");
    }

    private void UpdateServiceStatus()
    {
        if (AiService.IsLocalProviderSelected)
        {
            var model = AiProviderStore.SelectedModelId;
            ModelStatusText.Text = string.IsNullOrWhiteSpace(model)
                ? "本地模型（请先在「本地 AI 试炼场」下载模型）"
                : $"本地模型 · {model}";
            ModelStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }
        else if (AiService.IsUsingDefaultModel)
        {
            ModelStatusText.Text = "默认模型（建议在设置中配置 AI 服务）";
            ModelStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        }
        else
        {
            var provider = AiProviderStore.SelectedProvider;
            var (_, model, _) = AiService.GetConfig();
            ModelStatusText.Text = $"已连接 {provider.Name} · {model}";
            ModelStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }
    }

    private void UpdateInputState()
    {
        var busy = _isProcessing || _awaitingConfirmation;
        InputBox.IsEnabled = !busy;
        SendButton.IsEnabled = !busy;
        ProviderCombo.IsEnabled = !busy;
        ModelCombo.IsEnabled = !busy;
        SendButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = busy;
        UpdateRunState();
    }

    /// <summary>发送按钮旁的气泡：实时展示本会话累计 token 消耗（多轮累加），
    /// 提供商/网关返回缓存统计时附带缓存命中量。</summary>
    private void UpdateTokenUsage()
    {
        var session = _session;
        var tokens = session?.TotalTokens ?? 0;
        HeaderTokenText.Text = tokens <= 0 ? "0 tokens" : $"{FormatTokens(tokens)} tokens";
        if (tokens <= 0)
        {
            TokenUsageBubble.Visibility = Visibility.Collapsed;
            return;
        }
        TokenUsageBubble.Visibility = Visibility.Visible;
        var text = $"{FormatTokens(tokens)} tokens";
        if (session is { TotalCacheHitTokens: > 0 })
        {
            var total = session.TotalCacheHitTokens + session.TotalCacheMissTokens;
            var pct = total > 0 ? (int)Math.Round(session.TotalCacheHitTokens * 100.0 / total) : 100;
            text += $" · 缓存命中 {FormatTokens(session.TotalCacheHitTokens)} ({pct}%)";
        }
        TokenUsageText.Text = text;
    }

    private void UpdateRunState()
    {
        if (_awaitingConfirmation)
        {
            RunStateText.Text = "等待确认";
            RunStateText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            StateBadge.Background = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
            StateBadge.IconSource = new FontIconSource { Glyph = "\uE823" };
        }
        else if (_isProcessing)
        {
            RunStateText.Text = "执行中";
            RunStateText.Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
            StateBadge.Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            StateBadge.IconSource = new FontIconSource { Glyph = "\uE895" };
        }
        else
        {
            RunStateText.Text = AgentToolContext.IsFullAccess ? "完全访问" : "受控执行";
            RunStateText.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
            StateBadge.Background = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
            StateBadge.IconSource = new FontIconSource { Glyph = "\uE73E" };
        }
    }

    private static string FormatTokens(int tokens)
        => tokens >= 1000 ? $"{tokens / 1000.0:F1}k" : tokens.ToString();

    private void MsgScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        var distance = MsgScroll.ScrollableHeight - MsgScroll.VerticalOffset;
        _followLatest = distance < 64;
        ScrollToBottomButton.Visibility = _followLatest ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ScrollToBottomButton_Click(object sender, RoutedEventArgs e)
    {
        _followLatest = true;
        MsgScroll.ChangeView(null, MsgScroll.ScrollableHeight, null);
        ScrollToBottomButton.Visibility = Visibility.Collapsed;
    }

    private void SmartScroll()
    {
        if (!_followLatest || MsgScroll.ScrollableHeight <= 0) return;
        MsgScroll.ChangeView(null, MsgScroll.ScrollableHeight, null);
    }
}
