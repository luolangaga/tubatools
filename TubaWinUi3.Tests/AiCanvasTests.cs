using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

[Collection("GlobalConfigTests")]
public sealed class AiCanvasTests : IDisposable
{
    private readonly string? _original;
    private readonly string _root;

    public AiCanvasTests()
    {
        _original = ConfigManager.DataDirectoryOverride;
        _root = Path.Combine(Path.GetTempPath(), "tuba-canvas-" + Guid.NewGuid().ToString("N"));
        ConfigManager.DataDirectoryOverride = _root;
        AgentWorkspace.Prepare();
    }

    public void Dispose()
    {
        ConfigManager.DataDirectoryOverride = _original;
        for (var attempt = 0; Directory.Exists(_root) && attempt < 10; attempt++)
        {
            try { Directory.Delete(_root, true); }
            catch (IOException) when (attempt < 9) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (attempt < 9) { Thread.Sleep(100); }
        }
    }

    // ---- paths -----------------------------------------------------------------------------

    [Fact]
    public void DirectoryFor_StaysInsideCanvasRoot()
    {
        var directory = AgentCanvasStore.DirectoryFor("ses_abc-123");
        Assert.StartsWith(AgentCanvasStore.Root, directory, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("canvas", Path.GetFileName(AgentCanvasStore.Root));
    }

    [Theory]
    [InlineData("../../etc")]
    [InlineData("a/b\\c")]
    [InlineData("con")]
    [InlineData("")]
    public void DirectoryFor_RejectsPathMetacharacters(string sessionId)
    {
        var directory = AgentCanvasStore.DirectoryFor(sessionId);
        Assert.Equal(AgentCanvasStore.Root, Path.GetDirectoryName(directory));
        var name = Path.GetFileName(directory);
        Assert.DoesNotContain("..", name);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void RelativeIndex_UsesForwardSlashes()
    {
        var relative = AgentCanvasStore.RelativeIndex("s1");
        Assert.Equal("canvas/s1/index.html", relative);
        Assert.DoesNotContain('\\', relative);
    }

    [Fact]
    public void ResolvePage_RejectsEscapesAndNonHtml()
    {
        var directory = AgentCanvasStore.EnsureDirectory("s1");
        File.WriteAllText(Path.Combine(directory, "index.html"), "<html><body>x</body></html>");
        Assert.NotNull(AgentCanvasStore.ResolvePage("s1", "index.html"));
        Assert.Null(AgentCanvasStore.ResolvePage("s1", "../s1/index.html"));
        Assert.Null(AgentCanvasStore.ResolvePage("s1", "assets/../../index.html"));
        Assert.Null(AgentCanvasStore.ResolvePage("s1", "index.txt"));
        Assert.Null(AgentCanvasStore.ResolvePage("s1", "missing.html"));
    }

    // ---- html ------------------------------------------------------------------------------

    [Fact]
    public void LooksComplete_RequiresClosingTag()
    {
        Assert.False(AgentCanvasStore.LooksComplete("<html><body>half"));
        Assert.False(AgentCanvasStore.LooksComplete(""));
        Assert.True(AgentCanvasStore.LooksComplete("<html><body>done</body></html>"));
        Assert.True(AgentCanvasStore.LooksComplete("<html><body>done</body></html>\n  "));
    }

    [Fact]
    public void NormalizeHtml_WrapsFragmentAndRejectsEmpty()
    {
        var wrapped = AgentCanvasStore.NormalizeHtml("<p>hello</p>");
        Assert.NotNull(wrapped);
        Assert.Contains("<!DOCTYPE html>", wrapped);
        Assert.Contains("<p>hello</p>", wrapped);
        Assert.True(AgentCanvasStore.LooksComplete(wrapped));
        Assert.Null(AgentCanvasStore.NormalizeHtml("plain text"));
        Assert.Null(AgentCanvasStore.NormalizeHtml("<html><body>unfinished"));
    }

    [Fact]
    public void ExtractHtmlBlock_FindsFencedDocument()
    {
        var markdown = "说明\n\n```html\n<html><body>x</body></html>\n```\n\n结束";
        var html = AgentCanvasStore.ExtractHtmlBlock(markdown);
        Assert.NotNull(html);
        Assert.Contains("<html>", html);
        Assert.Null(AgentCanvasStore.ExtractHtmlBlock("```js\nconsole.log(1)\n```"));
        Assert.Null(AgentCanvasStore.ExtractHtmlBlock(""));
    }

    // ---- canvas messages -------------------------------------------------------------------

    [Fact]
    public void Snapshot_CopiesLivePageIntoAnArchiveThatCannotBeOverwritten()
    {
        var directory = AgentCanvasStore.EnsureDirectory("s1");
        var live = Path.Combine(directory, "index.html");
        File.WriteAllText(live, "<!DOCTYPE html><html><body>first</body></html>");
        var archive = AgentCanvasStore.Snapshot("s1");
        Assert.NotNull(archive);
        Assert.StartsWith("history/", archive, StringComparison.Ordinal);
        var archived = AgentCanvasStore.ResolvePage("s1", archive!);
        Assert.NotNull(archived);
        Assert.Contains("first", File.ReadAllText(archived!));
        Assert.NotEqual(live, archived);

        // Rewriting the live page must not touch the snapshot.
        File.WriteAllText(live, "<!DOCTYPE html><html><body>second</body></html>");
        Assert.Contains("first", File.ReadAllText(archived!));
    }

    [Fact]
    public void Archive_AddsBaseHrefSoAssetsStillResolve()
    {
        AgentCanvasStore.EnsureDirectory("s1");
        var archive = AgentCanvasStore.WriteArchive("s1", "<!DOCTYPE html><html><head><title>t</title></head><body>a</body></html>");
        Assert.NotNull(archive);
        var path = AgentCanvasStore.ResolvePage("s1", archive!);
        Assert.NotNull(path);
        Assert.Contains("<base href=\"../../\">", File.ReadAllText(path!));
    }

    [Fact]
    public void Archive_RejectsIncompleteHtml()
    {
        AgentCanvasStore.EnsureDirectory("s1");
        Assert.Null(AgentCanvasStore.WriteArchive("s1", "<html><body>half"));
        Assert.Null(AgentCanvasStore.Snapshot("s1"));
    }

    [Fact]
    public void Documents_RoundTripAndSurviveReload()
    {
        var documents = new[]
        {
            new CanvasDocument("d1", "m1", "index.html", "第一课", "hash1", 1),
            new CanvasDocument("d2", "m2", "history/x/index.html", "第二课", "hash2", 2)
        };
        AgentCanvasStore.SaveDocuments("s1", documents);
        var read = AgentCanvasStore.ReadDocuments("s1");
        Assert.Equal(2, read.Count);
        Assert.Equal("d1", read[0].Id);
        Assert.Equal("history/x/index.html", read[1].RelativePath);
        Assert.Empty(AgentCanvasStore.ReadDocuments("unknown-session"));
    }

    [Fact]
    public void Hash_ChangesOnlyWhenThePageChanges()
    {
        var directory = AgentCanvasStore.EnsureDirectory("s1");
        Assert.Null(AgentCanvasStore.HashIndex("s1"));
        File.WriteAllText(Path.Combine(directory, "index.html"), "<html><body>a</body></html>");
        var first = AgentCanvasStore.HashIndex("s1");
        Assert.NotNull(first);
        Assert.Equal(first, AgentCanvasStore.HashIndex("s1"));
        File.WriteAllText(Path.Combine(directory, "index.html"), "<html><body>b</body></html>");
        Assert.NotEqual(first, AgentCanvasStore.HashIndex("s1"));
    }

    // ---- images ----------------------------------------------------------------------------

    [Fact]
    public void ImportImage_DeduplicatesAndRejectsUnsupported()
    {
        var source = Path.Combine(_root, "picture.png");
        File.WriteAllBytes(source, [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3]);
        var first = AgentCanvasStore.ImportImage("s1", source);
        var second = AgentCanvasStore.ImportImage("s1", source);
        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.True(File.Exists(Path.Combine(AgentCanvasStore.AssetsPath("s1"), first!)));
        Assert.Single(AgentCanvasStore.ListImages("s1"));

        var rejected = Path.Combine(_root, "bad.exe");
        File.WriteAllBytes(rejected, [1, 2, 3]);
        Assert.Null(AgentCanvasStore.ImportImage("s1", rejected));
        Assert.Null(AgentCanvasStore.ImportImage("s1", Path.Combine(_root, "missing.png")));
    }

    [Fact]
    public void SafeFileName_StripsInvalidCharacters()
    {
        var name = AgentCanvasStore.SafeFileName("a<b>:c/d\\e|f?g*h");
        foreach (var c in new[] { '<', '>', ':', '/', '\\', '|', '?', '*' }) Assert.DoesNotContain(c, name);
        Assert.Equal("image", AgentCanvasStore.SafeFileName("   "));
    }

    // ---- prompt ----------------------------------------------------------------------------

    [Fact]
    public void PromptMarker_IsShortAndPointsAtCanvasFile()
    {
        var prefix = AgentCanvasPrompt.Prefix("s1");
        Assert.Contains("canvas/s1/index.html", prefix);
        Assert.Contains("教学模式", prefix);
        // The user-visible message must stay readable: rules live in AGENTS.md / the skill.
        Assert.True(prefix.Length < 200, "prefix should be a marker, not a rule dump");
        Assert.Contains("canvas/s1/index.html", AgentCanvasPrompt.CanvasTurnHint("s1"));
    }

    [Fact]
    public void Strip_RemovesTeachingMarkersFromTheTranscript()
    {
        // Regression: the marker used to render inside the user's chat bubble.
        var prompt = AgentCanvasPrompt.Prefix("s1") + "电脑的各个零件有哪些?";
        Assert.Equal("电脑的各个零件有哪些?", AgentCanvasPrompt.Strip(prompt));

        var fromCanvas = "再讲简单点" + AgentCanvasPrompt.CanvasTurnHint("s1");
        Assert.Equal("再讲简单点", AgentCanvasPrompt.Strip(fromCanvas));

        Assert.Equal("", AgentCanvasPrompt.Strip(null));
        Assert.Equal("普通消息", AgentCanvasPrompt.Strip("普通消息"));
        // A message that merely mentions the words must survive untouched.
        var harmless = "画布 canvas/s1/index.html 是什么？";
        Assert.Equal(harmless, AgentCanvasPrompt.Strip(harmless));
    }

    // ---- bridge ----------------------------------------------------------------------------

    [Fact]
    public void Bridge_ParsesWhitelistedKinds()
    {
        Assert.Equal(CanvasRequestKind.Prompt, CanvasBridge.Parse("{\"type\":\"prompt\",\"text\":\"hi\"}").Kind);
        Assert.Equal("hi", CanvasBridge.Parse("{\"type\":\"prompt\",\"text\":\"hi\"}").Text);
        Assert.Equal(CanvasRequestKind.Insert, CanvasBridge.Parse("{\"type\":\"insert\",\"text\":\"x\"}").Kind);
        Assert.Equal(CanvasRequestKind.Title, CanvasBridge.Parse("{\"type\":\"title\",\"text\":\"t\"}").Kind);
        Assert.Equal(CanvasRequestKind.Ready, CanvasBridge.Parse("{\"type\":\"ready\"}").Kind);
        Assert.Equal(CanvasRequestKind.Unknown, CanvasBridge.Parse("{\"type\":\"shell\",\"text\":\"rm -rf /\"}").Kind);
        Assert.Equal(CanvasRequestKind.Unknown, CanvasBridge.Parse("not json").Kind);
        Assert.Equal(CanvasRequestKind.Unknown, CanvasBridge.Parse(null).Kind);
        Assert.Equal(CanvasRequestKind.Unknown, CanvasBridge.Parse("{\"type\":\"prompt\"}").Kind);
    }

    [Fact]
    public void Bridge_ClampsLongTextAndRejectsOversizedPayloads()
    {
        var longText = new string('a', CanvasBridge.MaxTextLength + 500);
        var parsed = CanvasBridge.Parse("{\"type\":\"prompt\",\"text\":\"" + longText + "\"}");
        Assert.Equal(CanvasBridge.MaxTextLength, parsed.Text.Length);

        var huge = "{\"type\":\"prompt\",\"text\":\"" + new string('b', CanvasBridge.MaxMessageBytes + 10) + "\"}";
        Assert.Equal(CanvasRequestKind.Unknown, CanvasBridge.Parse(huge).Kind);
    }

    [Theory]
    [InlineData("{\"type\":\"height\",\"value\":640}", CanvasRequestKind.Height)]
    [InlineData("{\"type\":\"height\",\"value\":999999}", CanvasRequestKind.Height)]
    public void Bridge_AcceptsReportedHeight(string json, CanvasRequestKind kind)
    {
        var request = CanvasBridge.Parse(json);
        Assert.Equal(kind, request.Kind);
        Assert.InRange(request.Height, 0, CanvasBridge.MaxReportedHeight);
    }

    [Theory]
    [InlineData("{\"type\":\"height\"}")]
    [InlineData("{\"type\":\"height\",\"value\":\"tall\"}")]
    [InlineData("{\"type\":\"height\",\"value\":-5}")]
    public void Bridge_RejectsBadHeight(string json) =>
        Assert.Equal(CanvasRequestKind.Unknown, CanvasBridge.Parse(json).Kind);

    [Theory]
    [InlineData("https://example.com/a", true)]
    [InlineData("http://example.com", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("https://localhost/x", false)]
    [InlineData("https://canvas.local/x", false)]
    [InlineData("https://127.0.0.1/x", false)]
    [InlineData("https://192.168.1.1/x", false)]
    [InlineData("https://10.0.0.5/x", false)]
    [InlineData("https://user:pass@example.com/", false)]
    [InlineData("", false)]
    public void Bridge_AllowsOnlyPublicWebUrls(string url, bool allowed) =>
        Assert.Equal(allowed, CanvasBridge.IsAllowedUrl(url));

    [Fact]
    public void Bridge_RejectsForeignOrigins()
    {
        Assert.True(CanvasBridge.IsAllowedSource(CanvasShim.Origin + "index.html"));
        Assert.False(CanvasBridge.IsAllowedSource("https://evil.example/index.html"));
        Assert.False(CanvasBridge.IsAllowedSource(null));
    }

    [Fact]
    public void Bridge_OutputsParseableJson()
    {
        Assert.Equal("state", Kind(CanvasBridge.BuildState(true, "tool", "read")));
        Assert.Equal("message", Kind(CanvasBridge.BuildMessage("assistant", "hi")));
        Assert.Equal("notice", Kind(CanvasBridge.BuildNotice("oops", true)));
        Assert.Equal("images", Kind(CanvasBridge.BuildImages(["a b.png"])));

        static string Kind(string json)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.GetProperty("type").GetString()!;
        }
    }

    [Fact]
    public void Bridge_ImageUrlIsEscaped() =>
        Assert.Contains("assets/a%20b.png", CanvasBridge.BuildImages(["a b.png"]));

    [Fact]
    public void Shim_ExposesDocumentedApi()
    {
        foreach (var member in new[] { "tuba", "send", "insert", "title", "onMessage", "onState", "onImages", "data-tuba-prompt", "data-tuba-insert", "chrome.webview", "reportHeight" })
            Assert.Contains(member, CanvasShim.Script, StringComparison.Ordinal);
        Assert.Equal("https://canvas.local/", CanvasShim.Origin);
    }

    [Fact]
    public void Shim_ReportsContentHeightSoTheBubbleIsNotPadded()
    {
        // Without an automatic height report the inline bubble would keep a fixed tall box.
        Assert.Contains("type: 'height'", CanvasShim.Script, StringComparison.Ordinal);
        Assert.Contains("ResizeObserver", CanvasShim.Script, StringComparison.Ordinal);
        Assert.Contains("scrollHeight", CanvasShim.Script, StringComparison.Ordinal);
    }

    // ---- workspace -------------------------------------------------------------------------

    [Fact]
    public void Workspace_ImportsCanvasSkillAndReferencePage()
    {
        var skill = Path.Combine(AgentWorkspace.DirectoryPath, ".opencode", "skills", "tuba-canvas", "SKILL.md");
        Assert.True(File.Exists(skill));
        Assert.True(new FileInfo(skill).Length > 500);

        // The skill tells the model to reuse this page; it must ship with the skill.
        var reference = Path.Combine(AgentWorkspace.DirectoryPath, ".opencode", "skills", "tuba-canvas", "reference.html");
        Assert.True(File.Exists(reference));
        var html = File.ReadAllText(reference);
        Assert.Contains("preserve-3d", html);
        Assert.Contains("data-tuba-prompt", html);
        Assert.True(AgentCanvasStore.LooksComplete(html));
        // Offline requirement: no remote asset may sneak into the reference page.
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cdn.", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureDirectory_SeedsTheReferencePageForTheModel()
    {
        var assets = AgentCanvasStore.AssetsPath("s1");
        AgentCanvasStore.EnsureDirectory("s1");
        var copied = Path.Combine(assets, "reference.html");
        // Only seeded when the shipped asset exists in this build output.
        var shipped = Path.Combine(AppContext.BaseDirectory, "Assets", "AiAgent", "tuba-canvas", "reference.html");
        Assert.Equal(File.Exists(shipped), File.Exists(copied));
    }

    [Fact]
    public void Skill_MandatesVividHandDrawnStyleAnd3D()
    {
        var skill = File.ReadAllText(Path.Combine(AgentWorkspace.DirectoryPath, ".opencode", "skills", "tuba-canvas", "SKILL.md"));
        // The product requirement: lively visuals, hand-drawn by default, 3D when it helps.
        Assert.Contains("手绘", skill);
        Assert.Contains("3D", skill);
        Assert.Contains("可爱", skill);
        Assert.Contains("preserve-3d", skill);
        Assert.Contains("prefers-reduced-motion", skill);
        Assert.Contains("reference.html", skill);
        // Still no third-party runtime and no external assets.
        Assert.Contains("不允许外链", skill);
        Assert.DoesNotContain("three.js", skill.Replace("不要引 three.js", "", StringComparison.Ordinal));
    }

    [Fact]
    public void Workspace_AgentsMdExplainsCanvasContract()
    {
        var agents = File.ReadAllText(Path.Combine(AgentWorkspace.DirectoryPath, "AGENTS.md"));
        Assert.Contains("教学模式", agents);
        Assert.Contains("canvas/", agents);
        Assert.Contains("</html>", agents);
        // The rule must say the canvas is a message, not a standing panel.
        Assert.Contains("画布消息", agents);
        // And it must forbid the viewport-height layout that caused the dead white space.
        Assert.Contains("100vh", agents);
    }
}
