using TubaWinUi3.PluginSdk;

namespace Tubatools.ClassicTheme;

public sealed class ClassicThemePlugin : ITubaPlugin, IPluginStartup
{
    public void Register(IPluginHost host)
    {
        bool chinese = host.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        host.RegisterTool(new PluginToolDefinition
        {
            Id = "classic-theme",
            Name = chinese ? "经典样式" : "Classic Theme",
            Category = "其他工具",
            Description = chinese
                ? "经典蓝色渐变主题包：深蓝侧栏、浅蓝内容区、半透明白色卡片与白色文字"
                : "Classic blue gradient theme with a dark sidebar, light content area and translucent cards",
            IconPath = "assets/icon.svg",
            Glyph = "\uE790",
            Tags = ["主题", "外观", "经典", "蓝色", "Theme"],
            CreateContent = ClassicThemePage.Create
        });
    }

    public ValueTask StartupAsync(IPluginStartupContext context, CancellationToken ct = default)
        => ClassicThemeRuntime.StartAsync(context, ct);

    public ValueTask ShutdownAsync(CancellationToken ct = default)
        => ClassicThemeRuntime.StopAsync(ct);
}
