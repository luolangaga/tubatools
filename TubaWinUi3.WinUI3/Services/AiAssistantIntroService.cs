using System.Net.NetworkInformation;

namespace TubaWinUi3.Services;

/// <summary>
/// AI 助手「正式上线」引导的判据。
/// 只在升级/安装到当前版本后第一次打开工具箱、且本机有网络时展示一次；
/// 展示过就记入 AppSettings，之后不再打扰。进入过 AI 助手页的用户也不再提示。
/// </summary>
public static class AiAssistantIntroService
{
    /// <summary>已展示版本；有值即表示看过了（只用于记录，不按版本反复展示）。</summary>
    public const string SeenKey = "AiAssistantIntroSeen";

    /// <summary>用户已经打开过 AI 助手页；此时侧边栏角标不再显示。</summary>
    public const string VisitedKey = "AiAssistantVisited";

    /// <summary>
    /// 是否该展示引导：没看过、没主动进过 AI 助手页，并且当前有网络。
    /// 纯逻辑，单元测试覆盖。
    /// </summary>
    public static bool ShouldShow(bool seen, bool visited, bool networkAvailable)
        => networkAvailable && !visited && !seen;

    /// <summary>
    /// 本机是否有可用网络连接。AI 助手依赖在线模型服务，
    /// 离线时展示「欢迎试用」只会让用户点进去碰壁，因此宁可留到下次启动。
    /// </summary>
    public static bool IsNetworkAvailable()
    {
        try
        {
            return NetworkInterface.GetIsNetworkAvailable();
        }
        catch
        {
            return false;
        }
    }

    public static bool HasSeenIntro() => !string.IsNullOrWhiteSpace(AppSettings.Get(SeenKey));

    public static void MarkIntroSeen(Version version) => AppSettings.Set(SeenKey, version.ToString());

    public static bool HasVisitedAssistant() => AppSettings.GetBool(VisitedKey, false);

    public static void MarkAssistantVisited()
    {
        if (HasVisitedAssistant()) return;
        AppSettings.Set(VisitedKey, true);
    }
}
