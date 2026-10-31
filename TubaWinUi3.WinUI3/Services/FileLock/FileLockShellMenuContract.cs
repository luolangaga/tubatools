using System.Text;

namespace TubaWinUi3.ShellIntegration;

/// <summary>
/// 「文件占用查看」右键菜单的跨进程契约：主程序（WinUI3）与 ShellExtension（NativeAOT COM DLL）
/// 共享编译本文件，保证菜单标识、标记文件格式、命令行构造只有一个事实来源。
///
/// 约束：只允许 BCL 类型，不得引用 WinUI / WinRT —— ShellExtension 以 NativeAOT 发布为原生 DLL，
/// 由 dllhost（包内 COM 代理）加载，没有 UI 框架与 .NET 运行时。
///
/// 两种模式：
/// - 便携/安装版（非打包）：传统经典菜单，HKCU\Software\Classes\…\shell 注册表项，命令行走
///   <see cref="BuildClassicCommand"/>；
/// - MSIX 打包版：Win11 新版菜单，条目由包清单静态声明，开关状态由本文件的标记文件表达，
///   处理程序在 GetState 中返回 ECS_HIDDEN/ECS_ENABLED 动态显隐。
/// </summary>
internal static class FileLockShellMenuContract
{
    /// <summary>
    /// Win11 新版菜单命令的 COM 类标识。必须与 run-msix.ps1 / build-msix-store.ps1 / build-store.ps1 /
    /// Package.appxmanifest 四处清单中的 com:Class/@Id 与 desktop4:Verb/@Clsid 完全一致
    /// （ShellExtensionManifestTests 会拦截漂移）。
    /// </summary>
    public const string CommandClsid = "9C1D4A7E-2B63-4E5F-A08C-6D3B9F1E7A52";

    public static readonly Guid CommandClsidGuid = new(CommandClsid);

    /// <summary>包内 shell 扩展 DLL 文件名（清单 com:Class/@Path 引用）。</summary>
    public const string ShellExtensionDllName = "TubaWinUi3.ShellExtension.dll";

    /// <summary>
    /// 应用执行别名（清单 uap3:ExecutionAlias/@Alias）：位于 %LocalAppData%\Microsoft\WindowsApps。
    /// 处理程序通过它启动主程序——带包身份、可携带命令行参数；别名被用户关闭时回退包内 exe 直启。
    /// </summary>
    public const string ExecutionAlias = "TubaWinUi3.exe";

    /// <summary>经典右键菜单键名（HKCU\Software\Classes\*\shell 与 Directory\shell 下的同名子键）。</summary>
    public const string ClassicMenuKeyName = "TubaWinUi3.FileLocksmith";

    /// <summary>命令行开关：用指定路径打开「文件占用查看」（两种模式的菜单命令都走它）。</summary>
    public const string FileLockArg = "--file-lock";

    /// <summary>命令行开关：本次会话由打包版提权重启而来（无包身份时仍按打包语义运行）。</summary>
    public const string MsixAdminSessionArg = "--msix-admin-session";

    /// <summary>环境变量：提权会话应使用的包数据根（LocalState），保证设置/工具/元数据与打包版共享。</summary>
    public const string LocalStateEnvVar = "TUBA_MSIX_LOCALSTATE";

    /// <summary>标记文件相对路径（相对数据根）。文件存在 = 菜单已开启；第 1 行标题、第 2 行悬停提示。</summary>
    public const string MarkerRelativePath = @"TubaWinUi3\shell-menu\file-locksmith.txt";

    /// <summary>兜底标题：标记文件缺失/损坏时使用（正常由主程序按当前界面语言写入）。</summary>
    public const string DefaultTitle = "用图吧工具箱检测文件占用";

    /// <summary>兜底悬停提示。</summary>
    public const string DefaultToolTip = "查看是哪个进程占用了此文件";

    /// <summary>经典菜单命令中的 Shell 占位符：右键对象路径由资源管理器替换。</summary>
    public const string ClassicPathPlaceholder = "%1";

    /// <summary>右键菜单的呈现模式。</summary>
    public enum MenuMode
    {
        /// <summary>非打包：传统经典菜单（Win11 上位于「显示更多选项」）。</summary>
        ClassicRegistry,

        /// <summary>打包 + Windows 11：新版菜单（清单声明 + 处理程序动态显隐）。</summary>
        ModernMenu,

        /// <summary>打包 + Windows 10：系统不支持从打包应用注册可见的右键菜单。</summary>
        Unsupported,
    }

    public static MenuMode ResolveMode(bool packagedContext, int osBuild) =>
        !packagedContext ? MenuMode.ClassicRegistry
        : osBuild >= 22000 ? MenuMode.ModernMenu
        : MenuMode.Unsupported;

    /// <summary>经典菜单注册表键（HKCU 相对路径），两个作用域：所有文件、文件夹。</summary>
    public static IReadOnlyList<string> ClassicRegistryKeyPaths() =>
    [
        @"Software\Classes\*\shell\" + ClassicMenuKeyName,
        @"Software\Classes\Directory\shell\" + ClassicMenuKeyName,
    ];

    /// <summary>经典菜单 command 值：本程序 + --file-lock + Shell 占位符。</summary>
    public static string BuildClassicCommand(string exePath, string placeholder = ClassicPathPlaceholder) =>
        $"\"{exePath}\" {FileLockArg} \"{placeholder}\"";

    /// <summary>标记文件内容（写盘用）：标题行 + 提示行，均单行化。</summary>
    public static string SerializeMarker(string title, string tooltip) =>
        Sanitize(title, DefaultTitle) + "\n" + Sanitize(tooltip, DefaultToolTip);

    /// <summary>解析标记文件内容；标题为空视为无效（未开启）。</summary>
    public static bool TryParseMarker(string? text, out string title, out string tooltip)
    {
        title = string.Empty;
        tooltip = DefaultToolTip;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        title = lines.Length > 0 ? lines[0].Trim() : string.Empty;
        if (title.Length == 0) return false;

        var tip = lines.Length > 1 ? lines[1].Trim() : string.Empty;
        tooltip = tip.Length > 0 ? tip : DefaultToolTip;
        return true;
    }

    /// <summary>在候选数据根中依次查找标记文件并读取；均不存在/无效返回 false。</summary>
    public static bool TryReadMarker(IReadOnlyList<string> dataRoots, out string title, out string tooltip)
    {
        foreach (var root in dataRoots)
        {
            var path = TryGetMarkerPath(root);
            if (path is null) continue;
            try
            {
                if (File.Exists(path) && TryParseMarker(File.ReadAllText(path, Encoding.UTF8), out title, out tooltip))
                    return true;
            }
            catch
            {
                // 读不到（权限/占用等）按未开启处理，不抛给调用方
            }
        }

        title = string.Empty;
        tooltip = DefaultToolTip;
        return false;
    }

    /// <summary>候选数据根中是否至少存在一个标记文件（不校验内容）。</summary>
    public static bool MarkerExists(IReadOnlyList<string> dataRoots)
    {
        foreach (var root in dataRoots)
        {
            var path = TryGetMarkerPath(root);
            if (path is null) continue;
            try
            {
                if (File.Exists(path)) return true;
            }
            catch
            {
            }
        }

        return false;
    }

    /// <summary>写入标记文件（会自动创建目录）。</summary>
    public static void WriteMarker(string dataRoot, string title, string tooltip)
    {
        var path = TryGetMarkerPath(dataRoot) ?? throw new ArgumentException("数据根无效", nameof(dataRoot));
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(path, SerializeMarker(title, tooltip), new UTF8Encoding(false));
    }

    /// <summary>删除标记文件（不存在时静默）。</summary>
    public static void DeleteMarker(string dataRoot)
    {
        var path = TryGetMarkerPath(dataRoot);
        if (path is null) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    /// <summary>数据根 → 标记文件完整路径；数据根非法返回 null。</summary>
    public static string? TryGetMarkerPath(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot)) return null;
        try
        {
            return Path.Combine(Path.GetFullPath(dataRoot), MarkerRelativePath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 提权重启用 PowerShell 脚本：先设置数据根环境变量，再用 runas 启动目标（保留参数）。
    /// 与 App 侧一致走 -EncodedCommand（UTF-16 Base64），中文路径不经过代码页。
    /// </summary>
    public static string BuildElevatedRelaunchScript(string targetExe, string localStateRoot, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        builder.Append("$ErrorActionPreference='Stop';$env:").Append(LocalStateEnvVar).Append("='").Append(Escape(localStateRoot)).Append("';");
        builder.Append(" Start-Process -FilePath '").Append(Escape(targetExe)).Append("' -Verb RunAs");
        if (arguments.Count > 0)
        {
            builder.Append(" -ArgumentList ");
            for (var i = 0; i < arguments.Count; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append('\'').Append(Escape(arguments[i])).Append('\'');
            }
        }

        return builder.ToString();

        static string Escape(string value) => value.Replace("'", "''");
    }

    /// <summary>PowerShell -EncodedCommand 参数值（UTF-16LE + Base64）。</summary>
    public static string EncodePowerShellCommand(string script) =>
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static string Sanitize(string? value, string fallback)
    {
        var text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length > 0 ? text : fallback;
    }
}
