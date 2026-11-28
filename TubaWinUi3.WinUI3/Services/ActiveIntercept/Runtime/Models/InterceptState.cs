using System.Text.Json.Serialization;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

/// <summary>持久化的期望状态条目（state.json 中的一项）。</summary>
public sealed class InterceptStateEntry
{
    /// <summary>稳定标识：hive|view|subkey。</summary>
    public string Id { get; set; } = "";

    public RegHive Hive { get; set; }
    public RegView View { get; set; }
    public string SubKey { get; set; } = "";

    public ContextMenuKind Kind { get; set; }

    /// <summary>shellex 扩展的 CLSID。</summary>
    public string Clsid { get; set; } = "";

    /// <summary>显示名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>命令文本。</summary>
    public string Command { get; set; } = "";

    /// <summary>所属可执行文件/DLL 路径。</summary>
    public string ExePath { get; set; } = "";

    public DesiredState DesiredState { get; set; } = DesiredState.Blocked;

    /// <summary>注册表现状是否处于被屏蔽状态（最近一次扫描观测）。</summary>
    public bool ObservedBlocked { get; set; }

    /// <summary>是否待审核（新增/重现，等待用户 允许/保持拦截/移除）。</summary>
    public bool IsPendingApproval { get; set; }

    /// <summary>待审核来源：added / reappeared。仅 IsPendingApproval 时有效。</summary>
    public string PendingChangeKind { get; set; } = "";

    /// <summary>是否已删除（可撤销；删除时生成 .reg 备份）。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>删除时生成的 .reg 备份文件相对路径（UndoDelete 恢复用）。</summary>
    public string BackupFilePath { get; set; } = "";

    /// <summary>删除时间（UTC ISO 8601）。</summary>
    public string DeletedAtUtc { get; set; } = "";

    /// <summary>跳过下一次外部变更检测（用于消化本程序自身写入的注册表变化）。</summary>
    public bool SuppressNextDetection { get; set; }

    /// <summary>连续缺失快照次数（判定"已消失"的稳定阈值）。</summary>
    public int ConsecutiveMissingSnapshots { get; set; }

    /// <summary>最近更新时间（UTC ISO 8601）。</summary>
    public string UpdatedAtUtc { get; set; } = "";

    /// <summary>附加说明（如"写入 LegacyDisable"）。</summary>
    public string Note { get; set; } = "";

    /// <summary>首次出现时间（UTC，ISO 8601）。</summary>
    public string FirstSeenUtc { get; set; } = "";

    /// <summary>来源标注：baseline（基线）/ new（新增）/ reappeared（恢复）。</summary>
    public string Source { get; set; } = "";

    /// <summary>是否现代菜单（Windows 11 新右键菜单 / AppX 打包应用扩展）。</summary>
    public bool IsModernMenu { get; set; }
}

/// <summary>整个状态文件。</summary>
public sealed class InterceptStateFile
{
    /// <summary>当前架构版本。旧文件无此字段（0），加载时自动迁移。</summary>
    public int SchemaVersion { get; set; } = 1;

    public List<InterceptStateEntry> Entries { get; set; } = [];

    /// <summary>上次保存时间（UTC，ISO 8601）。</summary>
    public string SavedAtUtc { get; set; } = "";

    /// <summary>首次启动是否已建立基线。</summary>
    public bool BaselineEstablished { get; set; }
}

/// <summary>一条拦截/放行事件（events.jsonl 中的一行）。</summary>
public sealed class InterceptEvent
{
    /// <summary>事件行唯一 ID（GUID，删除单条记录时使用）。</summary>
    public string RowId { get; set; } = "";

    public string TimestampUtc { get; set; } = "";

    /// <summary>动作：Blocked（自动屏蔽）/ Allowed（用户放行）/ Reblocked（重新屏蔽）/ Unblocked（解除屏蔽）。</summary>
    public string Action { get; set; } = "";

    public string Id { get; set; } = "";
    public RegHive Hive { get; set; }
    public RegView View { get; set; }
    public string SubKey { get; set; } = "";
    public ContextMenuKind Kind { get; set; }
    public string Clsid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string Source { get; set; } = "";

    /// <summary>是否现代菜单（Windows 11 新右键菜单 / AppX 打包应用扩展）。</summary>
    public bool IsModernMenu { get; set; }

    /// <summary>附加说明（如"写入 Shell Extensions\Blocked"）。</summary>
    public string Note { get; set; } = "";
}

/// <summary>注册表操作备份（block-engine 写前快照，可回滚）。</summary>
public sealed class RegistryValueBackup
{
    public string Id { get; set; } = "";
    public RegHive Hive { get; set; }
    public RegView View { get; set; }
    public string SubKey { get; set; } = "";
    public string ValueName { get; set; } = "";

    /// <summary>操作前该值是否已存在。</summary>
    public bool Existed { get; set; }

    /// <summary>操作前值的内容（存在时）。</summary>
    public string? Value { get; set; }

    public string? ValueKind { get; set; }
    public string CreatedUtc { get; set; } = "";
}

