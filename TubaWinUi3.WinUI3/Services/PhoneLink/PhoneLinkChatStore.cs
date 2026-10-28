using System.Text.Json;

namespace TubaWinUi3.Services;

/// <summary>聊天落盘快照：消息本体 + 文件/图片消息对应的本机路径。</summary>
internal sealed class PhoneLinkChatSnapshot
{
    public List<PhoneChatMessage> Messages { get; set; } = [];
    public Dictionary<string, string> FilePaths { get; set; } = [];
}

/// <summary>
/// 「连接手机」聊天消息仓库：内存上限 <see cref="MaxMessages"/> 条，同时落盘到
/// <c>&lt;DataDir&gt;/phone_link/chat.json</c>，重启后会话与文件仍可查看。
/// 超出上限时从最旧的消息开始丢（同时清掉它的文件路径映射）。
/// </summary>
internal static class PhoneLinkChatStore
{
    internal const int MaxMessages = 500;

    private static readonly object Gate = new();
    private static readonly List<PhoneChatMessage> _messages = [];
    private static readonly Dictionary<string, string> _filePaths = [];
    private static long _seq;
    private static string? _loadedFrom;

    private static string StorePath => System.IO.Path.Combine(ConfigManager.GetDataDir(), "phone_link", "chat.json");

    private static void EnsureLoadedLocked()
    {
        var path = StorePath;
        if (string.Equals(_loadedFrom, path, StringComparison.OrdinalIgnoreCase)) return;
        _messages.Clear();
        _filePaths.Clear();
        _seq = 0;
        _loadedFrom = path;
        try
        {
            if (!File.Exists(path)) return;
            var snapshot = JsonSerializer.Deserialize<PhoneLinkChatSnapshot>(File.ReadAllText(path));
            if (snapshot is null) return;
            foreach (var message in snapshot.Messages.OrderBy(m => m.Seq))
            {
                _messages.Add(message);
                _seq = Math.Max(_seq, message.Seq);
            }
            foreach (var (id, filePath) in snapshot.FilePaths)
            {
                if (!string.IsNullOrEmpty(filePath)) _filePaths[id] = filePath;
            }
            TrimLocked();
        }
        catch
        {
            _messages.Clear();
            _filePaths.Clear();
        }
    }

    private static void TrimLocked()
    {
        while (_messages.Count > MaxMessages)
        {
            var oldest = _messages[0];
            _messages.RemoveAt(0);
            _filePaths.Remove(oldest.Id);
        }
    }

    private static void SaveLocked()
    {
        try
        {
            var path = StorePath;
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(new PhoneLinkChatSnapshot
            {
                Messages = _messages.ToList(),
                FilePaths = new Dictionary<string, string>(_filePaths)
            });
            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            // 落盘失败不影响会话本身，下次写入再试
        }
    }

    public static PhoneChatMessage Add(string from, string type, string text, string fileName, long size, string? filePath)
    {
        PhoneChatMessage message;
        lock (Gate)
        {
            EnsureLoadedLocked();
            message = new PhoneChatMessage
            {
                Seq = ++_seq,
                Id = Guid.NewGuid().ToString("N"),
                From = from,
                Type = type,
                Text = text,
                FileName = fileName,
                Size = size,
                Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            _messages.Add(message);
            if (filePath is not null) _filePaths[message.Id] = filePath;
            TrimLocked();
            SaveLocked();
        }
        return message;
    }

    public static IReadOnlyList<PhoneChatMessage> Snapshot()
    {
        lock (Gate)
        {
            EnsureLoadedLocked();
            return _messages.ToList();
        }
    }

    public static string? GetFilePath(string messageId)
    {
        lock (Gate)
        {
            EnsureLoadedLocked();
            return _filePaths.GetValueOrDefault(messageId);
        }
    }

    /// <summary>
    /// 分页：<paramref name="after"/> 取比它新的最早 <paramref name="limit"/> 条；
    /// 否则取 <paramref name="before"/> 之前（缺省为全部）最新的 <paramref name="limit"/> 条。
    /// </summary>
    internal static List<PhoneChatMessage> Page(
        IReadOnlyList<PhoneChatMessage> messages, long after, long before, int limit)
    {
        var pageSize = Math.Clamp(limit <= 0 ? MaxMessages : limit, 1, MaxMessages);
        if (after > 0)
            return messages.Where(m => m.Seq > after).Take(pageSize).ToList();

        var eligible = before > 0 ? messages.Where(m => m.Seq < before).ToList() : messages.ToList();
        return eligible.Count <= pageSize
            ? eligible
            : eligible.GetRange(eligible.Count - pageSize, pageSize);
    }

    /// <summary>测试隔离：清空内存与会话文件。</summary>
    internal static void ClearForTests()
    {
        lock (Gate)
        {
            _messages.Clear();
            _filePaths.Clear();
            _seq = 0;
            _loadedFrom = null;
            try
            {
                if (File.Exists(StorePath)) File.Delete(StorePath);
            }
            catch
            {
            }
        }
    }

    /// <summary>测试用：丢弃内存缓存但保留会话文件，模拟进程重启。</summary>
    internal static void ReloadForTests()
    {
        lock (Gate)
        {
            _messages.Clear();
            _filePaths.Clear();
            _loadedFrom = null;
        }
    }
}
