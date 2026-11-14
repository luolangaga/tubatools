using Tubatools.AiXamlCard;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 用 AI 实际生成的那三张卡片做回归：它们必须能过白名单校验并通过 Roslyn 编译。
/// 这是「卡片写了却显示不出来」最可能的失败点，值得用真实内容守住。
/// </summary>
public sealed class AiXamlCardUserCardTests
{
    private static string Workspace => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TubaWinUi3", "AiAssistant", "Workspace", "cards");

    [Theory]
    [InlineData("power-estimate")]
    [InlineData("ram-converter")]
    [InlineData("cpu-compare")]
    public void GeneratedCards_PassGuardAndCompile(string slug)
    {
        var directory = Path.Combine(Workspace, slug);
        if (!Directory.Exists(directory)) return; // 换机器时跳过：这是本地样本，不是产品资产。

        var xaml = File.ReadAllText(Path.Combine(directory, "card.xaml"));
        var guard = CardGuard.Validate(xaml);
        Assert.True(guard.Ok, $"{slug} XAML: {guard.Error}");

        var codePath = Path.Combine(directory, "card.cs");
        if (!File.Exists(codePath)) return;
        var compiled = CardCompiler.Compile(File.ReadAllText(codePath), "User_" + slug.Replace('-', '_'));
        Assert.True(compiled.Success, $"{slug} 代码：{compiled.Error}");
    }
}
