using System.Reflection;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 内置工具彩色矢量图标（<c>Assets/BuiltinIcons/&lt;id&gt;.svg</c>）的回归。
/// 写法约束（Fluent 规范 + Direct2D 兼容子集）见 <see cref="IconAssetValidation"/>。
/// </summary>
[Collection("BuiltinToolRegistry")]
public class BuiltinIconTests
{
    private static string IconDir => Path.Combine(AppContext.BaseDirectory, "Assets", "BuiltinIcons");

    /// <summary>MSIX 打包时不注册，但图标文件同样要留着。</summary>
    private static readonly string[] ExtraKnownIds = ["community-tools"];

    /// <summary>
    /// 注册表是全局静态的，且被同集合的其他测试清空/填充过；
    /// 这里先清空再注册默认项，拿到的一定是完整的默认集合。
    /// </summary>
    private static IReadOnlyList<IBuiltinTool> DefaultTools()
    {
        var list = (List<IBuiltinTool>)typeof(BuiltinToolRegistry)
            .GetField("_tools", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        list.Clear();
        BuiltinToolRegistry.RegisterDefaults();
        return BuiltinToolRegistry.Tools;
    }

    [Fact]
    public void EveryRegisteredBuiltinTool_HasColoredIcon()
    {
        var missing = DefaultTools()
            .Where(tool => BuiltinIconService.ResolveSvgPath(tool.Id) is null)
            .Select(tool => tool.Id)
            .ToList();

        Assert.True(missing.Count == 0,
            $"以下内置工具缺少 Assets/BuiltinIcons/{{id}}.svg：{string.Join("、", missing)}");
    }

    [Fact]
    public void IconFolder_HasNoOrphanFiles()
    {
        var known = DefaultTools()
            .Select(tool => tool.Id)
            .Concat(ExtraKnownIds)
            .ToHashSet(StringComparer.Ordinal);

        var orphans = IconAssetValidation.SvgIds(IconDir).Where(id => !known.Contains(id)).ToList();

        Assert.True(orphans.Count == 0,
            $"这些 svg 找不到对应内置工具（工具改名/删除后忘了清图标？）：{string.Join("、", orphans)}");
    }

    [Fact]
    public void EveryIcon_FollowsFluentConventions()
    {
        var problems = IconAssetValidation.ValidateAll(IconDir);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void EveryIcon_RasterizesToNonEmptyBitmap()
    {
        var problems = IconAssetValidation.ValidateRasterizes(IconDir);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void ResolveSvgPath_IsCaseInsensitiveAndRejectsUnknownIds()
    {
        Assert.Null(BuiltinIconService.ResolveSvgPath(null));
        Assert.Null(BuiltinIconService.ResolveSvgPath(""));
        Assert.Null(BuiltinIconService.ResolveSvgPath("   "));
        Assert.Null(BuiltinIconService.ResolveSvgPath("no-such-tool"));
        Assert.False(BuiltinIconService.Has("no-such-tool"));

        var known = IconAssetValidation.SvgIds(IconDir)[0];
        Assert.NotNull(BuiltinIconService.ResolveSvgPath(known));
        Assert.NotNull(BuiltinIconService.ResolveSvgPath(known.ToUpperInvariant()));
        Assert.True(BuiltinIconService.Has(known));
    }
}
