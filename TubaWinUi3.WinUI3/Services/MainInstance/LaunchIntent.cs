namespace TubaWinUi3.Services;

/// <summary>启动意图：新进程希望主实例做什么（首次启动与「重复启动转发」共用同一套解析）。</summary>
public enum LaunchIntentKind
{
    /// <summary>普通启动（无导航参数）：只恢复并置前窗口。</summary>
    Normal,

    /// <summary>--open-builtin &lt;id&gt;：直达内置工具并自动执行。</summary>
    OpenBuiltin,

    /// <summary>--file-lock &lt;路径&gt;：打开「文件占用查看」并预填路径。</summary>
    FileLock,

    /// <summary>--show-active-intercept：跳转「主动拦截」审核页。</summary>
    ActiveIntercept,

    /// <summary>--game-overlay-auto：后端检测到游戏自动拉起（不得抢焦点，子进程静默退出）。</summary>
    GameOverlayAuto,
}

/// <summary>解析出的启动意图。<see cref="Arg"/> 仅 OpenBuiltin / FileLock 有值。</summary>
public sealed record LaunchIntent(LaunchIntentKind Kind, string? Arg = null)
{
    /// <summary>命令行开关：本次启动由提权重启而来，需等待旧实例退出后接管。</summary>
    public const string TakeoverArg = "--takeover";

    public const string OpenBuiltinArg = "--open-builtin";
    public const string FileLockArg = "--file-lock";
    public const string ActiveInterceptArg = "--show-active-intercept";
    public const string GameOverlayAutoArg = "--game-overlay-auto";

    /// <summary>
    /// 从命令行（不含 argv[0] 或包含均可）解析导航意图。
    /// 优先级与 <c>App.OnLaunchedCore</c> 的内联判定保持一致：
    /// --file-lock &gt; --open-builtin &gt; --show-active-intercept &gt; --game-overlay-auto &gt; 普通。
    /// 各开关大小写不敏感；带值的开关取紧随其后的 token（去引号）。
    /// </summary>
    public static LaunchIntent Parse(IEnumerable<string> args)
    {
        var list = args as IReadOnlyList<string> ?? args.ToList();

        var fileLock = FindValue(list, FileLockArg);
        if (fileLock is not null) return new LaunchIntent(LaunchIntentKind.FileLock, fileLock);

        var openBuiltin = FindValue(list, OpenBuiltinArg);
        if (openBuiltin is not null) return new LaunchIntent(LaunchIntentKind.OpenBuiltin, openBuiltin);

        if (Has(list, ActiveInterceptArg)) return new LaunchIntent(LaunchIntentKind.ActiveIntercept);
        if (Has(list, GameOverlayAutoArg)) return new LaunchIntent(LaunchIntentKind.GameOverlayAuto);

        return new LaunchIntent(LaunchIntentKind.Normal);
    }

    /// <summary>命令行是否带 --takeover（提权重启会话需接管旧实例）。</summary>
    public static bool HasTakeover(IEnumerable<string> args) => Has(args as IReadOnlyList<string> ?? args.ToList(), TakeoverArg);

    private static bool Has(IReadOnlyList<string> args, string flag)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (IsFlag(args[i], flag)) return true;
        }
        return false;
    }

    /// <summary>查找带值开关：命中且后面还有非空 token 时返回该值，否则返回 null。</summary>
    private static string? FindValue(IReadOnlyList<string> args, string flag)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (!IsFlag(args[i], flag)) continue;
            if (i + 1 >= args.Count) return null;

            var value = args[i + 1].Trim().Trim('"');
            return value.Length == 0 ? null : value;
        }
        return null;
    }

    private static bool IsFlag(string token, string flag) =>
        string.Equals(token.Trim().Trim('"'), flag, StringComparison.OrdinalIgnoreCase);
}
