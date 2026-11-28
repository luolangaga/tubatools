using System.Text.Json;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 主动拦截通知（进程内版）：只负责把通知请求写入数据目录，
/// 由主程序的 <c>ActiveInterceptService</c> 文件监视器统一弹 Windows Toast。
/// 不再注册 COM 服务器、不再另起 <c>--toast</c> 子进程 —— 运行时本身就在主程序里。
/// </summary>
public static class InterceptNotifier
{
    /// <summary>新拦截通知。</summary>
    public static void NotifyNewBlock(string dataDir, string itemName, string subKey, string exePath)
    {
        var body = $"已自动屏蔽：{itemName}";
        if (!string.IsNullOrWhiteSpace(exePath))
            body += $"\n程序：{Truncate(exePath, 50)}";
        body += $"\n位置：{Truncate(subKey, 60)}";
        body += "\n点击查看详情并审核";
        WriteNotificationFile(dataDir, "图吧工具箱CE · 主动拦截", body);
    }

    /// <summary>纠偏重新拦截通知。</summary>
    public static void NotifyReblock(string dataDir, string itemName, string subKey)
    {
        var body = $"被屏蔽项重新出现，已再次拦截：{itemName}" +
                   $"\n位置：{Truncate(subKey, 60)}" +
                   "\n点击查看详情";
        WriteNotificationFile(dataDir, "图吧工具箱CE · 主动拦截纠偏", body);
    }

    /// <summary>写入通知请求文件（原子落盘由调用方监视目录读取后删除）。</summary>
    public static string WriteNotificationFile(string dataDir, string title, string body)
    {
        try
        {
            var notifDir = Path.Combine(dataDir, "active_intercept", "notifications");
            Directory.CreateDirectory(notifDir);

            var request = new NotificationRequest
            {
                Title = title,
                Body = body,
                TimestampUtc = DateTime.UtcNow.ToString("o"),
            };
            var json = JsonSerializer.Serialize(
                request, BackEndJsonContext.Default.NotificationRequest);
            var file = Path.Combine(notifDir,
                $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}"[..24] + ".json");
            File.WriteAllText(file, json);
            BackEndLog.Info($"已写入通知文件：{Path.GetFileName(file)}");
            return file;
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"写通知文件失败：{ex.Message}");
            return "";
        }
    }

    private static string Truncate(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLen ? value : "…" + value[^maxLen..];
    }
}

/// <summary>通知请求（写入 JSON 文件，主程序读取后弹 Toast）。</summary>
public sealed class NotificationRequest
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
}
