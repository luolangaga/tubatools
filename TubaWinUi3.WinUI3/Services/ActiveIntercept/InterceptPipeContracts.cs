using System.Text.Json.Serialization;

namespace TubaWinUi3.Services.ActiveIntercept;

// =====================================================================
// 命名管道 IPC 契约（与进程内 Runtime\PipeContracts.cs 对齐；
// 线上格式：UTF-8 无 BOM、每行一条 JSON，camelCase，枚举按数字）。
// 协议移植自 ContextMenuMgr（GPL-3.0，https://github.com/PLFJY/ContextMenuMgr）。
// =====================================================================

public static class InterceptPipeConstants
{
    /// <summary>拦截服务管道名（与运行时 InterceptPipeConstants.PipeName 保持一致）。</summary>
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
    ItemDetected = 0,
    ItemStateChanged = 1,
    ServiceMessage = 2,
    ServiceStopping = 3,
}

public enum InterceptPipeCommand
{
    Ping = 0,
    SubscribeNotifications = 1,
    GetSnapshot = 2,
    ApplyDecision = 3,
    DeleteItem = 4,
    UndoDelete = 5,
    PurgeDeletedItem = 6,
    StopTracking = 7,
    ResumeTracking = 8,
    RemoveEventRows = 9,
    ClearEvents = 10,
    SetTrustPolicy = 11,
    RequestShutdown = 12,
    SetLogLevel = 13,
}

public enum InterceptDecision
{
    Allow = 0,
    Deny = 1,
    Remove = 2,
}

public enum InterceptChangeKind
{
    None = 0,
    Added = 1,
    Removed = 2,
    Modified = 3,
    Reappeared = 4,
}

public sealed class InterceptPipeEnvelope
{
    public InterceptPipeMessageType MessageType { get; set; }
    public Guid CorrelationId { get; set; }
    public InterceptPipeRequest? Request { get; set; }
    public InterceptPipeResponse? Response { get; set; }
    public InterceptBackendNotification? Notification { get; set; }
}

public sealed class InterceptPipeRequest
{
    public InterceptPipeCommand Command { get; set; }
    public string? ItemId { get; set; }
    public InterceptDecision? Decision { get; set; }
    public bool? Trust { get; set; }
    public List<string>? RowIds { get; set; }
    public string? ExePath { get; set; }
    public string? Policy { get; set; }
    public Guid? ClientOperationId { get; set; }
}

public sealed class InterceptPipeResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? ErrorCode { get; set; }
    public InterceptSnapshot? Snapshot { get; set; }
    public InterceptItemDto? Item { get; set; }
    public Guid? ClientOperationId { get; set; }
}

public sealed class InterceptBackendNotification
{
    public InterceptPipeNotificationKind Kind { get; set; }
    public string Message { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public InterceptItemDto? Item { get; set; }
    public Guid? ClientOperationId { get; set; }
}

public sealed class InterceptSnapshot
{
    public List<InterceptItemDto> Items { get; set; } = [];
    public List<InterceptEventDto> Events { get; set; } = [];
    public List<InterceptIgnoredDto> Ignored { get; set; } = [];
    public Dictionary<string, string> Policies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int PendingCount { get; set; }
    public int BlockedCount { get; set; }
    public int AllowedCount { get; set; }
    public string StateFingerprint { get; set; } = "";
    public string ConfigNotice { get; set; } = "";
}

/// <summary>条目 DTO（与后端 InterceptItemDto 字段对齐）。</summary>
public sealed class InterceptItemDto
{
    public string Id { get; set; } = "";
    public int Hive { get; set; }
    public int View { get; set; }
    public string SubKey { get; set; } = "";
    public int Kind { get; set; }
    public string Clsid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string ExePath { get; set; } = "";
    public bool IsModernMenu { get; set; }
    public string DesiredState { get; set; } = "none";
    public bool IsBlocked { get; set; }
    public bool IsPendingApproval { get; set; }
    public string PendingChangeKind { get; set; } = "";
    public bool IsDeleted { get; set; }
    public bool HasBackup { get; set; }
    public bool IsIgnored { get; set; }
    public string ConsistencyIssue { get; set; } = "";
    public string Source { get; set; } = "";
    public string FirstSeenUtc { get; set; } = "";
    public string UpdatedAtUtc { get; set; } = "";
    public string Note { get; set; } = "";
}

public sealed class InterceptIgnoredDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SubKey { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string CreatedUtc { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>后端请求失败异常（对应 ContextMenuMgr 的 BackendRequestException）。</summary>
public sealed class BackendRequestException : Exception
{
    public BackendRequestException(string message, string? errorCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}