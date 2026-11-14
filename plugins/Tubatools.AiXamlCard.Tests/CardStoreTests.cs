using Tubatools.AiXamlCard;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>卡片磁盘布局、标记改写与技能文案的回归。</summary>
public sealed class AiXamlCardStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tuba-card-" + Guid.NewGuid().ToString("N"));

    public AiXamlCardStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void Store_WritesAndListsCards()
    {
        var store = new CardStore(_root);
        var card = store.WriteSample("demo", "演示卡片");
        Assert.NotNull(card);
        Assert.True(File.Exists(Path.Combine(_root, "cards", "demo", "card.xaml")));
        Assert.True(File.Exists(Path.Combine(_root, "cards", "demo", "card.cs")));

        var listed = store.List();
        Assert.Single(listed);
        Assert.True(listed[0].HasCode);
        Assert.Contains("演示卡片", listed[0].ReadXaml());
    }

    [Fact]
    public void Store_IgnoresDirectoriesWithoutXaml()
    {
        var store = new CardStore(_root);
        store.EnsureRoot();
        Directory.CreateDirectory(Path.Combine(store.Root, "empty"));
        Assert.Empty(store.List());
    }

    [Theory]
    [InlineData("../../evil", "evil")]
    [InlineData("a/b", "a-b")]
    [InlineData("中文", "card")]
    [InlineData("", "card")]
    public void SafeSlug_NeverEscapesTheFolder(string input, string expected) =>
        Assert.Equal(expected, CardStore.SafeSlug(input));

    [Fact]
    public void Markup_ReplacesPaletteTokensAndImageSources()
    {
        var directory = Path.Combine(_root, "cards", "demo");
        Directory.CreateDirectory(Path.Combine(directory, "assets"));
        File.WriteAllBytes(Path.Combine(directory, "assets", "pic.png"), [1, 2, 3]);
        CardPalette.IsDark = false;

        var xaml = """
            <Border Background="TubaCard" BorderBrush="TubaStroke">
              <Image Source="assets/pic.png" />
            </Border>
            """;
        var rewritten = CardMarkup.Rewrite(xaml, directory);

        Assert.Contains(CardPalette.Card!, rewritten);
        Assert.Contains(CardPalette.Stroke!, rewritten);
        Assert.DoesNotContain("TubaCard", rewritten);
        Assert.Contains("file:///", rewritten, StringComparison.OrdinalIgnoreCase);
        // 绝对路径本身以 assets/pic.png 结尾，关键是它不再是一个相对引用。
        Assert.DoesNotContain("Source=\"assets/pic.png\"", rewritten);
    }

    [Fact]
    public void Markup_LeavesMissingAssetsUntouched()
    {
        // 图片还没写进来时不改写，XamlReader 会自己忽略非法 URI，而不是整页失败。
        var rewritten = CardMarkup.Rewrite("""<Image Source="assets/none.png" />""", Path.Combine(_root, "cards", "demo"));
        Assert.Contains("assets/none.png", rewritten);
    }

    [Fact]
    public void Markup_ReadsTheTitleFromTheRoot()
    {
        var xaml = """
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Name="RootCard">
            </Border>
            """;
        Assert.Equal("RootCard", CardMarkup.TitleOf(xaml, "默认"));
        Assert.Equal("默认", CardMarkup.TitleOf("<Border", "默认"));
        Assert.Equal("默认", CardMarkup.TitleOf(null, "默认"));

        var named = """
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" AutomationProperties.Name="用量估算" />
            """;
        Assert.Equal("用量估算", CardMarkup.TitleOf(named, "默认"));
    }

    [Fact]
    public void Skill_TellsTheModelWhenToUseCardsAndHowToSubmit()
    {
        var skill = CardSkill.Content;
        Assert.Contains("cards/<slug>/card.xaml", skill);
        Assert.Contains("CardControllerBase", skill);
        Assert.Contains("TubaText", skill);
        Assert.Contains("事件不写在 XAML 里", skill);
        Assert.Contains("不可用", skill);
        // 必须主动多用：技能描述里要给出「什么时候该做卡片」的判据。
        Assert.Contains("优先用卡片", skill);
        Assert.Contains("主动做一张卡片", skill);
        Assert.Contains("多张卡片", skill);
        // 提交方式必须明确，否则写完也不显示。
        Assert.Contains("show-card.ps1", skill);
        Assert.Contains("-JsonFile", skill);
        Assert.Contains("已提交卡片", skill);
        Assert.Contains("MaxWidth", skill);
    }

    [Fact]
    public void Skill_InstallsIntoTheAgentWorkspace()
    {
        var data = Path.Combine(_root, "Data");
        Directory.CreateDirectory(Path.Combine(data, "AiAssistant", "Workspace"));
        var pluginData = Path.Combine(data, "PluginData", "tubatools.aixamlcard");
        Directory.CreateDirectory(pluginData);

        CardSkill.Install(pluginData);

        var installed = Path.Combine(data, "AiAssistant", "Workspace", ".opencode", "skills", "tuba-xaml-card", "SKILL.md");
        Assert.True(File.Exists(installed));
        Assert.Contains("CardControllerBase", File.ReadAllText(installed));
    }

    [Fact]
    public void Skill_CopiesTheSubmitScript()
    {
        var data = Path.Combine(_root, "Data");
        Directory.CreateDirectory(Path.Combine(data, "AiAssistant", "Workspace"));
        var pluginData = Path.Combine(data, "PluginData", "tubatools.aixamlcard");
        Directory.CreateDirectory(pluginData);
        var source = Path.Combine(_root, "show-card.ps1");
        File.WriteAllText(source, "# submit");

        CardSkill.Install(pluginData, source);

        var script = Path.Combine(data, "AiAssistant", "Workspace", ".opencode", "skills", "tuba-xaml-card", "scripts", "show-card.ps1");
        Assert.True(File.Exists(script));
        Assert.Equal("# submit", File.ReadAllText(script));
    }

    // ---- request contract ---------------------------------------------------------------

    [Fact]
    public void Request_ParsesAndClamps()
    {
        var json = """
            {"id":"r1","title":"功耗估算","xaml":"<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />","code":"public sealed class CardController : CardControllerBase { }"}
            """;
        var request = CardRequestFile.Parse(json);
        Assert.NotNull(request);
        Assert.Equal(1, request!.Count);
        Assert.Equal("功耗估算", request.Cards[0].Title);
        Assert.True(request.Cards[0].HasCode);
        Assert.Contains("Border", request.Cards[0].Xaml);
    }

    [Fact]
    public void Request_ParsesABatchOfCards()
    {
        // 「一次给全套面板」是主要用法，必须支持多张。
        var json = """
            {"id":"r2","title":"整机体检","cards":[
              {"title":"功耗","xaml":"<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />"},
              {"title":"内存","xaml":"<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" />","code":"x"},
              {"title":"无效","xaml":""}
            ]}
            """;
        var request = CardRequestFile.Parse(json);
        Assert.NotNull(request);
        Assert.Equal(2, request!.Count);
        Assert.Equal("功耗", request.Cards[0].Title);
        Assert.True(request.Cards[1].HasCode);
    }

    [Fact]
    public void Request_CapsBatchSize()
    {
        var cards = string.Join(",", Enumerable.Range(0, 20)
            .Select(i => $"{{\"title\":\"c{i}\",\"xaml\":\"<Border xmlns=\\\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\\\"/>\"}}"));
        var request = CardRequestFile.Parse("{\"cards\":[" + cards + "]}");
        Assert.NotNull(request);
        Assert.Equal(CardRequestFile.MaxCards, request!.Count);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"xaml\":\"\"}")]
    [InlineData("{\"code\":\"x\"}")]
    [InlineData("{\"cards\":[]}")]
    [InlineData("{\"cards\":[{\"title\":\"a\"}]}")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Request_RejectsRequestsWithoutXaml(string json) =>
        Assert.Null(CardRequestFile.Parse(json));

    [Fact]
    public void Request_ReadsFromDisk()
    {
        var directory = CardRequestFile.DirectoryFor(Path.Combine(_root, "PluginData", "tubatools.aixamlcard"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "a.json");
        File.WriteAllText(path, """
            {"id":"a","title":"t","xaml":"<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"/>","code":""}
            """);
        var request = CardRequestFile.Read(path);
        Assert.NotNull(request);
        Assert.Equal("t", request!.Cards[0].Title);
        Assert.False(request.Cards[0].HasCode);
    }

    // ---- workspace rules ----------------------------------------------------------------

    [Fact]
    public void WorkspaceRules_AppendOnceAndStayIdempotent()
    {
        var original = "你是图吧 AI 助手。\n";
        var once = CardWorkspaceRules.Apply(original);
        Assert.Contains("优先用界面卡片", once);
        Assert.StartsWith("你是图吧 AI 助手。", once);

        var twice = CardWorkspaceRules.Apply(once);
        Assert.Equal(once, twice);
        Assert.Equal(1, CountOccurrences(twice, "tuba-xaml-card:begin"));
    }

    [Fact]
    public void WorkspaceRules_ReplaceTheBlockAfterTheHostRewritesTheFile()
    {
        // 宿主每次连接都整份重写：段落没了就要能重新贴回，不能重复堆叠。
        var fresh = "你是图吧 AI 助手。\n";
        var applied = CardWorkspaceRules.Apply(fresh);
        var replaced = CardWorkspaceRules.Apply(applied);
        Assert.Equal(applied, replaced);
        Assert.Equal(1, CountOccurrences(replaced, "tuba-xaml-card:begin"));
    }

    [Fact]
    public void WorkspaceRules_PreserveSurroundingContent()
    {
        var current = "头部说明\n";
        var withRules = CardWorkspaceRules.Apply(current);
        var appended = withRules + "\n尾部新增说明\n";
        var reapplied = CardWorkspaceRules.Apply(appended);
        Assert.Contains("头部说明", reapplied);
        Assert.Contains("尾部新增说明", reapplied);
        Assert.Equal(1, CountOccurrences(reapplied, "tuba-xaml-card:begin"));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0) { count++; index += value.Length; }
        return count;
    }
}
