using Microsoft.Win32;
using System.Xml;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// Windows 11 新菜单（Packaged COM / AppX 右键扩展）扫描器。
/// 原理：枚举 HKCR\PackagedCom\ClassIndex 中注册的 CLSID，
/// 读取对应 AppX manifest 中的 fileExplorerContextMenus 声明，
/// 以 Shell Extensions\Blocked CLSID 列表进行屏蔽（与经典 shellex 一致）。
/// </summary>
public static class PackagedComScanner
{
    private const string ClassIndexPath = @"PackagedCom\ClassIndex";
    private const string BlockedRoot = @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";

    /// <summary>扫描所有 Packaged COM 右键菜单扩展（HKCR 64 位视图）。</summary>
    public static List<ContextMenuItem> Scan()
    {
        var items = new List<ContextMenuItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 收集 ClassIndex 下所有 CLSID → 包名的映射
        var clsidPackages = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
            using var index = classes.OpenSubKey(ClassIndexPath, false);
            if (index is null) return items;

            foreach (var clsid in index.GetSubKeyNames())
            {
                using var classKey = index.OpenSubKey(clsid, false);
                if (classKey is null) continue;
                foreach (var package in classKey.GetSubKeyNames())
                {
                    if (!clsidPackages.TryGetValue(clsid, out var list))
                    {
                        list = [];
                        clsidPackages[clsid] = list;
                    }
                    list.Add(package);
                }
            }
        }
        catch
        {
            return items;
        }

        // 扫描每个包的 AppX manifest，提取右键菜单声明
        string windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");

        foreach (var (clsid, packages) in clsidPackages)
        {
            foreach (var package in packages)
            {
                var manifest = Path.Combine(windowsApps, package, "AppxManifest.xml");
                if (!File.Exists(manifest)) continue;

                try
                {
                    ParseManifest(manifest, clsid, package, items, seen);
                }
                catch
                {
                    // manifest 解析失败（权限/损坏）跳过
                }
            }
        }

        return items;
    }

    private static void ParseManifest(string manifest, string clsid, string package,
        List<ContextMenuItem> items, HashSet<string> seen)
    {
        var xml = new XmlDocument { XmlResolver = null };
        xml.Load(manifest);

        // 提取包信息
        var identity = xml.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']");
        var packageName = identity?.Attributes?["Name"]?.Value ?? package;
        var publisher = identity?.Attributes?["Publisher"]?.Value ?? "";

        // 现代右键菜单：windows.fileExplorerContextMenus → Verb 的 CLSID
        var verbs = xml.SelectNodes(
            "//*[local-name()='Extension' and @Category='windows.fileExplorerContextMenus']" +
            "//*[local-name()='ItemType']/*[local-name()='Verb']");
        if (verbs is not null)
        {
            foreach (XmlNode verb in verbs)
            {
                var verbClsid = GetAttr(verb, "Clsid");
                var itemType = verb.ParentNode is not null ? GetAttr(verb.ParentNode, "Type") : "";
                AddEntry(verbClsid, itemType, package, packageName, publisher, "现代", modern: true, items, seen);
            }
        }

        // 传统兼容右键菜单：windows.fileExplorerClassicContextMenuHandler
        var classicHandlers = xml.SelectNodes(
            "//*[local-name()='Extension' and @Category='windows.fileExplorerClassicContextMenuHandler']" +
            "//*[local-name()='ExtensionHandler']");
        if (classicHandlers is not null)
        {
            foreach (XmlNode handler in classicHandlers)
            {
                var handlerClsid = GetAttr(handler, "Clsid");
                var itemType = GetAttr(handler, "Type");
                AddEntry(handlerClsid, itemType, package, packageName, publisher, "传统兼容", modern: false, items, seen);
            }
        }
    }

    private static void AddEntry(string clsid, string itemType, string package,
        string packageName, string publisher, string kind, bool modern,
        List<ContextMenuItem> items, HashSet<string> seen)
    {
        if (!Guid.TryParse(clsid, out _)) return;
        var normalized = "{" + clsid.Trim().Trim('{', '}').ToUpperInvariant() + "}";
        var normalizedItem = NormalizeItemType(itemType);
        var id = $"PackagedCom|HKCU|{normalized}|{normalizedItem}";
        if (!seen.Add(id)) return;

        // 检查当前是否已被屏蔽
        bool isBlocked = false;
        try
        {
            using var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);
            using var blocked = classes.OpenSubKey(BlockedRoot, false);
            isBlocked = blocked is not null &&
                        blocked.GetValueNames().Any(n => string.Equals(n, normalized, StringComparison.OrdinalIgnoreCase));
        }
        catch { }

        var scene = itemType switch
        {
            var t when string.Equals(t, "*", StringComparison.OrdinalIgnoreCase) => "文件右键",
            var t when string.Equals(t, "Directory", StringComparison.OrdinalIgnoreCase) => "文件夹右键",
            var t when string.Equals(t, @"Directory\Background", StringComparison.OrdinalIgnoreCase) => "文件夹空白处右键",
            var t when string.Equals(t, "Drive", StringComparison.OrdinalIgnoreCase) => "磁盘右键",
            _ => "文件资源管理器右键",
        };

        items.Add(new ContextMenuItem
        {
            Id = id,
            Hive = RegHive.HKCU, // Packaged COM 屏蔽写 HKCU 的 Blocked 列表
            View = RegView.Registry64,
            SubKey = BlockedRoot,
            Kind = ContextMenuKind.ShellExtension,
            Clsid = normalized,
            Name = $"{packageName}（{kind}）",
            Command = "", // Packaged COM 通过 manifest 声明，无 command
            ExePath = ResolvePackagePath(package),
            IsModernMenu = modern,
            Writable = true,
        });
    }

    private static string NormalizeItemType(string itemType)
    {
        if (string.IsNullOrWhiteSpace(itemType)) return "*";
        return itemType.Trim();
    }

    private static string ResolvePackagePath(string packageFullName)
    {
        try
        {
            var windowsApps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            return Path.Combine(windowsApps, packageFullName);
        }
        catch { return ""; }
    }

    private static string GetAttr(XmlNode node, string name)
    {
        return node.Attributes?[name]?.Value ?? "";
    }
}

