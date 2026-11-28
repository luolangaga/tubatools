using TubaWinUi3.PluginSdk;
using Tubatools.Onboarding;

namespace Tubatools.IconStudio;

public sealed class IconStudioPlugin : ITubaPlugin
{
    public void Register(IPluginHost host)
    {
        var chinese = host.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        host.RegisterTool(new PluginToolDefinition
        {
            Id = "icon-studio",
            Name = chinese ? "图标工坊" : "Icon Studio",
            Category = chinese ? "其他工具" : "Other tools",
            Description = chinese
                ? "自定义内置工具卡片、侧栏菜单与消息弹窗图标；支持导入本地图片，并内置远古彩色与 Fluent 字形预设。"
                : "Customize built-in tool cards, sidebar items and dialog icons with local images, plus classic-color and Fluent-glyph presets.",
            IconPath = "assets/icon.svg",
            Glyph = "\uE790",
            Tags = ["图标", "外观", "主题", "预设", "Icon", "Theme"],
            CreateContent = IconStudioPage.Create
        });
        PluginOpenCompat.RegisterToolEntry(host, "icon-studio");
        PluginOnboardingDialog.ScheduleInstallGuide(PluginOnboardingDialog.Guide(
            chinese ? "图标工坊" : "Icon Studio",
            chinese
                ? "内置工具、侧栏菜单和消息弹窗的图标都可以换成自己的图片，也可以一键切回远古彩色或 Fluent 字形。"
                : "Replace built-in tool, sidebar and dialog icons with your own images, or switch back to the classic-color or Fluent-glyph preset.",
            [
                new OnboardingEntry(
                    chinese ? "外观" : "Appearance",
                    chinese ? "图标工坊" : "Icon Studio",
                    chinese
                        ? "在「内置工具」「侧栏菜单」「消息弹窗」三组里逐项换图；全局预设会一次性套用整套图标。"
                        : "Swap individual icons in the Built-in, Sidebar and Dialog groups, or apply a complete preset at once.",
                    "\uE790",
                    chinese ? "打开图标工坊" : "Open Icon Studio",
                    PluginOnboardingDialog.OpenTool("icon-studio"))
            ], chinese));
    }
}
