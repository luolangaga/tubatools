using System.Text;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// The single browser engine is the built-in WebView2. Its profile is deliberately separate
/// from the user's daily Chrome/Edge data, and every tool result says so.
/// </summary>
internal static class AiBrowserInfo
{
    internal const string Name = "内置 WebView2";
    internal const string Note =
        "工具箱内置浏览器，开箱即用。使用专属用户数据目录，不是用户日常 Chrome/Edge 的配置，不包含其中的登录状态、书签和历史；需要登录站点时在这个窗口登录一次即可保存。";

    internal static string ProfilePath => AiBrowserPaths.WebView2UserData;

    internal const string ResultHeader =
        "[浏览器] 引擎：内置 WebView2（工具箱专属用户数据目录；不是用户日常 Chrome/Edge 的配置，不包含其中的登录状态、书签和历史）";

    internal static string StatusLine => $"{Name} · 用户数据：{ProfilePath}";

    internal static string DescribeForAgent()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# AI 浏览器");
        builder.AppendLine();
        builder.AppendLine($"- 当前引擎：{Name}（唯一引擎，不连接外部浏览器）");
        builder.AppendLine($"- 用户数据目录：`{ProfilePath}`");
        builder.AppendLine($"- 说明：{Note}");
        builder.AppendLine("- 每次 browser_use 工具结果都会标注当前引擎；以工具结果为准。");
        return builder.ToString();
    }
}
