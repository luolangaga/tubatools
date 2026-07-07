using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public enum AiActionKind
{
    ReadConfig,
    ModifyConfig,
    RunCommand,
    LaunchTool,
    Info
}

public sealed class AiActionStep
{
    public AiActionKind Kind { get; init; }
    public string Description { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Reason { get; init; } = "";
    public bool Confirmed { get; set; }
    public bool Executed { get; set; }
    public string? Result { get; set; }
}

public sealed record AiRecommendedTool
{
    public string Name { get; init; } = "";
    public string Reason { get; init; } = "";
    public string? ToolPath { get; init; }
    public bool IsBuiltin { get; init; }
    public string? BuiltinId { get; init; }
}

public sealed class ConversationMeta
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int MessageCount { get; set; }
}

public sealed partial class AiAssistantService
{
    private static readonly string SystemPrompt = """
你是"图吧助手"，一个 Windows 系统专家，拥有联网搜索能力。

---

## ⚠️ 最重要的规则：主动搜索

你拥有联网搜索工具 web_search，这是你最大的优势。以下情况**必须**使用 web_search：
- 用户询问任何硬件信息（CPU、GPU、内存、硬盘型号、性能、评测、对比、跑分）
- 用户询问驱动、BIOS、固件更新
- 用户询问软件版本、新功能、兼容性
- 用户询问价格、购买建议、性价比
- 用户询问技术新闻、行业动态
- 用户的任何问题涉及到你的知识截止日期之后的信息
- 你不确定某个具体参数或数据时

搜索策略：
- 搜索关键词用中文+英文混合效果最好，例如 "Intel Core Ultra 9 285K 评测 性能"
- 如果第一次搜索结果不够，换一组关键词再搜一次
- 可以同时调用多个工具（如先 get_hardware_info 再 web_search 同类产品对比）
- 永远不要凭记忆回答硬件参数，必须搜索确认
- 搜索结果只有摘要，如果需要详细信息（如完整评测、具体参数、价格），用 fetch_page 访问相关网页获取全文

---

## 输出规范

信息收集完成后，输出结构化的分析和方案。

格式要求（严格遵守）：

### 分析结果
简要总结发现的问题或现状

### 解决方案
按步骤列出操作，每步包含：
1. 步骤说明（用加粗标明关键操作）
2. 对应的工具推荐（每个工具单独一行用 [RECOMMEND_TOOL] 标记）
3. 相关网站（用 [WEBSITE] 标记）
4. 需要修改的设置（用 [SETTING] 标记）

### 注意事项
列出需要注意的风险点

---

## 标记格式

**推荐工具**（每个独占一行）：
[RECOMMEND_TOOL] 工具名 | reason=一句话理由

**推荐网站**（每个独占一行）：
[WEBSITE] URL | desc=网站名

**建议修改设置**（每个独占一行）：
[SETTING] path=注册表路径 | name=设置名 | current=当前值 | recommend=建议值 | reason=理由

---

## 关键规则

1. 推荐工具优先从工具箱已有软件中选
2. [RECOMMEND_TOOL] 必须独占一行，不要和其他文字混在同一行
3. 每个操作必须写清楚理由
4. 用中文回复
5. 方案要具体可执行，不要模糊的建议
6. 不要在 [RECOMMEND_TOOL] 同一行写标题或列表符号
7. 涉及硬件参数、性能对比、新品发布、驱动更新等，必须用 web_search 搜索，不要凭记忆回答
8. 宁可多搜一次，也不要给出过时或错误的信息
""";

    private static readonly List<AiToolDefinition> ToolDefinitions = BuildToolDefinitions();

    private static List<AiToolDefinition> BuildToolDefinitions()
    {
        return
        [
            new AiToolDefinition
            {
                Name = "web_search",
                Description = "联网搜索！获取最新硬件评测、驱动、新闻、价格等（最常用的工具，涉及任何最新信息时必须使用！）",
                ParametersJson = """{"type":"object","properties":{"query":{"type":"string","description":"搜索关键词"}},"required":["query"]}"""
            },
            new AiToolDefinition
            {
                Name = "fetch_page",
                Description = "访问网页内容！当搜索结果中的摘要信息不够详细时，用此工具获取完整网页文本",
                ParametersJson = """{"type":"object","properties":{"url":{"type":"string","description":"网页URL"}},"required":["url"]}"""
            },
            new AiToolDefinition
            {
                Name = "get_hardware_info",
                Description = "获取本机硬件信息（CPU、GPU、内存、主板等）",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            new AiToolDefinition
            {
                Name = "get_system_info",
                Description = "获取系统基本信息（OS、用户名、磁盘使用等）",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            new AiToolDefinition
            {
                Name = "list_programs",
                Description = "获取已安装软件列表",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            new AiToolDefinition
            {
                Name = "disk_usage",
                Description = "获取磁盘使用概况",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            new AiToolDefinition
            {
                Name = "network_info",
                Description = "获取网络信息（网卡、IP等）",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            new AiToolDefinition
            {
                Name = "list_processes",
                Description = "获取进程列表（按内存排序前50）",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            new AiToolDefinition
            {
                Name = "list_startup",
                Description = "获取启动项列表",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            new AiToolDefinition
            {
                Name = "list_services",
                Description = "获取服务列表",
                ParametersJson = """{"type":"object","properties":{"filter":{"type":"string","description":"筛选关键词"}},"required":[]}"""
            },
            new AiToolDefinition
            {
                Name = "list_dir",
                Description = "列出目录内容",
                ParametersJson = """{"type":"object","properties":{"path":{"type":"string","description":"目录路径"}},"required":["path"]}"""
            },
            new AiToolDefinition
            {
                Name = "get_info",
                Description = "获取文件或文件夹信息",
                ParametersJson = """{"type":"object","properties":{"path":{"type":"string","description":"文件或文件夹路径"}},"required":["path"]}"""
            },
            new AiToolDefinition
            {
                Name = "list_tools",
                Description = "获取工具箱软件列表",
                ParametersJson = """{"type":"object","properties":{"category":{"type":"string","description":"分类名称"}},"required":[]}"""
            },
            new AiToolDefinition
            {
                Name = "read_reg",
                Description = "读取注册表值",
                ParametersJson = """{"type":"object","properties":{"key":{"type":"string","description":"注册表键路径"},"value":{"type":"string","description":"值名称（可选，不填则列出所有值）"}},"required":["key"]}"""
            },
            new AiToolDefinition
            {
                Name = "run_command",
                Description = "执行命令（需要用户确认后才会执行）",
                ParametersJson = """{"type":"object","properties":{"cmd":{"type":"string","description":"要执行的命令"},"reason":{"type":"string","description":"执行此命令的理由和预期效果"}},"required":["cmd","reason"]}"""
            },
            new AiToolDefinition
            {
                Name = "write_reg",
                Description = "修改注册表（需要用户确认后才会执行）",
                ParametersJson = """{"type":"object","properties":{"key":{"type":"string","description":"注册表键路径"},"value":{"type":"string","description":"值名称"},"data":{"type":"string","description":"要写入的数据"},"type":{"type":"string","description":"值类型：REG_SZ(默认)、REG_DWORD、REG_QWORD、REG_EXPAND_SZ、REG_BINARY"},"reason":{"type":"string","description":"修改理由"}},"required":["key","value","data","reason"]}"""
            },
        ];
    }

    private static readonly HashSet<string> DangerousTools = ["run_command", "write_reg"];

    public static string BuildSystemContext()
    {
        var sb = new StringBuilder();

        sb.AppendLine("## 当前工具箱可用软件");
        sb.AppendLine();

        try
        {
            var categories = ToolCatalog.GetCategories();
            foreach (var cat in categories)
            {
                var tools = ToolCatalog.GetTools(cat);
                if (tools.Count == 0) continue;
                sb.AppendLine($"### {cat}");
                foreach (var tool in tools)
                {
                    var desc = string.IsNullOrWhiteSpace(tool.Description) ? "" : $" — {tool.Description}";
                    sb.AppendLine($"- {tool.Name}{desc}");
                }
                sb.AppendLine();
            }
        }
        catch { sb.AppendLine("(无法获取工具列表)"); }

        sb.AppendLine("## 内置工具");
        try
        {
            foreach (var tool in BuiltinToolRegistry.Tools)
            {
                sb.AppendLine($"- {tool.Name}：{tool.Description}");
            }
        }
        catch { }

        return sb.ToString();
    }

    public static string BuildSystemInfoContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 系统基本信息");
        sb.AppendLine($"操作系统：{Environment.OSVersion.VersionString}");
        sb.AppendLine($"用户名：{Environment.UserName}");
        sb.AppendLine($"用户目录：{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}");
        sb.AppendLine($"处理器核心数：{Environment.ProcessorCount}");
        sb.AppendLine($"系统架构：{(Environment.Is64BitOperatingSystem ? "64位" : "32位")}");
        sb.AppendLine($".NET 版本：{Environment.Version}");
        sb.AppendLine();

        sb.AppendLine("磁盘使用概况：");
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var used = drive.TotalSize - drive.AvailableFreeSpace;
                var pct = (double)used / drive.TotalSize * 100;
                sb.AppendLine($"  {drive.RootDirectory.FullName} 总共 {FormatSize(drive.TotalSize)}，已用 {FormatSize(used)} ({pct:F1}%)，可用 {FormatSize(drive.AvailableFreeSpace)}");
            }
        }
        catch { }

        return sb.ToString();
    }

    public static async Task ProcessUserMessageStreamAsync(
        string userMessage,
        List<AiChatMessage> conversationHistory,
        Action<string> onTextChunk,
        Action<string> onToolCall,
        Action<string> onToolResult,
        Action<List<AiActionStep>> onActions,
        Action<List<AiRecommendedTool>> onToolRecommendations,
        Action<string> onError,
        CancellationToken ct)
    {
        if (conversationHistory.Count == 0 ||
            conversationHistory[0].Role != "system")
        {
            var systemContent = SystemPrompt + "\n\n" + BuildSystemContext() + "\n\n" + BuildSystemInfoContext();
            conversationHistory.Insert(0, AiChatMessage.System(systemContent));
        }

        conversationHistory.Add(AiChatMessage.User(userMessage));

        await RunAgentLoop(conversationHistory, onTextChunk, onToolCall, onToolResult, onActions, onToolRecommendations, onError, ct, maxRounds: 30);
    }

    public static async Task ContinueConversationStreamAsync(
        List<AiChatMessage> conversationHistory,
        Action<string> onTextChunk,
        Action<string> onToolCall,
        Action<string> onToolResult,
        Action<List<AiActionStep>> onActions,
        Action<List<AiRecommendedTool>> onToolRecommendations,
        Action<string> onError,
        CancellationToken ct)
    {
        if (conversationHistory.Count == 0 ||
            conversationHistory[0].Role != "system")
        {
            var systemContent = SystemPrompt + "\n\n" + BuildSystemContext() + "\n\n" + BuildSystemInfoContext();
            conversationHistory.Insert(0, AiChatMessage.System(systemContent));
        }

        await RunAgentLoop(conversationHistory, onTextChunk, onToolCall, onToolResult, onActions, onToolRecommendations, onError, ct, maxRounds: 10);
    }

    private static async Task RunAgentLoop(
        List<AiChatMessage> conversationHistory,
        Action<string> onTextChunk,
        Action<string> onToolCall,
        Action<string> onToolResult,
        Action<List<AiActionStep>> onActions,
        Action<List<AiRecommendedTool>> onToolRecommendations,
        Action<string> onError,
        CancellationToken ct,
        int maxRounds)
    {
        for (int round = 0; round < maxRounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            var fullContent = new StringBuilder();
            var toolCallsAccum = new Dictionary<int, (string Id, StringBuilder Name, StringBuilder Args)>();
            string? streamError = null;

            await AiService.ChatStreamAsync(
                conversationHistory,
                onChunk: chunk =>
                {
                    fullContent.Append(chunk);
                    onTextChunk(chunk);
                },
                onError: err => streamError = err,
                ct: ct,
                temperature: 0.4,
                tools: ToolDefinitions,
                onToolCallDelta: (index, id, nameDelta, argsDelta) =>
                {
                    if (!toolCallsAccum.ContainsKey(index))
                        toolCallsAccum[index] = ("", new StringBuilder(), new StringBuilder());
                    var entry = toolCallsAccum[index];
                    if (!string.IsNullOrEmpty(id)) entry.Id = id;
                    if (!string.IsNullOrEmpty(nameDelta)) entry.Name.Append(nameDelta);
                    if (!string.IsNullOrEmpty(argsDelta)) entry.Args.Append(argsDelta);
                    toolCallsAccum[index] = entry;
                });

            if (streamError is not null)
            {
                onError(streamError);
                return;
            }

            var content = fullContent.ToString();
            var toolCalls = toolCallsAccum.OrderBy(kv => kv.Key)
                .Select(kv => new AiToolCallItem
                {
                    Id = kv.Value.Id,
                    Name = kv.Value.Name.ToString(),
                    Arguments = kv.Value.Args.ToString()
                })
                .Where(tc => !string.IsNullOrEmpty(tc.Name))
                .ToList();

            conversationHistory.Add(AiChatMessage.Assistant(content, toolCalls.Count > 0 ? toolCalls : null));

            var recommendations = ParseRecommendations(content);
            if (recommendations.Count > 0)
                onToolRecommendations(recommendations);

            var parsedActions = ParseActions(content);
            if (parsedActions.Count > 0)
            {
                onActions(parsedActions);
                return;
            }

            if (toolCalls.Count == 0)
                return;

            var pendingActions = new List<AiActionStep>();
            var toolResultsToSend = new List<AiChatMessage>();

            foreach (var toolCall in toolCalls)
            {
                var toolName = toolCall.Name;
                var toolArgs = toolCall.Arguments;

                if (DangerousTools.Contains(toolName))
                {
                    var kind = toolName == "run_command" ? AiActionKind.RunCommand : AiActionKind.ModifyConfig;
                    var argsDict = ParseJsonArgs(toolArgs);
                    var detail = toolName == "run_command"
                        ? (argsDict.TryGetValue("cmd", out var c) ? c : toolArgs)
                        : toolArgs;
                    var reason = argsDict.TryGetValue("reason", out var r) ? r : "AI 请求执行此操作";
                    var desc = toolName == "run_command"
                        ? $"执行命令: {detail}"
                        : $"修改注册表: {(argsDict.TryGetValue("key", out var k) ? k : "")}";

                    pendingActions.Add(new AiActionStep
                    {
                        Kind = kind,
                        Description = desc,
                        Detail = detail,
                        Reason = reason,
                    });

                    onToolCall($"{toolName} ⚠️ 需确认 | {toolArgs}");

                    toolResultsToSend.Add(AiChatMessage.Tool(
                        toolCall.Id,
                        "等待用户确认后执行",
                        toolName));
                }
                else
                {
                    onToolCall($"{toolName} {(string.IsNullOrWhiteSpace(toolArgs) ? "" : $"| {toolArgs}")}");

                    var toolArgsStr = ConvertJsonArgsToPipeFormat(toolName, toolArgs);
                    var toolResult = await ExecuteToolByNameAsync(toolName, toolArgsStr, ct);

                    onToolResult(toolResult);

                    toolResultsToSend.Add(AiChatMessage.Tool(
                        toolCall.Id,
                        toolResult,
                        toolName));
                }
            }

            conversationHistory.AddRange(toolResultsToSend);

            if (pendingActions.Count > 0)
            {
                onActions(pendingActions);
                return;
            }
        }

        onError("对话轮次已达上限，请简化你的问题。");
    }

    private static Dictionary<string, string> ParseJsonArgs(string jsonArgs)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(jsonArgs)) return result;
        try
        {
            using var doc = JsonDocument.Parse(jsonArgs);
            foreach (var prop in doc.RootElement.EnumerateObject())
                result[prop.Name] = prop.Value.GetString() ?? "";
        }
        catch { }
        return result;
    }

    private static string ConvertJsonArgsToPipeFormat(string toolName, string jsonArgs)
    {
        var dict = ParseJsonArgs(jsonArgs);
        if (dict.Count == 0) return jsonArgs;
        return string.Join(" | ", dict.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private static async Task<string> ExecuteToolByNameAsync(string toolName, string args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return toolName switch
        {
            "get_hardware_info" => await Task.Run(ExecuteGetHardwareInfo, ct),
            "get_system_info" => BuildSystemInfoContext(),
            "list_programs" => await Task.Run(ExecuteListPrograms, ct),
            "disk_usage" => ExecuteDiskUsage(),
            "network_info" => await Task.Run(ExecuteNetworkInfo, ct),
            "list_processes" => await Task.Run(ExecuteListProcesses, ct),
            "list_startup" => ExecuteListStartup(),
            "list_dir" => await Task.Run(() => ExecuteListDir(args), ct),
            "get_info" => ExecuteGetInfo(args),
            "list_tools" => ExecuteListTools(args),
            "read_reg" => ExecuteReadReg(args),
            "list_services" => await Task.Run(() => ExecuteListServices(args), ct),
            "web_search" => await ExecuteWebSearchAsync(args, ct),
            "fetch_page" => await ExecuteFetchPageAsync(args, ct),
            _ => $"错误：未知工具 '{toolName}'"
        };
    }

    private static async Task<string> ExecuteWebSearchAsync(string args, CancellationToken ct)
    {
        var query = ParseArg(args, "query");
        if (string.IsNullOrWhiteSpace(query))
            return "错误：缺少 query 参数，请提供搜索关键词";

        try
        {
            var result = await WebSearchService.SearchAsync(query, ct);
            return WebSearchService.FormatResult(result);
        }
        catch (OperationCanceledException)
        {
            return "搜索已取消";
        }
        catch (Exception ex)
        {
            return $"搜索失败：{ex.Message}";
        }
    }

    private static async Task<string> ExecuteFetchPageAsync(string args, CancellationToken ct)
    {
        var url = ParseArg(args, "url");
        if (string.IsNullOrWhiteSpace(url))
            return "错误：缺少 url 参数，请提供要访问的网页 URL";

        try
        {
            var page = await WebSearchService.FetchWebPageAsync(url, ct);
            var sb = new StringBuilder();
            sb.AppendLine($"页面标题：{page.Title}");
            sb.AppendLine($"URL：{page.Url}");
            sb.AppendLine($"内容格式：{page.ContentType}");
            sb.AppendLine();
            sb.AppendLine(page.Content);
            return sb.ToString();
        }
        catch (OperationCanceledException)
        {
            return "页面获取已取消";
        }
        catch (Exception ex)
        {
            return $"获取页面失败：{ex.Message}";
        }
    }

    public static async Task<string> ExecuteActionAsync(AiActionStep action, CancellationToken ct)
    {
        return action.Kind switch
        {
            AiActionKind.RunCommand => ExecuteRunCommand(action.Detail, ct),
            AiActionKind.ModifyConfig => ExecuteWriteReg(ConvertJsonArgsToPipeFormat("write_reg", action.Detail), ct),
            AiActionKind.LaunchTool => ExecuteLaunchTool(action.Detail),
            AiActionKind.ReadConfig => ExecuteReadReg(action.Detail),
            _ => "不支持的操作类型"
        };
    }

    private static string HistoryDir => Path.Combine(ConfigManager.GetDataDir(), "AiAssistant");

    public static void SaveConversation(string id, string title, List<AiChatMessage> messages)
    {
        try
        {
            Directory.CreateDirectory(HistoryDir);
            var meta = new ConversationMeta
            {
                Id = id,
                Title = title,
                CreatedAt = DateTime.Now,
                MessageCount = messages.Count
            };

            var metaPath = Path.Combine(HistoryDir, $"{id}.meta.json");
            File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, TubaDefaultIndentedContext.Default.ConversationMeta));

            var msgPath = Path.Combine(HistoryDir, $"{id}.messages.json");
            File.WriteAllText(msgPath, JsonSerializer.Serialize(messages, TubaDefaultIndentedContext.Default.ListAiChatMessage));
        }
        catch { }
    }

    public static List<ConversationMeta> ListConversations()
    {
        var result = new List<ConversationMeta>();
        try
        {
            Directory.CreateDirectory(HistoryDir);
            foreach (var file in Directory.GetFiles(HistoryDir, "*.meta.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var meta = JsonSerializer.Deserialize(json, TubaDefaultContext.Default.ConversationMeta);
                    if (meta is not null) result.Add(meta);
                }
                catch { }
            }
        }
        catch { }
        return result.OrderByDescending(m => m.CreatedAt).ToList();
    }

    public static List<AiChatMessage> LoadConversation(string id)
    {
        try
        {
            var msgPath = Path.Combine(HistoryDir, $"{id}.messages.json");
            if (!File.Exists(msgPath)) return [];
            var json = File.ReadAllText(msgPath);
            return JsonSerializer.Deserialize(json, TubaDefaultContext.Default.ListAiChatMessage) ?? [];
        }
        catch { return []; }
    }

    public static void DeleteConversation(string id)
    {
        try
        {
            var metaPath = Path.Combine(HistoryDir, $"{id}.meta.json");
            var msgPath = Path.Combine(HistoryDir, $"{id}.messages.json");
            if (File.Exists(metaPath)) File.Delete(metaPath);
            if (File.Exists(msgPath)) File.Delete(msgPath);
        }
        catch { }
    }



    public static bool TryLaunchTool(string toolName, out string message)
    {
        message = "";
        try
        {
            var allTools = ToolCatalog.GetAllToolsCached();
            var tool = allTools.FirstOrDefault(t =>
                t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains(toolName, StringComparison.OrdinalIgnoreCase));

            if (tool is not null)
            {
                Process.Start(new ProcessStartInfo(tool.EffectivePath) { UseShellExecute = true });
                message = $"已启动：{tool.Name}";
                return true;
            }

            var builtin = BuiltinToolRegistry.Tools.FirstOrDefault(t =>
                t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains(toolName, StringComparison.OrdinalIgnoreCase));

            if (builtin is not null)
            {
                message = $"内置工具 '{builtin.Name}' 需要在主界面中启动";
                return false;
            }

            message = $"未找到工具：{toolName}";
            return false;
        }
        catch (Exception ex)
        {
            message = $"启动失败：{ex.Message}";
            return false;
        }
    }

    public static List<AiRecommendedTool> ResolveRecommendations(List<AiRecommendedTool> recommendations)
    {
        var allTools = ToolCatalog.GetAllToolsCached();
        var builtins = BuiltinToolRegistry.Tools;

        foreach (var rec in recommendations)
        {
            var extTool = allTools.FirstOrDefault(t =>
                t.Name.Equals(rec.Name, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains(rec.Name, StringComparison.OrdinalIgnoreCase));

            if (extTool is not null)
            {
                var updated = rec with { ToolPath = extTool.EffectivePath, IsBuiltin = false };
                recommendations[recommendations.IndexOf(rec)] = updated;
                continue;
            }

            var builtin = builtins.FirstOrDefault(t =>
                t.Name.Equals(rec.Name, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains(rec.Name, StringComparison.OrdinalIgnoreCase));

            if (builtin is not null)
            {
                var updated = rec with { BuiltinId = builtin.Id, IsBuiltin = true };
                recommendations[recommendations.IndexOf(rec)] = updated;
            }
        }

        return recommendations;
    }

    private static List<AiRecommendedTool> ParseRecommendations(string content)
    {
        var result = new List<AiRecommendedTool>();

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("[RECOMMEND_TOOL]", StringComparison.OrdinalIgnoreCase)) continue;

            var after = trimmed.Substring("[RECOMMEND_TOOL]".Length).Trim();
            var pipeIdx = after.IndexOf('|');
            string name, reason;

            if (pipeIdx >= 0)
            {
                name = after.Substring(0, pipeIdx).Trim();
                var rest = after.Substring(pipeIdx + 1).Trim();
                reason = ParseArg(rest, "reason");
                if (string.IsNullOrWhiteSpace(reason)) reason = rest;
            }
            else
            {
                name = after.Trim();
                reason = "";
            }

            if (!string.IsNullOrWhiteSpace(name))
                result.Add(new AiRecommendedTool { Name = name, Reason = reason });
        }

        return result;
    }

    private static List<AiActionStep> ParseActions(string content)
    {
        var result = new List<AiActionStep>();
        var idx = content.IndexOf("[ACTION]", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return result;

        var afterAction = content.Substring(idx + "[ACTION]".Length);
        var jsonStart = afterAction.IndexOf('[');
        var jsonEnd = afterAction.LastIndexOf(']');
        if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart) return result;

        var json = afterAction.Substring(jsonStart, jsonEnd - jsonStart + 1);

        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                var kindStr = elem.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
                var kind = kindStr switch
                {
                    "run_command" => AiActionKind.RunCommand,
                    "write_reg" => AiActionKind.ModifyConfig,
                    "modify_config" => AiActionKind.ModifyConfig,
                    "launch_tool" => AiActionKind.LaunchTool,
                    "read_config" => AiActionKind.ReadConfig,
                    "read_reg" => AiActionKind.ReadConfig,
                    _ => AiActionKind.Info
                };

                result.Add(new AiActionStep
                {
                    Kind = kind,
                    Description = elem.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    Detail = elem.TryGetProperty("detail", out var dt) ? dt.GetString() ?? "" :
                            elem.TryGetProperty("cmd", out var cmd) ? cmd.GetString() ?? "" : "",
                    Reason = elem.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "",
                });
            }
        }
        catch { }

        return result;
    }

    private static string ExecuteGetHardwareInfo()
    {
        try
        {
            var sections = HardwareInfoService.LoadAsync(forceRefresh: false).GetAwaiter().GetResult();
            var sb = new StringBuilder();
            foreach (var section in sections)
            {
                sb.AppendLine($"### {section.Title}");
                foreach (var item in section.Items)
                {
                    sb.AppendLine($"- {item.Label}：{item.Value}");
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"获取硬件信息失败：{ex.Message}";
        }
    }

    private static string ExecuteListPrograms()
    {
        var sb = new StringBuilder();
        sb.AppendLine("已安装软件列表：");
        sb.AppendLine();

        try
        {
            var regPaths = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var regPath in regPaths)
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath);
                if (key is null) continue;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey is null) continue;

                    var name = subKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (seen.Contains(name)) continue;
                    seen.Add(name);

                    var version = subKey.GetValue("DisplayVersion") as string;
                    var publisher = subKey.GetValue("Publisher") as string;
                    var line = $"- {name}";
                    if (!string.IsNullOrEmpty(version)) line += $" (v{version})";
                    if (!string.IsNullOrEmpty(publisher)) line += $" [{publisher}]";
                    sb.AppendLine(line);
                }
            }

            using var userKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regPaths[0]);
            if (userKey is not null)
            {
                foreach (var subKeyName in userKey.GetSubKeyNames())
                {
                    using var subKey = userKey.OpenSubKey(subKeyName);
                    if (subKey is null) continue;

                    var name = subKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (seen.Contains(name)) continue;
                    seen.Add(name);

                    var version = subKey.GetValue("DisplayVersion") as string;
                    var publisher = subKey.GetValue("Publisher") as string;
                    var line = $"- {name}";
                    if (!string.IsNullOrEmpty(version)) line += $" (v{version})";
                    if (!string.IsNullOrEmpty(publisher)) line += $" [{publisher}]";
                    sb.AppendLine(line);
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static string ExecuteDiskUsage()
    {
        var sb = new StringBuilder();
        sb.AppendLine("磁盘使用概况：");

        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var used = drive.TotalSize - drive.AvailableFreeSpace;
                var pct = (double)used / drive.TotalSize * 100;
                sb.AppendLine($"  {drive.RootDirectory.FullName} 总共 {FormatSize(drive.TotalSize)}，已用 {FormatSize(used)} ({pct:F1}%)，可用 {FormatSize(drive.AvailableFreeSpace)}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static string ExecuteNetworkInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine("网络信息：");

        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;

                sb.AppendLine($"- {ni.Name} ({ni.NetworkInterfaceType})");
                sb.AppendLine($"  状态：{ni.OperationalStatus}");
                sb.AppendLine($"  速度：{ni.Speed / 1_000_000} Mbps");
                var ipProps = ni.GetIPProperties();
                foreach (var addr in ipProps.UnicastAddresses)
                {
                    sb.AppendLine($"  IP：{addr.Address}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"获取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static string ExecuteListProcesses()
    {
        var sb = new StringBuilder();
        sb.AppendLine("运行中进程（按内存排序前 50）：");
        sb.AppendLine();

        try
        {
            var procs = Process.GetProcesses()
                .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } })
                .Take(50);

            foreach (var p in procs)
            {
                try
                {
                    var mem = FormatSize(p.WorkingSet64);
                    sb.AppendLine($"- {p.ProcessName} (PID: {p.Id}) 内存: {mem}");
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"获取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static string ExecuteListStartup()
    {
        var sb = new StringBuilder();
        sb.AppendLine("启动项列表：");
        sb.AppendLine();

        try
        {
            var regPaths = new[]
            {
                (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                (Microsoft.Win32.Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            };

            foreach (var (hive, path) in regPaths)
            {
                using var key = hive.OpenSubKey(path);
                if (key is null) continue;

                sb.AppendLine($"[{hive.Name}\\{path}]");
                foreach (var name in key.GetValueNames())
                {
                    var val = key.GetValue(name) as string ?? "";
                    sb.AppendLine($"  {name} = {val}");
                }
                sb.AppendLine();
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static string ExecuteListDir(string args)
    {
        var path = ParseArg(args, "path");
        if (string.IsNullOrWhiteSpace(path))
            return "错误：缺少 path 参数";

        if (!Directory.Exists(path))
            return $"错误：目录 '{path}' 不存在";

        var sb = new StringBuilder();
        sb.AppendLine($"目录内容：{path}");
        sb.AppendLine();

        try
        {
            var count = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                RecurseSubdirectories = false
            }))
            {
                if (count >= 200)
                {
                    sb.AppendLine("... (超过 200 项，已截断)");
                    break;
                }

                try
                {
                    if (Directory.Exists(entry))
                    {
                        var di = new DirectoryInfo(entry);
                        sb.AppendLine($"[目录] {di.Name}  修改: {di.LastWriteTime:yyyy-MM-dd}");
                    }
                    else
                    {
                        var fi = new FileInfo(entry);
                        sb.AppendLine($"[文件] {fi.Name}  大小: {FormatSize(fi.Length)}  修改: {fi.LastWriteTime:yyyy-MM-dd}");
                    }
                }
                catch
                {
                    sb.AppendLine($"[未知] {Path.GetFileName(entry)}");
                }
                count++;
            }

            if (count == 0) sb.AppendLine("(空目录)");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static string ExecuteGetInfo(string args)
    {
        var path = ParseArg(args, "path");
        if (string.IsNullOrWhiteSpace(path))
            return "错误：缺少 path 参数";

        var sb = new StringBuilder();

        try
        {
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                sb.AppendLine($"类型：目录");
                sb.AppendLine($"路径：{di.FullName}");
                sb.AppendLine($"创建时间：{di.CreationTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"修改时间：{di.LastWriteTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"属性：{di.Attributes}");
            }
            else if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                sb.AppendLine($"类型：文件");
                sb.AppendLine($"路径：{fi.FullName}");
                sb.AppendLine($"大小：{FormatSize(fi.Length)}");
                sb.AppendLine($"创建时间：{fi.CreationTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"修改时间：{fi.LastWriteTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"属性：{fi.Attributes}");
            }
            else
            {
                sb.AppendLine($"路径 '{path}' 不存在");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"获取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static string ExecuteListTools(string args)
    {
        var category = ParseArg(args, "category");
        var sb = new StringBuilder();

        try
        {
            if (!string.IsNullOrWhiteSpace(category))
            {
                var tools = ToolCatalog.GetTools(category);
                sb.AppendLine($"分类 '{category}' 下的工具：");
                foreach (var tool in tools)
                {
                    var desc = string.IsNullOrWhiteSpace(tool.Description) ? "" : $" — {tool.Description}";
                    sb.AppendLine($"- {tool.Name}{desc}");
                }
            }
            else
            {
                var categories = ToolCatalog.GetCategories();
                foreach (var cat in categories)
                {
                    var tools = ToolCatalog.GetTools(cat);
                    if (tools.Count == 0) continue;
                    sb.AppendLine($"### {cat}");
                    foreach (var tool in tools)
                    {
                        var desc = string.IsNullOrWhiteSpace(tool.Description) ? "" : $" — {tool.Description}";
                        sb.AppendLine($"- {tool.Name}{desc}");
                    }
                    sb.AppendLine();
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"获取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static string ExecuteReadReg(string args)
    {
        var keyPath = ParseArg(args, "key");
        var valueName = ParseArg(args, "value");

        if (string.IsNullOrWhiteSpace(keyPath))
            return "错误：缺少 key 参数";

        var sb = new StringBuilder();

        try
        {
            var (hive, subPath) = ParseRegKey(keyPath);
            using var key = hive.OpenSubKey(subPath);
            if (key is null)
            {
                sb.AppendLine($"注册表键 '{keyPath}' 不存在");
                return sb.ToString();
            }

            if (!string.IsNullOrWhiteSpace(valueName))
            {
                var val = key.GetValue(valueName);
                if (val is null)
                {
                    sb.AppendLine($"值 '{valueName}' 不存在");
                }
                else
                {
                    sb.AppendLine($"{valueName} = {FormatRegValue(val)} (类型: {key.GetValueKind(valueName)})");
                }
            }
            else
            {
                sb.AppendLine($"注册表键：{keyPath}");
                sb.AppendLine("值列表：");
                foreach (var name in key.GetValueNames())
                {
                    var val = key.GetValue(name);
                    sb.AppendLine($"  {(string.IsNullOrEmpty(name) ? "(默认)" : name)} = {FormatRegValue(val ?? "")}");
                }
                sb.AppendLine("子键：");
                foreach (var sub in key.GetSubKeyNames())
                {
                    sb.AppendLine($"  {sub}");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static string ExecuteWriteReg(string args, CancellationToken ct)
    {
        var keyPath = ParseArg(args, "key");
        var valueName = ParseArg(args, "value");
        var data = ParseArg(args, "data");
        var type = ParseArg(args, "type");

        if (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(valueName))
            return "错误：缺少 key 或 value 参数";

        try
        {
            var (hive, subPath) = ParseRegKey(keyPath);
            using var key = hive.CreateSubKey(subPath, true);

            if (string.Equals(type, "REG_DWORD", StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(valueName, int.Parse(data), Microsoft.Win32.RegistryValueKind.DWord);
            }
            else if (string.Equals(type, "REG_QWORD", StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(valueName, long.Parse(data), Microsoft.Win32.RegistryValueKind.QWord);
            }
            else if (string.Equals(type, "REG_EXPAND_SZ", StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(valueName, data, Microsoft.Win32.RegistryValueKind.ExpandString);
            }
            else if (string.Equals(type, "REG_BINARY", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = Convert.FromHexString(data.Replace(" ", ""));
                key.SetValue(valueName, bytes, Microsoft.Win32.RegistryValueKind.Binary);
            }
            else
            {
                key.SetValue(valueName, data, Microsoft.Win32.RegistryValueKind.String);
            }

            return $"成功：已设置 {keyPath}\\{valueName} = {data}";
        }
        catch (Exception ex)
        {
            return $"修改失败：{ex.Message}";
        }
    }

    private static string ExecuteRunCommand(string cmd, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {cmd}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc is null) return "无法启动进程";

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(30000);

            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(stdout))
                sb.AppendLine(stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr))
                sb.AppendLine($"[stderr] {stderr.Trim()}");
            sb.AppendLine($"退出码：{proc.ExitCode}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"执行失败：{ex.Message}";
        }
    }

    private static string ExecuteLaunchTool(string toolName)
    {
        try
        {
            var allTools = ToolCatalog.GetAllToolsCached();
            var tool = allTools.FirstOrDefault(t =>
                t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains(toolName, StringComparison.OrdinalIgnoreCase));

            if (tool is not null)
            {
                Process.Start(new ProcessStartInfo(tool.EffectivePath) { UseShellExecute = true });
                return $"已启动工具：{tool.Name}";
            }

            var builtin = BuiltinToolRegistry.GetById(toolName);
            if (builtin is not null)
            {
                return $"内置工具 '{builtin.Name}' 需要在界面中手动启动";
            }

            return $"未找到工具：{toolName}";
        }
        catch (Exception ex)
        {
            return $"启动失败：{ex.Message}";
        }
    }

    private static string ExecuteListServices(string args)
    {
        var filter = ParseArg(args, "filter");
        var sb = new StringBuilder();
        sb.AppendLine("系统服务列表：");
        sb.AppendLine();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc",
                Arguments = "query state= all",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var proc = Process.Start(psi);
            if (proc is null) return "无法获取服务列表";

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);

            var lines = output.Split('\n');
            var serviceName = "";
            var displayName = "";
            var state = "";
            var count = 0;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
                    serviceName = trimmed.Substring("SERVICE_NAME:".Length).Trim();
                else if (trimmed.StartsWith("DISPLAY_NAME:", StringComparison.OrdinalIgnoreCase))
                    displayName = trimmed.Substring("DISPLAY_NAME:".Length).Trim();
                else if (trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
                {
                    if (trimmed.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                        state = "运行中";
                    else if (trimmed.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
                        state = "已停止";
                    else
                        state = trimmed;
                }
                else if (string.IsNullOrEmpty(trimmed) && !string.IsNullOrEmpty(serviceName))
                {
                    if (string.IsNullOrWhiteSpace(filter) ||
                        serviceName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        displayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"- {displayName} ({serviceName}) — {state}");
                        count++;
                        if (count >= 80)
                        {
                            sb.AppendLine("... (超过 80 项，已截断)");
                            break;
                        }
                    }
                    serviceName = "";
                    displayName = "";
                    state = "";
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"获取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static string ExecuteFetchPage(string args, CancellationToken ct)
    {
        var url = ParseArg(args, "url");
        if (string.IsNullOrWhiteSpace(url))
            return "错误：缺少 url 参数，请提供要访问的网页 URL";

        try
        {
            var page = WebSearchService.FetchWebPageAsync(url, ct).GetAwaiter().GetResult();
            var sb = new StringBuilder();
            sb.AppendLine($"页面标题：{page.Title}");
            sb.AppendLine($"URL：{page.Url}");
            sb.AppendLine($"内容格式：{page.ContentType}");
            sb.AppendLine();
            sb.AppendLine(page.Content);
            return sb.ToString();
        }
        catch (OperationCanceledException)
        {
            return "页面获取已取消";
        }
        catch (Exception ex)
        {
            return $"获取页面失败：{ex.Message}";
        }
    }

    private static string ExecuteWebSearch(string args, CancellationToken ct)
    {
        var query = ParseArg(args, "query");
        if (string.IsNullOrWhiteSpace(query))
            return "错误：缺少 query 参数，请提供搜索关键词";

        try
        {
            var result = WebSearchService.SearchAsync(query, ct).GetAwaiter().GetResult();
            return WebSearchService.FormatResult(result);
        }
        catch (OperationCanceledException)
        {
            return "搜索已取消";
        }
        catch (Exception ex)
        {
            return $"搜索失败：{ex.Message}";
        }
    }

    private static (Microsoft.Win32.RegistryKey hive, string subPath) ParseRegKey(string keyPath)
    {
        var parts = keyPath.Split(['\\'], 2);
        var hiveName = parts[0].ToUpperInvariant();
        var subPath = parts.Length > 1 ? parts[1] : "";

        var hive = hiveName switch
        {
            "HKEY_LOCAL_MACHINE" or "HKLM" => Microsoft.Win32.Registry.LocalMachine,
            "HKEY_CURRENT_USER" or "HKCU" => Microsoft.Win32.Registry.CurrentUser,
            "HKEY_CLASSES_ROOT" or "HKCR" => Microsoft.Win32.Registry.ClassesRoot,
            "HKEY_USERS" or "HKU" => Microsoft.Win32.Registry.Users,
            "HKEY_CURRENT_CONFIG" or "HKCC" => Microsoft.Win32.Registry.CurrentConfig,
            _ => throw new ArgumentException($"未知的注册表根键：{hiveName}")
        };

        return (hive, subPath);
    }

    private static string FormatRegValue(object val)
    {
        return val switch
        {
            byte[] bytes => Convert.ToHexString(bytes),
            string[] sa => string.Join("; ", sa),
            _ => val.ToString() ?? ""
        };
    }

    private static string ParseArg(string args, string key)
    {
        var pattern = key + "=";
        var idx = args.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";

        var start = idx + pattern.Length;
        var end = args.IndexOf('|', start);
        if (end < 0) end = args.Length;

        return args.Substring(start, end - start).Trim();
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unitIdx = 0;
        while (size >= 1024 && unitIdx < units.Length - 1)
        {
            size /= 1024;
            unitIdx++;
        }
        return $"{size:F1} {units[unitIdx]}";
    }
}
