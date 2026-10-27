using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

// 与 RogueCleanerRegistrationTests 共享同一集合，避免反射清空注册表时并行冲突。
[Collection("BuiltinToolRegistry")]
public class BuiltinToolRegistryTests
{
    private class StubTool : IBuiltinTool
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Glyph { get; init; } = "";
        public string Category { get; init; } = "";
        public BuiltinToolKind Kind { get; init; } = BuiltinToolKind.InstantAction;
        public Task ExecuteAsync(BuiltinToolContext context) => Task.CompletedTask;
    }

    private static void ClearRegistry()
    {
        while (BuiltinToolRegistry.Tools.Count > 0)
        {
            var tool = BuiltinToolRegistry.Tools[0];
            var list = (System.Collections.Generic.List<IBuiltinTool>)typeof(BuiltinToolRegistry)
                .GetField("_tools", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null)!;
            list.Clear();
        }
    }

    [Fact]
    public void Register_AddsTool()
    {
        ClearRegistry();
        var tool = new StubTool { Id = "test_tool_1", Name = "Test Tool" };
        BuiltinToolRegistry.Register(tool);
        Assert.Single(BuiltinToolRegistry.Tools);
        Assert.Equal("test_tool_1", BuiltinToolRegistry.Tools[0].Id);
        ClearRegistry();
    }

    [Fact]
    public void Register_DuplicateId_Throws()
    {
        ClearRegistry();
        var tool1 = new StubTool { Id = "dup_id" };
        var tool2 = new StubTool { Id = "dup_id" };
        BuiltinToolRegistry.Register(tool1);
        Assert.Throws<InvalidOperationException>(() => BuiltinToolRegistry.Register(tool2));
        ClearRegistry();
    }

    [Fact]
    public void GetById_ReturnsCorrectTool()
    {
        ClearRegistry();
        var tool = new StubTool { Id = "find_me" };
        BuiltinToolRegistry.Register(tool);
        Assert.NotNull(BuiltinToolRegistry.GetById("find_me"));
        Assert.Null(BuiltinToolRegistry.GetById("not_found"));
        ClearRegistry();
    }

    [Fact]
    public void GetById_CaseSensitive()
    {
        ClearRegistry();
        var tool = new StubTool { Id = "MyTool" };
        BuiltinToolRegistry.Register(tool);
        Assert.NotNull(BuiltinToolRegistry.GetById("MyTool"));
        Assert.Null(BuiltinToolRegistry.GetById("mytool"));
        ClearRegistry();
    }

    [Fact]
    public void LegacyJunkCleanerId_ResolvesToSingleMergedTool()
    {
        ClearRegistry();
        var tool = new SpaceCleanupTool(); BuiltinToolRegistry.Register(tool);
        Assert.Same(tool, BuiltinToolRegistry.GetById("space-cleanup"));
        Assert.Same(tool, BuiltinToolRegistry.GetById("junk-cleaner"));
        Assert.Single(BuiltinToolRegistry.AllTools);
        ClearRegistry();
    }

    [Fact]
    public void GetCategories_ReturnsDistinctCategories()
    {
        ClearRegistry();
        BuiltinToolRegistry.Register(new StubTool { Id = "t1", Category = "网络工具" });
        BuiltinToolRegistry.Register(new StubTool { Id = "t2", Category = "系统工具" });
        BuiltinToolRegistry.Register(new StubTool { Id = "t3", Category = "网络工具" });
        var cats = BuiltinToolRegistry.GetCategories();
        Assert.Equal(2, cats.Count);
        Assert.Contains("系统工具", cats);
        Assert.Contains("网络工具", cats);
        ClearRegistry();
    }

    [Fact]
    public void GetByCategory_ReturnsToolsInCategory()
    {
        ClearRegistry();
        BuiltinToolRegistry.Register(new StubTool { Id = "t1", Category = "网络工具", Name = "B Tool" });
        BuiltinToolRegistry.Register(new StubTool { Id = "t2", Category = "系统工具", Name = "A Tool" });
        BuiltinToolRegistry.Register(new StubTool { Id = "t3", Category = "网络工具", Name = "A Tool" });
        var tools = BuiltinToolRegistry.GetByCategory("网络工具");
        Assert.Equal(2, tools.Count);
        Assert.Equal("A Tool", tools[0].Name);
        Assert.Equal("B Tool", tools[1].Name);
        ClearRegistry();
    }

    [Fact]
    public void GetByCategory_NoMatch_ReturnsEmpty()
    {
        ClearRegistry();
        BuiltinToolRegistry.Register(new StubTool { Id = "t1", Category = "网络工具" });
        Assert.Empty(BuiltinToolRegistry.GetByCategory("不存在的分类"));
        ClearRegistry();
    }
}
