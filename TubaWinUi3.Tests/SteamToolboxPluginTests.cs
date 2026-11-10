using Tubatools.SteamToolbox;
using TubaWinUi3.PluginSdk;

namespace TubaWinUi3.Tests;

/// <summary>Steam 工具箱插件（plugins/Tubatools.SteamToolbox）的纯逻辑回归。</summary>
public sealed class SteamToolboxPluginTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void PluginRegistersOneMultiFunctionTool()
    {
        var host = new CapturingHost();
        new SteamToolboxPlugin().Register(host);
        var tool = Assert.Single(host.Tools);
        Assert.Equal("steam-toolbox", tool.Id);
        Assert.Equal("游戏工具", tool.Category);
        Assert.NotNull(tool.CreateContent);
        Assert.Null(tool.Xaml);
        Assert.Null(tool.ExecuteAsync);
    }

    [Fact]
    public void Vdf_ParsesEscapedPathsCommentsAndNestedObjects()
    {
        var data = SteamVdf.Parse("// test\n\"libraryfolders\" { \"1\" { \"path\" \"D:\\\\SteamLibrary\" } }");
        Assert.Equal(@"D:\SteamLibrary", data["LIBRARYFOLDERS"]["1"]["path"].Value);
    }

    [Theory]
    [InlineData("\"root\" { \"key\" \"value\"")]
    [InlineData("\"key\" \"unterminated")]
    [InlineData("}")]
    public void Vdf_RejectsIncompleteInput(string input) => Assert.Throws<FormatException>(() => SteamVdf.Parse(input));

    [Fact]
    public void Scan_MultipleLibrariesLegacyPathsAndBrokenManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "steam-test-" + Guid.NewGuid());
        var extra = Path.Combine(root, "other");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "steamapps", "common", "GameOne"));
            Directory.CreateDirectory(Path.Combine(extra, "steamapps", "common", "GameTwo"));
            Directory.CreateDirectory(Path.Combine(root, "config"));
            File.WriteAllText(Path.Combine(root, "config", "loginusers.vdf"), "\"users\" { \"76561197960265729\" { \"AccountName\" \"test\" \"PersonaName\" \"测试昵称\" \"MostRecent\" \"1\" } }");
            File.WriteAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"0\" { \"path\" \"" + root.Replace("\\", "\\\\") + "\" } \"1\" \"" + extra.Replace("\\", "\\\\") + "\" }");
            File.WriteAllText(Path.Combine(root, "steamapps", "appmanifest_1.acf"), "\"AppState\" { \"appid\" \"1\" \"name\" \"Game 1\" \"installdir\" \"GameOne\" \"SizeOnDisk\" \"1024\" }");
            File.WriteAllText(Path.Combine(extra, "steamapps", "appmanifest_2.acf"), "\"AppState\" { \"appid\" \"2\" \"name\" \"Game 2\" \"installdir\" \"GameTwo\" \"SizeOnDisk\" \"2048\" }");
            File.WriteAllText(Path.Combine(root, "steamapps", "appmanifest_3.acf"), "\"AppState\" {");
            File.WriteAllText(Path.Combine(root, "steamapps", "appmanifest_4.acf"), "\"AppState\" { \"appid\" \"4\" \"installdir\" \"../../escape\" }");
            var result = SteamCatalog.Scan(root);
            Assert.Equal(2, result.Libraries.Count);
            Assert.Equal(3072, result.Libraries.SelectMany(l => l.Games).Sum(g => g.Bytes));
            Assert.All(result.Libraries.SelectMany(l => l.Games), g => Assert.True(g.Installed));
            Assert.Equal("测试昵称", Assert.Single(result.Accounts).Name);
            Assert.Equal(2, result.Warnings.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RepeatedStartDoesNotResetTimeAndSwitchReplacesTask()
    {
        var task = new SteamIdleTaskTracker();
        task.Start(10, "First", "art", "account", Start);
        task.Start(10, "First", "art", "account", Start.AddMinutes(4));
        Assert.Equal(TimeSpan.FromMinutes(5), task.Snapshot(Start.AddMinutes(5)).Elapsed);
        task.Start(20, "Second", "art2", "account", Start.AddMinutes(6));
        var current = task.Snapshot(Start.AddMinutes(7));
        Assert.Equal((uint)20, current.AppId); Assert.Equal("Second", current.Name);
        Assert.Equal(TimeSpan.FromMinutes(1), current.Elapsed);
    }

    [Fact]
    public void PauseExcludesPausedTimeAndResumeKeepsOriginalStart()
    {
        var task = new SteamIdleTaskTracker(); task.Start(10, "Game", "art", "account", Start);
        task.End(SteamIdleTaskState.Paused, Start.AddMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(2), task.Snapshot(Start.AddHours(1)).Elapsed);
        task.Start(10, "Game", "art", "account", Start.AddMinutes(10));
        var current = task.Snapshot(Start.AddMinutes(13));
        Assert.Equal(TimeSpan.FromMinutes(5), current.Elapsed); Assert.Equal(Start, current.StartedAt);
    }

    [Fact]
    public void DisconnectStopsTimerAndDoesNotOverwriteDeliberateStop()
    {
        var task = new SteamIdleTaskTracker(); task.Start(10, "Game", "art", "account", Start);
        task.End(SteamIdleTaskState.Disconnected, Start.AddMinutes(3));
        Assert.Equal(TimeSpan.FromMinutes(3), task.Snapshot(Start.AddHours(1)).Elapsed);
        task.End(SteamIdleTaskState.Stopped, Start.AddHours(1));
        task.End(SteamIdleTaskState.Disconnected, Start.AddHours(2));
        Assert.Equal(SteamIdleTaskState.Stopped, task.Snapshot(Start.AddHours(3)).State);
    }

    [Fact]
    public async Task OnlineMonitorRepeatsWithoutOverlappingAndStopsOnLogout()
    {
        using var stop = new CancellationTokenSource();
        int requests = 0, confirmations = 0, active = 0;
        await SteamIdleConnectionMonitor.RunAsync(async token =>
        {
            Assert.Equal(1, Interlocked.Increment(ref active));
            Interlocked.Increment(ref requests);
            await Task.Yield();
            Interlocked.Decrement(ref active);
        }, () => { if (++confirmations == 3) stop.Cancel(); }, _ => Assert.Fail("Unexpected disconnect"),
            stop.Token, TimeSpan.FromMilliseconds(1));
        Assert.Equal(3, requests);
        Assert.Equal(3, confirmations);
    }

    [Fact]
    public async Task OnlineMonitorTimeoutDisconnectsAndFreezesTaskTime()
    {
        var task = new SteamIdleTaskTracker();
        task.Start(10, "Game", "art", "account", Start);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int failures = 0;
        await SteamIdleConnectionMonitor.RunAsync(_ => pending.Task, () => Assert.Fail("No server reply"), e =>
        {
            Assert.IsType<TimeoutException>(e);
            failures++;
            task.End(SteamIdleTaskState.Disconnected, Start.AddSeconds(10));
        }, CancellationToken.None, timeout: TimeSpan.FromMilliseconds(20));
        pending.SetResult(); // A late reply must not confirm or restart the ended task.
        Assert.Equal(1, failures);
        Assert.Equal(SteamIdleTaskState.Disconnected, task.Snapshot(Start.AddHours(1)).State);
        Assert.Equal(TimeSpan.FromSeconds(10), task.Snapshot(Start.AddHours(1)).Elapsed);
    }

    [Fact]
    public async Task OnlineMonitorCancellationIgnoresLateReplyFromPreviousLogin()
    {
        using var stop = new CancellationTokenSource();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = SteamIdleConnectionMonitor.RunAsync(_ => pending.Task,
            () => Assert.Fail("Old login confirmed"), _ => Assert.Fail("Old login disconnected"), stop.Token);
        stop.Cancel();
        await run;
        pending.SetResult();
    }

    [Fact]
    public async Task OnlineMonitorServerFailureEndsPolling()
    {
        int requests = 0, failures = 0;
        await SteamIdleConnectionMonitor.RunAsync(_ =>
        {
            requests++;
            throw new IOException("Connection lost");
        }, () => Assert.Fail("No server reply"), e => { Assert.IsType<IOException>(e); failures++; }, CancellationToken.None);
        Assert.Equal(1, requests);
        Assert.Equal(1, failures);
    }

    [Fact]
    public void GuardSubmissionWithoutAnActiveChallengeIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => SteamIdleService.SubmitGuardCode("12345"));
    }

    private sealed class CapturingHost : IPluginHost
    {
        public List<PluginToolDefinition> Tools { get; } = [];
        public void RegisterTool(PluginToolDefinition tool) => Tools.Add(tool);
    }
}
