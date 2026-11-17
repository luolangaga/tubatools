using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Tubatools.AiJunkCleanup.Models;
using Tubatools.AiJunkCleanup.Services;
using Tubatools.AiJunkCleanup.UI;
using Tubatools.Onboarding;
using TubaWinUi3.PluginSdk;

namespace Tubatools.AiJunkCleanup.Runtime;

/// <summary>插件进程内的单例：加载设置、写入技能/浅层扫描脚本、维护模式注入器和最近一次清单。</summary>
internal sealed class AiJunkCleanupRuntime
{
    internal static AiJunkCleanupRuntime Current { get; } = new();

    private static bool _installGuideScheduled;

    /// <summary>
    /// 安装/更新时调度已有的五步教程。必须从 Register 调用：已加载插件的启动钩子不会重跑，
    /// 只靠 StartupAsync 会出现「装完没反应、重启才弹」。
    /// </summary>
    internal static bool ScheduleInstallGuide()
    {
        _installGuideScheduled = PluginOnboardingDialog.ScheduleInstallGuide(ShowInstallGuideAsync);
        return _installGuideScheduled;
    }

    private static async Task ShowInstallGuideAsync(XamlRoot root)
    {
        var dialog = new TutorialDialog(root);
        await dialog.ShowAsync();
        // 教程已经看过：下次启动不再由启动钩子重复弹出。
        await MarkStartupTutorialShownAsync();
    }

    private static async Task MarkStartupTutorialShownAsync()
    {
        try
        {
            var data = HostBridge.TryGetDataDirectory();
            if (string.IsNullOrEmpty(data)) return;
            var directory = Path.Combine(data, "PluginData", "tubatools.aijunkcleanup");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json");
            var values = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(path))
                  ?? new Dictionary<string, string>()
                : new Dictionary<string, string>();
            values["startupTutorialShown"] = "true";
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(values));
            File.Move(temporary, path, true);
        }
        catch
        {
            // 设置写不进去最多导致下次启动再显示一次教程。
        }
    }

    private IPluginStartupContext? _context;
    private IPluginHostAccess? _access;
    private AgentModeInjector? _injector;
    private ShallowScanJob? _shallowJob;
    private string? _dataDirectory;
    private JunkScanDepth _depth = JunkScanDepth.Standard;
    private JunkScanDepth _lastPlanDepth = JunkScanDepth.Standard;
    private bool _tutorialShown;
    private HashSet<string> _handled = new(StringComparer.Ordinal);
    private JunkPlan? _lastPlan;
    private JunkCleanupResult? _lastResult;

    internal bool AccessAvailable => _access is not null;
    internal bool Attached => _injector?.IsAttached == true;
    internal JunkPlan? LastPlan => _lastPlan;
    internal JunkCleanupResult? LastResult => _lastResult;
    internal JunkScanDepth Depth => _depth;
    internal string Status { get; private set; } = "";

    internal async ValueTask StartupAsync(IPluginStartupContext context, CancellationToken ct)
    {
        await ShutdownAsync();
        _context = context;
        _access = context.HostAccess;
        _dataDirectory = context.DataDirectory;
        await LoadSettingsAsync(context, ct);
        EnsureSkill();

        if (_access is null)
        {
            Status = "缺少 fullTrust，无法把「垃圾清理」模式注入 AI 助手。";
            return;
        }

        await _access.RunOnUiThreadAsync(() =>
        {
            _injector = new AgentModeInjector(_access!, _depth, _tutorialShown || _installGuideScheduled, _handled, SaveSettingAsync, () => _lastPlan,
                RememberPlan, RememberResult, ScanShallowAsync, _dataDirectory);
            _injector.Attach();
            _ = _injector.ShowTutorialAtStartupAsync();
            return Task.CompletedTask;
        }, ct);
        Status = "已注入 AI 助手模式。";
    }

    internal async ValueTask ShutdownAsync()
    {
        _shallowJob?.Cancel();
        _shallowJob = null;
        var injector = _injector;
        _injector = null;
        if (injector is null) return;
        if (_access is null) { injector.Dispose(); return; }
        try
        {
            await _access.RunOnUiThreadAsync(() => { injector.Dispose(); return Task.CompletedTask; });
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
            try { injector.Dispose(); } catch { }
        }
    }

    internal async Task ShowLastPlanAsync()
    {
        if (_injector is null) return;
        await _injector.ShowLastPlanAsync();
    }

    /// <summary>用户在垃圾清理模式按下发送时调用：跑一次快速 NTFS/MFT 浅层扫描并返回结果，供插件拼进消息。</summary>
    private async Task<ShallowScanResult?> ScanShallowAsync(JunkScanDepth depth, IProgress<ShallowScanProgress> progress)
    {
        if (_shallowJob is { } running && !running.Completion.IsCompleted)
        {
            await running.Completion;
            return running.Result;
        }

        var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? "C:\\";
        var job = new ShallowScanJob(root, depth);
        _shallowJob = job;
        job.Start(progress);
        await job.Completion;
        return job.Result;
    }

    internal void RememberPlan(JunkPlan plan, string json)
    {
        _lastPlan = plan;
        _lastPlanDepth = _depth;
        _ = SaveSettingAsync("lastPlan", json);
        _ = SaveSettingAsync("lastPlanDepth", JunkScanDepths.Key(_lastPlanDepth));
    }

    internal void RememberResult(JunkCleanupResult result)
    {
        _lastResult = result;
        _ = SaveSettingAsync("lastResult", JsonSerializer.Serialize(result));
    }

    private async Task LoadSettingsAsync(IPluginStartupContext context, CancellationToken ct)
    {
        try
        {
            _depth = JunkScanDepths.Parse(await context.GetSettingAsync("scanDepth", ct));
            _tutorialShown = string.Equals(await context.GetSettingAsync("startupTutorialShown", ct), "true",
                StringComparison.OrdinalIgnoreCase);
            var handled = await context.GetSettingAsync("handledPlans", ct);
            if (!string.IsNullOrWhiteSpace(handled))
                _handled = new HashSet<string>(JsonSerializer.Deserialize<string[]>(handled) ?? [], StringComparer.Ordinal);
            var lastPlan = await context.GetSettingAsync("lastPlan", ct);
            if (!string.IsNullOrWhiteSpace(lastPlan))
            {
                _lastPlan = JsonSerializer.Deserialize<JunkPlan>(lastPlan);
                _lastPlanDepth = JunkScanDepths.Parse(await context.GetSettingAsync("lastPlanDepth", ct));
            }
            var lastResult = await context.GetSettingAsync("lastResult", ct);
            if (!string.IsNullOrWhiteSpace(lastResult))
                _lastResult = JsonSerializer.Deserialize<JunkCleanupResult>(lastResult);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            Status = "设置读取失败：" + ex.Message;
        }
    }

    private async Task SaveSettingAsync(string key, string? value)
    {
        if (_context is null) return;
        try { await _context.SetSettingAsync(key, value); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
    }

    private void EnsureSkill()
    {
        try
        {
            var directory = Path.Combine(WorkspaceDirectory(), ".opencode", "skills", "tuba-junk-cleanup");
            Directory.CreateDirectory(directory);
            var obsolete = Path.Combine(directory, "space-scan.ps1");
            if (File.Exists(obsolete)) File.Delete(obsolete);
            WriteIfChanged(directory, "SKILL.md", ReadResource("TubaJunkCleanupSkill.md"));
            WriteIfChanged(directory, "shallow-scan.ps1", ReadResource("TubaJunkCleanupShallowScanScript.ps1"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Status = "技能写入失败：" + ex.Message;
        }
    }

    private static void WriteIfChanged(string directory, string name, string content)
    {
        if (content.Length == 0) return;
        var path = Path.Combine(directory, name);
        if (!File.Exists(path) || File.ReadAllText(path) != content)
            File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static string WorkspaceDirectory()
    {
        var workspace = HostBridge.TryGetAgentWorkspacePath();
        if (!string.IsNullOrWhiteSpace(workspace)) return workspace;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubaWinUi3", "AiAssistant", "Workspace");
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(AiJunkCleanupPlugin).Assembly.GetManifestResourceStream(name);
        if (stream is null) return "";
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
