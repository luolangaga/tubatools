using Microsoft.Win32;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 扫描全部监控根下的右键菜单条目（HKCU/HKLM × 64/32 视图）。
/// 解析 CLSID → 所属 exe/dll，供主动拦截判断与展示。
/// </summary>
public static class ContextMenuScanner
{
    /// <summary>全量扫描，返回当前机器上的所有右键菜单条目（按 Id 去重）。
    /// 包括经典右键菜单（shell/shellex）+ Windows 11 新菜单（Packaged COM）。</summary>
    public static List<ContextMenuItem> Scan()
    {
        var items = new List<ContextMenuItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 经典右键菜单
        foreach (var (hive, view) in ContextMenuRoots.HiveViews())
        {
            foreach (var root in ContextMenuRoots.Roots)
            {
                ScanRoot(hive, view, root, items, seen);
            }
        }

        // Windows 11 新菜单（Packaged COM / AppX 右键扩展）
        try
        {
            var packaged = PackagedComScanner.Scan();
            foreach (var item in packaged)
            {
                if (seen.Add(item.Id)) items.Add(item);
            }
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"Packaged COM 扫描异常（跳过）：{ex.Message}");
        }

        return items;
    }

    private static void ScanRoot(
        RegHive hive, RegView view, ContextMenuRoots.RootDef root,
        List<ContextMenuItem> items, HashSet<string> seen)
    {
        using var key = RegistryAccess.OpenSubKey(hive, view, root.Path, writable: false);
        if (key is null) return;

        foreach (var childName in RegistryAccess.GetSubKeyNames(key))
        {
            var subKey = root.Path + "\\" + childName;
            using var child = RegistryAccess.OpenSubKey(hive, view, subKey, writable: false);
            if (child is null) continue;

            var id = $"{hive}|{view}|{subKey}";
            if (!seen.Add(id)) continue;

            var item = new ContextMenuItem
            {
                Id = id,
                Hive = hive,
                View = view,
                SubKey = subKey,
                Kind = root.Kind,
            };

            if (root.Kind == ContextMenuKind.ShellExtension)
            {
                item.Clsid = RegistryAccess.ReadString(child, "");
                item.Name = BuildName(child, childName, "");
                item.Writable = !string.IsNullOrWhiteSpace(item.Clsid);
                item.ExePath = ClsidResolver.Resolve(item.Clsid);
            }
            else
            {
                item.Command = ReadCommandDefault(hive, view, subKey);
                item.Name = BuildName(child, childName, item.Command);
                item.Writable = true;
                item.ExePath = ExtractExecutableFromCommand(item.Command);
            }

            items.Add(item);
        }
    }

    private static string ReadCommandDefault(RegHive hive, RegView view, string verbSubKey)
    {
        using var commandKey = RegistryAccess.OpenSubKey(hive, view, verbSubKey + "\\command", writable: false);
        return RegistryAccess.ReadString(commandKey, "");
    }

    /// <summary>构建显示名称：MUIVerb &gt; 子键默认值 &gt; 子键名。</summary>
    private static string BuildName(RegistryKey child, string childName, string command)
    {
        var raw = RegistryAccess.ReadString(child, "MUIVerb");
        if (string.IsNullOrWhiteSpace(raw)) raw = RegistryAccess.ReadString(child, "");
        if (string.IsNullOrWhiteSpace(raw)) raw = childName;
        return CleanName(raw);
    }

    internal static string CleanName(string raw)
    {
        var value = (raw ?? "").Trim();
        if (value.Length > 0 && value.StartsWith("@", StringComparison.Ordinal))
        {
            // @dll,-id 资源引用：尝试 SHLoadIndirectString 解析
            var resolved = ShellResolve.ResolveIndirectString(value);
            if (!string.IsNullOrWhiteSpace(resolved)) value = resolved;
        }
        return value.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&").Trim();
    }

    /// <summary>从 command 默认值解析所属 exe 路径。</summary>
    internal static string ExtractExecutableFromCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "";
        var text = command.Trim();
        try { text = Environment.ExpandEnvironmentVariables(text); } catch { }
        text = text.Trim();

        if (text.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text.Substring(1, end - 1) : "";
        }
        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe >= 0 ? text.Substring(0, exe + 4).Trim() : "";
    }
}

