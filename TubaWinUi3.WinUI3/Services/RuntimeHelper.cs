namespace TubaWinUi3.Services;

public static class RuntimeHelper
{
    private static readonly bool _isMsixPackaged = DetectMsixPackaged();
    private static readonly bool _isLiteBuild = DetectLiteBuild();
    private static readonly bool _isInstalled = DetectInstalled();
    private static readonly bool _msixAdminSession = DetectMsixAdminSession();

    public static bool IsMsixPackaged => _isMsixPackaged;

    /// <summary>
    /// 是否为 MSIX 提权重启出来的会话：打包版里的「以管理员身份重新启动」经 PowerShell 载体以
    /// runas 拉起自身（打包进程无法直接 ShellExecute runas，会 0x32 失败），提权进程会丢失包身份，
    /// 因此用命令行标记（--msix-admin-session）+ 环境变量（TUBA_MSIX_LOCALSTATE）显式恢复打包语义。
    /// </summary>
    public static bool MsixAdminSession => _msixAdminSession;

    /// <summary>
    /// 打包上下文：物理 MSIX 打包，或由打包版提权重启的会话。凡「商店版应如何表现」的行为判定
    /// （自更新跳过、社区工具隐藏、Tools/数据根、主动拦截不启动等）都走这里，而不是 IsMsixPackaged。
    /// </summary>
    public static bool IsPackagedContext => _isMsixPackaged || _msixAdminSession;

    public static bool IsLiteBuild => _isLiteBuild;

    public static bool IsInstalled => _isInstalled;

    private static bool DetectMsixPackaged()
    {
        try
        {
            var _ = Windows.ApplicationModel.Package.Current;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectMsixAdminSession()
    {
        try
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, ShellIntegration.FileLockShellMenuContract.MsixAdminSessionArg,
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string? _localAppDataRoot;

    /// <summary>
    /// 包身份解析失败、数据根目录回滚到共享 %LocalAppData% 的标志。
    /// MSIX 下发生回滚时 ToolsRoot/数据目录会指向非打包路径（可能与旧安装版互相污染），
    /// 启动时据此输出诊断日志。
    /// </summary>
    public static bool LocalAppDataRootUsedFallback { get; private set; }

    /// <summary>
    /// 应用数据根目录（即 %LocalAppData%\TubaWinUi3 中的 %LocalAppData%）。
    /// 优先使用包身份(ApplicationData.Current.LocalFolder)解析 —— 它基于包身份，
    /// 即使进程以管理员身份(提权)运行也依然返回包内目录；而
    /// Environment.GetFolderPath(LocalApplicationData) 在提权后可能丢失
    /// 已知文件夹重定向、错误地返回真实用户目录，导致下载/扫描/打开指向不同位置。
    /// 若包身份解析失败（无包身份 / 路径无效 / 不可访问），自动回滚到 KnownFolder 方式。
    /// </summary>
    public static string GetLocalAppDataRoot()
    {
        if (_localAppDataRoot is not null) return _localAppDataRoot;

        if (_msixAdminSession)
        {
            // 提权重启会话：沿用打包版的 LocalState（重启命令经环境变量传入，
            // 见 App.TryRelaunchElevated），设置/工具/元数据与打包版完全共享；
            // 环境变量缺失/非法时才回滚到 KnownFolder，并置回滚标志供启动诊断。
            var elevatedRoot = GetElevatedLocalStateOverride();
            if (elevatedRoot is not null)
            {
                _localAppDataRoot = elevatedRoot;
                return elevatedRoot;
            }

            LocalAppDataRootUsedFallback = true;
        }

        if (_isMsixPackaged)
        {
            try
            {
                var path = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path))
                {
                    _localAppDataRoot = path;
                    return path;
                }
                System.Diagnostics.Debug.WriteLine("[RuntimeHelper] 包身份路径无效，回滚到 KnownFolder 解析");
                LocalAppDataRootUsedFallback = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RuntimeHelper] 包身份解析失败({ex.GetType().Name}: {ex.Message})，回滚到 KnownFolder 解析");
                LocalAppDataRootUsedFallback = true;
            }
        }

        _localAppDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return _localAppDataRoot;
    }

    private static string? GetElevatedLocalStateOverride()
    {
        try
        {
            var value = Environment.GetEnvironmentVariable(ShellIntegration.FileLockShellMenuContract.LocalStateEnvVar);
            if (!string.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value))
                return value;
        }
        catch
        {
        }

        return null;
    }

    private static bool DetectLiteBuild()
    {
        if (_isMsixPackaged) return false;

        try
        {
            var markerPath = Path.Combine(AppContext.BaseDirectory, ".lite_build");
            return File.Exists(markerPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectInstalled()
    {
        if (_isMsixPackaged) return true;

        try
        {
            var markerPath = Path.Combine(AppContext.BaseDirectory, ".installed");
            return File.Exists(markerPath);
        }
        catch
        {
            return false;
        }
    }
}
