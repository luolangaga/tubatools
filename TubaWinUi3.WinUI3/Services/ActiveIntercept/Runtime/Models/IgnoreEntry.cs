namespace TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

/// <summary>被用户「停止追踪」的条目（不再拦截、不再提醒，直到用户恢复追踪）。</summary>
public sealed class IgnoreEntry
{
    /// <summary>条目 Id：hive|view|subkey。</summary>
    public string Id { get; set; } = "";

    /// <summary>显示名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>注册表位置。</summary>
    public string SubKey { get; set; } = "";

    /// <summary>所属程序路径。</summary>
    public string ExePath { get; set; } = "";

    /// <summary>首次加入时间（UTC ISO 8601）。</summary>
    public string CreatedUtc { get; set; } = "";

    /// <summary>备注（如「用户停止追踪」）。</summary>
    public string Note { get; set; } = "";
}

/// <summary>忽略列表文件。</summary>
public sealed class IgnoreFile
{
    public List<IgnoreEntry> Items { get; set; } = [];
}
