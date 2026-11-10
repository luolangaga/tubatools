using System.Runtime.InteropServices;
using System.Text.Json;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

public sealed class AgentProtocolTests
{
    [Fact]
    public void FreeSelection_RequiresExplicitZeroPricesForEveryTier()
    {
        using var json = JsonDocument.Parse("""
        {"data":[
          {"id":"free","providerID":"p","name":"Free","enabled":true,"cost":[{"input":0,"output":0}]},
          {"id":"unknown","providerID":"p","name":"Unknown","enabled":true,"cost":[]},
          {"id":"paid","providerID":"p","name":"Paid","enabled":true,"cost":[{"input":0,"output":1}]},
          {"id":"tiered","providerID":"p","name":"Tiered","enabled":true,"cost":[{"input":0,"output":0},{"input":1,"output":1}]},
          {"id":"disabled","providerID":"p","enabled":false,"cost":[{"input":0,"output":0}]}
        ]}
        """);
        var models = AgentProtocol.ParseModels(json.RootElement);
        Assert.Equal(4, models.Count);
        Assert.Equal("free", Assert.Single(models, m => m.IsFree).Id);
        Assert.Equal("free", AgentProtocol.SelectDefault(models, null)!.Id);
        Assert.Equal("paid", AgentProtocol.SelectDefault(models, "p/paid")!.Id);
        Assert.Equal("free", AgentProtocol.SelectDefault(models, "removed/old")!.Id);
    }

    [Fact]
    public void NoFreeModel_DoesNotSilentlyChoosePaidModel()
    {
        Assert.Null(AgentProtocol.SelectDefault([new("paid", "p", "Paid", false)], null));
    }

    [Fact]
    public void Messages_PreserveMarkdown_AndExposeNativeToolStatus()
    {
        using var json = JsonDocument.Parse("""
        {"data":[
          {"id":"1","type":"user","text":"Create a plugin"},
          {"id":"2","type":"assistant","content":[{"type":"text","text":"## Done\n**Hello**"},{"type":"tool","name":"bash","state":{"status":"completed"}}]},
          {"id":"3","type":"idle"}
        ]}
        """);
        var messages = AgentProtocol.ParseMessages(json.RootElement);
        Assert.Equal(2, messages.Count);
        Assert.Equal("## Done\n**Hello**", messages[1].Text);
        Assert.NotEmpty(messages[1].Activity);
        Assert.DoesNotContain("bash", messages[1].Activity);
    }

    [Theory]
    [InlineData("streaming", "\"\"")]
    [InlineData("streaming", "\"{\\\"filePath\\\":\\\"C:\"")]
    [InlineData("streaming", "null")]
    [InlineData("running", "{\"filePath\":\"plugin-api.md\"}")]
    [InlineData("completed", "{\"id\":\"tuba-plugin\"}")]
    [InlineData("error", "{}")]
    public void ToolArgumentStreaming_DoesNotInterruptConversation(string status, string input)
    {
        using var json = JsonDocument.Parse("""
            {"data":[{"id":"tool-message","type":"assistant","content":[
              {"type":"tool","name":"read","state":{"status":"STATUS","input":INPUT}}
            ]}]}
            """.Replace("STATUS", status).Replace("INPUT", input));
        var message = Assert.Single(AgentProtocol.ParseMessages(json.RootElement));
        Assert.Null(message.Error);
        Assert.NotEmpty(message.Activity);
        Assert.DoesNotContain("模型返回错误", message.Text);
    }

    [Fact]
    public void ToolCall_TransitionsFromPartialArgumentsToCompletedOutput()
    {
        using var streaming = JsonDocument.Parse("""
            {"data":[{"id":"1","type":"assistant","content":[{"type":"tool","name":"read","state":{"status":"streaming","input":"{\"filePath\":"}}]}]}
            """);
        using var completed = JsonDocument.Parse("""
            {"data":[{"id":"1","type":"assistant","content":[{"type":"tool","name":"read","state":{"status":"completed","input":{"filePath":"plugin-api.md"},"content":[{"type":"text","text":"API v1"},null]}}]}]}
            """);
        Assert.Null(Assert.Single(AgentProtocol.ParseMessages(streaming.RootElement)).Error);
        var final = Assert.Single(AgentProtocol.ParseMessages(completed.RootElement));
        Assert.Contains("plugin-api.md", final.Activity);
        Assert.Contains("API v1", final.Activity);
        Assert.Null(final.Error);
    }

    [Fact]
    public void Timeline_PreservesTextToolOrder_AndShowsCommandAndEdit()
    {
        using var json = JsonDocument.Parse("""
            {"data":[{"id":"1","type":"assistant","content":[
              {"type":"text","text":"Inspecting the project."},
              {"id":"read-1","type":"tool","name":"read","state":{"status":"completed","input":{"path":"Plugin.cs"}}},
              {"id":"edit-1","type":"tool","name":"edit","state":{"status":"completed","input":{"filePath":"Plugin.cs","oldString":"return 1;","newString":"return 2;"}}},
              {"id":"cmd-1","type":"tool","name":"bash","state":{"status":"completed","input":{"command":"dotnet pack Plugin.csproj"},"metadata":{"exitCode":0},"content":[{"type":"text","text":"Created Demo.nupkg"}]}},
              {"type":"text","text":"Build succeeded."}
            ]}]}
            """);
        var parts = Assert.Single(AgentProtocol.ParseMessages(json.RootElement)).Content!;
        Assert.Equal(5, parts.Count);
        Assert.Equal("Inspecting the project.", parts[0].Text);
        Assert.Equal("Plugin.cs", parts[1].Operation!.Target);
        Assert.Contains("- return 1;", parts[2].Operation!.Diff);
        Assert.Contains("+ return 2;", parts[2].Operation!.Diff);
        Assert.Equal("dotnet pack Plugin.csproj", parts[3].Operation!.Command);
        Assert.Equal(0, parts[3].Operation!.ExitCode);
        Assert.Equal("Build succeeded.", parts[4].Text);
    }

    [Fact]
    public void FailedCommand_IsFailedEvenWhenToolCallCompletes()
    {
        using var json = JsonDocument.Parse("""
            {"data":[{"id":"1","type":"assistant","content":[{"type":"tool","name":"bash","state":{"status":"completed","input":{"command":"dotnet pack"},"metadata":{"exit":1}}}]}]}
            """);
        var operation = Assert.Single(Assert.Single(AgentProtocol.ParseMessages(json.RootElement)).Content!).Operation!;
        Assert.Equal("error", operation.Status);
        Assert.Equal(1, operation.ExitCode);
    }

    [Fact]
    public void InterruptedToolCall_IsNotReportedAsModelFailure()
    {
        using var json = JsonDocument.Parse("""
            {"data":[{"id":"1","type":"assistant","error":{"type":"aborted","message":"Step interrupted"}}]}
            """);
        var message = Assert.Single(AgentProtocol.ParseMessages(json.RootElement));
        Assert.NotNull(message.Error);
        Assert.DoesNotContain("模型返回错误", message.Error);
    }

    [Fact]
    public void InstallAssets_FollowOsArchitecture()
    {
        Assert.EndsWith("opencode-windows-x64.zip", AiComponentService.DownloadUrl(Architecture.X64));
        Assert.EndsWith("opencode-windows-arm64.zip", AiComponentService.DownloadUrl(Architecture.Arm64));
        Assert.Throws<PlatformNotSupportedException>(() => AiComponentService.DownloadUrl(Architecture.X86));
    }

    [Theory]
    [InlineData("## Title\n\nParagraph", false)]
    [InlineData("```cs\nvar x = 1;\n```", true)]
    [InlineData("~~~ps\nGet-Date\n~~~", true)]
    [InlineData("```cs\npartially streamed code", true)]
    public void Markdown_CodeFencesAndBlankLines(string source, bool code)
    {
        var block = Assert.Single(MarkdownBlocks.Parse(source));
        Assert.Equal(code, block.IsCode);
        Assert.DoesNotContain("```", block.Text);
    }

    [Fact]
    public void Markdown_LongerOuterFencePreservesInnerFence()
    {
        var blocks = MarkdownBlocks.Parse("Before\n````md\n```cs\ncode\n```\n````\nAfter");
        Assert.Equal(3, blocks.Count);
        Assert.Contains("```cs", blocks[1].Text);
        Assert.Equal("md", blocks[1].Language);
        Assert.Equal("After", blocks[2].Text);
    }
}
