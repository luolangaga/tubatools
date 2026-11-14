using System.Reflection;
using System.Text.Json;
using Tubatools.AiJunkCleanup;
using Tubatools.AiJunkCleanup.Models;
using Tubatools.AiJunkCleanup.Runtime;
using Tubatools.AiJunkCleanup.Services;
using TubaWinUi3.PluginSdk;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>Tubatools.AiJunkCleanup 的纯逻辑回归：清单解析、三档深度、路径安全、技能与注册边界。</summary>
public sealed class AiJunkCleanupTests
{
    private const string Plan = """
        这些位置可以清理。

        ```tuba-junk-plan
        {
          "version": 1,
          "summary": "测试清单",
          "items": [
            {
              "path": "C:\\Temp\\a.tmp",
              "kind": "file",
              "sizeBytes": 2048,
              "risk": "low",
              "category": "临时文件",
              "reason": "临时文件可以重建",
              "riskDetail": "程序会重新生成",
              "suggestion": "建议删除"
            },
            {
              "path": "C:\\Temp\\cache",
              "kind": "directory",
              "size": "1.5 GB",
              "risk": "medium",
              "category": "缓存",
              "reason": "缓存目录",
              "riskDetail": "删除后会重新下载"
            }
          ]
        }
        ```
        """;

    [Fact]
    public void HostMembersUsedByPluginStillExist()
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var page = typeof(TubaWinUi3.Pages.AiAssistantPage);
        var active = page.GetField("_active", instance);
        Assert.NotNull(active);
        Assert.NotNull(active!.FieldType.GetField("Messages", instance));
        Assert.NotNull(active.FieldType.GetField("Run", instance));
        Assert.NotNull(active.FieldType.GetField("Id", instance));
        Assert.NotNull(typeof(TubaWinUi3.Services.AiAgent.AgentMessage).GetProperty("Content", instance));
        Assert.NotNull(typeof(TubaWinUi3.Services.AiAgent.AgentMessage).GetProperty("Completed", instance));
        Assert.NotNull(typeof(TubaWinUi3.Services.AiAgent.AgentContent).GetProperty("Operation", instance));
        foreach (var property in new[] { "Kind", "Status", "Command", "Target" })
            Assert.NotNull(typeof(TubaWinUi3.Services.AiAgent.AgentOperation).GetProperty(property, instance));
        var window = typeof(TubaWinUi3.Services.BuiltinToolWindow);
        Assert.NotNull(window.GetField("_openWindows", statics));
        Assert.NotNull(window.GetField("_frame", instance));
        Assert.NotNull(window.GetMethod("FindHostingWindow", statics));
    }

    [Fact]
    public void HostBridge_FindsHostTypesAndPaths()
    {
        Assert.NotNull(HostBridge.FindHostType("TubaWinUi3.Services.AiAgent.AgentWorkspace"));
        Assert.NotNull(HostBridge.FindHostType("TubaWinUi3.Services.ConfigManager"));
        Assert.NotNull(HostBridge.FindHostType("TubaWinUi3.Services.BuiltinToolWindow"));
        Assert.False(string.IsNullOrWhiteSpace(HostBridge.TryGetAgentWorkspacePath()));
        Assert.False(string.IsNullOrWhiteSpace(HostBridge.TryGetDataDirectory()));
        Assert.NotNull(HostBridge.GetToolWindowFrames());
    }

    [Fact]
    public void Project_DoesNotRegisterAnyToolCard()
    {
        var host = new CapturingHost();
        var plugin = new AiJunkCleanupPlugin();
        plugin.Register(host);
        Assert.Empty(host.Tools);
        Assert.IsAssignableFrom<IPluginStartup>(plugin);
    }

    [Fact]
    public void Manifest_IsApiV2FullTrust()
    {
        var root = Path.Combine(TestPaths.RepoRoot, "plugins", "Tubatools.AiJunkCleanup");
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tubatools.json")));
        var manifest = document.RootElement;
        Assert.Equal(2, manifest.GetProperty("apiVersion").GetInt32());
        Assert.True(manifest.GetProperty("fullTrust").GetBoolean());
        Assert.Contains("Tubatools.AiJunkCleanup", manifest.GetProperty("assembly").GetString());
        Assert.Equal("assets/icon.svg", manifest.GetProperty("icon").GetString());
    }

    [Fact]
    public void Parser_ReadsFencedPlanAndSizes()
    {
        Assert.True(JunkPlanParser.TryParse(Plan, out var plan));
        Assert.Equal("测试清单", plan.Summary);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal("C:\\Temp\\a.tmp", plan.Items[0].Path);
        Assert.False(plan.Items[0].IsDirectory);
        Assert.Equal(2048, plan.Items[0].SizeBytes);
        Assert.Equal(JunkRisk.Low, plan.Items[0].Risk);
        Assert.True(plan.Items[1].IsDirectory);
        Assert.Equal((long)(1.5 * 1024 * 1024 * 1024), plan.Items[1].SizeBytes);
        Assert.Equal(JunkRisk.Medium, plan.Items[1].Risk);
    }

    [Fact]
    public void Parser_StripsFenceAndKeepsExplanation()
    {
        var text = JunkPlanParser.Strip(Plan);
        Assert.Contains("这些位置可以清理", text);
        Assert.DoesNotContain("tuba-junk-plan", text);
        Assert.DoesNotContain("\"items\"", text);
    }

    [Fact]
    public void Parser_AcceptsFourBackticksTrailingCommaAndMissingRisk()
    {
        var markdown = """
            ````tuba-junk-plan
            {
              "summary": "四反引号",
              "items": [
                { "path": "C:\\Temp\\x", "kind": "file", "reason": "没有风险字段", },
              ]
            }
            ````
            """;
        Assert.True(JunkPlanParser.TryParse(markdown, out var plan));
        Assert.Single(plan.Items);
        Assert.Equal(JunkRisk.High, plan.Items[0].Risk);
        Assert.Equal("没有风险字段", plan.Items[0].Reason);
    }

    [Fact]
    public void Parser_AcceptsRawJsonFallbackWhenModelForgetsFence()
    {
        var markdown = """
            说明文字
            { "summary": "裸 JSON", "items": [ { "path": "C:\\Temp\\x", "risk": "low", "reason": "临时" } ] }
            """;
        Assert.True(JunkPlanParser.TryParse(markdown, out var plan));
        Assert.Equal("裸 JSON", plan.Summary);
        Assert.Single(plan.Items);
    }

    [Fact]
    public void Parser_DeduplicatesAndKeepsHigherRisk()
    {
        var markdown = """
            ```tuba-junk-plan
            {
              "summary": "重复路径",
              "items": [
                { "path": "C:\\Temp\\A", "risk": "low", "reason": "第一次" },
                { "path": "c:\\temp\\a", "risk": "high", "reason": "第二次" }
              ]
            }
            ```
            """;
        Assert.True(JunkPlanParser.TryParse(markdown, out var plan));
        var item = Assert.Single(plan.Items);
        Assert.Equal(JunkRisk.High, item.Risk);
        Assert.Contains("第一次", item.Reason);
        Assert.Contains("第二次", item.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("没有清单")]
    [InlineData("```tuba-junk-plan\n{ broken }\n```")]
    [InlineData("```tuba-junk-plan\n{\"summary\":\"空\",\"items\":[]}\n```")]
    public void Parser_RejectsInvalidPayloads(string markdown) =>
        Assert.False(JunkPlanParser.TryParse(markdown, out _));

    [Fact]
    public void Parser_FindAllReturnsEveryPlan()
    {
        var two = Plan + "\n\n" + Plan.Replace("测试清单", "第二份");
        var plans = JunkPlanParser.FindAll(two);
        Assert.Equal(2, plans.Count);
        Assert.Equal("第二份", plans[1].Summary);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(2048, "2.0 KB")]
    [InlineData(1024L * 1024, "1.0 MB")]
    [InlineData(1024L * 1024 * 1024, "1.0 GB")]
    [InlineData(1_395_864_371, "1.3 GB")]
    [InlineData(34_359_738_368, "32.0 GB")]
    [InlineData(120_586_240, "115 MB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1.0 TB")]
    public void FormatBytes_UsesCorrectUnit(long bytes, string expected) =>
        Assert.Equal(expected, Tubatools.AiJunkCleanup.Services.JunkSizeScanner.FormatBytes(bytes));

    [Theory]
    [InlineData("quick", 0)]
    [InlineData("快速", 0)]
    [InlineData("deep", 2)]
    [InlineData("深度", 2)]
    [InlineData("something", 1)]
    public void ScanDepth_ParsesExpectedValue(string value, int expected) =>
        Assert.Equal((JunkScanDepth)expected, JunkScanDepths.Parse(value));

    [Fact]
    public void ScanDepth_ThreePromptsAreDifferentAndExploreWider()
    {
        var quick = JunkScanDepths.Prompt(JunkScanDepth.Quick);
        var standard = JunkScanDepths.Prompt(JunkScanDepth.Standard);
        var deep = JunkScanDepths.Prompt(JunkScanDepth.Deep);
        Assert.NotEqual(quick, standard);
        Assert.NotEqual(standard, deep);
        Assert.Contains("优先", quick);
        Assert.Contains("扩大", standard);
        Assert.Contains("意外的大文件", deep);
        Assert.Contains("仍然禁止任何删除", deep);
        Assert.Equal(JunkScanDepth.Deep, JunkScanDepths.Parse(JunkScanDepths.Key(JunkScanDepth.Deep)));
    }

    [Fact]
    public void ScanDepth_IndexExpandsDepthAndLowersThresholds()
    {
        var quick = JunkScanDepths.Index(JunkScanDepth.Quick);
        var standard = JunkScanDepths.Index(JunkScanDepth.Standard);
        var deep = JunkScanDepths.Index(JunkScanDepth.Deep);
        Assert.True(quick.Depth < standard.Depth);
        Assert.True(standard.Depth < deep.Depth);
        Assert.True(quick.MinDirectoryBytes > standard.MinDirectoryBytes);
        Assert.True(standard.MinDirectoryBytes > deep.MinDirectoryBytes);
        Assert.True(quick.MinFileBytes > standard.MinFileBytes);
        Assert.True(standard.MinFileBytes > deep.MinFileBytes);
    }

    [Fact]
    public void Plan_JsonRoundTripsForLastPlanPersistence()
    {
        Assert.True(JunkPlanParser.TryParse(Plan, out var plan));
        var json = JsonSerializer.Serialize(plan);
        var back = JsonSerializer.Deserialize<JunkPlan>(json);
        Assert.NotNull(back);
        Assert.Equal(plan.Summary, back!.Summary);
        Assert.Equal(plan.Items.Count, back.Items.Count);
        Assert.Equal(plan.Items[0].Risk, back.Items[0].Risk);
    }

    [Fact]
    public void PromptTemplate_ContainsSkillFenceDepthAndNoDeleteRule()
    {
        var template = AgentModeInjector.BuildTemplate(JunkScanDepth.Standard);
        Assert.Contains("〔垃圾清理模式〕", template);
        Assert.Contains("tuba-junk-cleanup", template);
        Assert.Contains(".opencode/skills/tuba-junk-cleanup/SKILL.md", template);
        Assert.Contains("tuba-junk-plan", template);
        Assert.Contains("扫描深度：标准", template);
        Assert.Contains("禁止删除", template);
        Assert.Contains("risk(low|medium|high)", template);
        Assert.Contains("shallow-scan.json", template);
        Assert.Contains("scan-index.json", template);
        Assert.Contains("消息末尾", template);
        Assert.Contains("自动附带", template);
        Assert.Contains("临时目录", template);
        Assert.Contains("不要只根据索引里的大文件", template);
        Assert.Contains("我的清理目标", template);
    }

    [Fact]
    public void ScanSummary_UsesTopLevel3DirectoriesAndTellsAiNotToRescan()
    {
        var result = new ShallowScanResult(
            1,
            "C:\\",
            3,
            "NTFS MFT",
            3L * 1024 * 1024 * 1024,
            1200,
            80,
            2.4,
            [
                new ShallowDirItem(1, "C:\\Users", 3L * 1024 * 1024 * 1024, 1200),
                new ShallowDirItem(2, "C:\\Users\\me", 2L * 1024 * 1024 * 1024, 800),
                new ShallowDirItem(3, "C:\\Users\\me\\AppData", 1024L * 1024 * 1024, 500)
            ],
            0);
        var summary = AgentModeInjector.BuildScanSummary(result);
        Assert.Contains("〔插件浅层扫描结果·只读〕", summary);
        Assert.Contains("3.0 GB", summary);
        Assert.Contains("shallow-scan.json", summary);
        Assert.Contains("不要再跑全盘递归扫描", summary);
        var bullets = summary.Split('\n').Where(line => line.StartsWith("- ", StringComparison.Ordinal)).ToList();
        Assert.Single(bullets);
        Assert.Contains("AppData", bullets[0]);
    }

    [Fact]
    public void ScanSummary_MentionsDeepIndexAndNoRescan()
    {
        var result = new ShallowScanResult(
            1,
            "C:\\",
            3,
            "NTFS MFT",
            3L * 1024 * 1024 * 1024,
            1200,
            80,
            2.4,
            [],
            0,
            IndexDepth: 8,
            IndexMinDirectoryBytes: 16L << 20,
            IndexMinFileBytes: 128L << 20,
            IndexDirectoryCount: 1200,
            IndexFileCount: 30,
            IndexPath: "C:\\ws\\scan-index.json");
        var summary = AgentModeInjector.BuildScanSummary(result);
        Assert.Contains("scan-index.json", summary);
        Assert.Contains("第 8 层", summary);
        Assert.Contains("不要再跑全盘递归扫描", summary);
    }

    [Theory]
    [InlineData("Remove-Item -Recurse -Force C:\\Temp\\x")]
    [InlineData("Get-ChildItem C:\\Temp | Remove-Item")]
    [InlineData("del \"C:\\Temp\\x\"")]
    [InlineData("cmd /c del C:\\Temp\\x")]
    [InlineData("cleanmgr /sagerun:1")]
    [InlineData("Clear-RecycleBin -Force")]
    [InlineData("Set-Content C:\\Temp\\x.txt hello")]
    [InlineData("Dism /Online /Cleanup-Image /StartComponentCleanup")]
    [InlineData("ForEach-Object { Remove-Item C:\\Temp\\x }")]
    public void ReadOnlyGuard_DetectsDestructiveCommands(string command) =>
        Assert.True(AgentModeInjector.IsDestructiveCommand(command));

    [Theory]
    [InlineData("Get-ChildItem C:\\Temp -Force")]
    [InlineData("Get-ChildItem -Filter *del*")]
    [InlineData("Get-ChildItem | Where-Object { $_.Name -like '*del*' }")]
    [InlineData("Format-Table Name,Length")]
    [InlineData("Get-Content C:\\Temp\\log.txt | Select-String 'remove-item'")]
    [InlineData("Measure-Object -Property Length -Sum")]
    public void ReadOnlyGuard_DoesNotFlagReadOnlyCommands(string command) =>
        Assert.False(AgentModeInjector.IsDestructiveCommand(command));

    [Fact]
    public void SkillFile_ExplainsDepthAndStrictReadOnlyRules()
    {
        var path = Path.Combine(TestPaths.RepoRoot, "plugins", "Tubatools.AiJunkCleanup", "assets", "tuba-junk-cleanup", "SKILL.md");
        var text = File.ReadAllText(path);
        Assert.Contains("name: tuba-junk-cleanup", text);
        Assert.Contains("快速", text);
        Assert.Contains("标准", text);
        Assert.Contains("深度", text);
        Assert.Contains("tuba-junk-plan", text);
        Assert.Contains("意外", text);
        Assert.Contains("禁止任何删除", text);
        Assert.Contains("Remove-Item", text);
        Assert.Contains("shallow-scan.json", text);
        Assert.Contains("scan-index.json", text);
        Assert.Contains("shallow-scan.ps1", text);
        Assert.Contains("浅层扫描", text);
        Assert.Contains("消息末尾", text);
        Assert.Contains("按下发送", text);
        Assert.Contains("不要只盯着大文件", text);
        Assert.Contains("临时目录", text);
    }

    [Fact]
    public void Safety_BlocksCriticalRootsAndAllowsKnownCaches()
    {
        using var root = new TempTree();
        var env = root.Environment;
        var windows = root.Dir("Windows");
        var system32 = root.Dir("Windows", "System32");
        var windowsTemp = root.Dir("Windows", "Temp");
        var updateDownload = root.Dir("Windows", "SoftwareDistribution", "Download");
        var programFiles = root.Dir("Program Files");
        var programData = root.Dir("ProgramData");
        var profile = root.Dir("Users", "Tester");
        var appData = root.Dir("Users", "Tester", "AppData");
        var local = root.Dir("Users", "Tester", "AppData", "Local");
        var roaming = root.Dir("Users", "Tester", "AppData", "Roaming");
        var temp = root.Dir("Users", "Tester", "AppData", "Local", "Temp");
        var desktop = root.Dir("Users", "Tester", "Desktop");
        var downloads = root.Dir("Users", "Tester", "Downloads");

        Assert.True(Item(windows).Let(i => JunkSafety.Evaluate(i, env)).Blocked);
        Assert.True(Item(system32).Let(i => JunkSafety.Evaluate(i, env)).Blocked);
        Assert.True(Item(programFiles).Let(i => JunkSafety.Evaluate(i, env)).Blocked);
        Assert.True(Item(programData).Let(i => JunkSafety.Evaluate(i, env)).Blocked);
        Assert.True(Item(profile).Let(i => JunkSafety.Evaluate(i, env)).Blocked);
        Assert.True(Item(local).Let(i => JunkSafety.Evaluate(i, env)).Blocked);
        Assert.True(Item(roaming).Let(i => JunkSafety.Evaluate(i, env)).Blocked);
        Assert.True(Item(desktop).Let(i => JunkSafety.Evaluate(i, env)).Blocked);
        Assert.True(Item(downloads).Let(i => JunkSafety.Evaluate(i, env)).Blocked);
        Assert.True(Item(root.Dir("App", "data.bin")).Let(i => JunkSafety.Evaluate(i, env)).Blocked);
        Assert.True(Item(root.Dir("Data", "cache.bin")).Let(i => JunkSafety.Evaluate(i, env)).Blocked);
        Assert.True(Item(root.Dir("Users", "Tester", "AppData", "Local", "Programs", "App")).Let(i => JunkSafety.Evaluate(i, env)).Blocked);

        var safeTemp = JunkSafety.Evaluate(Item(root.File("Windows", "Temp", "a.tmp")), env);
        Assert.False(safeTemp.Blocked);
        Assert.Equal(JunkRisk.Low, safeTemp.Risk);

        var update = JunkSafety.Evaluate(Item(root.Dir("Windows", "SoftwareDistribution", "Download", "u.cab")), env);
        Assert.False(update.Blocked);
        Assert.Equal(JunkRisk.Medium, update.Risk);

        // 临时测试环境把 Windows 指向临时目录，但真实系统盘上的 Windows / Program Files 仍必须拒绝。
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        Assert.True(JunkSafety.Evaluate(Item(Path.Combine(systemDrive, "Windows")), env).Blocked);
        Assert.True(JunkSafety.Evaluate(Item(Path.Combine(systemDrive, "Program Files")), env).Blocked);

        var userTemp = JunkSafety.Evaluate(Item(root.File("Users", "Tester", "AppData", "Local", "Temp", "b.tmp")), env);
        Assert.False(userTemp.Blocked);
        Assert.Equal(JunkRisk.Low, userTemp.Risk);

        // 临时目录由 AI 标成高风险时不能被安全规则降级。
        Assert.Equal(JunkRisk.High, JunkSafety.Evaluate(Item(root.File("Users", "Tester", "AppData", "Local", "Temp", "b.tmp"), JunkRisk.High), env).Risk);
    }

    [Fact]
    public void Safety_EscalatesPersonalFoldersAndProgramData()
    {
        using var root = new TempTree();
        var env = root.Environment;
        root.Dir("Users", "Tester", "Desktop");
        var desktopFile = root.File("Users", "Tester", "Desktop", "note.txt");
        var desktopFolder = root.Dir("Users", "Tester", "Desktop", "OldProject");
        var downloadedFile = root.File("Users", "Tester", "Downloads", "setup.exe");
        var programDataCache = root.Dir("ProgramData", "VendorCache");
        var browserUserData = root.Dir("Users", "Tester", "AppData", "Local", "Google", "Chrome", "User Data");

        Assert.Equal(JunkRisk.Medium, JunkSafety.Evaluate(Item(desktopFile), env).Risk);
        Assert.Equal(JunkRisk.High, JunkSafety.Evaluate(Item(desktopFolder), env).Risk);
        Assert.Equal(JunkRisk.Medium, JunkSafety.Evaluate(Item(downloadedFile), env).Risk);
        Assert.Equal(JunkRisk.High, JunkSafety.Evaluate(Item(programDataCache), env).Risk);
        Assert.Equal(JunkRisk.High, JunkSafety.Evaluate(Item(browserUserData), env).Risk);
    }

    [Fact]
    public void Safety_BlocksMissingRelativeNetworkAndReparseTargets()
    {
        using var root = new TempTree();
        var env = root.Environment;
        var missing = Path.Combine(root.Path, "not-there.tmp");
        Assert.True(JunkSafety.Evaluate(Item(missing), env).Blocked);
        Assert.True(JunkSafety.Evaluate(Item("relative\\path.tmp"), env).Blocked);
        Assert.True(JunkSafety.Evaluate(Item(@"\\server\share\file.tmp"), env).Blocked);
    }

    [Fact]
    public void Safety_AllowsBrowserCacheButNotWholeUserData()
    {
        using var root = new TempTree();
        var env = root.Environment;
        var cache = root.File("Users", "Tester", "AppData", "Local", "Google", "Chrome", "User Data", "Default", "Cache", "data_0");
        var userData = root.Dir("Users", "Tester", "AppData", "Local", "Google", "Chrome", "User Data");
        var cacheResult = JunkSafety.Evaluate(Item(cache), env);
        Assert.False(cacheResult.Blocked);
        Assert.Equal(JunkRisk.Low, cacheResult.Risk);
        Assert.Equal(JunkRisk.High, JunkSafety.Evaluate(Item(userData), env).Risk);
    }

    [Fact]
    public void ShallowScanScript_IsEmbeddedAndReadOnly()
    {
        using var stream = typeof(AiJunkCleanupPlugin).Assembly.GetManifestResourceStream("TubaJunkCleanupShallowScanScript.ps1");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var script = reader.ReadToEnd();
        Assert.Contains("ShallowScan", script);
        Assert.Contains("ReparsePoint", script);
        Assert.Contains("EnumerateFiles", script);
        Assert.Contains("Depth", script);
        Assert.DoesNotContain("Remove-Item", script);
        Assert.DoesNotContain("Delete", script);
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Temp", true)]
    [InlineData(@"C:\Windows\Logs", true)]
    [InlineData(@"C:\Windows\SoftwareDistribution\Download", true)]
    [InlineData(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache", true)]
    [InlineData(@"C:\Program Files\App", false)]
    [InlineData(@"C:\Users\me\Documents", false)]
    public void CommonJunkDirectory_IsRecognized(string path, bool expected) =>
        Assert.Equal(expected, HostSpaceScan.IsCommonJunkDirectory(path));

    [Fact]
    public async Task HostSpaceScan_BuildsThreeLevelsFast()
    {
        var root = NewTemp("tuba-fast-scan-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a", "b", "c", "d"));
            Directory.CreateDirectory(Path.Combine(root, "a", "b", "c", "d", "Logs"));
            await File.WriteAllBytesAsync(Path.Combine(root, "a", "b", "c", "d", "Logs", "tiny.log"), new byte[10]);
            await File.WriteAllBytesAsync(Path.Combine(root, "a", "one.bin"), new byte[100]);
            await File.WriteAllBytesAsync(Path.Combine(root, "a", "b", "two.bin"), new byte[200]);
            await File.WriteAllBytesAsync(Path.Combine(root, "a", "b", "c", "three.bin"), new byte[300]);
            await File.WriteAllBytesAsync(Path.Combine(root, "a", "b", "c", "d", "four.bin"), new byte[400]);

            var host = HostSpaceScan.TryCreate(root, CancellationToken.None, out var error);
            Assert.NotNull(host);
            Assert.Equal("", error);
            try
            {
                await host!.ScanTask.WaitAsync(TimeSpan.FromSeconds(30));
                var data = host.BuildShallow(3, 100);
                Assert.True(data.TotalBytes >= 600, $"total={data.TotalBytes}");
                Assert.True(data.Files >= 3, $"files={data.Files}");
                Assert.Contains(data.Directories, item => item.Level == 1 && item.Path.EndsWith("a", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(data.Directories, item => item.Level == 2 && item.Path.EndsWith(Path.Combine("a", "b"), StringComparison.OrdinalIgnoreCase));
                Assert.Contains(data.Directories, item => item.Level == 3 && item.Path.EndsWith(Path.Combine("a", "b", "c"), StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(data.Directories, item => item.Level == 4);

                var index = host.BuildIndex(10, 0, 0, 100, 100);
                Assert.Contains(index.Directories, item => item.Level == 4 &&
                    item.Path.EndsWith(Path.Combine("a", "b", "c", "d"), StringComparison.OrdinalIgnoreCase));
                Assert.Contains(index.Files, item => item.Path.EndsWith("four.bin", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(index.Files, item => item.Path.EndsWith("three.bin", StringComparison.OrdinalIgnoreCase));

                // 门槛设得比所有目录都大时，常见垃圾目录（Logs）仍应被收录。
                var common = host.BuildIndex(10, 1L << 30, 1L << 30, 100, 100);
                Assert.Contains(common.Directories, item => item.Path.EndsWith(Path.Combine("d", "Logs"), StringComparison.OrdinalIgnoreCase));
                Assert.Contains(common.Files, item => item.Path.EndsWith("tiny.log", StringComparison.OrdinalIgnoreCase));
            }
            finally { host.Dispose(); }
        }
        finally { DeleteTemp(root); }
    }

    [Fact]
    public async Task ShallowScanJob_WritesShallowScanJson()
    {
        var root = NewTemp("tuba-fast-job-");
        var workspace = NewTemp("tuba-fast-ws-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a", "b", "c"));
            await File.WriteAllBytesAsync(Path.Combine(root, "a", "b", "c", "deep.bin"), new byte[321]);
            var job = new ShallowScanJob(root, workspace);
            job.Start(null);
            await job.Completion.WaitAsync(TimeSpan.FromSeconds(45));
            Assert.True(string.IsNullOrEmpty(job.Error), job.Error);
            Assert.NotNull(job.ResultPath);
            Assert.True(File.Exists(job.ResultPath));
            Assert.NotNull(job.Result);
            Assert.Equal(3, job.Result!.Depth);
            Assert.Contains(job.Result.Directories, item => item.Level == 3);
            Assert.Equal(8, job.Result.IndexDepth);
            Assert.NotNull(job.Result.IndexPath);
            Assert.NotNull(job.IndexPath);
            Assert.True(File.Exists(job.IndexPath));
            var json = await File.ReadAllTextAsync(job.ResultPath!);
            Assert.Contains("\"directories\"", json);
            Assert.Contains("\"level\": 3", json);
            Assert.Contains("\"totalBytes\"", json);
            var indexJson = await File.ReadAllTextAsync(job.IndexPath!);
            Assert.Contains("\"directories\"", indexJson);
            Assert.Contains("\"level\":3", indexJson);
            Assert.Contains("\"minDirectoryBytes\"", indexJson);
            Assert.Contains("\"files\"", indexJson);
        }
        finally
        {
            DeleteTemp(root);
            DeleteTemp(workspace);
        }
    }

    [Fact]
    public async Task FastFileDeleter_DeletesTreeDirectlyWithProgress()
    {
        var root = NewTemp("tuba-direct-del-");
        try
        {
            var nested = Path.Combine(root, "a", "b");
            Directory.CreateDirectory(nested);
            await File.WriteAllBytesAsync(Path.Combine(root, "one.bin"), new byte[1024]);
            for (var i = 0; i < 8; i++)
                await File.WriteAllBytesAsync(Path.Combine(root, $"many-{i}.bin"), new byte[10]);
            var two = Path.Combine(nested, "two.bin");
            await File.WriteAllBytesAsync(two, new byte[2048]);
            File.SetAttributes(two, FileAttributes.ReadOnly);

            var reports = new List<DeleteProgress>();
            var outcome = await FastFileDeleter.DeleteAsync(
                [new DeleteTarget(root, true, 3072)], 3072, new SyncProgress<DeleteProgress>(reports.Add),
                CancellationToken.None);

            Assert.False(outcome.Canceled);
            var target = Assert.Single(outcome.Targets);
            Assert.True(target.Success, target.Error);
            Assert.False(Directory.Exists(root));
            Assert.True(outcome.BytesDeleted >= 3072, $"bytes={outcome.BytesDeleted}");
            Assert.True(outcome.ItemsDeleted >= 1, $"items={outcome.ItemsDeleted}");
            Assert.NotEmpty(reports);
            Assert.Contains(reports, report => report.BytesDeleted >= 3072);
        }
        finally { DeleteTemp(root); }
    }

    [Fact]
    public async Task FastFileDeleter_CancelBeforeStartLeavesTree()
    {
        var root = NewTemp("tuba-direct-cancel-");
        try
        {
            var file = Path.Combine(root, "keep.bin");
            await File.WriteAllBytesAsync(file, new byte[16]);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var outcome = await FastFileDeleter.DeleteAsync(
                [new DeleteTarget(root, true, 16)], 16, null, cts.Token);
            Assert.True(outcome.Canceled);
            Assert.True(File.Exists(file));
        }
        finally { DeleteTemp(root); }
    }

    private static string NewTemp(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTemp(string path)
    {
        try { Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static JunkPlanItem Item(string path, JunkRisk risk = JunkRisk.Low, bool isDirectory = false) =>
        new(path, isDirectory, 0, risk, "测试", "测试原因", "", "");

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;
        private readonly object _gate = new();
        internal SyncProgress(Action<T> report) => _report = report;
        public void Report(T value) { lock (_gate) _report(value); }
    }

    private sealed class CapturingHost : IPluginHost
    {
        public List<PluginToolDefinition> Tools { get; } = [];
        public void RegisterTool(PluginToolDefinition tool) => Tools.Add(tool);
    }

    private sealed class TempTree : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tuba-junk-" + Guid.NewGuid().ToString("N"));
        internal JunkSafetyEnvironment Environment { get; }

        internal TempTree()
        {
            Directory.CreateDirectory(Path);
            Environment = new JunkSafetyEnvironment(
                Windows: Dir("Windows"),
                ProgramFiles: Dir("Program Files"),
                ProgramFilesX86: Dir("Program Files (x86)"),
                ProgramData: Dir("ProgramData"),
                UserProfile: Dir("Users", "Tester"),
                AppData: Dir("Users", "Tester", "AppData"),
                LocalAppData: Dir("Users", "Tester", "AppData", "Local"),
                RoamingAppData: Dir("Users", "Tester", "AppData", "Roaming"),
                Temp: Dir("Users", "Tester", "AppData", "Local", "Temp"),
                Desktop: Dir("Users", "Tester", "Desktop"),
                Documents: Dir("Users", "Tester", "Documents"),
                Downloads: Dir("Users", "Tester", "Downloads"),
                Pictures: Dir("Users", "Tester", "Pictures"),
                Music: Dir("Users", "Tester", "Music"),
                Videos: Dir("Users", "Tester", "Videos"),
                OneDrive: null,
                AppDirectory: Dir("App"),
                DataDirectory: Dir("Data"));
        }

        internal string Dir(params string[] parts)
        {
            var path = Combine(parts);
            Directory.CreateDirectory(path);
            return path;
        }

        internal string File(params string[] parts)
        {
            var path = Combine(parts);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, "test");
            return path;
        }

        private string Combine(string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

internal static class TestExtensions
{
    internal static TResult Let<T, TResult>(this T value, Func<T, TResult> selector) => selector(value);
}

internal static class TestPaths
{
    internal static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string callerFilePath = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(callerFilePath), AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(start)) continue;
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TubaWinUi3.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
        }
        throw new InvalidOperationException("未找到仓库根目录（TubaWinUi3.sln）");
    }
}
