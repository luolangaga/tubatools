using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.AiAgent;

public sealed record AgentOperation(string Id, string Name, string Kind, string Status, string Target,
    string Command, string Output, string Diff, int? ExitCode = null, string AfterMessageId = "", IReadOnlyList<AgentFileChange>? Files = null)
{
    public int? Additions => Files is { Count: > 0 } && Files.All(f => f.Additions.HasValue) ? Files.Sum(f => f.Additions!.Value) : null;
    public int? Deletions => Files is { Count: > 0 } && Files.All(f => f.Deletions.HasValue) ? Files.Sum(f => f.Deletions!.Value) : null;
    public string Label => AgentProtocol.ToolLabel(Name);
    public string StatusText => AgentText.L(Status switch
    {
        "completed" => "Complete", "error" => "ToolFailed", "cancelled" => "ToolCancelled", "pending" or "streaming" => "ToolPending", _ => "Running"
    }, Status switch { "completed" => "完成", "error" => "失败", "cancelled" => "已取消", "pending" or "streaming" => "等待执行", _ => "进行中" });
}
public sealed record AgentFileChange(string Path, int? Additions, int? Deletions, string Diff);
public sealed record AgentContent(string Id, string Text, AgentOperation? Operation = null);

internal static class AgentOperations
{
    internal static AgentOperation Parse(JsonElement part, string fallbackId)
    {
        var name = AgentProtocol.Text(part, "name");
        var state = part.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
        var input = state.ValueKind == JsonValueKind.Object && state.TryGetProperty("input", out var i) && i.ValueKind == JsonValueKind.Object ? i : default;
        var metadata = state.ValueKind == JsonValueKind.Object && state.TryGetProperty("metadata", out var m) && m.ValueKind == JsonValueKind.Object ? m : default;
        var kind = name switch
        {
            _ when ComputerUseMcpProxy.IsComputerTool(name) => "computer",
            _ when BrowserToolNames.IsBrowserTool(name) => "browser",
            "bash" or "shell" => "command", "write" or "edit" or "apply_patch" or "patch" => "edit",
            "skill" => "skill", "question" => "question", "read" => "read", "glob" or "grep" => "search", _ => "tool"
        };
        var command = AgentProtocol.Text(input, "command");
        var target = kind == "skill" ? AgentProtocol.Text(input, "id", AgentProtocol.Text(input, "name")) :
            kind == "computer" ? AgentProtocol.ComputerTarget(name, input) :
            kind == "browser" ? AgentProtocol.BrowserTarget(name, input) :
            kind == "command" ? AgentProtocol.Text(input, "description", command) :
            kind == "question" ? AgentProtocol.Text(input, "title", AgentText.L("QuestionTitle", "需要你的回答")) :
            AgentProtocol.Text(input, "filePath", AgentProtocol.Text(input, "path", AgentProtocol.Text(input, "pattern")));
        var status = AgentProtocol.Text(state, "status", "streaming");
        int? exit = null;
        foreach (var key in new[] { "exit", "exitCode", "exit_code" })
            if (metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty(key, out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var value)) { exit = value; break; }
        if (kind == "command" && exit is not null && exit != 0) status = "error";
        var output = new StringBuilder();
        if (state.ValueKind == JsonValueKind.Object && state.TryGetProperty("content", out var contents) && contents.ValueKind == JsonValueKind.Array)
            foreach (var content in contents.EnumerateArray())
                if (AgentProtocol.Text(content, "type") == "text") output.AppendLine(AgentProtocol.Text(content, "text"));
        if (state.ValueKind == JsonValueKind.Object && state.TryGetProperty("error", out var error))
        {
            output.AppendLine(AgentProtocol.Text(error, "message"));
            if (AgentProtocol.Text(error, "type") == "aborted") status = "cancelled";
        }
        var files = kind == "edit" ? ParseFiles(input, metadata, target) : null;
        if (files is { Count: > 0 } && target.Length == 0) target = files.Count == 1 ? files[0].Path : string.Format(AgentText.L("ChangedFiles", "{0} 个文件"), files.Count);
        return new(AgentProtocol.Text(part, "id", fallbackId), name, kind, status, target,
            Limit(command, 12000), OutputPreview(output.ToString().TrimEnd()), files is { Count: > 0 } ? string.Join("\n", files.Select(f => f.Diff)) : EditDiff(input, metadata), exit, Files: files);
    }

    private static IReadOnlyList<AgentFileChange>? ParseFiles(JsonElement input, JsonElement metadata, string target)
    {
        if (metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("files", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            var files = entries.EnumerateArray().Select(f =>
            {
                var patch = AgentProtocol.Text(f, "patch", AgentProtocol.Text(f, "diff"));
                var counts = CountDiff(patch);
                return new AgentFileChange(AgentProtocol.Text(f, "file", AgentProtocol.Text(f, "path", target)),
                    Number(f, "additions") ?? counts.Added, Number(f, "deletions") ?? counts.Deleted, Limit(patch, 20000));
            }).ToArray();
            if (files.Length > 0) return files;
        }
        var diff = EditDiff(input, metadata);
        var count = CountDiff(diff);
        // Plain write contents are not a diff: an unknown previous file must not be reported as a new file.
        return diff.Length == 0 ? null : [new(target, count.Added, count.Deleted, diff)];
    }
    private static int? Number(JsonElement element, string key) => element.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) && n >= 0 ? n : null;
    internal static (int? Added, int? Deleted) CountDiff(string diff)
    {
        var lines = diff.Replace("\r\n", "\n").Split('\n');
        if (!lines.Any(l => l.StartsWith("@@") || l.StartsWith("*** "))) return (null, null);
        return (lines.Count(l => l.StartsWith('+') && !l.StartsWith("+++")), lines.Count(l => l.StartsWith('-') && !l.StartsWith("---")));
    }

    private static string EditDiff(JsonElement input, JsonElement metadata)
    {
        var diff = AgentProtocol.Text(metadata, "diff", AgentProtocol.Text(input, "patchText", AgentProtocol.Text(input, "patch")));
        if (diff.Length > 0) return Limit(diff, 20000);
        var before = AgentProtocol.Text(input, "oldString", AgentProtocol.Text(input, "oldText"));
        var after = AgentProtocol.Text(input, "newString", AgentProtocol.Text(input, "newText"));
        if (metadata.ValueKind == JsonValueKind.Object && (metadata.TryGetProperty("filediff", out var fd) || metadata.TryGetProperty("fileDiff", out fd)))
        {
            before = AgentProtocol.Text(fd, "before", before);
            after = AgentProtocol.Text(fd, "after", after);
        }
        if (before.Length > 0 || after.Length > 0)
            return Limit("@@ replacement @@\n" + (before.Length > 0 ? string.Join('\n', before.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Select(l => "- " + l)) + "\n" : "") +
                (after.Length > 0 ? string.Join('\n', after.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Select(l => "+ " + l)) : ""), 20000);
        // write 的实际内容按原文展示，不伪造旧文件的逐行差异。
        return Limit(AgentProtocol.Text(input, "content"), 20000);
    }

    internal static string Limit(string text, int length) => text.Length <= length ? text : text[..length] + "\n…";
    private static string OutputPreview(string text) => text.Length <= 12000 ? text : text[..4000] + "\n…\n" + text[^8000..];
}
