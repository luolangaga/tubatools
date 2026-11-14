using System.Text.Json;

namespace TubaWinUi3.Services.Plugins;

/// <summary>
/// 会话退出标记：每次启动先写下「本次尚未正常退出」，走到 <see cref="MarkClean"/> 才算干净退出。
///
/// 判据只有「上一条记录是否干净」，不数次数——这比「连续失败 N 次」准确得多：
/// 用户手动杀进程、蓝屏、插件把主线程拖死，全都表现为没有走到 MarkClean。
/// 文件缺失（首次运行）按干净处理，不会第一次启动就弹恢复提示。
/// </summary>
internal static class PluginSessionMarker
{
    internal sealed record State(bool CleanExit, DateTimeOffset Utc);

    internal static string DefaultPath => Path.Combine(PluginManager.Root, "session.json");
    internal static string PathOverride { get; set; } = "";

    private static string FilePath => PathOverride.Length > 0 ? PathOverride : DefaultPath;

    /// <summary>
    /// 读取上一次会话是否正常退出，并立即把本次标记为「进行中」。
    /// 必须在启动早期调用一次，否则本次崩溃时不会留下任何痕迹。
    /// </summary>
    internal static bool BeginSession()
    {
        var unclean = LastExitWasUnclean();
        Write(clean: false);
        return unclean;
    }

    /// <summary>上一次是否异常退出；标记文件不存在（首次运行）或损坏时返回 false。</summary>
    internal static bool LastExitWasUnclean()
    {
        try
        {
            if (!File.Exists(FilePath)) return false;
            return JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) is { CleanExit: false };
        }
        catch
        {
            return false;
        }
    }

    internal static void MarkClean() => Write(clean: true);

    private static void Write(bool clean)
    {
        // 写失败只会让下次少一次恢复提示，绝不能因此让启动失败。
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(new State(clean, DateTimeOffset.UtcNow)));
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        catch { }
    }
}
