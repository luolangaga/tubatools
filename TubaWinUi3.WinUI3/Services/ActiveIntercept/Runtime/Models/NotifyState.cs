namespace TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

/// <summary>通知去重状态：每条目最近一次通知时间（用于冷却期去重，防止通知风暴）。</summary>
public sealed class NotifyStateEntry
{
    /// <summary>条目 Id：hive|view|subkey。</summary>
    public string Id { get; set; } = "";

    /// <summary>最近一次通知时间（UTC ISO 8601）。</summary>
    public string LastNotifiedUtc { get; set; } = "";
}

/// <summary>通知去重状态文件。</summary>
public sealed class NotifyStateFile
{
    public List<NotifyStateEntry> Items { get; set; } = [];
}
