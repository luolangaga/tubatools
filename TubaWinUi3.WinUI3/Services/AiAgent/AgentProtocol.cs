using System.Text.Json;

namespace TubaWinUi3.Services.AiAgent;

public sealed record AgentModel(string Id, string ProviderId, string Name, bool IsFree, double Released = 0, bool SupportsImages = false)
{
    public string Key => ProviderId + "/" + Id;
    public string DisplayName => Name + (IsFree ? " · " + AgentText.L("Free", "免费") : "") + (SupportsImages ? " · " + AgentText.L("ImageInput", "图片") : "");
    public object Reference => new { id = Id, providerID = ProviderId };
}
public sealed record AgentConversation(string Id, string Title)
{
    public override string ToString() => Title;
}
public sealed record AgentMessage(string Id, string Role, string Text, string Activity, string? Error = null,
    IReadOnlyList<AgentContent>? Content = null, bool Thinking = false, bool Completed = false, int Revision = 0,
    IReadOnlyList<AgentAttachment>? Attachments = null);
public sealed record AgentPermission(string Id, string Action, string[] Resources, string Message);
internal sealed class AgentModelFailureException(string message) : Exception(message);

internal static class AgentText
{
    public static string L(string key, string fallback) => LocalizationService.L("Agent_" + key, fallback);
    public static string FriendlyError(Exception ex) => ex is AgentModelFailureException ? ex.Message : ex is OperationCanceledException
        ? L("Cancelled", "任务已停止。")
        : L("Failed", "AI 任务未完成，请检查网络、模型可用性和 AI 组件状态后重试。");
}

internal static class AgentProtocol
{
    internal static string Text(JsonElement element, string key, string fallback = "") =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;
    internal static JsonElement Data(JsonElement root) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d) ? d : root;

    internal static IReadOnlyList<AgentModel> ParseModels(JsonElement root) => Data(root).EnumerateArray()
        .Where(m => m.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True && Text(m, "status") != "deprecated")
        .Select(m => new AgentModel(Text(m, "id"), Text(m, "providerID"), Text(m, "name"), IsFree(m),
            m.TryGetProperty("time", out var t) && t.TryGetProperty("released", out var r) && r.TryGetDouble(out var released) ? released : 0,
            m.TryGetProperty("capabilities", out var caps) && caps.TryGetProperty("input", out var inputs) && inputs.ValueKind == JsonValueKind.Array && inputs.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == "image")))
        .Where(m => m.Id.Length > 0 && m.ProviderId.Length > 0)
        .OrderByDescending(m => m.IsFree).ThenBy(m => m.Name).ToList();

    private static bool IsFree(JsonElement model) => model.TryGetProperty("cost", out var costs) &&
        costs.ValueKind == JsonValueKind.Array && costs.GetArrayLength() > 0 &&
        costs.EnumerateArray().All(c => c.TryGetProperty("input", out var i) && i.TryGetDecimal(out var input) && input == 0 &&
            c.TryGetProperty("output", out var o) && o.TryGetDecimal(out var output) && output == 0);

    internal static AgentModel? SelectDefault(IReadOnlyList<AgentModel> models, string? saved) =>
        models.FirstOrDefault(m => m.Key == saved) ?? models.Where(m => m.IsFree).OrderByDescending(m => m.Released).FirstOrDefault();

    internal static IReadOnlyList<AgentMessage> ParseMessages(JsonElement root)
    {
        var messages = new List<AgentMessage>();
        foreach (var item in Data(root).EnumerateArray())
        {
            var role = Text(item, "type");
            if (role is not ("user" or "assistant")) continue;
            var text = Text(item, "text");
            string? errorMessage = null;
            var activities = new List<string>();
            var parts = new List<AgentContent>();
            var messageId = Text(item, "id");
            var thinking = false;
            var completed = item.TryGetProperty("time", out var time) && time.TryGetProperty("completed", out var end) && end.ValueKind == JsonValueKind.Number;
            if (role == "assistant" && item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                var texts = new List<string>();
                var index = 0;
                foreach (var part in content.EnumerateArray())
                {
                    // Only expose a phase indicator, never the model's private reasoning text.
                    thinking = Text(part, "type") == "reasoning";
                    var partId = messageId + ":" + index++;
                    if (Text(part, "type") == "text")
                    {
                        var body = Text(part, "text");
                        texts.Add(body);
                        if (body.Length > 0) parts.Add(new(partId, body));
                    }
                    if (Text(part, "type") == "tool")
                    {
                        var operation = AgentOperations.Parse(part, partId);
                        parts.Add(new(partId, "", operation));
                        activities.Add(operation.Label + " · " + operation.StatusText + "\n" + operation.Target + "\n" + operation.Command + "\n" + operation.Output);
                    }
                }
                text = string.Join("\n\n", texts);
            }
            if (role == "assistant" && item.TryGetProperty("error", out var error) && error.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                errorMessage = ModelError(error);
                text += "\n\n" + errorMessage;
                parts.Add(new(messageId + ":error", errorMessage));
            }
            var attachments = new List<AgentAttachment>();
            if (role == "user" && item.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                foreach (var file in files.EnumerateArray())
                    if (file.TryGetProperty("source", out var source) && Text(source, "type") == "uri" &&
                        Uri.TryCreate(Text(source, "uri"), UriKind.Absolute, out var uri) && uri.IsFile)
                        attachments.Add(new(uri.LocalPath, Text(file, "name", Path.GetFileName(uri.LocalPath)), Text(file, "mime")));
            if (role == "user" && item.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object &&
                metadata.TryGetProperty("tubaAttachments", out var references) && references.ValueKind == JsonValueKind.Array)
                foreach (var reference in references.EnumerateArray())
                    if (Uri.TryCreate(Text(reference, "uri"), UriKind.Absolute, out var uri) && uri.IsFile &&
                        !attachments.Any(a => a.Path.Equals(uri.LocalPath, StringComparison.OrdinalIgnoreCase)))
                        attachments.Add(new(uri.LocalPath, Text(reference, "name", Path.GetFileName(uri.LocalPath)), Text(reference, "mime")));
            messages.Add(new(Text(item, "id"), role, text, string.Join("\n", activities), errorMessage, parts,
                thinking && !completed, completed, role == "user" ? HashCode.Combine(messageId, text) : item.GetRawText().GetHashCode(StringComparison.Ordinal), attachments));
        }
        return messages;
    }

    private static string ModelError(JsonElement error)
    {
        var detail = error.ToString();
        if (Text(error, "type") == "aborted" || detail.Contains("interrupted", StringComparison.OrdinalIgnoreCase))
            return AgentText.L("Cancelled", "任务已停止。");
        if (detail.Contains("429") || detail.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("rate_limit", StringComparison.OrdinalIgnoreCase) || detail.Contains("quota", StringComparison.OrdinalIgnoreCase))
            return AgentText.L("ModelBusy", "当前模型繁忙或额度已用尽，请稍后重试，或从列表选择其他模型。");
        if (detail.Contains("fetch", StringComparison.OrdinalIgnoreCase) || detail.Contains("connect", StringComparison.OrdinalIgnoreCase))
            return AgentText.L("ModelNetwork", "连接模型服务失败，请检查网络和代理设置后重试。");
        return AgentText.L("ModelError", "模型返回错误，请重试或选择其他可用模型。");
    }

    internal static string ToolLabel(string name) => name switch
    {
        _ when ComputerUseMcpProxy.IsComputerTool(name) && ComputerTool(name) == "AX" => AgentText.L("ToolAx", "界面操作"),
        _ when ComputerUseMcpProxy.IsComputerTool(name) => AgentText.L("ToolGui", "屏幕操作"),
        "bash" or "shell" => AgentText.L("ToolShell", "执行命令"),
        "read" => AgentText.L("ToolRead", "读取文件"),
        "edit" or "write" or "apply_patch" or "patch" => AgentText.L("ToolEdit", "编辑文件"),
        "question" => AgentText.L("ToolQuestion", "询问"),
        "skill" => AgentText.L("ToolSkill", "查看技能"),
        "glob" or "grep" => AgentText.L("ToolSearch", "搜索文件"),
        "webfetch" or "websearch" => AgentText.L("ToolWeb", "查询资料"),
        _ => AgentText.L("ToolTask", "执行任务")
    };

    /// <summary>The unprefixed Hybrid Computer Use tool name: AX or GUI.</summary>
    internal static string ComputerTool(string name)
    {
        var tool = name.Trim();
        var index = tool.LastIndexOfAny(['.', '_']);
        return index >= 0 && index + 1 < tool.Length ? tool[(index + 1)..] : tool;
    }

    /// <summary>A short timeline line for an AX/GUI call built from its arguments.</summary>
    internal static string ComputerTarget(string name, JsonElement input)
    {
        var description = Text(input, "description");
        if (description.Length > 0) return description;
        var tool = ComputerTool(name);
        var mode = Text(input, "mode");
        var actions = input.ValueKind == JsonValueKind.Object && input.TryGetProperty("actions", out var list) && list.ValueKind == JsonValueKind.Array
            ? SummarizeActions(list) : "";
        var parts = new List<string>(3);
        if (mode.Length > 0) parts.Add(mode);
        if (actions.Length > 0) parts.Add(actions);
        if (parts.Count == 0) parts.Add(tool);
        var text = string.Join(" · ", parts);
        return text.Length <= 120 ? text : text[..120] + "…";
    }

    private static string SummarizeActions(JsonElement actions)
    {
        var types = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var action in actions.EnumerateArray())
        {
            var type = Text(action, "type");
            if (type.Length == 0) continue;
            if (!counts.ContainsKey(type)) types.Add(type);
            counts[type] = counts.TryGetValue(type, out var count) ? count + 1 : 1;
        }
        var text = string.Join(", ", types.Select(type => counts[type] > 1 ? type + " ×" + counts[type] : type));
        return text.Length <= 90 ? text : text[..90] + "…";
    }
    private static string StateLabel(string state) => state switch
    {
        "completed" => AgentText.L("Complete", "完成"),
        "error" => AgentText.L("ToolFailed", "失败"),
        _ => AgentText.L("Running", "进行中")
    };
}
