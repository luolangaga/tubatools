namespace TubaWinUi3.Services.Plugins;

/// <summary>
/// 插件能力声明。插件与宿主同进程运行，这里**不是沙箱**——它是明示的权限清单：
/// 插件在 tubatools.json 的 <c>permissions</c> 里列出真正需要的能力，宿主按清单放行其余宿主服务；
/// 用户也能在插件页看到每个插件要求了什么。
///
/// 兼容口径：<c>permissions</c> 缺省（null）= 放行全部非 fullTrust 能力，这样已有的插件
/// 不写清单也照常工作；写成空数组 <c>[]</c> = 一个都不给，是明确的最小权限。
/// <c>fullTrust</c> 仍然覆盖全部能力（它本来就是「等同宿主代码」）。
/// </summary>
internal static class PluginCapabilities
{
    internal const string Background = "background";
    internal const string Navigation = "navigation";
    internal const string Appearance = "appearance";
    internal const string Diagnostics = "diagnostics";
    internal const string Tools = "tools";
    internal const string Process = "process";
    internal const string Clipboard = "clipboard";
    internal const string Files = "files";
    internal const string Uri = "uri";

    /// <summary>全部已知能力（也是清单校验的白名单）。</summary>
    internal static IReadOnlyList<string> All { get; } =
        [Background, Navigation, Appearance, Diagnostics, Tools, Process, Clipboard, Files, Uri];

    /// <summary>能力的中文说明，用于插件页展示与校验报错。</summary>
    internal static string Describe(string capability) => capability switch
    {
        Background => "修改工具箱背景",
        Navigation => "自定义侧栏导航与页面跳转",
        Appearance => "修改主题、导航布局与紧凑模式",
        Diagnostics => "读取硬件与传感器诊断数据",
        Tools => "枚举工具箱内的工具列表",
        Process => "启动工具箱里的其他工具（外部进程）",
        Clipboard => "写入剪贴板",
        Files => "弹出文件与文件夹选择器",
        Uri => "打开外部链接",
        _ => capability
    };

    /// <summary>
    /// 清单校验：未知能力名必须拒绝。静默忽略会让作者以为权限已声明（实际没生效），
    /// 而静默全放行又等于把拼错的权限名变成提权。
    /// </summary>
    internal static void Validate(IReadOnlyList<string>? permissions)
    {
        if (permissions is null) return;
        foreach (var permission in permissions)
        {
            if (string.IsNullOrWhiteSpace(permission) || !All.Contains(permission, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"未知的插件能力「{permission}」。可用值：{string.Join(", ", All)}。");
        }
    }

    /// <summary>规范化（去空白、小写、去重），写入清单时用。</summary>
    internal static IReadOnlyList<string> Normalize(IReadOnlyList<string>? permissions) =>
        permissions is null
            ? []
            : permissions.Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    /// <summary>
    /// 是否放行某项能力。<paramref name="declared"/> 为 null 表示插件没有声明清单（历史行为）。
    /// </summary>
    internal static bool IsAllowed(IReadOnlyList<string>? declared, bool fullTrust, string capability)
        => fullTrust
           || declared is null
           || declared.Contains(capability, StringComparer.OrdinalIgnoreCase);

    /// <summary>拒绝时抛出的统一异常：消息直接展示给插件作者，说明该怎么改清单。</summary>
    internal static Exception Denied(string capability) => new InvalidOperationException(
        $"插件没有声明「{capability}」（{Describe(capability)}）能力，宿主已拒绝本次调用。请在 tubatools.json 的 permissions 中加入 \"{capability}\"。");
}
