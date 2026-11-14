using System.Text.Json;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

[Collection("GlobalConfigTests")]
public sealed class AgentWorkspaceTests : IDisposable
{
    private readonly string? _previous;
    private readonly string _root;

    public AgentWorkspaceTests()
    {
        _previous = ConfigManager.DataDirectoryOverride;
        _root = Path.Combine(Path.GetTempPath(), "tuba-workspaces-" + Guid.NewGuid().ToString("N"));
        ConfigManager.DataDirectoryOverride = _root;
    }

    public void Dispose()
    {
        ConfigManager.DataDirectoryOverride = _previous;
        for (var attempt = 0; Directory.Exists(_root) && attempt < 10; attempt++)
        {
            try { Directory.Delete(_root, true); }
            catch (IOException) when (attempt < 9) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (attempt < 9) { Thread.Sleep(100); }
        }
    }

    [Fact]
    public void DefaultWorkspace_KeepsThePreExistingPath_AndIsCurrentWithoutRegistry()
    {
        var current = AgentWorkspaceStore.ResolveCurrent();
        Assert.True(current.IsDefault);
        Assert.Equal(Path.Combine(AiComponentService.Root, "Workspace"), current.Path);
        Assert.Equal(current.Path, AgentWorkspace.DirectoryPath);
        Assert.Equal(new[] { AgentWorkspaceStore.DefaultId }, AgentWorkspaceStore.List().Select(w => w.Id));
    }

    [Fact]
    public void Create_AddsAReadableFolder_AndSelectPersistsAcrossReads()
    {
        var created = AgentWorkspaceStore.Create("网络工具");

        Assert.False(created.IsDefault);
        Assert.StartsWith(Path.Combine(AiComponentService.Root, "Workspaces"), created.Path);
        Assert.Contains("网络工具", Path.GetFileName(created.Path));
        Assert.True(Directory.Exists(created.Path));
        Assert.Contains(created, AgentWorkspaceStore.List());

        AgentWorkspaceStore.Select(created);
        Assert.Equal(created.Path, AgentWorkspace.DirectoryPath);
        Assert.Equal(created.Id, AgentWorkspaceStore.ResolveCurrent().Id);

        AgentWorkspace.Prepare();
        Assert.True(File.Exists(Path.Combine(created.Path, "AGENTS.md")));
        Assert.True(Directory.Exists(Path.Combine(created.Path, "artifacts")));
        Assert.True(File.Exists(Path.Combine(created.Path, "opencode.json")));

        var registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(AiComponentService.Root, "Workspaces", "workspaces.json")));
        Assert.Equal(created.Id, registry.RootElement.GetProperty("current").GetString());
        Assert.Throws<InvalidOperationException>(() => AgentWorkspaceStore.Select(new("missing", "missing", created.Path, false)));
    }

    [Fact]
    public void Create_WithCustomDirectory_UsesThatDirectory_AndPersistsThePath()
    {
        var custom = Path.Combine(_root, "custom-workspace");
        var created = AgentWorkspaceStore.Create("自定义目录", custom);

        Assert.False(created.IsDefault);
        Assert.Equal(Path.GetFullPath(custom), created.Path);
        Assert.True(Directory.Exists(custom));
        Assert.Contains(created, AgentWorkspaceStore.List());

        AgentWorkspaceStore.Select(created);
        Assert.Equal(created.Path, AgentWorkspace.DirectoryPath);
        AgentWorkspace.Prepare();
        Assert.True(File.Exists(Path.Combine(custom, "AGENTS.md")));
        Assert.True(Directory.Exists(Path.Combine(custom, "artifacts")));

        var registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(AiComponentService.Root, "Workspaces", "workspaces.json")));
        var entry = registry.RootElement.GetProperty("workspaces").EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == created.Id);
        Assert.Equal(created.Path, entry.GetProperty("path").GetString());
        Assert.False(entry.TryGetProperty("folder", out _));
    }

    [Fact]
    public void EmptyCustomDirectory_FallsBackToTheManagedFolder()
    {
        var created = AgentWorkspaceStore.Create("默认位置", "   ");

        Assert.StartsWith(Path.Combine(AiComponentService.Root, "Workspaces"), created.Path);
        Assert.True(Directory.Exists(created.Path));
        var registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(AiComponentService.Root, "Workspaces", "workspaces.json")));
        var entry = registry.RootElement.GetProperty("workspaces").EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == created.Id);
        Assert.False(entry.TryGetProperty("path", out _));
        Assert.NotNull(entry.GetProperty("folder").GetString());
    }

    [Fact]
    public void CustomDirectory_MustBeAbsolute_Unique_AndNotOverlapping()
    {
        Assert.Equal(AgentWorkspaceDirectoryIssue.None, AgentWorkspaceStore.ValidateDirectory(" "));
        Assert.Equal(AgentWorkspaceDirectoryIssue.Invalid, AgentWorkspaceStore.ValidateDirectory(".\\relative"));
        Assert.Equal(AgentWorkspaceDirectoryIssue.Invalid, AgentWorkspaceStore.ValidateDirectory(AgentWorkspaceStore.Root));
        Assert.Equal(AgentWorkspaceDirectoryIssue.Invalid, AgentWorkspaceStore.ValidateDirectory(AgentWorkspaceStore.DefaultPath));
        Assert.Equal(AgentWorkspaceDirectoryIssue.Invalid, AgentWorkspaceStore.ValidateDirectory(Path.GetPathRoot(_root)!));
        Assert.Equal(AgentWorkspaceDirectoryIssue.Invalid, AgentWorkspaceStore.ValidateDirectory(Path.Combine(_root, "bad*name")));

        var first = Path.Combine(_root, "one");
        var created = AgentWorkspaceStore.Create("一号", first);
        Assert.True(Directory.Exists(first));
        Assert.Equal(AgentWorkspaceDirectoryIssue.Duplicate, AgentWorkspaceStore.ValidateDirectory(first));
        Assert.Equal(AgentWorkspaceDirectoryIssue.Duplicate, AgentWorkspaceStore.ValidateDirectory(first.ToUpperInvariant()));
        Assert.Equal(AgentWorkspaceDirectoryIssue.Overlapping, AgentWorkspaceStore.ValidateDirectory(Path.Combine(first, "child")));
        Assert.Equal(AgentWorkspaceDirectoryIssue.Overlapping, AgentWorkspaceStore.ValidateDirectory(_root));
        Assert.Equal(AgentWorkspaceDirectoryIssue.None, AgentWorkspaceStore.ValidateDirectory(Path.Combine(_root, "two")));

        var file = Path.Combine(_root, "not-a-folder.txt");
        File.WriteAllText(file, "x");
        Assert.Equal(AgentWorkspaceDirectoryIssue.NotDirectory, AgentWorkspaceStore.ValidateDirectory(file));

        Assert.Throws<ArgumentException>(() => AgentWorkspaceStore.Create("二号", first));
        Assert.Throws<ArgumentException>(() => AgentWorkspaceStore.Create("三号", Path.Combine(first, "nested")));
        Assert.Throws<ArgumentException>(() => AgentWorkspaceStore.Create("四号", ".\\relative"));
        Assert.Throws<ArgumentException>(() => AgentWorkspaceStore.Create("五号", file));
        Assert.Equal(created.Path, AgentWorkspaceStore.List().Single(w => w.Id == created.Id).Path);
    }

    [Fact]
    public void Registry_LoadsAbsoluteCustomPaths_AndRejectsRelativeOnes()
    {
        var registryPath = Path.Combine(AiComponentService.Root, "Workspaces", "workspaces.json");
        Directory.CreateDirectory(Path.GetDirectoryName(registryPath)!);
        var custom = Path.Combine(_root, "external");
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["current"] = "custom",
            ["workspaces"] = new object[]
            {
                new Dictionary<string, object?> { ["id"] = "custom", ["name"] = "外部目录", ["path"] = custom },
                new Dictionary<string, object?> { ["id"] = "legacy", ["name"] = "旧格式", ["folder"] = "legacy-folder" },
                new Dictionary<string, object?> { ["id"] = "relative", ["name"] = "相对路径", ["path"] = "relative\\outside" }
            }
        });
        File.WriteAllText(registryPath, json);

        var list = AgentWorkspaceStore.List();
        Assert.Equal(3, list.Count);
        Assert.Equal(custom, list.Single(w => w.Id == "custom").Path);
        Assert.Contains(list, w => w.Id == "legacy" &&
            w.Path == Path.Combine(AiComponentService.Root, "Workspaces", "legacy-folder"));
        Assert.DoesNotContain(list, w => w.Id == "relative");
        Assert.Equal(custom, AgentWorkspaceStore.ResolveCurrent().Path);
    }

    [Fact]
    public void Names_AreValidated_AndMustBeUnique()
    {
        Assert.Equal(AgentWorkspaceNameIssue.Empty, AgentWorkspaceStore.ValidateName("   "));
        Assert.Equal(AgentWorkspaceNameIssue.TooLong, AgentWorkspaceStore.ValidateName(new string('a', AgentWorkspaceStore.MaxNameLength + 1)));
        Assert.Equal(AgentWorkspaceNameIssue.Invalid, AgentWorkspaceStore.ValidateName("a/b"));
        Assert.Equal(AgentWorkspaceNameIssue.Invalid, AgentWorkspaceStore.ValidateName("a:b"));
        Assert.Equal(AgentWorkspaceNameIssue.Invalid, AgentWorkspaceStore.ValidateName("a*b"));
        Assert.Equal(AgentWorkspaceNameIssue.Invalid, AgentWorkspaceStore.ValidateName(".."));
        Assert.Equal(AgentWorkspaceNameIssue.None, AgentWorkspaceStore.ValidateName("插件开发"));

        var first = AgentWorkspaceStore.Create("插件开发");
        Assert.Equal(AgentWorkspaceNameIssue.Duplicate, AgentWorkspaceStore.ValidateName("插件开发"));
        Assert.Equal(AgentWorkspaceNameIssue.Duplicate, AgentWorkspaceStore.ValidateName(" 插件开发 "));
        Assert.Equal(AgentWorkspaceNameIssue.None, AgentWorkspaceStore.ValidateName("插件开发", first.Id));
        Assert.Throws<ArgumentException>(() => AgentWorkspaceStore.Create("插件开发"));
        Assert.Throws<ArgumentException>(() => AgentWorkspaceStore.Create("../越界"));
    }

    [Fact]
    public void Rename_KeepsTheFolder_AndPersistsTheDisplayName()
    {
        var created = AgentWorkspaceStore.Create("临时");
        var renamed = AgentWorkspaceStore.Rename(created, "正式");

        Assert.Equal("正式", renamed.Name);
        Assert.Equal(created.Path, renamed.Path);
        Assert.True(Directory.Exists(created.Path));
        Assert.Equal("正式", AgentWorkspaceStore.List().Single(w => w.Id == created.Id).Name);
        Assert.Throws<ArgumentException>(() => AgentWorkspaceStore.Rename(renamed, ""));
    }

    [Fact]
    public void Registry_IgnoresEscapingEntries_AndFallsBackWhenCorrupt()
    {
        var registryPath = Path.Combine(AiComponentService.Root, "Workspaces", "workspaces.json");
        Directory.CreateDirectory(Path.GetDirectoryName(registryPath)!);
        File.WriteAllText(registryPath, """
            {"current":"evil","workspaces":[{"id":"evil","name":"越界","folder":"../outside"}]}
            """);

        var list = AgentWorkspaceStore.List();
        Assert.Single(list);
        Assert.True(AgentWorkspaceStore.ResolveCurrent().IsDefault);

        File.WriteAllText(registryPath, "{ broken");
        Assert.Single(AgentWorkspaceStore.List());
        Assert.Equal(AgentWorkspaceStore.DefaultId, AgentWorkspaceStore.ResolveCurrent().Id);
    }
}
