using System.Text.Json;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>一条对话消息（落盘用）。命名带 AiPlayground 前缀以区别于 AiService.AiChatMessage。</summary>
public sealed class AiPlaygroundChatMessage
{
    public string Role { get; set; } = "user";
    public string Text { get; set; } = "";
}

/// <summary>
/// 一个对话会话（按模型分别保存，落盘于 <DataDir>/AiPlayground/chat/&lt;modelId&gt;/）。
/// <see cref="Title"/> 为空表示尚未生成标题，显示层回退到本地化的「新对话」。
/// </summary>
public sealed class AiChatSession
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public List<AiPlaygroundChatMessage> Messages { get; set; } = [];
}

/// <summary>
/// 本地 AI 试炼场对话会话的持久化：按模型分目录的 json 文件（每会话一个），
/// 提供新建 / 列出 / 载入 / 保存 / 重命名 / 删除。全部操作静默容错，
/// 磁盘异常只退化为「本次不保存」，不打断对话。
/// </summary>
public static class AiChatSessionStore
{
    /// <summary>测试用：覆盖会话根目录。</summary>
    internal static string? RootOverride;

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public static string ChatRoot => RootOverride ?? Path.Combine(ConfigManager.GetDataDir(), "AiPlayground", "chat");

    private static string ModelDir(string modelId) => Path.Combine(ChatRoot, Sanitize(modelId));

    private static string SessionPath(string modelId, string sessionId) =>
        Path.Combine(ModelDir(modelId), Sanitize(sessionId) + ".json");

    private static string Sanitize(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_').ToArray();
        var result = new string(chars).Trim('_');
        return string.IsNullOrEmpty(result) ? "session" : result;
    }

    /// <summary>列出某模型的全部会话（按最近更新倒序）。</summary>
    public static List<AiChatSession> ListSessions(string modelId)
    {
        var result = new List<AiChatSession>();
        try
        {
            var dir = ModelDir(modelId);
            if (!Directory.Exists(dir)) return result;
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var session = JsonSerializer.Deserialize<AiChatSession>(File.ReadAllText(file), JsonOpts);
                    if (session is null || string.IsNullOrEmpty(session.Id) || session.Messages.Count == 0) continue;
                    result.Add(session);
                }
                catch { }
            }
        }
        catch { }
        return result.OrderByDescending(s => s.UpdatedAt).ToList();
    }

    /// <summary>新建一个空会话（不落盘，直到有消息保存）。</summary>
    public static AiChatSession CreateSession(string modelId) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Title = "",
        UpdatedAt = DateTime.Now,
    };

    /// <summary>保存会话（原子写）。无消息的空会话不落盘。</summary>
    public static void SaveSession(string modelId, AiChatSession session)
    {
        if (session.Messages.Count == 0) return;
        lock (Gate)
        {
            try
            {
                // 标题取首条 user 消息（截断），保留用户已设的自定义标题
                if (string.IsNullOrWhiteSpace(session.Title))
                {
                    var first = session.Messages.FirstOrDefault(m => m.Role == "user" && !string.IsNullOrWhiteSpace(m.Text));
                    if (first is not null)
                    {
                        var text = first.Text.Trim().ReplaceLineEndings(" ");
                        session.Title = text.Length > 20 ? text[..20] + "…" : text;
                    }
                }
                session.UpdatedAt = DateTime.Now;

                var dir = ModelDir(modelId);
                Directory.CreateDirectory(dir);
                var path = SessionPath(modelId, session.Id);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(session, JsonOpts));
                File.Move(temp, path, overwrite: true);
            }
            catch { }
        }
    }

    public static void DeleteSession(string modelId, string sessionId)
    {
        lock (Gate)
        {
            try
            {
                var path = SessionPath(modelId, sessionId);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }

}