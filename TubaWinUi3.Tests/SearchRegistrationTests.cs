using System.Reflection;
using System.Runtime.InteropServices;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

[Collection("GlobalConfigTests")]
public sealed class SearchRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tuba-search-test-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousData = ConfigManager.DataDirectoryOverride;
    private readonly string? _previousStartMenu = WindowsSearchIndexService.StartMenuDirectoryOverride;
    private readonly Dictionary<string, string> _previousSettings;
    private readonly List<IBuiltinTool> _registry;
    private readonly IBuiltinTool[] _previousTools;
    private string Menu => Path.Combine(_root, "开始菜单");

    public SearchRegistrationTests()
    {
        AppSettings.Flush();
        _previousSettings = new(AppSettings.Load());
        ConfigManager.DataDirectoryOverride = _root;
        AppSettings.Load().Clear();
        WindowsSearchIndexService.StartMenuDirectoryOverride = Menu;
        _registry = (List<IBuiltinTool>)typeof(BuiltinToolRegistry).GetField("_tools", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        _previousTools = _registry.ToArray();
        _registry.Clear();
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void SelectAll_WritesEveryExternalAndBuiltinShortcutBeyondOneStartPage()
    {
        var tools = Enumerable.Range(1, 32).Select(i => NewTool(i)).ToList();
        BuiltinToolRegistry.Register(new ScreenTestTool());
        BuiltinToolRegistry.Register(new KeyboardTestTool());
        ToolVisibilityService.SaveSearchSelection(tools.Select(ToolVisibilityService.SearchKey)
            .Concat(new[] { "builtin:screen-test", "builtin:keyboard-test" }));

        var result = WindowsSearchIndexService.RegisterTools(tools);

        Assert.Empty(result.Errors);
        Assert.Equal(34, result.Registered);
        Assert.Equal(34, Directory.GetFiles(Menu, "*.lnk").Length);
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            var link = ((dynamic)shell).CreateShortcut(Path.Combine(Menu, "屏幕坏点检测.lnk"));
            try
            {
                Assert.Equal("--open-builtin screen-test", (string)link.Arguments);
                Assert.EndsWith(".ico,0", (string)link.IconLocation);
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    [Fact]
    public void SelectNone_RemovesAllRegistrationsEvenWhenLegacyFlagIsTrue()
    {
        var tools = new[] { NewTool(1), NewTool(2) };
        ToolVisibilityService.SaveSearchSelection(tools.Select(ToolVisibilityService.SearchKey));
        Assert.Equal(2, WindowsSearchIndexService.RegisterTools(tools).Registered);
        ToolVisibilityService.SaveSearchSelection([]);
        AppSettings.Set("WindowsSearchIndex", true);

        var result = WindowsSearchIndexService.RegisterTools(tools);

        Assert.False(ToolVisibilityService.HasSearchSelection);
        Assert.Empty(result.Errors);
        Assert.Equal(0, result.Registered);
        Assert.False(Directory.Exists(Menu));
    }

    [Fact]
    public void ExplicitSelection_DoesNotRegisterNewUnselectedToolsAndReportsMissingFiles()
    {
        var selected = NewTool(1);
        var unselected = NewTool(2);
        var missing = NewTool(3);
        File.Delete(missing.Path);
        ToolVisibilityService.SaveSearchSelection(new[] { ToolVisibilityService.SearchKey(selected), ToolVisibilityService.SearchKey(missing) });

        var result = WindowsSearchIndexService.RegisterTools(new[] { selected, unselected, missing });

        Assert.Equal(1, result.Registered);
        Assert.Equal(1, result.Unavailable);
        Assert.Empty(result.Errors);
        Assert.Single(Directory.GetFiles(Menu, "*.lnk"));
        Assert.False(ToolVisibilityService.IsSearchEnabled(ToolVisibilityService.SearchKey(unselected)));
    }

    [Fact]
    public void LegacySettings_MigrateExistingChoicesWithoutEnablingPreviouslyDisabledRegistration()
    {
        string[] keys = ["tool:a", "tool:b"];
        AppSettings.Set("WindowsSearchIndex", false);
        Assert.Empty(ToolVisibilityService.LoadSearchSelection(keys));
        AppSettings.Set("WindowsSearchIndex", true);
        AppSettings.Set(ToolVisibilityService.SearchExcludedKey, "[\"tool:b\"]");
        Assert.Equal(new[] { "tool:a" }, ToolVisibilityService.LoadSearchSelection(keys));
    }

    private ToolItem NewTool(int index)
    {
        var path = Path.Combine(_root, $"工具{index}.cmd");
        File.WriteAllText(path, "@echo off");
        return new ToolItem { Name = $"测试工具{index}", Category = "综合工具", Path = path,
            RelativePath = Path.GetFileName(path), Extension = ".cmd", LibraryId = "search-test-" + index };
    }

    public void Dispose()
    {
        AppSettings.Flush();
        var settings = AppSettings.Load();
        settings.Clear();
        foreach (var (key, value) in _previousSettings) settings[key] = value;
        _registry.Clear();
        _registry.AddRange(_previousTools);
        WindowsSearchIndexService.StartMenuDirectoryOverride = _previousStartMenu;
        ConfigManager.DataDirectoryOverride = _previousData;
        Directory.Delete(_root, recursive: true);
    }
}
