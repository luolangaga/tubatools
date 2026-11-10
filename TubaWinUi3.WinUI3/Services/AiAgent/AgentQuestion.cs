using System.Text.Json;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services.AiAgent;

public sealed record AgentQuestionOption(string Value, string Label, string Description);
public sealed record AgentQuestionField(string Key, string Kind, string Title, string Description, bool Required,
    bool Custom, IReadOnlyList<AgentQuestionOption> Options, JsonElement Schema);
public sealed record AgentQuestion(string Id, string Title, IReadOnlyList<AgentQuestionField> Fields);

internal static class AgentQuestions
{
    internal static AgentQuestion Parse(JsonElement form)
    {
        var fields = new List<AgentQuestionField>();
        foreach (var field in form.GetProperty("fields").EnumerateArray())
        {
            var key = AgentProtocol.Text(field, "key", "external-" + fields.Count);
            var options = new List<AgentQuestionOption>();
            if (field.TryGetProperty("options", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var option in list.EnumerateArray()) options.Add(new(AgentProtocol.Text(option, "value"),
                    AgentProtocol.Text(option, "label"), AgentProtocol.Text(option, "description")));
            fields.Add(new(key, AgentProtocol.Text(field, "type", "string"), AgentProtocol.Text(field, "title", key),
                AgentProtocol.Text(field, "description"), Flag(field, "required"), Flag(field, "custom"), options, field.Clone()));
        }
        return new(AgentProtocol.Text(form, "id"), AgentProtocol.Text(form, "title"), fields);
    }
    internal static bool Flag(JsonElement schema, string key) => schema.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    internal static bool Visible(AgentQuestionField field, IReadOnlyDictionary<string, object> answers)
    {
        if (Flag(field.Schema, "hidden")) return false;
        if (!field.Schema.TryGetProperty("when", out var conditions) || conditions.ValueKind != JsonValueKind.Array) return true;
        foreach (var condition in conditions.EnumerateArray())
        {
            var equal = answers.TryGetValue(AgentProtocol.Text(condition, "key"), out var actual) &&
                JsonElement.DeepEquals(JsonSerializer.SerializeToElement(actual), condition.GetProperty("value"));
            if (AgentProtocol.Text(condition, "op") == "neq" ? equal : !equal) return false;
        }
        return true;
    }

    internal static string? Validate(AgentQuestion question, IReadOnlyDictionary<string, object> answers)
    {
        foreach (var field in question.Fields.Where(f => Visible(f, answers)))
        {
            var present = answers.TryGetValue(field.Key, out var value);
            if (field.Required && (!present || value is string text && string.IsNullOrWhiteSpace(text) || value is string[] array && array.Length == 0))
                return AgentText.L("AnswerRequired", "请回答所有必填问题。");
            if (!present) continue;
            if (field.Kind == "string" && value is not string || field.Kind == "boolean" && value is not bool ||
                field.Kind == "multiselect" && value is not string[] || field.Kind is "number" or "integer" && value is not double)
                return AgentText.L("AnswerFormat", "请按要求填写回答。");
            if (value is string s)
            {
                if (field.Options.Count > 0 && !field.Custom && !field.Options.Any(o => o.Value == s)) return AgentText.L("AnswerChoice", "请选择有效选项。");
                if (field.Schema.TryGetProperty("minLength", out var min) && s.Length < min.GetInt32() ||
                    field.Schema.TryGetProperty("maxLength", out var max) && s.Length > max.GetInt32()) return AgentText.L("AnswerLength", "回答长度不符合要求。");
                if (field.Schema.TryGetProperty("pattern", out var pattern))
                {
                    try { if (!Regex.IsMatch(s, pattern.GetString()!, RegexOptions.None, TimeSpan.FromMilliseconds(100))) return AgentText.L("AnswerFormat", "请按要求填写回答。"); }
                    catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException) { return AgentText.L("AnswerFormat", "请按要求填写回答。"); }
                }
            }
            if (value is double number && (!double.IsFinite(number) || field.Kind == "integer" && number != Math.Truncate(number) ||
                field.Schema.TryGetProperty("minimum", out var lower) && lower.ValueKind == JsonValueKind.Number && number < lower.GetDouble() ||
                field.Schema.TryGetProperty("maximum", out var upper) && upper.ValueKind == JsonValueKind.Number && number > upper.GetDouble()))
                return AgentText.L("AnswerNumber", "请输入范围内的有效数字。");
            if (value is string[] selected)
            {
                if (!field.Custom && selected.Any(v => !field.Options.Any(o => o.Value == v))) return AgentText.L("AnswerChoice", "请选择有效选项。");
                if (field.Schema.TryGetProperty("minItems", out var minItems) && selected.Length < minItems.GetInt32() ||
                    field.Schema.TryGetProperty("maxItems", out var maxItems) && selected.Length > maxItems.GetInt32())
                    return AgentText.L("AnswerSelection", "选择数量不符合要求。");
            }
        }
        return null;
    }
}
