using System.IO.Compression;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

public sealed class AgentPluginAutoLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tuba-auto-plugin-" + Guid.NewGuid().ToString("N"));
    public AgentPluginAutoLoaderTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private string Package(string version = "1.0.0", string id = "Tubatools.AutoTest")
    {
        var path = Path.Combine(_root, id + "." + version + ".nupkg");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("auto.nuspec").Open());
        writer.Write($"<package><metadata><id>{id}</id><version>{version}</version><tags>Tubatools</tags></metadata></package>");
        return path;
    }
    private static IReadOnlyList<AgentMessage> Messages(string output, int exit = 0, string id = "pack", string command = "dotnet pack Plugin.csproj -o artifacts") =>
        [new("message", "assistant", "", "", Content: [new("content", "", new AgentOperation(id, "bash", "command", exit == 0 ? "completed" : "error", "Pack plugin", command, output, "", exit))])];

    [Fact]
    public async Task SuccessfulBuild_AutomaticallyInstallsOnlyNewPackage_AndReportsProgress()
    {
        var installed = new List<string>();
        var loader = await AgentPluginAutoLoader.CaptureAsync([], default, _root, (p, _) => { installed.Add(p); return Task.CompletedTask; });
        var path = Package();
        var progress = new List<AgentOperation>();
        var result = await loader.LoadAsync(Messages("Successfully created package '" + path + "'."), progress.Add, default);
        Assert.Equal(path, Assert.Single(installed));
        Assert.True(Assert.Single(result).Success);
        Assert.Equal(new[] { "running", "completed" }, progress.Select(p => p.Status));
    }

    [Theory]
    [InlineData(1, "dotnet pack Plugin.csproj")]
    [InlineData(0, "dotnet --version")]
    public async Task FailedOrUnrelatedCommand_DoesNotExecuteNewPackage(int exit, string command)
    {
        var loader = await AgentPluginAutoLoader.CaptureAsync([], default, _root, (_, _) => throw new Exception("Must not load"));
        var path = Package();
        Assert.Empty(await loader.LoadAsync(Messages(path, exit, command: command), _ => { }, default));
    }

    [Fact]
    public async Task UnchangedExistingPackage_AndPastBuild_AreNotReplayed()
    {
        var path = Package();
        var loader = await AgentPluginAutoLoader.CaptureAsync(Messages(path), default, _root, (_, _) => throw new Exception("Must not load"));
        Assert.Empty(await loader.LoadAsync(Messages(path, id: "new-command"), _ => { }, default));
        var fresh = Package("1.0.1");
        Assert.Empty(await loader.LoadAsync(Messages(fresh), _ => { }, default));
    }

    [Fact]
    public async Task UnmentionedConcurrentOutput_IsNotLoaded()
    {
        var loader = await AgentPluginAutoLoader.CaptureAsync([], default, _root, (_, _) => throw new Exception("Must not load"));
        Package();
        Assert.Empty(await loader.LoadAsync(Messages("Successfully created unrelated-package.nupkg"), _ => { }, default));
    }

    [Fact]
    public async Task LastBuildOfEachPackage_Wins()
    {
        var installed = new List<string>();
        var loader = await AgentPluginAutoLoader.CaptureAsync([], default, _root, (p, _) => { installed.Add(p); return Task.CompletedTask; });
        var old = Package();
        var latest = Package("1.0.1");
        File.SetLastWriteTimeUtc(latest, DateTime.UtcNow.AddSeconds(2));
        await loader.LoadAsync(Messages(old + "\n" + latest), _ => { }, default);
        Assert.Equal(latest, Assert.Single(installed));
    }

    [Fact]
    public async Task MultiplePackages_AutoLoadTogether_ThenUpdateOnlyChangedProject()
    {
        var installed = new List<string>();
        Task Install(string path, CancellationToken _) { installed.Add(path); return Task.CompletedTask; }
        var firstTurn = await AgentPluginAutoLoader.CaptureAsync([], default, _root, Install);
        var a = Package(id: "Tubatools.Cleanup");
        var b = Package(id: "Tubatools.Network");
        var messages = Messages(a + "\n" + b);
        Assert.Equal(2, (await firstTurn.LoadAsync(messages, _ => { }, default)).Count);
        Assert.Equal(new[] { a, b }.Order(), installed.Order());
        installed.Clear();
        var nextTurn = await AgentPluginAutoLoader.CaptureAsync(messages, default, _root, Install);
        var updated = Package("1.0.1", "Tubatools.Cleanup");
        Assert.Single(await nextTurn.LoadAsync(Messages(updated, id: "pack-update"), _ => { }, default));
        Assert.Equal(updated, Assert.Single(installed));
        Assert.True(File.Exists(b));
    }

    [Fact]
    public async Task LoadFailure_IsShownAsFailedOperation()
    {
        var loader = await AgentPluginAutoLoader.CaptureAsync([], default, _root, (_, _) => throw new InvalidDataException("Missing entry"));
        var path = Package();
        var progress = new List<AgentOperation>();
        var result = await loader.LoadAsync(Messages(path), progress.Add, default);
        Assert.False(Assert.Single(result).Success);
        Assert.Equal("error", progress.Last().Status);
        Assert.Contains("Missing entry", progress.Last().Output);
    }
}
