using System.Text.Json.Serialization;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

// =====================================================================
// 命名管道 IPC 契约（移植自 ContextMenuMgr 的 ContextMenuMgr.Contracts，
// GPL-3.0，https://github.com/PLFJY/ContextMenuMgr —— 已按 GPL 要求在
// 衍生文件中保留协议结构与语义）。改动：命令集收敛为本功能域（主动拦截）。
// 线上格式：UTF-8 无 BOM、每行一条 JSON（JsonSerializerDefaults.Web）。
// =====================================================================

public static class InterceptPipeConstants
{
    /// <summary>后端管道名（主程序与后端之间的唯一权威通道）。</summary>
    public const string PipeName = "TubaWinUi3.Intercept.Backend";

    /// <summary>连接超时（毫秒）。</summary>
    public const int ConnectTimeoutMs = 2000;

    /// <summary>通知订阅断线重连间隔（秒）。</summary>
    public const int NotificationReconnectDelaySeconds = 1;
}

public enum InterceptPipeMessageType
{
    Request = 0,
    Response = 1,
    Notification = 2,
}

public enum InterceptPipeNotificationKind
{
    /// <summary>检测到新增/重现条目（已拦截并待审核）。</summary>
    ItemDetected = 0,

    /// <summary>任意条目状态变更（放行/拦截/删除/恢复/停止追踪等）。</summary>
    ItemStateChanged = 1,

    /// <summary>后端服务消息（需前端提示）。</summary>
    ServiceMessage = 2,

    /// <summary>后端即将退出。前端应断开订阅并标记后端离线。</summary>
    ServiceStopping = 3,
}

/// <summary>管道命令。全部为主动拦截域的权威操作。</summary>
public enum InterceptPipeCommand
{
    /// <summary>连通性探测。</summary>
    Ping = 0,

    /// <summary>订阅推送通知（保持连接常开）。</summary>
    SubscribeNotifications = 1,

    /// <summary>拉取全量快照（拦截列表 + 操作记录 + 已忽略 + 信任策略 + 配置）。</summary>
    GetSnapshot = 2,

    /// <summary>审核决策：Allow（放行）/ Deny（保持拦截）/ Remove（移除出队列/注册表）。</summary>
    ApplyDecision = 3,

    /// <summary>彻底删除注册表项（可撤销，含 .reg 备份）。</summary>
    DeleteItem = 4,

    /// <summary>撤销删除（从备份恢复注册表项并重新进入审核）。</summary>
    UndoDelete = 5,

    /// <summary>永久清除已删除项（删除备份文件与状态）。</summary>
    PurgeDeletedItem = 6,

    /// <summary>停止追踪：加入忽略列表，不再拦截/提醒（不删除注册表项）。</summary>
    StopTracking = 7,

    /// <summary>恢复追踪：移出忽略列表。</summary>
    ResumeTracking = 8,

    /// <summary>仅删除操作记录中的若干行（按 RowId）。</summary>
    RemoveEventRows = 9,

    /// <summary>清空全部操作记录。</summary>
    ClearEvents = 10,

    /// <summary>写入信任策略（allow/block/ask）。</summary>
    SetTrustPolicy = 11,

    /// <summary>请求后端优雅退出。</summary>
    RequestShutdown = 12,

    /// <summary>设置运行时日志级别（保留，暂未接入文件日志级别切换）。</summary>
    SetLogLevel = 13,
}

/// <summary>审核决策（对应 ContextMenuMgr 的 ContextMenuDecision）。</summary>
public enum InterceptDecision
{
    Allow = 0,
    Deny = 1,
    Remove = 2,
}

/// <summary>变更分类（移植自 ContextMenuMgr 的 ContextMenuChangeKind）。</summary>
public enum InterceptChangeKind
{
    None = 0,
    Added = 1,
    Removed = 2,
    Modified = 3,
    Reappeared = 4,
}

/// <summary>管道信封：每行一条 JSON。</summary>
public sealed class InterceptPipeEnvelope
{
    public InterceptPipeMessageType MessageType { get; set; }
    public System.Guid CorrelationId { get; set; }
    public InterceptPipeRequest? Request { get; set; }
    public InterceptPipeResponse? Response { get; set; }
    public InterceptBackendNotification? Notification { get; set; }
}

/// <summary>请求载荷。字段超集，按命令取用，与 ContextMenuMgr 的 PipeRequest 同构。</summary>
public sealed class InterceptPipeRequest
{
    public InterceptPipeCommand Command { get; set; }

    /// <summary>条目 Id（hive|view|subkey）。</summary>
    public string? ItemId { get; set; }

    /// <summary>审核决策（ApplyDecision 用）。</summary>
    public InterceptDecision? Decision { get; set; }

    /// <summary>放行时是否同时信任此程序（总是放行，不再拦截该程序的所有项）。</summary>
    public bool? Trust { get; set; }

    /// <summary>要删除的操作记录行 Id（RemoveEventRows 用）。</summary>
    public System.Collections.Generic.List<string>? RowIds { get; set; }

    /// <summary>信任策略程序路径（SetTrustPolicy 用，键 = ExePath 小写）。</summary>
    public string? ExePath { get; set; }

    /// <summary>信任策略值（SetTrustPolicy 用）：allow / block / ask。</summary>
    public string? Policy { get; set; }

    /// <summary>客户端操作 Id：用于去重自己触发的广播（自己发起的变更不再重复应用）。</summary>
    public System.Guid? ClientOperationId { get; set; }
}

/// <summary>响应载荷。</summary>
public sealed class InterceptPipeResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? ErrorCode { get; set; }

    /// <summary>全量快照（GetSnapshot 用）。</summary>
    public InterceptSnapshot? Snapshot { get; set; }

    /// <summary>变更后的条目（状态变更命令返回，供前端免刷新更新）。</summary>
    public InterceptItemDto? Item { get; set; }

    /// <summary>回显客户端操作 Id。</summary>
    public System.Guid? ClientOperationId { get; set; }
}

/// <summary>后端主动推送的通知。</summary>
public sealed class InterceptBackendNotification
{
    public InterceptPipeNotificationKind Kind { get; set; }
    public string Message { get; set; } = "";
    public System.DateTimeOffset Timestamp { get; set; } = System.DateTimeOffset.UtcNow;

    /// <summary>相关条目（ItemDetected / ItemStateChanged 时携带）。</summary>
    public InterceptItemDto? Item { get; set; }

    /// <summary>引起该通知的客户端操作 Id（前端据此去重）。</summary>
    public System.Guid? ClientOperationId { get; set; }
}

/// <summary>全量快照（前端 GetSnapshot 的返回）。</summary>
public sealed class InterceptSnapshot
{
    /// <summary>条目列表（含挂起审核、已删除、已忽略标记），按 Name 排序。</summary>
    public System.Collections.Generic.List<InterceptItemDto> Items { get; set; } = [];

    /// <summary>操作记录（倒序，最新在前）。</summary>
    public System.Collections.Generic.List<InterceptEventDto> Events { get; set; } = [];

    /// <summary>已停止追踪列表。</summary>
    public System.Collections.Generic.List<InterceptIgnoredDto> Ignored { get; set; } = [];

    /// <summary>信任策略（ExePath → allow/block/ask）。</summary>
    public System.Collections.Generic.Dictionary<string, string> Policies { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>待审核数量（前端显示"待审核 N"）。</summary>
    public int PendingCount { get; set; }

    /// <summary>拦截中数量（DesiredState=Blocked 且未删除未挂起）。</summary>
    public int BlockedCount { get; set; }

    /// <summary>已放行数量。</summary>
    public int AllowedCount { get; set; }

    /// <summary>状态指纹：state.json 上次保存时间戳。前端轮询以此判断是否需要刷新。</summary>
    public string StateFingerprint { get; set; } = "";

    public string ConfigNotice { get; set; } = "";
}

/// <summary>条目 DTO（前端展示模型）。</summary>
public sealed class InterceptItemDto
{
    public string Id { get; set; } = "";
    public Models.RegHive Hive { get; set; }
    public Models.RegView View { get; set; }
    public string SubKey { get; set; } = "";
    public Models.ContextMenuKind Kind { get; set; }
    public string Clsid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string ExePath { get; set; } = "";

    /// <summary>是否现代菜单（Windows 11 新右键菜单 / AppX 打包应用扩展）。</summary>
    public bool IsModernMenu { get; set; }

    /// <summary>期望状态：blocked / allowed / none（未审核）。</summary>
    public string DesiredState { get; set; } = "none";

    /// <summary>注册表现状是否被屏蔽。</summary>
    public bool IsBlocked { get; set; }

    /// <summary>是否待审核（新增/重现，等待用户 允许/保持拦截/移除）。</summary>
    public bool IsPendingApproval { get; set; }

    /// <summary>待审核来源：added / reappeared。仅 IsPendingApproval 时有效。</summary>
    public string PendingChangeKind { get; set; } = "";

    /// <summary>是否已删除（可撤销）。</summary>
    public bool IsDeleted { get; set; }

    /// <summary>删除时是否产生了 .reg 备份（可撤销删除）。</summary>
    public bool HasBackup { get; set; }

    /// <summary>是否在忽略列表（停止追踪）。</summary>
    public bool IsIgnored { get; set; }

    /// <summary>一致性提示（期望与现状不符等），无则空。</summary>
    public string ConsistencyIssue { get; set; } = "";

    /// <summary>来源标注：baseline / runtime。</summary>
    public string Source { get; set; } = "";

    /// <summary>首次出现时间（UTC ISO8601）。</summary>
    public string FirstSeenUtc { get; set; } = "";

    /// <summary>最近更新时间（UTC ISO8601）。</summary>
    public string UpdatedAtUtc { get; set; } = "";

    /// <summary>附加说明（如"写入 LegacyDisable"）。</summary>
    public string Note { get; set; } = "";
}

/// <summary>操作记录 DTO（events.jsonl 行）。</summary>
public sealed class InterceptEventDto
{
    public string RowId { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
    public string Action { get; set; } = "";
    public string Id { get; set; } = "";
    public Models.RegHive Hive { get; set; }
    public Models.RegView View { get; set; }
    public string SubKey { get; set; } = "";
    public Models.ContextMenuKind Kind { get; set; }
    public string Clsid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string Source { get; set; } = "";

    /// <summary>是否现代菜单（Windows 11 新右键菜单 / AppX 打包应用扩展）。</summary>
    public bool IsModernMenu { get; set; }

    public string Note { get; set; } = "";
}

/// <summary>已停止追踪 DTO。</summary>
public sealed class InterceptIgnoredDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SubKey { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string CreatedUtc { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>前端侧错误码（结构化的失败原因）。</summary>
public static class InterceptPipeErrorCodes
{
    public const string RegistryWriteFailed = "REGISTRY_WRITE_FAILED";
    public const string RegistryExportFailed = "REGISTRY_EXPORT_FAILED";
    public const string RegistryRestoreFailed = "REGISTRY_RESTORE_FAILED";
    public const string ItemNotFound = "ITEM_NOT_FOUND";
    public const string BackendStopping = "BACKEND_STOPPING";
}
