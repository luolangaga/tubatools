using Microsoft.Extensions.AI;
using TubaWinUi3.Services.Ai.Onnx;
using TubaWinUi3.Services.AiPlayground;

namespace TubaWinUi3.Tests;

/// <summary>
/// 本地模型提示词改编（LocalChatPrompt）与工具调用流式解析（ToolCallStreamParser）测试。
/// 纯逻辑，不依赖 ORT / 模型文件。
/// </summary>
public class LocalChatPromptTests
{
    private static ChatMessage Msg(ChatRole role, string text)
        => new(role, text);

    [Fact]
    public void Build_SystemAndUser_RendersPhiFormat()
    {
        var prompt = LocalChatPrompt.Build(AiPromptFormat.Phi,
        [
            Msg(ChatRole.System, "你是助手"),
            Msg(ChatRole.User, "你好"),
        ]);

        Assert.Contains("<|system|>\n你是助手<|end|>", prompt);
        Assert.Contains("<|user|>\n你好<|end|>", prompt);
        Assert.EndsWith("<|assistant|>\n", prompt);
    }

    [Fact]
    public void Build_MergesMultipleSystemMessages()
    {
        var prompt = LocalChatPrompt.Build(AiPromptFormat.Plain,
        [
            Msg(ChatRole.System, "第一条"),
            Msg(ChatRole.System, "第二条"),
            Msg(ChatRole.User, "问题"),
        ]);

        // 多条 system 合并进同一段系统提示（以空行分隔）
        Assert.Contains("第一条", prompt);
        Assert.Contains("第二条", prompt);
        Assert.Contains("[用户] 问题", prompt);
    }

    [Fact]
    public void Build_ToolResult_RendersAsToolResultText()
    {
        var toolMsg = new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent("call-1", "文件内容：hello")]);

        var prompt = LocalChatPrompt.Build(AiPromptFormat.Phi, [toolMsg]);

        Assert.Contains("[工具结果] 文件内容：hello", prompt);
    }

    [Fact]
    public void Build_AssistantWithToolCall_RendersToolCallText()
    {
        var assistant = new ChatMessage(ChatRole.Assistant, "我来查一下");
        assistant.Contents.Add(new FunctionCallContent("c1", "read_file",
            new Dictionary<string, object?> { ["path"] = "C:/a.txt" }));

        var prompt = LocalChatPrompt.Build(AiPromptFormat.Phi, [assistant]);

        Assert.Contains("我来查一下", prompt);
        Assert.Contains(LocalChatPrompt.ToolCallOpen + "{\"name\":\"read_file\"", prompt);
        Assert.Contains("\"path\":\"C:/a.txt\"", prompt);
    }

    [Fact]
    public void BuildToolSchemaBlock_IncludesToolNamesAndSchema()
    {
        var tool = AIFunctionFactory.Create((string path) => "ok", new AIFunctionFactoryOptions
        {
            Name = "read_file",
            Description = "读取文件",
        });

        var block = LocalChatPrompt.BuildToolSchemaBlock([tool]);

        Assert.Contains("read_file", block);
        Assert.Contains("读取文件", block);
        Assert.Contains("参数(JSON Schema)", block);
    }

    [Fact]
    public void BuildToolSchemaBlock_NoTools_Empty()
    {
        Assert.Equal("", LocalChatPrompt.BuildToolSchemaBlock(null));
        Assert.Equal("", LocalChatPrompt.BuildToolSchemaBlock([]));
    }
}

public class ToolCallStreamParserTests
{
    [Fact]
    public void PlainText_IsEmittedVerbatim()
    {
        var parser = new ToolCallStreamParser();
        var result = parser.Append("你好，世界");
        Assert.Equal("你好，世界", result.Text);
        Assert.Empty(result.Tools);
    }

    [Fact]
    public void ToolCall_IsExtracted_NotLeakedIntoText()
    {
        var parser = new ToolCallStreamParser();
        var result = parser.Append(
            "我查一下" + LocalChatPrompt.ToolCallOpen +
            "{\"name\":\"read_file\",\"arguments\":{\"path\":\"a.txt\"}}" +
            LocalChatPrompt.ToolCallClose);

        Assert.Equal("我查一下", result.Text);
        Assert.Single(result.Tools);
        Assert.Equal("read_file", result.Tools[0].Name);
        Assert.Equal("a.txt", result.Tools[0].Arguments["path"]?.ToString());
    }

    [Fact]
    public void TextAfterToolCall_IsEmitted_ButMarkerItselfIsNot()
    {
        var parser = new ToolCallStreamParser();
        var result = parser.Append(
            "查一下" + LocalChatPrompt.ToolCallOpen +
            "{\"name\":\"read_file\",\"arguments\":{}}" +
            LocalChatPrompt.ToolCallClose + "好的，已读取");

        Assert.Equal("查一下好的，已读取", result.Text);
        Assert.Single(result.Tools);
    }

    [Fact]
    public void SplitMarkerAcrossChunks_IsBuffered_NotLeaked()
    {
        var parser = new ToolCallStreamParser();

        // 逐字符喂入工具块，标记被切成多段
        var input = LocalChatPrompt.ToolCallOpen +
                    "{\"name\":\"web_search\",\"arguments\":{\"query\":\"显卡\"}}" +
                    LocalChatPrompt.ToolCallClose;

        var text = "";
        var tools = new List<LocalToolCall>();
        foreach (var ch in input)
        {
            var r = parser.Append(ch.ToString());
            text += r.Text;
            tools.AddRange(r.Tools);
        }

        Assert.Equal("", text);           // 不泄漏半截标记
        Assert.Single(tools);
        Assert.Equal("web_search", tools[0].Name);
        Assert.Equal("显卡", tools[0].Arguments["query"]?.ToString());
    }

    [Fact]
    public void MultipleToolCalls_InOneChunk()
    {
        var parser = new ToolCallStreamParser();
        var result = parser.Append(
            LocalChatPrompt.ToolCallOpen + "{\"name\":\"a\",\"arguments\":{}}" + LocalChatPrompt.ToolCallClose +
            LocalChatPrompt.ToolCallOpen + "{\"name\":\"b\",\"arguments\":{}}" + LocalChatPrompt.ToolCallClose);

        Assert.Equal("", result.Text);
        Assert.Equal(2, result.Tools.Count);
        Assert.Equal("a", result.Tools[0].Name);
        Assert.Equal("b", result.Tools[1].Name);
    }

    [Fact]
    public void InvalidJsonToolCall_IsDropped_WithoutTextLeak()
    {
        var parser = new ToolCallStreamParser();
        var result = parser.Append(
            LocalChatPrompt.ToolCallOpen + "not json" + LocalChatPrompt.ToolCallClose);

        Assert.Empty(result.Tools);
        Assert.Equal("", result.Text); // 无法解析的工具块不当作可见文本
    }

    [Fact]
    public void PartialMarkerAtEnd_IsHeldUntilFlush()
    {
        var parser = new ToolCallStreamParser();
        // 末尾是 "<tool" —— 可能是 <tool_call> 的前缀，必须保留
        var r1 = parser.Append("文本<tool");
        Assert.Equal("文本", r1.Text);

        var r2 = parser.Append("_call>{\"name\":\"x\",\"arguments\":{}}" + LocalChatPrompt.ToolCallClose);
        Assert.Equal("", r2.Text);
        Assert.Single(r2.Tools);
        Assert.Equal("x", r2.Tools[0].Name);
    }

    [Fact]
    public void IncompleteToolCallAtFlush_IsBestEffortParsed()
    {
        var parser = new ToolCallStreamParser();
        parser.Append(LocalChatPrompt.ToolCallOpen + "{\"name\":\"y\",\"arguments\":{}}");

        var tail = parser.Flush();
        Assert.Single(tail.Tools);
        Assert.Equal("y", tail.Tools[0].Name);
    }

    [Theory]
    [InlineData("<tool", "<tool_call>", 5)]
    [InlineData("abc<tool_ca", "<tool_call>", 8)]
    [InlineData("abc", "<tool_call>", 0)]
    public void LongestMarkerPrefixSuffix_DetectsPrefixes(string text, string marker, int expected)
    {
        Assert.Equal(expected, ToolCallStreamParser.LongestMarkerPrefixSuffix(text, marker));
    }
}
