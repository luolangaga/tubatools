using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// CLSID → COM 服务器路径解析（ContextMenuMgr 的 GuidMetadataCatalog 手法）：
/// 对每个候选 CLSID 根（HKCR / HKLM / HKCU 及 WOW64 变体）读取
/// InprocServer32 / LocalServer32 默认值，得到所属 DLL/EXE。
/// </summary>
public static class ClsidResolver
{
    private static readonly string[] _serverValues = ["InprocServer32", "LocalServer32"];

    /// <summary>解析 CLSID 到所属文件路径；解析失败返回空串。</summary>
    public static string Resolve(string clsid)
    {
        if (string.IsNullOrWhiteSpace(clsid)) return "";

        foreach (var candidate in Candidates(clsid))
        {
            foreach (var serverValue in _serverValues)
            {
                var path = ReadServerPath(candidate.Hive, candidate.View, candidate.ClsidKey, serverValue);
                if (!string.IsNullOrWhiteSpace(path)) return path;
            }
        }
        return "";
    }

    private readonly record struct ClsidCandidate(RegHive Hive, RegView View, string ClsidKey);

    private static IEnumerable<ClsidCandidate> Candidates(string clsid)
    {
        // HKCR\CLSID 是 HKLM\Software\Classes\CLSID 与 HKCU\Software\Classes\CLSID
        // 的合并视图；这里显式遍历两个真实后备存储，并分别覆盖 64/32 视图。
        var cls = $@"Software\Classes\CLSID\{clsid}";

        // HKLM（机器级，64/32 视图）
        yield return new ClsidCandidate(RegHive.HKLM, RegView.Registry64, cls);
        if (Environment.Is64BitOperatingSystem)
            yield return new ClsidCandidate(RegHive.HKLM, RegView.Registry32, cls);

        // HKCU（用户级）
        yield return new ClsidCandidate(RegHive.HKCU, RegView.Default, cls);
        if (Environment.Is64BitOperatingSystem)
            yield return new ClsidCandidate(RegHive.HKCU, RegView.Registry32, cls);
    }

    private static string ReadServerPath(RegHive hive, RegView view, string clsidKey, string serverValue)
    {
        using var key = RegistryAccess.OpenSubKey(hive, view, clsidKey, writable: false);
        if (key is null) return "";

        var raw = RegistryAccess.ReadString(key, serverValue);
        return NormalizeServerPath(raw);
    }

    /// <summary>展开环境变量、去引号、截断到文件边界。</summary>
    internal static string NormalizeServerPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var text = raw.Trim();
        try { text = Environment.ExpandEnvironmentVariables(text); } catch { }
        text = text.Trim();

        // "C:\...\foo.dll"
        if (text.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = text.IndexOf('"', 1);
            if (end > 1) text = text.Substring(1, end - 1);
        }
        else
        {
            var space = text.IndexOf(' ');
            if (space > 0) text = text.Substring(0, space);
        }

        // 截断到 .dll/.exe 边界
        text = TruncateToBinaryBoundary(text);
        return text;
    }

    private static string TruncateToBinaryBoundary(string text)
    {
        var dll = text.IndexOf(".dll", StringComparison.OrdinalIgnoreCase);
        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        int idx = -1;
        if (dll >= 0 && exe >= 0) idx = Math.Min(dll, exe);
        else if (dll >= 0) idx = dll;
        else if (exe >= 0) idx = exe;
        if (idx >= 0) text = text.Substring(0, idx + 4);
        return text.Trim();
    }
}

