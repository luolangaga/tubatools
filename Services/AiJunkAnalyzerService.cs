using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TubaWinUi3.Services;

public sealed class AiJunkSuggestion
{
    public string Path { get; init; } = "";
    public string Description { get; init; } = "";
    public string Reason { get; init; } = "";
    public string RiskLevel { get; init; } = "safe";
    public string Category { get; init; } = "";
    public bool Selected { get; set; } = true;
    public long SizeBytes { get; set; }
}

public sealed class AiAnalyzerProgress
{
    public string Status { get; init; } = "";
    public int Round { get; init; }
    public string? CurrentTool { get; init; }
    public string? CurrentPath { get; init; }
    public string? Log { get; init; }
}

public sealed class AiMaxRoundsReachedException : Exception
{
    public List<AiChatMessage> Messages { get; }
    public int RoundsCompleted { get; }

    public AiMaxRoundsReachedException(int roundsCompleted, List<AiChatMessage> messages)
        : base($"AI 分析已达到 {roundsCompleted} 轮上限")
    {
        RoundsCompleted = roundsCompleted;
        Messages = messages;
    }
}

public static class AiJunkAnalyzerService
{
    private const int MaxRounds = 40;
    private const int MaxDirEntries = 200;
    private const long MaxSizeRecursionBytes = 2L * 1024 * 1024 * 1024;

    private static readonly HashSet<string> _blockedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\'),
        Environment.GetFolderPath(Environment.SpecialFolder.System).TrimEnd('\\'),
        "C:\\Windows\\System32",
        "C:\\Windows\\SysWOW64",
        "C:\\Windows\\WinSxS",
        "C:\\Windows\\Boot",
        "C:\\Windows\\Branding",
        "C:\\Windows\\DigitalLocker",
        "C:\\Windows\\Ehome",
        "C:\\Windows\\Fonts",
        "C:\\Windows\\Globalization",
        "C:\\Windows\\Help",
        "C:\\Windows\\IME",
        "C:\\Windows\\InputMethod",
        "C:\\Windows\\LiveKernelReports",
        "C:\\Windows\\ModemLogs",
        "C:\\Windows\\Panther",
        "C:\\Windows\\Performance",
        "C:\\Windows\\Policies",
        "C:\\Windows\\Prefetch",
        "C:\\Windows\\Registration",
        "C:\\Windows\\RemotePackages",
        "C:\\Windows\\Resources",
        "C:\\Windows\\Servicing",
        "C:\\Windows\\Setup",
        "C:\\Windows\\SoftwareDistribution",
        "C:\\Windows\\Speech",
        "C:\\Windows\\System",
        "C:\\Windows\\SystemApps",
        "C:\\Windows\\Temp",
        "C:\\Windows\\Tracing",
        "C:\\Windows\\Twain_32",
        "C:\\Windows\\Vss",
        "C:\\Windows\\Web",
        "C:\\Windows\\winsxs",
    };

    private static readonly string SystemPrompt = """
你是一个 Windows 系统清理专家。你的任务是分析用户电脑上的文件和目录，找出可以安全清理的垃圾文件。

## 可用工具

你可以通过以下文本标记调用工具。你可以在一次回复中调用多个工具，每行一个：

1. 列出目录内容：
   [TOOL] list_dir | path=目录路径

2. 获取文件/文件夹详细信息：
   [TOOL] get_info | path=文件或目录路径

3. 获取文件夹大小：
   [TOOL] get_size | path=目录路径

4. 获取已安装软件列表：
   [TOOL] list_programs

5. 获取磁盘使用概况：
   [TOOL] disk_usage

## 规则

1. 你可以在一次回复中调用多个工具（每个占一行），这样效率更高。例如同时列出多个目录的内容。
2. 只建议清理安全的文件（临时文件、缓存、日志、旧备份、已卸载软件残留等）
3. 绝对不要建议删除系统关键文件、用户文档、正在使用的程序文件
4. 标记每个建议的风险等级：safe（安全，纯临时/缓存）、low（低风险，残留文件）、medium（中风险，可能有用但通常可删）、high（高风险，需谨慎）
5. high 风险项目默认不选中
6. 用中文描述每个文件/文件夹是做什么的
7. 从用户目录、AppData、ProgramData、临时目录等常见垃圾聚集地开始探查
8. 当你完成分析，输出最终结果，格式如下：

[RESULT]
```json
[
  {
    "path": "C:\\Users\\xxx\\AppData\\Local\\Temp\\SomeFolder",
    "description": "这是XXX软件的临时缓存文件夹，用于存储运行时产生的临时数据",
    "reason": "该软件已卸载，缓存文件不再有用，可以安全删除",
    "riskLevel": "safe",
    "category": "软件缓存"
  }
]
```

注意：
- path 必须是实际存在的完整路径
- 每个建议必须包含 description（描述这个文件/文件夹是干什么的）和 reason（为什么可以清理）
- riskLevel 只能是 safe / low / medium / high
- category 可以是：临时文件、软件缓存、浏览器缓存、日志文件、更新缓存、软件残留、下载缓存、其他
- 尽量找出所有可清理的项目，不要遗漏
""";

    private static readonly string FullScanSystemPrompt = """
你是一个 Windows 系统清理专家。你的任务是全面深入地分析用户电脑上的文件和目录，找出所有可以安全清理的垃圾文件。

## 可用工具

你可以通过以下文本标记调用工具。你可以在一次回复中调用多个工具，每行一个：

1. 列出目录内容：
   [TOOL] list_dir | path=目录路径

2. 获取文件/文件夹详细信息：
   [TOOL] get_info | path=文件或目录路径

3. 获取文件夹大小：
   [TOOL] get_size | path=目录路径

4. 获取已安装软件列表：
   [TOOL] list_programs

5. 获取磁盘使用概况：
   [TOOL] disk_usage

## 规则

1. 你可以在一次回复中调用多个工具（每个占一行），这样效率更高。例如同时列出多个目录的内容。
2. 只建议清理安全的文件（临时文件、缓存、日志、旧备份、已卸载软件残留等）
3. 绝对不要建议删除系统关键文件、用户文档、正在使用的程序文件
4. 标记每个建议的风险等级：safe（安全，纯临时/缓存）、low（低风险，残留文件）、medium（中风险，可能有用但通常可删）、high（高风险，需谨慎）
5. high 风险项目默认不选中
6. 用中文描述每个文件/文件夹是做什么的
7. **这是完全扫描模式**：你必须从磁盘根目录开始，逐层深入探查所有可能的垃圾文件聚集地，包括但不限于：
   - 所有磁盘根目录下的大文件夹
   - C:\ 根目录下的非系统目录
   - 用户目录的所有子目录（Downloads、Documents、Desktop等）
   - AppData 的所有子目录（Local、Roaming、LocalLow）
   - ProgramData 目录
   - Program Files / Program Files (x86) 中已卸载软件的残留
   - 所有用户的目录（C:\Users\下每个用户）
   - 公共目录（C:\Users\Public）
   - 各软件的缓存目录（游戏平台、开发工具、设计软件等）
   - 下载目录中的大文件和安装包
8. 尽可能多地使用工具探查，不要遗漏任何可能的垃圾文件
9. 当你完成分析，输出最终结果，格式如下：

[RESULT]
```json
[
  {
    "path": "C:\\Users\\xxx\\AppData\\Local\\Temp\\SomeFolder",
    "description": "这是XXX软件的临时缓存文件夹，用于存储运行时产生的临时数据",
    "reason": "该软件已卸载，缓存文件不再有用，可以安全删除",
    "riskLevel": "safe",
    "category": "软件缓存"
  }
]
```

注意：
- path 必须是实际存在的完整路径
- 每个建议必须包含 description（描述这个文件/文件夹是干什么的）和 reason（为什么可以清理）
- riskLevel 只能是 safe / low / medium / high
- category 可以是：临时文件、软件缓存、浏览器缓存、日志文件、更新缓存、软件残留、下载缓存、其他
- 完全扫描必须尽可能全面，找出所有可清理的项目
""";

    public static async Task<List<AiJunkSuggestion>> AnalyzeAsync(
        IProgress<AiAnalyzerProgress>? progress,
        CancellationToken ct = default,
        List<AiChatMessage>? existingMessages = null,
        bool fullScan = false)
    {
        var systemPrompt = fullScan ? FullScanSystemPrompt : SystemPrompt;

        var messages = existingMessages ?? new List<AiChatMessage>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = BuildInitialContext(fullScan) }
        };

        var startRound = existingMessages is not null
            ? CountRoundsFromMessages(messages)
            : 0;

        for (int round = startRound; round < MaxRounds; round++)
        {
            progress?.Report(new AiAnalyzerProgress
            {
                Status = $"正在分析（第 {round + 1} 轮）...",
                Round = round + 1,
                Log = $"[第 {round + 1} 轮] 等待 AI 响应..."
            });

            var response = await AiService.ChatWithToolsAsync(messages, tools: null, ct: ct, temperature: 0.2);

            if (!response.Success)
                throw new InvalidOperationException($"AI 调用失败：{response.Error}");

            var content = response.Content;

            var aiThinking = ExtractThinking(content);
            if (!string.IsNullOrEmpty(aiThinking))
            {
                progress?.Report(new AiAnalyzerProgress
                {
                    Status = $"AI 正在思考（第 {round + 1} 轮）...",
                    Round = round + 1,
                    Log = $"[AI 思考] {aiThinking}"
                });
            }

            var resultMatch = Regex.Match(content, @"\[RESULT\]", RegexOptions.IgnoreCase);
            if (resultMatch.Success)
            {
                progress?.Report(new AiAnalyzerProgress
                {
                    Status = "正在生成清理报告...",
                    Round = round + 1,
                    Log = "[完成] AI 已完成分析，正在生成清理报告..."
                });

                var afterResult = content.Substring(resultMatch.Index + resultMatch.Value.Length);
                return ParseResult(afterResult);
            }

            var toolCalls = Regex.Matches(content, @"\[TOOL\]\s*(\w+)\s*\|\s*(.*)", RegexOptions.IgnoreCase);
            if (toolCalls.Count > 0)
            {
                messages.Add(new AiChatMessage { Role = "assistant", Content = content });

                var allResults = new StringBuilder();

                for (int i = 0; i < toolCalls.Count; i++)
                {
                    var match = toolCalls[i];
                    var toolName = match.Groups[1].Value.Trim().ToLowerInvariant();
                    var toolArgs = match.Groups[2].Value.Trim();

                    var toolIndexLabel = toolCalls.Count > 1 ? $" ({i + 1}/{toolCalls.Count})" : "";

                    progress?.Report(new AiAnalyzerProgress
                    {
                        Status = $"正在执行工具{toolIndexLabel}：{toolName} {toolArgs}",
                        Round = round + 1,
                        CurrentTool = toolName,
                        CurrentPath = TryExtractPath(toolArgs),
                        Log = $"[工具调用{toolIndexLabel}] {toolName} | {toolArgs}"
                    });

                    var toolResult = ExecuteTool(toolName, toolArgs, progress, round + 1, ct);

                    var resultPreview = toolResult.Length > 300 ? toolResult[..300] + "..." : toolResult;
                    progress?.Report(new AiAnalyzerProgress
                    {
                        Status = $"工具{toolIndexLabel}执行完成",
                        Round = round + 1,
                        CurrentTool = toolName,
                        Log = $"[工具结果{toolIndexLabel}] {toolName} 返回 {toolResult.Length} 字符\n{resultPreview}"
                    });

                    allResults.AppendLine($"=== 工具 {i + 1}: {toolName} | {toolArgs} ===");
                    allResults.AppendLine(toolResult);
                    allResults.AppendLine();
                }

                messages.Add(new AiChatMessage { Role = "user", Content = $"[TOOL_RESULT]\n{allResults}" });
                continue;
            }

            messages.Add(new AiChatMessage { Role = "assistant", Content = content });
            messages.Add(new AiChatMessage
            {
                Role = "user",
                Content = "请继续分析。如果你已经完成，请输出 [RESULT] 标记和 JSON 格式的清理建议列表。如果还需要探查更多目录，请继续使用 [TOOL] 标记（可以一次调用多个工具）。"
            });
        }

        throw new AiMaxRoundsReachedException(MaxRounds, messages);
    }

    private static int CountRoundsFromMessages(List<AiChatMessage> messages)
    {
        int rounds = 0;
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i].Role == "assistant" && messages[i].Content.Contains("[TOOL]", StringComparison.OrdinalIgnoreCase))
                rounds++;
            else if (messages[i].Role == "assistant" && !messages[i].Content.Contains("[RESULT]", StringComparison.OrdinalIgnoreCase))
                rounds++;
        }
        return rounds;
    }

    private static string BuildInitialContext(bool fullScan = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine(fullScan
            ? "请对我的电脑进行全面深度扫描，从磁盘根目录开始，找出所有可以安全清理的垃圾文件。以下是我的系统基本信息："
            : "请分析我的电脑，找出可以安全清理的垃圾文件。以下是我的系统基本信息：");
        sb.AppendLine();

        try
        {
            sb.AppendLine($"操作系统：{Environment.OSVersion.VersionString}");
            sb.AppendLine($"用户名：{Environment.UserName}");
            sb.AppendLine($"用户目录：{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}");
            sb.AppendLine($"处理器核心数：{Environment.ProcessorCount}");
        }
        catch { }

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

        sb.AppendLine();
        sb.AppendLine("常见垃圾目录概况：");
        var junkDirs = new (string Label, string Path)[]
        {
            ("用户临时目录", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Temp")),
            ("系统临时目录", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")),
            ("下载目录", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
            ("桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            ("AppData Local", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local")),
            ("AppData Roaming", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Roaming")),
            ("ProgramData", @"C:\ProgramData"),
        };

        if (fullScan)
        {
            var extraDirs = new (string Label, string Path)[]
            {
                ("C:\\ 根目录", @"C:\"),
                ("Program Files", @"C:\Program Files"),
                ("Program Files (x86)", @"C:\Program Files (x86)"),
                ("用户目录", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                ("公共目录", @"C:\Users\Public"),
                ("AppData LocalLow", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow")),
            };
            junkDirs = [.. junkDirs, .. extraDirs];
        }

        foreach (var (label, path) in junkDirs)
        {
            if (Directory.Exists(path))
            {
                try
                {
                    var size = GetDirectorySizeSafe(path);
                    sb.AppendLine($"  {label} ({path}): {FormatSize(size)}");
                }
                catch
                {
                    sb.AppendLine($"  {label} ({path}): 无法计算大小");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine(fullScan
            ? "请从磁盘根目录开始，逐层深入探查所有可能的垃圾文件聚集地，进行全面深度扫描。"
            : "请开始探查这些目录，找出可以清理的垃圾文件。");

        return sb.ToString();
    }

    private static string ExecuteTool(
        string toolName,
        string args,
        IProgress<AiAnalyzerProgress>? progress,
        int round,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return toolName switch
        {
            "list_dir" => ExecuteListDir(args, progress, round),
            "get_info" => ExecuteGetInfo(args, progress, round),
            "get_size" => ExecuteGetSize(args, progress, round),
            "list_programs" => ExecuteListPrograms(progress, round),
            "disk_usage" => ExecuteDiskUsage(progress, round),
            _ => $"错误：未知工具 '{toolName}'"
        };
    }

    private static string ExecuteListDir(string args, IProgress<AiAnalyzerProgress>? progress, int round)
    {
        var path = ParseArg(args, "path");
        if (string.IsNullOrWhiteSpace(path))
            return "错误：缺少 path 参数";

        if (IsPathBlocked(path))
            return $"错误：路径 '{path}' 是系统保护目录，不允许探查";

        progress?.Report(new AiAnalyzerProgress
        {
            Status = $"正在列出目录：{path}",
            Round = round,
            CurrentTool = "list_dir",
            CurrentPath = path
        });

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
                if (count >= MaxDirEntries)
                {
                    sb.AppendLine($"... (共超过 {MaxDirEntries} 项，已截断)");
                    break;
                }

                ct_default().ThrowIfCancellationRequested();

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

            if (count == 0)
                sb.AppendLine("(空目录)");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static CancellationToken ct_default() => CancellationToken.None;

    private static string ExecuteGetInfo(string args, IProgress<AiAnalyzerProgress>? progress, int round)
    {
        var path = ParseArg(args, "path");
        if (string.IsNullOrWhiteSpace(path))
            return "错误：缺少 path 参数";

        if (IsPathBlocked(path))
            return $"错误：路径 '{path}' 是系统保护目录";

        progress?.Report(new AiAnalyzerProgress
        {
            Status = $"正在获取信息：{path}",
            Round = round,
            CurrentTool = "get_info",
            CurrentPath = path
        });

        var sb = new StringBuilder();

        try
        {
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                sb.AppendLine($"类型：目录");
                sb.AppendLine($"路径：{di.FullName}");
                sb.AppendLine($"名称：{di.Name}");
                sb.AppendLine($"创建时间：{di.CreationTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"修改时间：{di.LastWriteTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"属性：{di.Attributes}");

                try
                {
                    var fileCount = 0;
                    var dirCount = 0;
                    foreach (var _ in Directory.EnumerateFiles(path, "*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false }))
                        fileCount++;
                    foreach (var _ in Directory.EnumerateDirectories(path, "*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false }))
                        dirCount++;
                    sb.AppendLine($"直接子项：{dirCount} 个目录，{fileCount} 个文件");
                }
                catch { }
            }
            else if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                sb.AppendLine($"类型：文件");
                sb.AppendLine($"路径：{fi.FullName}");
                sb.AppendLine($"名称：{fi.Name}");
                sb.AppendLine($"扩展名：{fi.Extension}");
                sb.AppendLine($"大小：{FormatSize(fi.Length)}");
                sb.AppendLine($"创建时间：{fi.CreationTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"修改时间：{fi.LastWriteTime:yyyy-MM-dd HH:mm}");
                sb.AppendLine($"属性：{fi.Attributes}");

                try
                {
                    var ver = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrEmpty(ver.CompanyName))
                        sb.AppendLine($"公司：{ver.CompanyName}");
                    if (!string.IsNullOrEmpty(ver.ProductName))
                        sb.AppendLine($"产品：{ver.ProductName}");
                    if (!string.IsNullOrEmpty(ver.FileDescription))
                        sb.AppendLine($"描述：{ver.FileDescription}");
                }
                catch { }
            }
            else
            {
                sb.AppendLine("路径不存在");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"获取信息失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static string ExecuteGetSize(string args, IProgress<AiAnalyzerProgress>? progress, int round)
    {
        var path = ParseArg(args, "path");
        if (string.IsNullOrWhiteSpace(path))
            return "错误：缺少 path 参数";

        if (IsPathBlocked(path))
            return $"错误：路径 '{path}' 是系统保护目录";

        progress?.Report(new AiAnalyzerProgress
        {
            Status = $"正在计算大小：{path}",
            Round = round,
            CurrentTool = "get_size",
            CurrentPath = path
        });

        if (!Directory.Exists(path))
            return File.Exists(path)
                ? FormatSize(new FileInfo(path).Length)
                : "路径不存在";

        try
        {
            var (size, fileCount) = GetDirectorySizeDetailed(path);
            return $"目录大小：{FormatSize(size)}，包含 {fileCount} 个文件";
        }
        catch (Exception ex)
        {
            return $"计算失败：{ex.Message}";
        }
    }

    private static string ExecuteListPrograms(IProgress<AiAnalyzerProgress>? progress, int round)
    {
        progress?.Report(new AiAnalyzerProgress
        {
            Status = "正在读取已安装软件列表...",
            Round = round,
            CurrentTool = "list_programs"
        });

        var sb = new StringBuilder();
        sb.AppendLine("已安装软件列表：");
        sb.AppendLine();

        var programs = new List<(string Name, string Version, string Date, string Size, string Location)>();

        try
        {
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (key is null) continue;

                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        try
                        {
                            using var subKey = key.OpenSubKey(subKeyName);
                            if (subKey is null) continue;

                            var name = subKey.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            var version = subKey.GetValue("DisplayVersion") as string ?? "";
                            var date = subKey.GetValue("InstallDate") as string ?? "";
                            var sizeObj = subKey.GetValue("EstimatedSize");
                            var size = sizeObj is int s ? FormatSize((long)s * 1024) : "";
                            var location = subKey.GetValue("InstallLocation") as string ?? "";

                            programs.Add((name, version, date, size, location));
                        }
                        catch { }
                    }
                }
                catch { }
            }

            if (programs.Count == 0)
            {
                sb.AppendLine("(未找到已安装软件信息)");
            }
            else
            {
                foreach (var p in programs.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var line = $"  {p.Name}";
                    if (!string.IsNullOrEmpty(p.Version)) line += $" v{p.Version}";
                    if (!string.IsNullOrEmpty(p.Size)) line += $" ({p.Size})";
                    if (!string.IsNullOrEmpty(p.Location)) line += $" → {p.Location}";
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

    private static string ExecuteDiskUsage(IProgress<AiAnalyzerProgress>? progress, int round)
    {
        progress?.Report(new AiAnalyzerProgress
        {
            Status = "正在获取磁盘使用概况...",
            Round = round,
            CurrentTool = "disk_usage"
        });

        var sb = new StringBuilder();
        sb.AppendLine("磁盘使用概况：");
        sb.AppendLine();

        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var used = drive.TotalSize - drive.AvailableFreeSpace;
                var pct = (double)used / drive.TotalSize * 100;
                sb.AppendLine($"{drive.RootDirectory.FullName} 总共 {FormatSize(drive.TotalSize)}，已用 {FormatSize(used)} ({pct:F1}%)，可用 {FormatSize(drive.AvailableFreeSpace)}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"读取失败：{ex.Message}");
        }

        return sb.ToString();
    }

    private static List<AiJunkSuggestion> ParseResult(string text)
    {
        var jsonMatch = Regex.Match(text, @"```json\s*(\[.*?\])\s*```", RegexOptions.Singleline);
        if (!jsonMatch.Success)
        {
            jsonMatch = Regex.Match(text, @"(\[.*?\])", RegexOptions.Singleline);
        }

        if (!jsonMatch.Success)
            throw new InvalidOperationException("AI 返回的结果中未找到有效的 JSON 数据");

        var json = jsonMatch.Groups[1].Value;

        try
        {
            var items = JsonSerializer.Deserialize(json, TubaDefaultContext.Default.ListJsonElement);
            if (items is null) return [];

            var suggestions = new List<AiJunkSuggestion>();

            foreach (var item in items)
            {
                var path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                var desc = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                var reason = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
                var risk = item.TryGetProperty("riskLevel", out var rl) ? rl.GetString() ?? "safe" : "safe";
                var category = item.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";

                if (string.IsNullOrWhiteSpace(path)) continue;

                risk = risk.ToLowerInvariant() switch
                {
                    "safe" => "safe",
                    "low" => "low",
                    "medium" => "medium",
                    "high" => "high",
                    _ => "safe"
                };

                long sizeBytes = 0;
                try
                {
                    if (Directory.Exists(path))
                        sizeBytes = GetDirectorySizeSafe(path);
                    else if (File.Exists(path))
                        sizeBytes = new FileInfo(path).Length;
                }
                catch { }

                suggestions.Add(new AiJunkSuggestion
                {
                    Path = path,
                    Description = desc,
                    Reason = reason,
                    RiskLevel = risk,
                    Category = category,
                    Selected = risk is "safe" or "low",
                    SizeBytes = sizeBytes
                });
            }

            return suggestions;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"解析 AI 返回的 JSON 失败：{ex.Message}\n原始内容：{json}");
        }
    }

    private static string? ParseArg(string args, string key)
    {
        var match = Regex.Match(args, $@"{key}\s*=\s*(.+?)(?:\s*\||$)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static bool IsPathBlocked(string path)
    {
        var normalized = path.TrimEnd('\\');
        foreach (var blocked in _blockedPaths)
        {
            if (string.Equals(normalized, blocked, StringComparison.OrdinalIgnoreCase))
                return true;
            if (normalized.StartsWith(blocked + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static long GetDirectorySizeSafe(string path)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                RecurseSubdirectories = true
            }))
            {
                try { size += new FileInfo(file).Length; } catch { }
                if (size > MaxSizeRecursionBytes) break;
            }
        }
        catch { }
        return size;
    }

    private static (long Size, int FileCount) GetDirectorySizeDetailed(string path)
    {
        long size = 0;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
        {
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            RecurseSubdirectories = true
        }))
        {
            try
            {
                size += new FileInfo(file).Length;
                count++;
            }
            catch { }
            if (size > MaxSizeRecursionBytes) break;
        }
        return (size, count);
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int idx = 0;
        while (size >= 1024 && idx < units.Length - 1) { size /= 1024; idx++; }
        return $"{size:0.##} {units[idx]}";
    }

    public static string ExportReportJson(List<AiJunkSuggestion> suggestions)
    {
        var data = suggestions.Select(s => new AiJunkExportDto
        {
            Path = s.Path,
            Description = s.Description,
            Reason = s.Reason,
            RiskLevel = s.RiskLevel,
            Category = s.Category
        }).ToList();

        return JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = TubaDefaultContext.Default
        });
    }

    public static List<AiJunkSuggestion> ImportReportJson(string json)
    {
        var items = JsonSerializer.Deserialize(json, TubaDefaultContext.Default.ListJsonElement);
        if (items is null) return [];

        var suggestions = new List<AiJunkSuggestion>();
        foreach (var item in items)
        {
            var path = item.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
            var desc = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            var reason = item.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";
            var risk = item.TryGetProperty("riskLevel", out var rl) ? rl.GetString() ?? "safe" : "safe";
            var category = item.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(path)) continue;

            suggestions.Add(new AiJunkSuggestion
            {
                Path = path,
                Description = desc,
                Reason = reason,
                RiskLevel = risk,
                Category = category,
                Selected = risk is "safe" or "low"
            });
        }

        return suggestions;
    }

    public static long CleanSelected(List<AiJunkSuggestion> suggestions, IProgress<CleanProgress>? progress = null, CancellationToken ct = default)
    {
        long totalCleaned = 0;
        var items = suggestions.Where(s => s.Selected).ToList();
        var total = items.Count;

        for (int i = 0; i < items.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var item = items[i];
            progress?.Report(new CleanProgress
            {
                Current = i + 1,
                Total = total,
                CurrentPath = item.Path,
                CleanedBytes = totalCleaned
            });

            try
            {
                if (Directory.Exists(item.Path))
                {
                    var size = GetDirectorySizeSafe(item.Path);
                    Directory.Delete(item.Path, true);
                    totalCleaned += size;
                }
                else if (File.Exists(item.Path))
                {
                    var fi = new FileInfo(item.Path);
                    totalCleaned += fi.Length;
                    fi.Delete();
                }
            }
            catch { }
        }

        return totalCleaned;
    }

    private static string ExtractThinking(string content)
    {
        var beforeTool = Regex.Match(content, @"\[TOOL\]", RegexOptions.IgnoreCase);
        var beforeResult = Regex.Match(content, @"\[RESULT\]", RegexOptions.IgnoreCase);

        int cutoff = content.Length;
        if (beforeTool.Success) cutoff = Math.Min(cutoff, beforeTool.Index);
        if (beforeResult.Success) cutoff = Math.Min(cutoff, beforeResult.Index);

        var thinking = content[..cutoff].Trim();
        return thinking.Length > 300 ? thinking[..300] + "..." : thinking;
    }

    private static string? TryExtractPath(string args)
    {
        var match = Regex.Match(args, @"path\s*=\s*(.+?)(?:\s*\||$)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
