using System.Text.Json;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

public sealed class AgentQuestionTests
{
    private static AgentQuestion Parse(string json) { using var doc = JsonDocument.Parse(json); return AgentQuestions.Parse(doc.RootElement); }
    [Fact]
    public void PluginConsent_RequiresExplicitChoice_AndPreservesDescriptions()
    {
        var question = Parse("""
            {"id":"form-1","title":"是否将此方案封装为插件？","fields":[{"key":"plugin","type":"string","title":"你的选择","required":true,"options":[{"value":"yes","label":"封装为插件","description":"今后可重复使用"},{"value":"no","label":"仅处理这次问题"}]}]}
            """);
        Assert.Equal("今后可重复使用", question.Fields[0].Options[0].Description);
        Assert.NotNull(AgentQuestions.Validate(question, new Dictionary<string, object>()));
        Assert.Null(AgentQuestions.Validate(question, new Dictionary<string, object> { ["plugin"] = "no" }));
        Assert.NotNull(AgentQuestions.Validate(question, new Dictionary<string, object> { ["plugin"] = "invented" }));
    }
    [Fact]
    public void ConditionalFields_OnlyRequireVisibleAnswers()
    {
        var question = Parse("""
            {"id":"f","fields":[{"key":"choice","type":"string"},{"key":"detail","type":"string","required":true,"when":[{"key":"choice","op":"eq","value":"other"}]}]}
            """);
        Assert.Null(AgentQuestions.Validate(question, new Dictionary<string, object> { ["choice"] = "known" }));
        Assert.NotNull(AgentQuestions.Validate(question, new Dictionary<string, object> { ["choice"] = "other" }));
        Assert.Null(AgentQuestions.Validate(question, new Dictionary<string, object> { ["choice"] = "other", ["detail"] = "My answer" }));
    }
    [Fact]
    public void ConditionalFields_PreserveValueTypes()
    {
        var question = Parse("""{"id":"f","fields":[{"key":"detail","type":"string","when":[{"key":"count","op":"eq","value":1}]}]}""");
        Assert.False(AgentQuestions.Visible(question.Fields[0], new Dictionary<string, object> { ["count"] = "1" }));
        Assert.True(AgentQuestions.Visible(question.Fields[0], new Dictionary<string, object> { ["count"] = 1d }));
    }
    [Fact]
    public void MultiSelectAndCustomText_ValidateConstraints()
    {
        var question = Parse("""
            {"id":"f","fields":[{"key":"symptoms","type":"multiselect","minItems":1,"maxItems":2,"options":[{"value":"slow","label":"卡顿"},{"value":"hot","label":"过热"}]},{"key":"notes","type":"string","custom":true,"maxLength":5,"options":[{"value":"yes"}]}]}
            """);
        Assert.Null(AgentQuestions.Validate(question, new Dictionary<string, object> { ["symptoms"] = new[] { "slow", "hot" }, ["notes"] = "Other" }));
        Assert.NotNull(AgentQuestions.Validate(question, new Dictionary<string, object> { ["symptoms"] = Array.Empty<string>() }));
        Assert.NotNull(AgentQuestions.Validate(question, new Dictionary<string, object> { ["symptoms"] = new[] { "unknown" } }));
    }
    [Theory]
    [InlineData(3, true)] [InlineData(0, false)] [InlineData(9, false)] [InlineData(1.5, false)] [InlineData(double.NaN, false)]
    public void Numbers_RespectRangeAndInteger(double value, bool valid)
    {
        var question = Parse("""{"id":"f","fields":[{"key":"n","type":"integer","minimum":1,"maximum":5}]}""");
        Assert.Equal(valid, AgentQuestions.Validate(question, new Dictionary<string, object> { ["n"] = value }) is null);
    }
    [Fact]
    public void UnifiedAgent_AutoLoadsSkills_AndRequiresPluginConsent()
    {
        Assert.Equal("tuba", AgentPersona.Id);
        Assert.Contains("tuba-plugin", AgentPersona.SystemPrompt);
        Assert.Contains("tuba-toolbox", AgentPersona.SystemPrompt);
        Assert.Contains("tuba-computer-use", AgentPersona.SystemPrompt);
        Assert.Contains("必须先通过 question", AgentPersona.SystemPrompt);
        Assert.Contains("拒绝或跳过", AgentPersona.SystemPrompt);
    }
}
