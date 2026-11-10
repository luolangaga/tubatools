using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>Desktop control cannot start until this visible, nonactivating stop banner is shown.</summary>
internal static class ComputerControlService
{
    internal static string Root => Path.Combine(AiComponentService.Root, "ComputerControl");
    internal static string WindowTitle => AgentText.L("ComputerControlWindow", "AI 电脑控制提示");
    private static readonly Dictionary<string, Action> Sessions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, AgentRunProgress> SessionProgress = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Lease> Leases = new(StringComparer.Ordinal);
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private static readonly DispatcherTimer EdgeTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private static Window? _owner;
    private static ControlBannerOverlay? _banner;
    private static ControlProgressOverlay? _progressPopup;
    private static DispatcherQueue? _dispatcher;
    private static ScreenEdgeFlowLight? _edgeLight;
    private static WindowCaptureFlash? _captureFlash;
    private static string? _latestProgressSession;
    private static readonly Dictionary<string, DateTime> SessionStarted = new(StringComparer.Ordinal);
    private static string? _progressTargetTitle;
    private static bool _shown;
    private sealed record Lease(string Key, string SessionId, int ProcessId, long UpdatedAt, string RequestId,
        string? Tool = null, string? Window = null, string? Foreground = null);
    private sealed record CaptureSignal(string? Window);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan OverlayGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OverlaySessionGrace = TimeSpan.FromSeconds(8);
    private static readonly HashSet<string> ComputerSessions = new(StringComparer.Ordinal);
    private static bool _overlayIdle;
    private static DateTime _overlayIdleSince;
    internal static event Action<string>? PendingInteractionRequested;

    internal static string SessionKey(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..32].ToLowerInvariant();
    internal static void Attach(Window owner)
    {
        _owner = owner; _dispatcher = DispatcherQueue.GetForCurrentThread(); Directory.CreateDirectory(Root);
        Timer.Tick -= Tick; Timer.Tick += Tick; Timer.Start();
        EdgeTimer.Tick -= EdgeTick; EdgeTimer.Tick += EdgeTick;
    }
    internal static void BeginSession(string id, Action stop)
    {
        Sessions[id] = stop;
        SessionProgress[id] = new("waiting");
        SessionStarted[id] = DateTime.UtcNow;
        TryDelete(Path.Combine(Root, "stop-" + SessionKey(id) + ".flag"));
        // A new user turn is the explicit permission to use the computer again.
        TryDelete(Path.Combine(Root, "stop-" + SessionKey(ComputerUseMcpProxy.DefaultSession) + ".flag"));
    }
    internal static void UpdateProgress(string id, AgentRunProgress progress)
    {
        void Apply()
        {
            if (!Sessions.ContainsKey(id)) return;
            SessionProgress[id] = progress;
            _latestProgressSession = id;
            if (_progressPopup is not null)
            {
                var active = Leases.Values.MaxBy(l => l.UpdatedAt);
                var show = !IsAssistantPageForeground(GetForegroundWindow());
                if (active is not null) UpdateProgressPopupText(active, GetForegroundWindow(), show);
                else UpdateProgressPopupCore(id, id, GetForegroundWindow(), null, show);
            }
        }
        if (_dispatcher?.HasThreadAccess == true) Apply(); else _dispatcher?.TryEnqueue(Apply);
    }
    internal static void EndSession(string id)
    {
        Sessions.Remove(id);
        SessionProgress.Remove(id);
        SessionStarted.Remove(id);
        ComputerSessions.Remove(id);
        if (_latestProgressSession == id) _latestProgressSession = Sessions.Keys.LastOrDefault();
        var key = SessionKey(id);
        TryDelete(Path.Combine(Root, "lease-" + key + ".json"));
        TryDelete(Path.Combine(Root, "shown-" + key + ".flag"));
        Leases.Remove(key);
        if (Leases.Count == 0) CloseAllOverlays();
    }
    private static bool ValidKey(string? key) => key is { Length: 32 } && key.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Tick(object? sender, object e)
    {
        if (_owner is null) return;
        ProcessCaptureSignals();
        var live = new Dictionary<string, Lease>(StringComparer.Ordinal);
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "lease-*.json").Take(64))
            {
                try
                {
                    if (new FileInfo(file).Length > 4096) continue;
                    var lease = JsonSerializer.Deserialize<Lease>(File.ReadAllText(file), Json);
                    if (lease is null || !ValidKey(lease.Key) || Path.GetFileName(file) != "lease-" + lease.Key + ".json" ||
                        lease.SessionId is not { Length: > 0 and <= 256 } || !Guid.TryParseExact(lease.RequestId, "N", out _) || lease.Key != SessionKey(lease.SessionId)) continue;
                    if (File.Exists(Path.Combine(Root, "stop-" + lease.Key + ".flag")))
                    { TryDelete(file); TryDelete(Path.Combine(Root, "shown-" + lease.Key + ".flag")); continue; }
                    using var process = Process.GetProcessById(lease.ProcessId);
                    if (process.HasExited || (!Sessions.ContainsKey(lease.SessionId) && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - lease.UpdatedAt > 120))
                    { TryDelete(file); TryDelete(Path.Combine(Root, "shown-" + lease.Key + ".flag")); continue; }
                    live[lease.Key] = lease;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    var key = Path.GetFileNameWithoutExtension(file)[6..];
                    if (ValidKey(key)) { TryDelete(file); TryDelete(Path.Combine(Root, "shown-" + key + ".flag")); }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            }
        }
        catch (IOException) { return; }
        Leases.Clear(); foreach (var pair in live) Leases[pair.Key] = pair.Value;
        foreach (var lease in Leases.Values) ComputerSessions.Add(ResolveProgressSession(lease) ?? lease.SessionId);
        if (Leases.Count == 0)
        {
            if (_banner is null && _progressPopup is null && _edgeLight is null) return;
            if (!_overlayIdle) { _overlayIdle = true; _overlayIdleSince = DateTime.UtcNow; }
            // Between tool calls the turn still owns the desktop; keep the indicator briefly
            // instead of flickering it off and on for every AX/GUI call.
            var keep = ComputerSessions.Any(id => Sessions.ContainsKey(id));
            if (DateTime.UtcNow - _overlayIdleSince < (keep ? OverlaySessionGrace : OverlayGrace)) return;
            CloseAllOverlays(); return;
        }
        _overlayIdle = false;
        var foreground = GetForegroundWindow();
        var anchor = foreground != IntPtr.Zero ? foreground : WinRT.Interop.WindowNative.GetWindowHandle(_owner);
        foreach (var lease in Leases.Values) SessionStarted.TryAdd(lease.SessionId, DateTime.UtcNow);
        ShowBanner(anchor);
        _captureFlash ??= new WindowCaptureFlash();
        _captureFlash.Prepare();
        // The overlay timer also drives the capture frame, even if the edge glow failed.
        if (!EdgeTimer.IsEnabled) EdgeTimer.Start();

        try
        {
            _edgeLight ??= new ScreenEdgeFlowLight();
            _edgeLight.SetTarget(anchor);
            if (!EdgeTimer.IsEnabled) EdgeTimer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[AgentComputerEdgeLight] " + ex);
            _edgeLight?.Dispose(); _edgeLight = null;
        }

        if (!IsAssistantPageForeground(foreground))
        {
            var active = Leases.Values.MaxBy(l => l.UpdatedAt)!;
            ShowProgressPopup(active, anchor, show: true);
        }
        else
        {
            // Keep the card window in the layout even while the AI page is visible; only its
            // pixels are cleared. Creating/hiding a topmost window mid-session makes HCU reject
            // the next coordinate input as gui_frame_stale.
            var active = Leases.Values.MaxBy(l => l.UpdatedAt)!;
            ShowProgressPopup(active, anchor, show: false);
        }
    }
    private static void EdgeTick(object? sender, object e)
    {
        _banner?.Advance();
        _banner?.UpdateHitThrough();
        _progressPopup?.AdvanceSpin();
        _progressPopup?.UpdateHitThrough();
        try
        {
            _edgeLight?.Advance();
            _captureFlash?.Advance();
        }
        catch (Exception ex)
        {
            // Keep the timer alive: it also drives the capture frame.
            System.Diagnostics.Debug.WriteLine("[AgentComputerEdgeLight] " + ex);
            _edgeLight?.Dispose(); _edgeLight = null;
            _captureFlash?.Dispose(); _captureFlash = null;
        }
    }
    private static bool IsAssistantPageForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _owner is null) return false;
        var hostWindow = WinRT.Interop.WindowNative.GetWindowHandle(_owner);
        // The card is redundant only while the user is actually looking at the AI conversation;
        // another toolbox page (or any other app) still needs the floating status + stop button.
        return hwnd == hostWindow && IsWindowVisible(hostWindow) && !IsIconic(hostWindow)
            && App.MainWindow?.IsAssistantPageActive == true;
    }
    private static void AcknowledgeShown()
    {
        foreach (var lease in Leases.Values)
            try { File.WriteAllText(Path.Combine(Root, "shown-" + lease.Key + ".flag"), JsonSerializer.Serialize(new { requestId = lease.RequestId, hostPid = Environment.ProcessId })); } catch (IOException) { }
    }
    private static void ProcessCaptureSignals()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "capture-*.json").Take(16))
            {
                CaptureSignal? signal = null;
                try
                {
                    if (new FileInfo(file).Length <= 4096)
                        signal = JsonSerializer.Deserialize<CaptureSignal>(File.ReadAllText(file), Json);
                }
                catch (Exception ex) when (ex is IOException or JsonException) { }
                TryDelete(file);
                if (!string.IsNullOrWhiteSpace(signal?.Window) && (_banner is not null || _progressPopup is not null))
                    ShowCaptureFlash(signal.Window);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private static void ShowCaptureFlash(string? hint)
    {
        var target = FindCaptureTarget(hint);
        if (target == IntPtr.Zero) return;
        _captureFlash ??= new WindowCaptureFlash();
        _captureFlash.Flash(target);
    }
    private static IntPtr FindCaptureTarget(string? hint)
    {
        var foreground = GetForegroundWindow();
        if (!string.IsNullOrWhiteSpace(hint))
        {
            var match = FindWindowByTitle(hint!);
            if (match != IntPtr.Zero) return match;
        }
        return IsOverlayWindow(foreground) ? IntPtr.Zero : foreground;
    }
    private static IntPtr FindWindowByTitle(string hint)
    {
        var foreground = GetForegroundWindow();
        var result = IntPtr.Zero;
        _ = EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window) || IsOverlayWindow(window)) return true;
            var title = new StringBuilder(512);
            if (GetWindowText(window, title, title.Capacity) == 0) return true;
            if (title.ToString().IndexOf(hint, StringComparison.OrdinalIgnoreCase) < 0) return true;
            if (window == foreground) { result = window; return false; }
            if (result == IntPtr.Zero) result = window;
            return true;
        }, IntPtr.Zero);
        return result;
    }
    private static bool IsOverlayWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return true;
        var title = new StringBuilder(128);
        if (GetWindowText(window, title, title.Capacity) > 0)
        {
            var text = title.ToString();
            if (text == ControlBannerOverlay.WindowTitle || text == ControlProgressOverlay.WindowTitle ||
                text == ScreenEdgeFlowLight.WindowTitle || text == WindowCaptureFlash.WindowTitle) return true;
        }
        return false;
    }
    private static void ShowBanner(IntPtr anchor)
    {
        if (_owner is null || anchor == IntPtr.Zero) return;
        _banner ??= new ControlBannerOverlay { OnStop = StopAll };
        _banner.Show(anchor);
        _shown = true;
        AcknowledgeShown();
    }
    private static void ShowProgressPopup(Lease lease, IntPtr anchor, bool show)
    {
        _progressPopup ??= new ControlProgressOverlay
        {
            OnStopSession = StopSession,
            OnRespond = id => PendingInteractionRequested?.Invoke(id)
        };
        UpdateProgressPopupText(lease, anchor, show);
    }
    private static void UpdateProgressPopupText(Lease lease, IntPtr anchor, bool show) =>
        UpdateProgressPopupCore(ResolveProgressSession(lease), lease.SessionId, anchor, lease.Foreground ?? lease.Window, show);
    private static void UpdateProgressPopupCore(string? displaySession, string fallbackSession, IntPtr anchor, string? targetHint = null, bool show = true)
    {
        if (_progressPopup is null) return;
        if (anchor == IntPtr.Zero) anchor = GetForegroundWindow();
        if (anchor == IntPtr.Zero) return;
        if (!string.IsNullOrWhiteSpace(targetHint)) _progressTargetTitle = targetHint.Trim();
        if (string.IsNullOrWhiteSpace(_progressTargetTitle))
        {
            var live = new StringBuilder(512);
            _ = GetWindowText(anchor, live, live.Capacity);
            var text = live.ToString().Trim();
            if (text.Length > 0) _progressTargetTitle = text;
        }
        var sessionId = displaySession ?? fallbackSession;
        var progress = displaySession is null ? null : SessionProgress.GetValueOrDefault(displaySession);
        var phase = progress?.Phase ?? "";
        var phaseText = phase switch
        {
            "thinking" => AgentText.L("ComputerProgressAnalyzing", "正在分析下一步"),
            "responding" => AgentText.L("ComputerProgressResponding", "正在整理结果"),
            "preparing-tool" => AgentText.L("ComputerProgressPreparing", "正在准备电脑操作"),
            "tool" => AgentText.L("ComputerProgressTool", "正在操作界面"),
            "permission" or "question" => AgentText.L("ComputerProgressWaiting", "等待你的回答"),
            "reconnecting" or "retrying" => AgentText.L("ComputerProgressReconnecting", "正在重新连接"),
            "stalled" => AgentText.L("ComputerProgressStalled", "仍在等待模型返回"),
            _ => AgentText.L("ComputerProgressFallback", "正在处理电脑问题")
        };
        var detail = progress?.Detail.Trim() ?? "";
        if (detail == phaseText) detail = "";
        if (detail.Length > 180) detail = detail[..180] + "…";
        var targetTitle = _progressTargetTitle ?? "";
        if (targetTitle.Length > 96) targetTitle = targetTitle[..96] + "…";
        var target = string.Format(AgentText.L("ComputerProgressCurrentWindow", "当前窗口：{0}"),
            string.IsNullOrEmpty(targetTitle) ? AgentText.L("ComputerProgressOtherWindow", "其他窗口") : targetTitle);
        var waiting = phase is "question" or "permission";
        _progressPopup.Update(sessionId,
            AgentText.L("ComputerProgressTitle", "AI 正在操作电脑"),
            phaseText, detail, target, FormatElapsed(sessionId), waiting, waiting, show, anchor);
    }
    private static string? ResolveProgressSession(Lease lease)
    {
        if (SessionProgress.ContainsKey(lease.SessionId)) return lease.SessionId;
        if (_latestProgressSession is { } latest && SessionProgress.ContainsKey(latest)) return latest;
        return Sessions.Count == 1 ? Sessions.Keys.First() : null;
    }
    private static string FormatElapsed(string sessionId)
    {
        if (!SessionStarted.TryGetValue(sessionId, out var start)) return "";
        var span = DateTime.UtcNow - start;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }
    internal static void StopAll()
    {
        foreach (var lease in Leases.Values.ToArray())
        {
            try { File.WriteAllText(Path.Combine(Root, "stop-" + lease.Key + ".flag"), "user stopped"); } catch (IOException) { }
            TryDelete(Path.Combine(Root, "shown-" + lease.Key + ".flag"));
            if (Sessions.TryGetValue(lease.SessionId, out var cancel)) try { cancel(); } catch (ObjectDisposedException) { }
        }
        Leases.Clear(); CloseAllOverlays();
    }
    private static void StopSession(string id)
    {
        var key = SessionKey(id);
        try { File.WriteAllText(Path.Combine(Root, "stop-" + key + ".flag"), "user stopped"); } catch (IOException) { }
        TryDelete(Path.Combine(Root, "shown-" + key + ".flag"));
        // The bridge uses one synthetic session for every AX/GUI call; stop it as well so an
        // in-flight call is rejected before its next request reaches the desktop.
        if (id != ComputerUseMcpProxy.DefaultSession)
            try { File.WriteAllText(Path.Combine(Root, "stop-" + SessionKey(ComputerUseMcpProxy.DefaultSession) + ".flag"), "user stopped"); } catch (IOException) { }
        // Hybrid Computer Use leases use a fixed bridge session rather than a conversation id;
        // stopping one is a global stop, exactly like the banner button.
        if (!Sessions.TryGetValue(id, out var cancel)) { StopAll(); return; }
        try { cancel(); } catch (ObjectDisposedException) { }
        Leases.Remove(key);
        if (Leases.Count == 0) CloseAllOverlays(); else CloseProgressPopup();
    }
    internal static void Shutdown()
    {
        Timer.Stop(); EdgeTimer.Stop(); StopAll();
        Sessions.Clear(); SessionProgress.Clear(); SessionStarted.Clear();
        _latestProgressSession = null; _owner = null; _dispatcher = null;
    }
    private static void CloseBanner()
    {
        _banner?.Dispose(); _banner = null; _shown = false;
    }
    private static void CloseProgressPopup()
    {
        _progressPopup?.Dispose(); _progressPopup = null; _progressTargetTitle = null;
    }
    private static void StopEdgeLight()
    {
        EdgeTimer.Stop(); _edgeLight?.Dispose(); _edgeLight = null;
    }
    private static void CloseAllOverlays()
    {
        CloseBanner(); CloseProgressPopup(); StopEdgeLight();
        _captureFlash?.Dispose(); _captureFlash = null;
        ComputerSessions.Clear();
    }
    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
}
