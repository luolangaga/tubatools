using System.IO.Compression;
using System.Text.Json;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Plugins;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

[Collection("BuiltinToolRegistry")]
public sealed class PluginTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tuba-plugin-test-" + Guid.NewGuid().ToString("N"));
    private readonly string? _oldRoot = ConfigManager.DataDirectoryOverride;
    public PluginTests() { Directory.CreateDirectory(_root); ConfigManager.DataDirectoryOverride = _root; }
    public void Dispose()
    {
        ConfigManager.DataDirectoryOverride = _oldRoot;
        // Collectible ALC 的 Unload 只发起卸载，GC 后才释放 Windows DLL 映射。
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("C:/outside.dll")]
    [InlineData("/outside.dll")]
    [InlineData("plugin/../../outside.dll")]
    [InlineData("plugin/file.dll:stream")]
    public void ManifestPaths_CannotEscapePackage(string path) =>
        Assert.Throws<InvalidDataException>(() => PluginPackageRules.ResolveFile(_root, path));

    [Fact]
    public void Search_RequiresExactTag_IgnoresCase()
    {
        var packages = NuGetPluginSource.ParseSearch("""
            {"data":[
              {"id":"Good","version":"1.0.0","tags":["tUbAtOoLs"],"authors":["A"]},
              {"id":"Wrong","version":"1.0.0","tags":["TubatoolsOther"]},
              {"id":"Missing","version":"1.0.0"}
            ]}
            """);
        Assert.Equal("Good", Assert.Single(packages).Id);
    }

    [Fact]
    public void Search_ParsesCommunityMetadata()
    {
        var packages = NuGetPluginSource.ParseSearch("""
            {"data":[{"id":"Good","version":"1.2.3","tags":["Tubatools"],"authors":["A","B"],
              "description":"sample","projectUrl":null,"iconUrl":"https://example.test/icon.png","totalDownloads":42}]}
            """);
        var package = Assert.Single(packages);
        Assert.Equal("A、B", package.Authors);
        Assert.Equal("sample", package.Description);
        Assert.Equal(42, package.TotalDownloads);
        Assert.Equal("https://example.test/icon.png", package.IconUrl);
        Assert.Equal("https://www.nuget.org/packages/Good/1.2.3", NuGetPluginSource.PackagePage("Good", "1.2.3").AbsoluteUri);
    }

    private string Archive(string entryName, string? nuspec = null)
    {
        var archive = Path.Combine(_root, Guid.NewGuid() + ".nupkg");
        using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry(entryName).Open())) writer.Write("test");
        if (nuspec is not null)
        {
            using var writer = new StreamWriter(zip.CreateEntry("test.nuspec").Open());
            writer.Write(nuspec);
        }
        return archive;
    }

    [Fact]
    public void Extract_RejectsZipTraversal()
    {
        var archive = Archive("../escape.dll");
        Assert.Throws<InvalidDataException>(() => PluginManager.ExtractPackage(archive, Path.Combine(_root, "extract"), new("Test", "1.0.0", "", "")));
        Assert.False(File.Exists(Path.Combine(_root, "escape.dll")));
    }

    [Theory]
    [InlineData("Other", "Tubatools")]
    [InlineData("Test", "TubatoolsOther")]
    public void Extract_RejectsIdentityOrTagMismatch(string id, string tags)
    {
        var archive = Archive("plugin/test.dll", $"<package><metadata><id>{id}</id><version>1.0.0</version><tags>{tags}</tags></metadata></package>");
        Assert.Throws<InvalidDataException>(() => PluginManager.ExtractPackage(archive, Path.Combine(_root, "extract"), new("Test", "1.0.0", "", "")));
    }

    [Fact]
    public async Task Disable_HidesPluginToolsImmediately_AndEnableRestores()
    {
        var packageId = "vis-" + Guid.NewGuid().ToString("N");
        var tool = CommandTool($"plugin:{packageId}:demo", "Demo");
        try
        {
            BuiltinToolRegistry.RegisterPluginTools([tool]);
            Assert.NotNull(BuiltinToolRegistry.GetById(tool.Id));
            Directory.CreateDirectory(PluginManager.Root);
            File.WriteAllText(Path.Combine(PluginManager.Root, "installed.json"),
                JsonSerializer.Serialize(new[] { new InstalledPlugin(packageId, "1.0.0") }));
            await PluginManager.SetEnabledAsync(packageId, false);
            Assert.Null(BuiltinToolRegistry.GetById(tool.Id));
            await PluginManager.SetEnabledAsync(packageId, true);
            Assert.NotNull(BuiltinToolRegistry.GetById(tool.Id));
        }
        finally
        {
            BuiltinToolRegistry.ReplacePluginTools(packageId, []);
        }
    }

    [Fact]
    public async Task DisableAndUninstall_PersistWithoutDeletingCurrentAssembly()
    {
        Directory.CreateDirectory(PluginManager.Root);
        var record = new InstalledPlugin("Test", "1.0.0");
        File.WriteAllText(Path.Combine(PluginManager.Root, "installed.json"), JsonSerializer.Serialize(new[] { record }));
        var directory = Path.Combine(PluginManager.Root, "test", "1.0.0");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "test.dll"), "loaded");
        await PluginManager.SetEnabledAsync("TEST", false);
        Assert.False(Assert.Single(PluginManager.GetInstalled()).Enabled);
        await PluginManager.SetEnabledAsync("test", null);
        Assert.Empty(PluginManager.GetInstalled());
        Assert.True(Directory.Exists(directory));
        PluginManager.CleanupUnused(PluginManager.GetInstalled());
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void BrokenRegistry_IsNotSilentlyOverwritten()
    {
        Directory.CreateDirectory(PluginManager.Root);
        File.WriteAllText(Path.Combine(PluginManager.Root, "installed.json"), "broken");
        Assert.Throws<JsonException>(() => PluginManager.GetInstalled());
        Assert.Equal("broken", File.ReadAllText(Path.Combine(PluginManager.Root, "installed.json")));
    }

    private static PluginXamlTool CommandTool(string id, string name)
    {
        var parts = id.Split(':');
        return new PluginXamlTool(parts[1], new TubaWinUi3.PluginSdk.PluginToolDefinition
        { Id = parts[2], Name = name, ExecuteAsync = _ => Task.CompletedTask }, null);
    }

    [Fact]
    public void Registration_DuplicateBatchDoesNotPartiallyRegister()
    {
        var id = "plugin:test:" + Guid.NewGuid().ToString("N");
        var first = CommandTool(id, "Test");
        Assert.Throws<InvalidOperationException>(() => BuiltinToolRegistry.RegisterPluginTools([first, first]));
        Assert.Null(BuiltinToolRegistry.GetById(id));
    }

    [Fact]
    public void HotReplacement_IsAtomic_AndPreservesOtherPackages()
    {
        var prefix = "plugin:hot-test:";
        var original = CommandTool(prefix + "hello", "Old");
        var updated = CommandTool(prefix + "hello", "New");
        var other = CommandTool("plugin:other-test:hello", "Other");
        try
        {
            BuiltinToolRegistry.RegisterPluginTools([original, other]);
            Assert.Throws<InvalidOperationException>(() => BuiltinToolRegistry.ReplacePluginTools("hot-test", [updated, updated]));
            Assert.Same(original, BuiltinToolRegistry.GetById(original.Id));
            Assert.Throws<InvalidOperationException>(() => BuiltinToolRegistry.ReplacePluginTools("hot-test", [other]));
            BuiltinToolRegistry.ReplacePluginTools("hot-test", [updated]);
            Assert.Same(updated, BuiltinToolRegistry.GetById(original.Id));
            Assert.Same(other, BuiltinToolRegistry.GetById(other.Id));
            Assert.Equal("Old", original.Name); // 已打开的旧实例不被强制改变。
        }
        finally
        {
            BuiltinToolRegistry.ReplacePluginTools("hot-test", []);
            BuiltinToolRegistry.ReplacePluginTools("other-test", []);
        }
    }

    [Fact]
    public async Task LocalPackages_MultiplePlugins_UpgradeOne_AndRollbackBrokenEntry()
    {
        const string packageId = "Tubatools.HotSmoke";
        const string otherId = "Tubatools.SecondSmoke";
        string BuildPackage(string version, string type = "HelloPlugin", string id = packageId)
        {
            var path = Path.Combine(_root, id + "-" + version + ".nupkg");
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var writer = new StreamWriter(zip.CreateEntry("plugin.nuspec").Open()))
                writer.Write($"<package><metadata><id>{id}</id><version>{version}</version><tags>Tubatools</tags></metadata></package>");
            using (var writer = new StreamWriter(zip.CreateEntry("tubatools.json").Open()))
                writer.Write(JsonSerializer.Serialize(new PluginManifest(2, "plugin/HelloPlugin.dll", type)));
            zip.CreateEntryFromFile(typeof(global::HelloPlugin).Assembly.Location, "plugin/HelloPlugin.dll");
            using (var icon = new StreamWriter(zip.CreateEntry("assets/icon.svg").Open())) icon.Write("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 128 128\" />");
            return path;
        }
        IBuiltinTool? first = null;
        IBuiltinTool? second = null;
        try
        {
            var artifacts = Path.Combine(_root, "artifacts");
            Directory.CreateDirectory(artifacts);
            var auto = await AgentPluginAutoLoader.CaptureAsync([], default, artifacts);
            var firstPackage = BuildPackage("1.0.0");
            var outputPackage = Path.Combine(artifacts, Path.GetFileName(firstPackage));
            File.Move(firstPackage, outputPackage);
            var command = new AgentOperation("pack-first", "bash", "command", "completed", "Build plugin", "dotnet pack Plugin.csproj -o artifacts", outputPackage, "", 0);
            var built = new AgentMessage("build", "assistant", "", "", Content: [new("build:0", "", command)]);
            Assert.True(Assert.Single(await auto.LoadAsync([built], _ => { }, default)).Success);
            first = BuiltinToolRegistry.GetById("plugin:tubatools.hotsmoke:hello");
            Assert.NotNull(first);
            await PluginManager.InstallLocalAsync(BuildPackage("1.0.0", id: otherId), CancellationToken.None);
            var other = BuiltinToolRegistry.GetById("plugin:tubatools.secondsmoke:hello");
            Assert.NotNull(other);
            await PluginManager.InstallLocalAsync(BuildPackage("1.0.1"), CancellationToken.None);
            second = BuiltinToolRegistry.GetById(first.Id);
            Assert.NotNull(second);
            Assert.NotSame(first, second);
            Assert.Equal("1.0.1", Assert.Single(PluginManager.GetInstalled(), p => p.Id == packageId).Version);
            Assert.Same(other, BuiltinToolRegistry.GetById(other.Id));
            await Assert.ThrowsAsync<TypeLoadException>(() => PluginManager.InstallLocalAsync(BuildPackage("1.0.2", "MissingEntry"), CancellationToken.None));
            Assert.Equal("1.0.1", Assert.Single(PluginManager.GetInstalled(), p => p.Id == packageId).Version);
            Assert.Equal("1.0.0", Assert.Single(PluginManager.GetInstalled(), p => p.Id == otherId).Version);
            Assert.Same(second, BuiltinToolRegistry.GetById(first.Id));
        }
        finally
        {
            first = second = null;
            BuiltinToolRegistry.ReplacePluginTools(packageId, []);
            BuiltinToolRegistry.ReplacePluginTools(otherId, []);
            ReleaseTestContexts(packageId);
            ReleaseTestContexts(otherId);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ReleaseTestContexts(string id)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var loaded = (System.Collections.IDictionary)typeof(PluginManager).GetField("Loaded", flags)!.GetValue(null)!;
        if (loaded[id] is System.Runtime.Loader.AssemblyLoadContext context) context.Unload();
        loaded.Remove(id);
        var retired = (System.Collections.IList)typeof(PluginManager).GetField("Retired", flags)!.GetValue(null)!;
        foreach (System.Runtime.Loader.AssemblyLoadContext old in retired) old.Unload();
        retired.Clear();
    }

    [Fact]
    public void LoadInstalled_LoadsRealPlugin_AndSharesSdkIdentity()
    {
        var directory = Path.Combine(PluginManager.Root, "tubatools.hello", "1.0.0");
        Directory.CreateDirectory(Path.Combine(directory, "plugin"));
        File.Copy(typeof(global::HelloPlugin).Assembly.Location, Path.Combine(directory, "plugin", "HelloPlugin.dll"));
        Directory.CreateDirectory(Path.Combine(directory, "assets"));
        File.WriteAllText(Path.Combine(directory, "assets", "icon.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" />");
        File.WriteAllText(Path.Combine(directory, "tubatools.json"), """
            {"apiVersion":2,"assembly":"plugin/HelloPlugin.dll","entryType":"HelloPlugin"}
            """);
        File.WriteAllText(Path.Combine(PluginManager.Root, "installed.json"), JsonSerializer.Serialize(new[] { new InstalledPlugin("Tubatools.Hello", "1.0.0") }));
        try
        {
            PluginManager.LoadInstalled();
            Assert.Null(PluginManager.GetError("Tubatools.Hello"));
            Assert.True(PluginManager.IsLoaded("Tubatools.Hello"));
            var tool = BuiltinToolRegistry.GetById("plugin:tubatools.hello:hello");
            Assert.NotNull(tool);
            Assert.Equal("多功能插件示例", tool.Name);
            Assert.Equal("其他工具", tool.Category);
            Assert.StartsWith("plugin-", Path.GetFileName(ToolCatalog.GetBuiltinFavoriteKey(tool)));
        }
        finally
        {
            // 测试隔离：还原全局静态注册表与加载上下文。
            var tools = (List<IBuiltinTool>)typeof(BuiltinToolRegistry).GetField("_tools", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            tools.RemoveAll(t => t.Id == "plugin:tubatools.hello:hello");
            var loaded = (System.Collections.IDictionary)typeof(PluginManager).GetField("Loaded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            if (loaded["Tubatools.Hello"] is System.Runtime.Loader.AssemblyLoadContext context) context.Unload();
            loaded.Remove("Tubatools.Hello");
        }
    }
}
