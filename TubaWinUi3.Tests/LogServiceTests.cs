using System.Reflection;
using System.Text;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>统一日志服务：目录解析、写入格式、滚动、读取末尾、清空与旧位置迁移。</summary>
public class LogServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string? _previousOverride;

    public LogServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "TubaLogTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _previousOverride = ConfigManager.DataDirectoryOverride;
        ConfigManager.DataDirectoryOverride = _root;
        AppSettings.InvalidateCache();

        // 本机若设置过自定义日志目录，测试期间必须去掉，否则会写进用户的真实目录
        AppSettings.Remove(LogService.LogDirSettingKey);
        ResetPrepareState();
    }

    public void Dispose()
    {
        try { AppSettings.Remove(LogService.LogDirSettingKey); } catch { }
        ConfigManager.DataDirectoryOverride = _previousOverride;
        AppSettings.InvalidateCache();
        try { Directory.Delete(_root, true); } catch { }
    }

    /// <summary>Prepare 每进程只跑一次，测试之间要复位，才能重复验证迁移。</summary>
    private static void ResetPrepareState()
    {
        typeof(LogService)
            .GetField("_prepared", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, 0);
    }

    private static string Category => "unit-test";

    [Fact]
    public void Info_WritesIntoUnifiedDirectoryWithLevelTag()
    {
        LogService.Info(Category, "hello world");

        var path = LogService.PathFor(Category);
        Assert.Equal(Path.Combine(_root, "Logs"), Path.GetDirectoryName(path));
        Assert.True(File.Exists(path));

        var text = File.ReadAllText(path);
        Assert.Contains("[INFO ]", text);
        Assert.Contains("hello world", text);
    }

    [Fact]
    public void Error_AppendsExceptionDetailOnFollowingLines()
    {
        LogService.Error(Category, "boom", new InvalidOperationException("inner"));

        var text = LogService.ReadTailFor(Category);
        Assert.Contains("[ERROR]", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("inner", text);
    }

    [Theory]
    [InlineData(LogLevel.Debug, "[DEBUG]")]
    [InlineData(LogLevel.Info, "[INFO ]")]
    [InlineData(LogLevel.Warn, "[WARN ]")]
    [InlineData(LogLevel.Error, "[ERROR]")]
    public void Write_UsesFixedWidthLevelTags(LogLevel level, string expected)
    {
        LogService.Write(level, Category, "x");

        Assert.Contains(expected, LogService.ReadTailFor(Category));
    }

    [Fact]
    public void WriteBlock_KeepsOriginalTextUntouched()
    {
        var block = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 崩溃报告\n第二行\n{new string('-', 20)}\n";

        LogService.WriteBlock(LogService.Crash, block);

        var text = LogService.ReadTailFor(LogService.Crash);
        Assert.Contains("崩溃报告", text);
        Assert.Contains("第二行", text);
        Assert.DoesNotContain("[INFO ]", text);
    }

    [Fact]
    public void ResetFile_TruncatesPreviousContent()
    {
        LogService.Info(Category, "first run");
        LogService.Info(Category, "still first run");

        LogService.ResetFile(Category, "===== 新会话 =====");

        var text = LogService.ReadTailFor(Category);
        Assert.Contains("新会话", text);
        Assert.DoesNotContain("first run", text);
    }

    [Fact]
    public void Writes_RotateInsteadOfGrowingForever()
    {
        var chunk = new string('x', 8 * 1024);
        var path = LogService.PathFor(Category);

        // 超过 1 MB 阈值后应滚动出 .1 / .2 / …
        for (var i = 0; i < 200; i++) LogService.Info(Category, chunk);

        Assert.True(File.Exists(path + ".1"), "超过上限后应生成滚动文件 .1");
        Assert.True(new FileInfo(path).Length <= LogService.MaxFileBytes);

        var rolled = Enumerable.Range(1, LogService.MaxRotatedFiles + 2)
            .Count(i => File.Exists($"{path}.{i}"));
        Assert.InRange(rolled, 1, LogService.MaxRotatedFiles);
    }

    [Fact]
    public void ReadTail_ReturnsOnlyLastLines()
    {
        var path = Path.Combine(_root, "tail.log");
        File.WriteAllLines(path, Enumerable.Range(1, 100).Select(i => $"line-{i}"));

        var text = LogService.ReadTail(path, maxLines: 10);

        var lines = text.Split('\n');
        Assert.Equal(10, lines.Length);
        Assert.Equal("line-91", lines[0]);
        Assert.Equal("line-100", lines[^1]);
    }

    [Fact]
    public void ReadTail_MissingFile_ReturnsEmpty()
        => Assert.Equal(string.Empty, LogService.ReadTail(Path.Combine(_root, "nope.log")));

    [Fact]
    public void ListFiles_PutsKnownCategoriesFirstAndIgnoresRotatedCopies()
    {
        LogService.Info(LogService.App, "a");
        LogService.Info("zzz-unknown", "b");
        File.WriteAllText(LogService.PathFor(LogService.App) + ".1", "rotated");

        var files = LogService.ListFiles();

        Assert.Equal(LogService.App, files[0].Category);
        Assert.Equal("zzz-unknown", files[^1].Category);
        Assert.DoesNotContain(files, f => f.Category.EndsWith(".1", StringComparison.Ordinal));
    }

    [Fact]
    public void Clear_RemovesFileAndRotatedCopies()
    {
        LogService.Info(Category, "keep? no");
        var path = LogService.PathFor(Category);
        File.WriteAllText(path + ".1", "rotated");

        LogService.Clear(Category);

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".1"));
    }

    [Fact]
    public void Sanitize_ReplacesInvalidFileNameChars()
    {
        Assert.Equal("a_b", LogService.Sanitize("a/b"));
        Assert.Equal(LogService.App, LogService.Sanitize(""));
    }

    [Fact]
    public void Prepare_MergesLegacyLogFilesIntoUnifiedDirectory()
    {
        // 旧位置：数据目录根部的分散日志 + 旧的 TEMP 崩溃日志
        var legacyAgent = Path.Combine(_root, "agent-debug.log");
        File.WriteAllText(legacyAgent, "旧版 AI 日志内容\n");
        var legacyStress = Path.Combine(_root, "stress_test.log");
        File.WriteAllText(legacyStress, "旧版烤机日志内容\n");
        var legacyBackendDir = Path.Combine(_root, "active_intercept");
        Directory.CreateDirectory(legacyBackendDir);
        File.WriteAllText(Path.Combine(legacyBackendDir, "backend.log"), "旧版后端日志内容\n");

        LogService.Prepare();

        Assert.False(File.Exists(legacyAgent));
        Assert.False(File.Exists(legacyStress));
        Assert.Contains("旧版 AI 日志内容", LogService.ReadTailFor(LogService.Agent));
        Assert.Contains("旧版烤机日志内容", LogService.ReadTailFor(LogService.StressTest));
        Assert.Contains("旧版后端日志内容", LogService.ReadTailFor(LogService.Backend));
    }

    [Fact]
    public void DisplayName_FallsBackToCategoryForUnknownKeys()
        => Assert.Equal("zzz-unknown", LogService.DisplayName("zzz-unknown"));

    [Fact]
    public void IsDirectoryWritable_DetectsUnwritablePath()
    {
        var dir = Path.Combine(_root, "writable");
        Assert.True(LogService.IsDirectoryWritable(dir));

        // 文件路径当目录用 → 不可写
        var file = Path.Combine(_root, "file.txt");
        File.WriteAllText(file, "x");
        Assert.False(LogService.IsDirectoryWritable(file));
    }

    [Fact]
    public void TotalSize_CountsRotatedCopies()
    {
        var path = LogService.PathFor(Category);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[100]);
        File.WriteAllBytes(path + ".1", new byte[50]);

        Assert.True(LogService.TotalSizeBytes() >= 150);
    }

    // ── 总量配额 ───────────────────────────────────────────

    [Fact]
    public void EnforceQuota_NoopWhenUnderLimit()
    {
        var path = LogService.PathFor(LogService.Agent);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[100]);

        Assert.Equal(0, LogService.EnforceQuota(10_000));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void EnforceQuota_DeletesRotatedCopiesBeforeCurrentFiles()
    {
        var dir = LogService.LogDirectory;
        Directory.CreateDirectory(dir);
        var appPath = Path.Combine(dir, "app.log");
        var agentPath = Path.Combine(dir, "agent.log");
        File.WriteAllBytes(appPath, new byte[400]);
        File.WriteAllBytes(agentPath, new byte[400]);
        File.WriteAllBytes(agentPath + ".1", new byte[400]);

        // 总量 1200；配额 1000 → 目标 800：只删得掉滚动副本，主文件都还在
        Assert.Equal(1, LogService.EnforceQuota(1000));

        Assert.False(File.Exists(agentPath + ".1"));
        Assert.True(File.Exists(agentPath));
        Assert.True(File.Exists(appPath));
    }

    [Fact]
    public void EnforceQuota_DeletesOldestUnprotectedFileWhenStillOver()
    {
        var dir = LogService.LogDirectory;
        Directory.CreateDirectory(dir);
        var appPath = Path.Combine(dir, "app.log");
        var agentPath = Path.Combine(dir, "agent.log");
        var tunnelPath = Path.Combine(dir, "game-tunnel.log");
        File.WriteAllBytes(appPath, new byte[300]);
        File.WriteAllBytes(agentPath, new byte[400]);
        File.WriteAllBytes(tunnelPath, new byte[300]);
        File.SetLastWriteTimeUtc(agentPath, DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc(tunnelPath, DateTime.UtcNow.AddHours(-1));

        // 总量 1000，配额 900 → 目标 720：只删得掉最久没写过的 agent.log
        LogService.EnforceQuota(900);

        Assert.False(File.Exists(agentPath));
        Assert.True(File.Exists(tunnelPath));
        Assert.True(File.Exists(appPath));
    }

    [Fact]
    public void EnforceQuota_NeverDeletesProtectedCategoryFiles()
    {
        var dir = LogService.LogDirectory;
        Directory.CreateDirectory(dir);
        var crashPath = Path.Combine(dir, "crash.log");
        var startupPath = Path.Combine(dir, "startup.log");
        File.WriteAllBytes(crashPath, new byte[500]);
        File.WriteAllBytes(startupPath, new byte[500]);

        Assert.Equal(0, LogService.EnforceQuota(100));

        Assert.True(File.Exists(crashPath));
        Assert.True(File.Exists(startupPath));
    }

    [Fact]
    public void EnforceQuota_StillTrimsProtectedCategoryRotatedCopies()
    {
        var dir = LogService.LogDirectory;
        Directory.CreateDirectory(dir);
        var crashPath = Path.Combine(dir, "crash.log");
        File.WriteAllBytes(crashPath, new byte[300]);
        File.WriteAllBytes($"{crashPath}.1", new byte[300]);
        File.WriteAllBytes($"{crashPath}.2", new byte[300]);
        File.SetLastWriteTimeUtc($"{crashPath}.1", DateTime.UtcNow.AddHours(-2));
        File.SetLastWriteTimeUtc($"{crashPath}.2", DateTime.UtcNow.AddHours(-1));

        // 总量 900，配额 800 → 目标 640：只能删更旧的滚动副本，主文件与较新的副本保留
        Assert.Equal(1, LogService.EnforceQuota(800));

        Assert.True(File.Exists(crashPath));
        Assert.False(File.Exists($"{crashPath}.1"));
        Assert.True(File.Exists($"{crashPath}.2"));
    }
}
