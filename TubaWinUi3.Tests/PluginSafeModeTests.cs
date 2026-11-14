using System.Text.Json;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Plugins;

namespace TubaWinUi3.Tests;

/// <summary>
/// 插件安全模式与故障隔离的纯逻辑测试：异常退出会话标记、故障计数（含自动停用阈值）、
/// 能力清单校验、命令行开关识别。真实加载/卸载与恢复提示弹窗不做单测（需完整应用）。
/// </summary>
public sealed class PluginSafeModeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tuba-safe-" + Guid.NewGuid().ToString("N"));

    public PluginSafeModeTests()
    {
        Directory.CreateDirectory(_root);
        PluginFailureStore.PathOverride = Path.Combine(_root, "failures.json");
        PluginSessionMarker.PathOverride = Path.Combine(_root, "session.json");
    }

    public void Dispose()
    {
        PluginFailureStore.PathOverride = "";
        PluginSessionMarker.PathOverride = "";
        try { Directory.Delete(_root, true); } catch { }
    }

    // ── 异常退出会话标记 ──────────────────────────────────

    [Fact]
    public void FirstRun_IsNotTreatedAsUnclean()
    {
        // 首次运行没有任何标记文件：不能一装完就弹「上次没正常退出」。
        Assert.False(PluginSessionMarker.LastExitWasUnclean());
        Assert.False(PluginSessionMarker.BeginSession());
    }

    [Fact]
    public void BeginSession_ReportsUncleanAfterPreviousCrash()
    {
        PluginSessionMarker.BeginSession();       // 模拟崩溃前的一次启动：只标记「进行中」
        Assert.True(PluginSessionMarker.LastExitWasUnclean());
        Assert.True(PluginSessionMarker.BeginSession());
    }

    [Fact]
    public void MarkClean_SuppressesNextPrompt()
    {
        PluginSessionMarker.BeginSession();
        PluginSessionMarker.MarkClean();
        Assert.False(PluginSessionMarker.LastExitWasUnclean());
        Assert.False(PluginSessionMarker.BeginSession());
    }

    [Fact]
    public void BeginSession_RearmsMarkerImmediately()
    {
        PluginSessionMarker.MarkClean();
        PluginSessionMarker.BeginSession();
        // 本次进程一旦启动就必须已经处于「未干净退出」状态，否则崩溃就留不下痕迹。
        Assert.True(PluginSessionMarker.LastExitWasUnclean());
    }

    [Fact]
    public void CorruptMarker_IsNotTreatedAsUnclean()
    {
        File.WriteAllText(PluginSessionMarker.PathOverride, "{ not json");
        Assert.False(PluginSessionMarker.LastExitWasUnclean());
    }

    // ── 能力清单 ──────────────────────────────────────────

    [Fact]
    public void Capabilities_MissingListAllowsEverything()
    {
        // 历史兼容：没写 permissions 的插件必须照常能用。
        foreach (var capability in PluginCapabilities.All)
            Assert.True(PluginCapabilities.IsAllowed(null, fullTrust: false, capability));
    }

    [Fact]
    public void Capabilities_EmptyListDeniesEverything()
    {
        IReadOnlyList<string> declared = [];
        foreach (var capability in PluginCapabilities.All)
            Assert.False(PluginCapabilities.IsAllowed(declared, fullTrust: false, capability));
    }

    [Fact]
    public void Capabilities_OnlyDeclaredAreAllowed()
    {
        IReadOnlyList<string> declared = [PluginCapabilities.Clipboard];
        Assert.True(PluginCapabilities.IsAllowed(declared, false, PluginCapabilities.Clipboard));
        Assert.False(PluginCapabilities.IsAllowed(declared, false, PluginCapabilities.Process));
    }

    [Fact]
    public void Capabilities_ComparisonIsCaseInsensitive()
    {
        IReadOnlyList<string> declared = ["CLIPBOARD"];
        Assert.True(PluginCapabilities.IsAllowed(declared, false, PluginCapabilities.Clipboard));
    }

    [Fact]
    public void Capabilities_FullTrustOverridesList()
    {
        IReadOnlyList<string> declared = [];
        Assert.True(PluginCapabilities.IsAllowed(declared, fullTrust: true, PluginCapabilities.Background));
    }

    [Fact]
    public void Capabilities_UnknownNameIsRejected()
    {
        // 拼错的权限名不能静默放行（等于提权），也不能静默忽略（作者以为生效了）。
        var ex = Assert.Throws<InvalidDataException>(() => PluginCapabilities.Validate(["clipbord"]));
        Assert.Contains("clipbord", ex.Message);
    }

    [Fact]
    public void Capabilities_NullAndValidListsPassValidation()
    {
        PluginCapabilities.Validate(null);
        PluginCapabilities.Validate(PluginCapabilities.All);
    }

    [Fact]
    public void Capabilities_NormalizeTrimsDedupesAndLowercases()
    {
        var normalized = PluginCapabilities.Normalize([" Clipboard", "clipboard", "PROCESS", " "]);
        Assert.Equal(["clipboard", "process"], normalized);
        Assert.Empty(PluginCapabilities.Normalize(null));
    }

    [Fact]
    public void Capabilities_EveryKnownNameHasDescription()
    {
        foreach (var capability in PluginCapabilities.All)
            Assert.NotEqual(capability, PluginCapabilities.Describe(capability));
    }

    // ── 故障计数 ──────────────────────────────────────────

    [Fact]
    public void RecordLoadFailure_AutoDisablesAtThreshold()
    {
        Assert.False(PluginFailureStore.RecordLoadFailure("A.B", "1.0.0", "boom"));
        Assert.False(PluginFailureStore.RecordLoadFailure("A.B", "1.0.0", "boom"));
        Assert.True(PluginFailureStore.RecordLoadFailure("A.B", "1.0.0", "boom"));
        var record = PluginFailureStore.Get("A.B");
        Assert.NotNull(record);
        Assert.Equal(PluginFailureStore.AutoDisableAfter, record!.LoadFailures);
        Assert.Equal("boom", record.LastError);
    }

    [Fact]
    public void RecordLoadFailure_ResetsCountForNewVersion()
    {
        PluginFailureStore.RecordLoadFailure("A.B", "1.0.0", "boom");
        PluginFailureStore.RecordLoadFailure("A.B", "1.0.0", "boom");
        // 升级到新版本＝新代码，旧版本的连续失败不再累计。
        Assert.False(PluginFailureStore.RecordLoadFailure("A.B", "2.0.0", "boom"));
        Assert.Equal(1, PluginFailureStore.Get("A.B")!.LoadFailures);
    }

    [Fact]
    public void ExecutionFailures_AccumulateWithoutAutoDisable()
    {
        PluginFailureStore.RecordExecutionFailure("C.D", "bad input");
        PluginFailureStore.RecordExecutionFailure("C.D", "bad input");
        PluginFailureStore.RecordExecutionFailure("C.D", "bad input");
        var record = PluginFailureStore.Get("C.D")!;
        Assert.Equal(3, record.ExecutionFailures);
        Assert.Equal(0, record.LoadFailures);
    }

    [Fact]
    public void Clear_RemovesRecord()
    {
        PluginFailureStore.RecordExecutionFailure("E.F", "x");
        PluginFailureStore.Clear("E.F");
        Assert.Null(PluginFailureStore.Get("E.F"));
    }

    [Fact]
    public void FailureStore_IgnoresCorruptFile()
    {
        File.WriteAllText(PluginFailureStore.PathOverride, "{ broken");
        Assert.Null(PluginFailureStore.Get("G.H"));
        Assert.False(PluginFailureStore.RecordLoadFailure("G.H", "1.0.0", "boom"));
    }

    [Fact]
    public void FailureStore_SerializesRoundTrip()
    {
        PluginFailureStore.RecordLoadFailure("I.J", "1.2.3.4", "err");
        var json = File.ReadAllText(PluginFailureStore.PathOverride);
        Assert.Contains("I.J", json);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, PluginFailureRecord>>(json)!;
        Assert.Equal("1.2.3.4", parsed["I.J"].Version);
    }

    // ── 命令行与清单 ───────────────────────────────────────

    [Theory]
    [InlineData(new[] { "app.exe", "--safe-mode" }, true)]
    [InlineData(new[] { "app.exe", "--SAFE-MODE" }, true)]
    [InlineData(new[] { "app.exe", "\"--safe-mode\"" }, true)]
    [InlineData(new[] { "app.exe", "--safe-mode=off" }, false)]
    [InlineData(new[] { "app.exe" }, false)]
    public void ParseSafeModeSwitch(string[] args, bool expected) =>
        Assert.Equal(expected, LaunchIntent.HasSwitch(args, PluginSafeMode.CommandLineArg));

    [Fact]
    public void SafeModeSwitchDoesNotChangeNavigationIntent()
    {
        // 安全模式是独立开关，不是导航意图：带它启动仍然走普通启动流程。
        var intent = LaunchIntent.Parse(new[] { "app.exe", PluginSafeMode.CommandLineArg });
        Assert.Equal(LaunchIntentKind.Normal, intent.Kind);
        Assert.Null(intent.Arg);
    }

    [Fact]
    public void Manifest_ReadsPermissionsList()
    {
        File.WriteAllText(Path.Combine(_root, "entry.dll"), "fixture");
        File.WriteAllText(Path.Combine(_root, "tubatools.json"),
            """{"apiVersion":2,"assembly":"entry.dll","entryType":"Entry","permissions":["clipboard","process"]}""");
        var manifest = PluginPackageRules.ReadManifest(_root);
        Assert.Equal(["clipboard", "process"], manifest.Permissions);
    }

    [Fact]
    public void Manifest_RejectsUnknownPermission()
    {
        File.WriteAllText(Path.Combine(_root, "entry.dll"), "fixture");
        File.WriteAllText(Path.Combine(_root, "tubatools.json"),
            """{"apiVersion":2,"assembly":"entry.dll","entryType":"Entry","permissions":["teleport"]}""");
        Assert.Throws<InvalidDataException>(() => PluginPackageRules.ReadManifest(_root));
    }

    [Fact]
    public void Manifest_WithoutPermissionsLeavesItNull()
    {
        File.WriteAllText(Path.Combine(_root, "entry.dll"), "fixture");
        File.WriteAllText(Path.Combine(_root, "tubatools.json"),
            """{"apiVersion":2,"assembly":"entry.dll","entryType":"Entry"}""");
        Assert.Null(PluginPackageRules.ReadManifest(_root).Permissions);
    }

    [Fact]
    public void SettingKey_IsStable()
    {
        // 持久化键写进了用户设置文件，改名等于丢掉用户的选择。
        Assert.Equal("PluginsSafeMode", PluginSafeMode.SettingKey);
        Assert.Equal("--safe-mode", PluginSafeMode.CommandLineArg);
    }
}
