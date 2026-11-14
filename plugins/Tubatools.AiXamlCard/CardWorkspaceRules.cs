using System.Text;

namespace Tubatools.AiXamlCard;

/// <summary>
/// 把「优先用卡片」这条偏好写进 AI 工作区的 AGENTS.md。
///
/// 宿主每次建立 AI 连接都会整份重写 AGENTS.md，所以手改一次不会长期有效。
/// 这里用带标记的段落 + 文件监听：宿主重写后立刻把段落贴回去，
/// 标记块之外的内容一律不动，也不会重复堆叠。
/// </summary>
internal static class CardWorkspaceRules
{
    private const string Begin = "<!-- tuba-xaml-card:begin -->";
    private const string End = "<!-- tuba-xaml-card:end -->";

    public static readonly string Section = string.Join("\n",
    [
        Begin,
        "## 优先用界面卡片（AI 界面卡片插件）",
        "",
        "图吧工具箱的用户是来「看东西、调参数」的。只要回答里有任何一项符合下面任意一条，",
        "**主动做一张卡片**，用 `tuba-xaml-card` 技能里的 `show-card.ps1` 提交，不要等用户要求：",
        "",
        "- 3 个以上可以并排比较的项目（CPU/显卡/内存/型号/方案/价格）",
        "- 有能用滑块或输入框调、调完结果会变的量（功耗、频率、容量、预算、时长）",
        "- 需要用户填参数才能算出的结论（电源瓦数、带宽、等效频率、续航）",
        "- 「结论 + 依据 + 建议」的结构化内容（诊断结果、优化清单、体检报告）",
        "- 用户会反复回来看的数据（硬件清单、当前配置、待办项）",
        "",
        "一句话能说清时（确认、报错原因、单个数值）继续用纯文字。",
        "一次回答可以、也经常应该给多张卡片：用 `show-card.ps1 -JsonFile` 一次提交多个标签页，",
        "比来回追问效率高。提交成功后不要在正文里贴 XAML/C# 源码。",
        End
    ]);

    /// <summary>对当前 agents.md 内容应用规则块；没有变化时返回原字符串。</summary>
    public static string Apply(string current)
    {
        var newline = current.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var section = Section.ReplaceLineEndings(newline);
        var begin = current.IndexOf(Begin, StringComparison.Ordinal);
        if (begin >= 0)
        {
            var end = current.IndexOf(End, begin, StringComparison.Ordinal);
            if (end >= 0)
            {
                var head = current[..begin];
                var tail = current[(end + End.Length)..].TrimStart('\r', '\n');
                // 结尾统一补一个换行：首次追加与「替换已有块」必须产生同样的结果，
                // 否则文件监视器会在两者之间反复来回改写。
                return head + section + (tail.Length > 0 ? newline + newline + tail : newline);
            }
        }

        if (current.Length == 0) return section;
        var trimmed = current.TrimEnd('\r', '\n');
        return trimmed + newline + newline + section + newline;
    }

    /// <summary>把规则写进工作区；文件不存在（宿主还没准备）时跳过。</summary>
    public static void Install(string pluginDataDirectory)
    {
        foreach (var workspace in Workspaces(pluginDataDirectory))
            ApplyTo(Path.Combine(workspace, "AGENTS.md"));
    }

    internal static void ApplyTo(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var current = File.ReadAllText(path);
            var next = Apply(current);
            if (next == current) return;
            var temporary = path + ".card.tmp";
            File.WriteAllText(temporary, next, new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static IEnumerable<string> Workspaces(string pluginData)
    {
        var directory = new DirectoryInfo(pluginData);
        for (var i = 0; i < 4 && directory is not null; i++, directory = directory.Parent)
        {
            var aiAssistant = Path.Combine(directory.FullName, "AiAssistant");
            if (Directory.Exists(aiAssistant)) yield return Path.Combine(aiAssistant, "Workspace");
        }
    }
}

/// <summary>
/// 监听工作区 AGENTS.md：宿主每次连接都会重写它，这里在重写后立刻贴回规则块。
/// 只在内容真的缺块时写入，避免自触发死循环。
/// </summary>
internal sealed class CardWorkspaceWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private bool _disposed;

    public CardWorkspaceWatcher(string pluginDataDirectory)
    {
        foreach (var workspace in CardWorkspaceRules.Workspaces(pluginDataDirectory).Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(workspace, "AGENTS.md")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };
                watcher.Changed += (_, _) => Reapply(workspace);
                watcher.Created += (_, _) => Reapply(workspace);
                watcher.Renamed += (_, _) => Reapply(workspace);
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
    }

    private void Reapply(string workspace)
    {
        if (_disposed) return;
        // 宿主刚写完时文件可能仍被占用：短暂延迟后再试一次。
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 3 && !_disposed; attempt++)
            {
                try
                {
                    CardWorkspaceRules.ApplyTo(Path.Combine(workspace, "AGENTS.md"));
                    return;
                }
                catch (IOException) { }
                await Task.Delay(150).ConfigureAwait(false);
            }
        });
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var watcher in _watchers)
        {
            try { watcher.EnableRaisingEvents = false; watcher.Dispose(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        }
        _watchers.Clear();
    }
}
