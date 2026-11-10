namespace TubaWinUi3.Models;

/// <summary>保留采集证据；显示文案在 UI 线程按当前语言构建。</summary>
public sealed record HardwarePortInfo(string Name, string Kind, string Source,
    uint Protocols = 0, int? Speed = null, uint SpeedFlags = 0,
    int? ConnectionStatus = null, bool? TypeC = null, bool? UserConnectable = null,
    long? LinkSpeed = null, string? UsbKey = null, string[]? CompanionKeys = null,
    bool IsRootHub = false);
