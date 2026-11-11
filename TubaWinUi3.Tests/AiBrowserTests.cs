using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

/// <summary>
/// AI 浏览器：固定内置 WebView2 的引擎口径、URL/按键、工具名与 MCP 工具清单、
/// 页面快照格式化。全部为纯逻辑，不启动真实浏览器或 WebView2。
/// </summary>
public class AiBrowserTests
{
    [Fact]
    public void Info_TellsAgentAboutDedicatedWebView2Profile()
    {
        Assert.Contains("内置 WebView2", AiBrowserInfo.ResultHeader);
        Assert.Contains("不是用户日常", AiBrowserInfo.ResultHeader);
        Assert.Contains("登录状态", AiBrowserInfo.ResultHeader);
        Assert.Contains(AiBrowserInfo.ProfilePath, AiBrowserInfo.StatusLine);
    }

    [Fact]
    public void DescribeForAgent_IncludesProfileAndLiveHeaderRule()
    {
        var text = AiBrowserInfo.DescribeForAgent();

        Assert.Contains("当前引擎：内置 WebView2", text);
        Assert.Contains(AiBrowserInfo.ProfilePath, text);
        Assert.Contains("每次 browser_use 工具结果都会标注当前引擎", text);
    }

    [Fact]
    public void ProfilePath_IsDedicatedToAiBrowser()
    {
        Assert.EndsWith(Path.Combine("AiBrowser", "WebView2"), AiBrowserInfo.ProfilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("https://example.com/a", "https://example.com/a")]
    [InlineData("//example.com", "https://example.com")]
    [InlineData("localhost:3000", "http://localhost:3000")]
    [InlineData("127.0.0.1:8080/x", "http://127.0.0.1:8080/x")]
    [InlineData("about:blank", "about:blank")]
    public void NormalizeUrl_AcceptsCommonForms(string input, string expected)
    {
        Assert.Equal(expected, BrowserCdp.NormalizeUrl(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/win.ini")]
    public void NormalizeUrl_RejectsInvalidOrScriptUrls(string input)
    {
        Assert.Throws<BrowserToolException>(() => BrowserCdp.NormalizeUrl(input));
    }

    [Fact]
    public void NormalizeKey_ParsesShortcutsAndSpecialKeys()
    {
        Assert.True(BrowserCdp.TryParseKey("Control+A", out var ctrlA));
        Assert.Equal("A", ctrlA.Key);
        Assert.Equal(2, ctrlA.Modifiers);
        Assert.Null(ctrlA.Text);

        Assert.True(BrowserCdp.TryParseKey("Enter", out var enter));
        Assert.Equal("\r", enter.Text);
        Assert.Equal(13, enter.VirtualKey);

        Assert.True(BrowserCdp.TryParseKey("PageDown", out var pageDown));
        Assert.Equal(34, pageDown.VirtualKey);

        Assert.True(BrowserCdp.TryParseKey("F5", out var f5));
        Assert.Equal(116, f5.VirtualKey);

        Assert.False(BrowserCdp.TryParseKey("hello", out _));
        Assert.False(BrowserCdp.TryParseKey("Control+", out _));
    }

    [Theory]
    [InlineData("status", "status")]
    [InlineData("browser_use_navigate", "navigate")]
    [InlineData("browser_click", "click")]
    [InlineData("mcp_browser_use_history", "history")]
    [InlineData("nope", "")]
    public void ToolName_NormalizesClientPrefixes(string input, string expected)
    {
        Assert.Equal(expected, BrowserToolNames.Normalize(input));
    }

    [Fact]
    public void ToolDefinitions_AreUniqueAndCoverCoreActions()
    {
        var definitions = BrowserUseMcpProxy.Definitions();
        var names = definitions.Select(item => item!["name"]!.GetValue<string>()).ToArray();

        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.Contains("status", names);
        Assert.Contains("navigate", names);
        Assert.Contains("snapshot", names);
        Assert.Contains("click", names);
        Assert.Contains("type", names);
        Assert.Contains("screenshot", names);
        Assert.DoesNotContain("tab_select", names);
        Assert.All(definitions, item => Assert.NotNull(item!["inputSchema"]));
    }

    [Fact]
    public void Snapshot_FormatIncludesRefsTextAndHints()
    {
        var snapshot = new BrowserSnapshotData(
            "https://example.com/", "Example", "complete", "Hello world",
            [new BrowserSnapshotItem("e1", "button", "", "保存", false, null, "", "", 10, 20)], 0, 800, 600);

        var text = snapshot.Format();

        Assert.Contains("URL: https://example.com/", text);
        Assert.Contains("页面文本:", text);
        Assert.Contains("Hello world", text);
        Assert.Contains("e1 <button> \"保存\"", text);
        Assert.Contains("ref 仅在本次页面状态有效", text);
    }
}
