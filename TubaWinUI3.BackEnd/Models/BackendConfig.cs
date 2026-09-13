using System.Text.Json;
using System.Text.Json.Serialization;

namespace TubaWinUI3.BackEnd.Models;

/// <summary>后端配置（由主程序写入，后端 --config 参数读取）。</summary>
public sealed class BackendConfig
{
    /// <summary>
    /// 是否启用「流氓软件右键菜单主动拦截」子系统。默认 true 是为了兼容
    /// 旧版配置文件（无此字段时保持拦截器原行为）；主程序新版写入时会显式赋值。
    /// 两个功能开关互相独立：没开的子系统绝不装配、绝不激活。
    /// </summary>
    public bool EnableIntercept { get; set; } = true;

    /// <summary>是否启用「游戏后台自动监控」子系统（检测到游戏自动显示 FPS 覆盖层）。</summary>
    public bool EnableGameMonitor { get; set; } = false;

    /// <summary>轮询间隔（秒），默认 10。</summary>
    public int PollIntervalSeconds { get; set; } = 10;

    /// <summary>数据目录（拦截状态/事件落盘位置）。</summary>
    public string DataDir { get; set; } = "";

    /// <summary>日志文件路径（为空则不写文件日志）。</summary>
    public string LogFile { get; set; } = "";

    /// <summary>通知模式：always（每次拦截都通知）、batch_only（仅批量时通知）、never（不通知）。</summary>
    public string NotifyMode { get; set; } = "always";

    /// <summary>同一条目两次通知的最小间隔（分钟），默认 30。第三方反复重写时防通知风暴。</summary>
    public int NotifyCooldownMinutes { get; set; } = 30;

    /// <summary>事件日志最大行数，超出后自动删除最旧记录。默认 1000。</summary>
    public int MaxEventRows { get; set; } = 1000;
}

public static class BackendConfigLoader
{
    /// <summary>从配置文件加载；文件不存在或解析失败时返回默认配置。</summary>
    public static BackendConfig Load(string configPath)
    {
        var config = new BackendConfig();
        try
        {
            if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var parsed = JsonSerializer.Deserialize(json, BackEndJsonContext.Default.BackendConfig);
                if (parsed is not null) config = parsed;
            }
        }
        catch
        {
            // 配置损坏时退回默认，保证后端仍能启动
        }
        return config;
    }
}
