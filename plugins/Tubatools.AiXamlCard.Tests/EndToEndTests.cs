using Tubatools.AiXamlCard;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 端到端冒烟：一张「示例卡片」从写盘、校验、改写、编译到实例化的完整链路。
/// 不启动 UI（没有 XamlRoot），因此只验证到控制器实例化与 OnClosed 为止。
/// </summary>
public sealed class AiXamlCardEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tuba-card-e2e-" + Guid.NewGuid().ToString("N"));

    public AiXamlCardEndToEndTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void SampleCard_PassesEveryLayer()
    {
        var store = new CardStore(_root);
        var card = store.WriteSample("e2e", "端到端");
        Assert.NotNull(card);

        // 1. XAML 过白名单
        var guard = CardGuard.Validate(card!.ReadXaml());
        Assert.True(guard.Ok, guard.Error);

        // 2. 标记改写后仍是合法 XAML 文本
        var rewritten = CardMarkup.Rewrite(card.ReadXaml(), card.Directory);
        Assert.DoesNotContain("TubaCard", rewritten);
        Assert.NotNull(CardMarkup.TitleOf(card.ReadXaml(), "默认"));

        // 3. 代码后置真的能编译出控制器
        var compiled = CardCompiler.Compile(card.ReadCode(), "E2E_" + Guid.NewGuid().ToString("N")[..8]);
        Assert.True(compiled.Success, compiled.Error);
        var controller = Assert.IsAssignableFrom<CardControllerBase>(Activator.CreateInstance(compiled.Controller!));
        controller.OnClosed();
    }

    [Fact]
    public void CardWithBadCodeStillRendersTheXaml()
    {
        // 代码坏了 → 卡片仍以静态界面展示，这是产品要求（错误不该吞掉整条消息）。
        var store = new CardStore(_root);
        var card = store.WriteSample("partial", "半成品");
        Assert.NotNull(card);
        File.WriteAllText(card!.CodePath, "public sealed class Wrong { }");

        Assert.True(CardGuard.Validate(card.ReadXaml()).Ok);
        Assert.False(CardCompiler.Compile(card.ReadCode(), "E2E_bad").Success);
    }

    [Fact]
    public void Store_SurvivesConcurrentSampleWrites()
    {
        var store = new CardStore(_root);
        var results = Enumerable.Range(0, 8).AsParallel()
            .Select(i => store.WriteSample("parallel-" + i, "并行 " + i)).ToArray();
        Assert.All(results, r => Assert.NotNull(r));
        Assert.Equal(8, store.List().Count);
    }
}
