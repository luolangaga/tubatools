using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class AllToolsSortTests
{
    private static ToolItem Tool(string name, string? path = null, bool builtin = false) => new()
    {
        Name = name, Path = path ?? name, Category = "测试",
        RelativePath = name, Extension = ".exe", SortOrder = 0, IsBuiltinLink = builtin
    };

    [Fact]
    public void NoHistory_PutsBuiltinsFirstAndPreservesOriginalOrderWithinEachGroup()
    {
        var tools = new[] { Tool("网络"), Tool("Zulu", builtin: true), Tool("磁盘"), Tool("Alpha", builtin: true) };
        var result = AllToolsSortService.Sort(tools, []);

        Assert.Equal(new[] { "Zulu", "Alpha", "网络", "磁盘" }, result.Select(t => t.Name));
        Assert.Equal("网络", tools[0].Name); // 不改变共享目录缓存
    }

    [Fact]
    public void Recent_UsesTimestampCaseInsensitivePathAndOriginalOrderFallback()
    {
        var now = DateTime.UtcNow;
        var tools = new[] { Tool("Zulu", "C:\\Tools\\Z"), Tool("Beta"), Tool("Alpha", builtin: true), Tool("Delta") };
        var result = AllToolsSortService.Sort(tools,
        [
            new LaunchRecord { Path = "Delta", LastLaunched = now.AddDays(-1), Count = 100 },
            new LaunchRecord { Path = "c:\\tools\\z", LastLaunched = now, Count = 1 }
        ]);

        Assert.Equal(new[] { "Zulu", "Delta", "Alpha", "Beta" }, result.Select(t => t.Name));
    }

    [Fact]
    public void Recent_RecognizesBuiltinHistoryFromAnotherCategory()
    {
        var builtin = new ToolItem
        {
            Name = "Zulu", Category = "分类一", Categories = ["分类一", "分类二"],
            Path = "C:\\Tools\\分类一\\内置工具", RelativePath = "", Extension = "",
            IsBuiltinLink = true, BuiltinToolId = "test"
        };
        var result = AllToolsSortService.Sort([Tool("Alpha"), builtin],
            [new LaunchRecord { Path = "C:\\Tools\\分类二\\内置工具", LastLaunched = DateTime.UtcNow }]);

        Assert.Same(builtin, result[0]);
    }

    [Fact]
    public void NewlyOpenedExternalTool_MovesAheadOfPreviouslyOpenedBuiltin()
    {
        var now = DateTime.UtcNow;
        var tools = new[] { Tool("Builtin", builtin: true), Tool("External") };
        var history = new List<LaunchRecord> { new() { Path = "Builtin", LastLaunched = now } };
        Assert.Equal("Builtin", AllToolsSortService.Sort(tools, history)[0].Name);

        history.Add(new() { Path = "External", LastLaunched = now.AddSeconds(1) });
        Assert.Equal("External", AllToolsSortService.Sort(tools, history)[0].Name);
        Assert.Equal("Builtin", AllToolsSortService.Sort(tools, [])[0].Name);
    }
}
