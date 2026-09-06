using TubaWinUi3.Services;

namespace TubaWinUi3.Tests.RogueCleaner;

/// <summary>内置工具注册测试：「流氓软件的克星」注册成功且右键菜单管理仍可用。</summary>
[Collection("BuiltinToolRegistry")]
public class RogueCleanerRegistrationTests
{
    private static void ClearRegistry()
    {
        var list = (List<IBuiltinTool>)typeof(BuiltinToolRegistry)
            .GetField("_tools", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        list.Clear();
    }

    [Fact]
    public void RegisterDefaults_ContainsRogueCleanerTool()
    {
        ClearRegistry();
        BuiltinToolRegistry.RegisterDefaults();
        var tool = BuiltinToolRegistry.GetById("rogue-cleaner");
        Assert.NotNull(tool);
        Assert.Equal("流氓软件的克星", tool.Name);
        // 060ea4f 优化内置工具的分类体系：从「安全工具」并入「系统工具」
        Assert.Equal("系统工具", tool.Category);
        Assert.Equal(BuiltinToolKind.ProgressTask, tool.Kind);
    }

    [Fact]
    public void RegisterDefaults_ContextMenuMgrStillRegistered()
    {
        ClearRegistry();
        BuiltinToolRegistry.RegisterDefaults();
        var tool = BuiltinToolRegistry.GetById("context-menu-mgr");
        Assert.NotNull(tool);
        Assert.Equal("右键菜单管理", tool.Name);
    }

    [Fact]
    public void Register_NoDuplicateIds()
    {
        ClearRegistry();
        BuiltinToolRegistry.RegisterDefaults();
        var ids = BuiltinToolRegistry.Tools.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
