using System.Text.Json.Serialization;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

/// <summary>注册表 hive。</summary>
public enum RegHive
{
    HKCU = 0,
    HKLM = 1,
}

/// <summary>注册表视图（WOW64）。</summary>
public enum RegView
{
    Default = 0,
    Registry64 = 1,
    Registry32 = 2,
}

/// <summary>右键菜单项类型。</summary>
public enum ContextMenuKind
{
    /// <summary>Shell 命令（verb，含 command 默认值）。</summary>
    ShellVerb = 0,
    /// <summary>COM Shell 扩展（shellex\ContextMenuHandlers，含 CLSID）。</summary>
    ShellExtension = 1,
}

/// <summary>用户/后端对该条目的期望状态。</summary>
public enum DesiredState
{
    /// <summary>期望禁用（主动拦截）。</summary>
    Blocked = 0,
    /// <summary>期望放行（用户审核放行）。</summary>
    Allowed = 1,
}

/// <summary>一次扫描发现的右键菜单条目。</summary>
public sealed class ContextMenuItem
{
    /// <summary>稳定标识：hive|view|subkey（大小写不敏感比较）。</summary>
    public string Id { get; set; } = "";

    public RegHive Hive { get; set; }
    public RegView View { get; set; }

    /// <summary>注册表子键路径（不含 hive 前缀，如 Software\Classes\*\shell\foo）。</summary>
    public string SubKey { get; set; } = "";

    public ContextMenuKind Kind { get; set; }

    /// <summary>shellex 扩展的 CLSID（仅 ShellExtension 有）。</summary>
    public string Clsid { get; set; } = "";

    /// <summary>显示名称（MUIVerb / 默认值 / 子键名）。</summary>
    public string Name { get; set; } = "";

    /// <summary>Shell 命令的 command 默认值（仅 ShellVerb 有）。</summary>
    public string Command { get; set; } = "";

    /// <summary>所属可执行文件/DLL 路径（CLSID → InprocServer32/LocalServer32，或从 command 解析）。</summary>
    public string ExePath { get; set; } = "";

    /// <summary>是否现代菜单（Windows 11 新右键菜单 / AppX 打包应用扩展，PackagedCom 声明）。</summary>
    public bool IsModernMenu { get; set; }

    /// <summary>是否为受控可编辑（shellex 无 CLSID 时只读不动作）。</summary>
    [JsonIgnore]
    public bool Writable { get; set; }
}

