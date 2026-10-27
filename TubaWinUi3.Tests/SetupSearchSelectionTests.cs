using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class SetupSearchSelectionTests
{
    private static readonly SetupSearchCandidate[] Candidates =
    [
        new("tool:cpu/a", "CPU-Z", "处理器工具"),
        new("tool:all/a", "CPU-Z", "综合检测"),
        new("builtin:keyboard-test", "键盘测试", "内置工具", "keyboard-test")
    ];

    [Fact]
    public void SameToolAcrossCategories_HasOneChoiceAndExcludesEveryCopy()
    {
        var selection = new SetupSearchSelection(Candidates, new HashSet<string>());
        Assert.Equal(2, selection.Tools.Count);
        var cpu = Assert.Single(selection.Tools, t => t.Name == "CPU-Z");
        Assert.Equal(2, cpu.Keys.Count);
        selection.SetSelected(cpu, false);
        var excluded = selection.BuildExclusions([]);
        Assert.Contains("tool:cpu/a", excluded);
        Assert.Contains("tool:all/a", excluded);
        Assert.DoesNotContain("builtin:keyboard-test", excluded);
    }

    [Fact]
    public void FilterDoesNotDiscardChoicesOutsideTheResults()
    {
        var selection = new SetupSearchSelection(Candidates, new HashSet<string>());
        var cpu = Assert.Single(selection.Filter("cpu"));
        selection.SetSelected(cpu, false);
        Assert.Single(selection.Filter("内置"));
        Assert.False(selection.IsSelected(Assert.Single(selection.Filter("CPU-Z"))));
        Assert.True(selection.IsSelected(Assert.Single(selection.Filter("键盘"))));
    }

    [Fact]
    public void SavingPreservesUnknownExclusionsAndAllowsReenabling()
    {
        var previous = new HashSet<string>(["TOOL:CPU/A", "builtin:not-installed"], StringComparer.OrdinalIgnoreCase);
        var selection = new SetupSearchSelection(Candidates, previous);
        var cpu = Assert.Single(selection.Tools, t => t.Name == "CPU-Z");
        Assert.False(selection.IsSelected(cpu));
        selection.SetSelected(cpu, true);
        var excluded = selection.BuildExclusions(previous);
        Assert.Single(excluded);
        Assert.Contains("builtin:not-installed", excluded);
        Assert.Contains("TOOL:CPU/A", previous); // staged choices never mutate persisted preferences
    }

    [Fact]
    public void SelectNoneAndSelectAllAffectEveryStableKey()
    {
        var selection = new SetupSearchSelection(Candidates, new HashSet<string>());
        foreach (var tool in selection.Tools) selection.SetSelected(tool, false);
        Assert.Equal(3, selection.BuildExclusions([]).Count);
        foreach (var tool in selection.Tools) selection.SetSelected(tool, true);
        Assert.Empty(selection.BuildExclusions([]));
        Assert.Empty(selection.Filter("does-not-exist"));
    }
}
