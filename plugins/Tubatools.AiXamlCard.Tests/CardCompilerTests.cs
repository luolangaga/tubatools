using Tubatools.AiXamlCard;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// Roslyn 动态编译的回归。这些用例真的会编译代码，因此也顺带证明
/// 「引用在宿主里可用」——这是整个插件的核心假设。
/// </summary>
public sealed class AiXamlCardCompilerTests
{
    [Fact]
    public void Compile_ProducesAController()
    {
        var result = CardCompiler.Compile("""
            public sealed class CardController : CardControllerBase
            {
                public override void OnReady() => Notice("ready");
            }
            """, "CardTest_" + Guid.NewGuid().ToString("N")[..8]);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Controller);
        Assert.NotNull(result.Assembly);
        Assert.True(typeof(CardControllerBase).IsAssignableFrom(result.Controller));
    }

    [Fact]
    public void Compile_RejectsCodeWithoutTheEntryType()
    {
        var result = CardCompiler.Compile("public sealed class SomethingElse { }", "CardTest_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.False(result.Success);
        Assert.Contains("CardController", result.Error);
    }

    [Fact]
    public void Compile_ReportsErrorsWithLineNumbers()
    {
        var result = CardCompiler.Compile("""
            public sealed class CardController : CardControllerBase
            {
                public override void OnReady() { int x = "text"; }
            }
            """, "CardTest_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.False(result.Success);
        Assert.Contains(":", result.Error);
    }

    /// <summary>
    /// 关键边界：文件系统、网络与进程 API 写不出来也无从调用。
    /// 注意核心库里就有 System.IO / Environment，因此这层由语法策略保证。
    /// </summary>
    [Theory]
    [InlineData("System.IO.File.Delete(\"x\")")]
    [InlineData("System.Diagnostics.Process.Start(\"cmd\")")]
    [InlineData("new System.Net.Http.HttpClient()")]
    [InlineData("Environment.GetEnvironmentVariable(\"PATH\")")]
    [InlineData("System.Activator.CreateInstance(typeof(object))")]
    [InlineData("System.Reflection.Assembly.GetExecutingAssembly()")]
    [InlineData("new Windows.Storage.StorageFolder()")]
    public void Compile_RefusesOutOfScopeApis(string statement)
    {
        var result = CardCompiler.Compile($$"""
            public sealed class CardController : CardControllerBase
            {
                public override void OnReady() { {{statement}}; }
            }
            """, "CardTest_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.False(result.Success);
    }

    [Theory]
    [InlineData("using System.IO;")]
    [InlineData("using System.Net.Http;")]
    [InlineData("using System.Management;")]
    public void Compile_RefusesForbiddenUsings(string directive)
    {
        var result = CardCompiler.Compile($$"""
            {{directive}}
            public sealed class CardController : CardControllerBase { }
            """, "CardTest_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.False(result.Success);
    }

    [Fact]
    public void Compile_AllowsCardPaletteAndUiTypes()
    {
        var result = CardCompiler.Compile("""
            public sealed class CardController : CardControllerBase
            {
                public override void OnReady()
                {
                    var brush = CardPalette.AccentBrush;
                    Thickness margin = new(8, 4, 8, 4);
                    var label = new Microsoft.UI.Xaml.Controls.TextBlock { Text = "x", Margin = margin };
                    Notice(label.Text + (brush is null ? "" : ""));
                }
            }
            """, "CardTest_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void Compile_AllowsPureComputationAndLinq()
    {
        var result = CardCompiler.Compile("""
            public sealed class CardController : CardControllerBase
            {
                public override void OnReady()
                {
                    var values = new List<double> { 1, 2, 3 };
                    var total = values.Where(v => v > 1).Sum() * Math.PI;
                    Notice(total > 0 ? "ok" : "no");
                }
            }
            """, "CardTest_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void Compile_AppliesImplicitUsings()
    {
        // 模型不必自己写 using System.Linq / Microsoft.UI.Xaml。
        var result = CardCompiler.Compile("""
            public sealed class CardController : CardControllerBase
            {
                public override void OnReady()
                {
                    Thickness margin = new(4);
                    var empty = Enumerable.Empty<int>().Any();
                    Notice(margin.Left.ToString() + empty);
                }
            }
            """, "CardTest_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void Compile_RejectsOversizedSource()
    {
        var code = "// " + new string('x', CardCompiler.MaxSourceCharacters) + "\npublic sealed class CardController : CardControllerBase { }";
        var result = CardCompiler.Compile(code, "CardTest");
        Assert.False(result.Success);
    }

    [Fact]
    public void Palette_ExposesBothThemes()
    {
        foreach (var token in CardPalette.Tokens) Assert.NotNull(CardPalette.Token(token));
        CardPalette.IsDark = false;
        var light = CardPalette.Token("TubaText");
        CardPalette.IsDark = true;
        var dark = CardPalette.Token("TubaText");
        CardPalette.IsDark = false;
        Assert.NotEqual(light, dark);
        Assert.Null(CardPalette.Token("NotAToken"));
    }
}
