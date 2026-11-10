using TubaWinUi3.PluginSdk;

namespace Tubatools.Windows11Optimizer;

public sealed class Windows11OptimizerPlugin : ITubaPlugin
{
    public void Register(IPluginHost host)
    {
        var chinese = host.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        host.RegisterTool(new PluginToolDefinition
        {
            Id = "windows11-optimizer",
            Name = chinese ? "Windows 11 优化" : "Windows 11 Optimizer",
            Category = "系统工具",
            Description = chinese
                ? "去快捷方式箭头与盾牌、隐私遥测、性能游戏、Windows 更新、电源声音、通知建议与预装应用清理"
                : "Shortcut arrow/shield removal, privacy, performance, updates, power, notifications and preinstalled app cleanup",
            IconPath = "assets/icon.svg",
            Glyph = "\uE713",
            Tags = ["Windows 11", "Windows 10", "优化", "去箭头", "去盾牌", "隐私", "遥测", "性能", "游戏", "更新", "电源", "通知", "预装应用"],
            CreateContent = Windows11OptimizerView.Create,
            InitializeAsync = view =>
            {
                if (view.Root is Windows11OptimizerView optimizer) optimizer.Attach(view);
                return Task.CompletedTask;
            }
        });
    }
}
