using System.ComponentModel;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

public sealed class AgentPluginAutoLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tuba-auto-plugin-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = [];
    public AgentPluginAutoLoaderTests() => Directory.CreateDirectory(_root);
    public void Dispose()
    {
        // 先摘掉联接，再删根目录，避免递归删除跟随到目标目录。
        foreach (var junction in _junctions)
            try { Directory.Delete(junction); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Directory.Delete(_root, true);
    }

    /// <summary>建目录联接（不需要管理员权限），模拟系统盘搬家把用户目录/AppData 做成链接的机器。</summary>
    private void Junction(string alias, string target)
    {
        Directory.CreateDirectory(alias);
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        var print = Encoding.Unicode.GetBytes(target);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0xA0000003u); // IO_REPARSE_TAG_MOUNT_POINT
        writer.Write(checked((ushort)(8 + substitute.Length + 2 + print.Length + 2)));
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(checked((ushort)substitute.Length));
        writer.Write(checked((ushort)(substitute.Length + 2)));
        writer.Write(checked((ushort)print.Length));
        writer.Write(substitute);
        writer.Write((ushort)0);
        writer.Write(print);
        writer.Write((ushort)0);
        var buffer = stream.ToArray();
        using var handle = CreateFileW(alias, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!DeviceIoControl(handle, 0x000900A4, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _junctions.Add(alias);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize,
        IntPtr output, int outputSize, out int returned, IntPtr overlapped);

    private string Package(string version = "1.0.0", string id = "Tubatools.AutoTest", string? directory = null)
    {
        var path = Path.Combine(directory ?? _root, id + "." + version + ".nupkg");
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

    /// <summary>上层目录被系统盘搬家工具做成联接时必须继续工作，否则整轮 AI 对话都以「AI 组件不可用」失败。</summary>
    [Fact]
    public async Task LinkedWorkspaceAboveArtifacts_StillAutoLoads()
    {
        var workspace = Path.Combine(_root, "real-workspace");
        Directory.CreateDirectory(Path.Combine(workspace, "artifacts"));
        Junction(Path.Combine(_root, "workspace-link"), workspace);
        var artifacts = Path.Combine(_root, "workspace-link", "artifacts");
        var installed = new List<string>();
        var loader = await AgentPluginAutoLoader.CaptureAsync([], default, artifacts,
            (p, _) => { installed.Add(p); return Task.CompletedTask; });
        var path = Package(directory: artifacts);
        Assert.True(Assert.Single(await loader.LoadAsync(Messages(path), _ => { }, default)).Success);
        Assert.Equal(path, Assert.Single(installed));
    }

    /// <summary>产物目录自身是联接时仍然拒绝扫描，避免从任意位置加载包。</summary>
    [Fact]
    public async Task LinkedArtifactsDirectory_IsStillRejected()
    {
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        var artifacts = Path.Combine(_root, "work", "artifacts");
        Directory.CreateDirectory(Path.GetDirectoryName(artifacts)!);
        Junction(artifacts, outside);
        var error = await Assert.ThrowsAsync<IOException>(() =>
            AgentPluginAutoLoader.CaptureAsync([], default, artifacts, (_, _) => Task.CompletedTask));
        Assert.Contains("不能是链接", error.Message);
    }
}
