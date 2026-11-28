using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 右键菜单监控根清单（相对路径，挂到各 hive 的 Software\Classes 之下）。
/// 与 ContextMenuMgr 的 MonitoredRoots / 现有 RogueCleaner 的 Roots 对齐。
/// </summary>
public static class ContextMenuRoots
{
    public readonly record struct RootDef(string Scene, string Path, ContextMenuKind Kind);

    private static readonly RootDef[] _roots =
    [
        // Shell 命令（verb）
        new("所有文件", @"Software\Classes\*\shell", ContextMenuKind.ShellVerb),
        new("所有文件系统对象", @"Software\Classes\AllFilesystemObjects\shell", ContextMenuKind.ShellVerb),
        new("文件夹", @"Software\Classes\Directory\shell", ContextMenuKind.ShellVerb),
        new("文件夹背景", @"Software\Classes\Directory\Background\shell", ContextMenuKind.ShellVerb),
        new("桌面背景", @"Software\Classes\DesktopBackground\shell", ContextMenuKind.ShellVerb),
        new("磁盘", @"Software\Classes\Drive\shell", ContextMenuKind.ShellVerb),
        new("文件夹对象", @"Software\Classes\Folder\shell", ContextMenuKind.ShellVerb),
        new("快捷方式", @"Software\Classes\lnkfile\shell", ContextMenuKind.ShellVerb),
        new("可执行文件", @"Software\Classes\exefile\shell", ContextMenuKind.ShellVerb),
        new("未知文件", @"Software\Classes\Unknown\shell", ContextMenuKind.ShellVerb),
        // Shell 扩展（COM）
        new("所有文件", @"Software\Classes\*\shellex\ContextMenuHandlers", ContextMenuKind.ShellExtension),
        new("所有文件系统对象", @"Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers", ContextMenuKind.ShellExtension),
        new("文件夹", @"Software\Classes\Directory\shellex\ContextMenuHandlers", ContextMenuKind.ShellExtension),
        new("文件夹背景", @"Software\Classes\Directory\Background\shellex\ContextMenuHandlers", ContextMenuKind.ShellExtension),
        new("桌面背景", @"Software\Classes\DesktopBackground\shellex\ContextMenuHandlers", ContextMenuKind.ShellExtension),
        new("磁盘", @"Software\Classes\Drive\shellex\ContextMenuHandlers", ContextMenuKind.ShellExtension),
        new("文件夹对象", @"Software\Classes\Folder\shellex\ContextMenuHandlers", ContextMenuKind.ShellExtension),
        new("快捷方式", @"Software\Classes\lnkfile\shellex\ContextMenuHandlers", ContextMenuKind.ShellExtension),
        new("可执行文件", @"Software\Classes\exefile\shellex\ContextMenuHandlers", ContextMenuKind.ShellExtension),
    ];

    public static IReadOnlyList<RootDef> Roots => _roots;

    /// <summary>所有要扫描的 (hive, view) 组合。</summary>
    public static IEnumerable<(RegHive Hive, RegView View)> HiveViews()
    {
        foreach (var hive in new[] { RegHive.HKCU, RegHive.HKLM })
        {
            if (hive == RegHive.HKCU)
            {
                // HKCU\Software\Classes 不受 WOW64 重定向（同一物理键）：只扫描默认视图，
                // 避免同一物理注册表键被 64/32 两个视图重复枚举，导致重复屏蔽、状态重复。
                yield return (hive, RegView.Default);
            }
            else if (Environment.Is64BitOperatingSystem)
            {
                // HKLM\Software\Classes 的 64/32 视图是不同物理键（WOW6432Node），
                // 32 位右键扩展注册在 32 位视图，必须分别扫描。
                yield return (hive, RegView.Registry64);
                yield return (hive, RegView.Registry32);
            }
            else
            {
                yield return (hive, RegView.Default);
            }
        }
    }
}

