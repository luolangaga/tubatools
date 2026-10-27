using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Controls;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class KeyboardTestTool : IBuiltinTool
{
    public string Id => "keyboard-test";
    public string Name => LocalizationService.L("Builtin_keyboard-test_Name", "键盘测试");
    public string Description => LocalizationService.L("Builtin_keyboard-test_Desc", "检测键盘按键是否正常，按键后高亮显示，支持带数字小键盘区的大键盘/无数字区的小键盘(TKL)布局切换，可区分左右 Shift/Ctrl/Alt，支持 Copilot 键。");
    public string Glyph => "\uE92E";
    public string Category => "硬件工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        var keyboardControl = new KeyboardTest3DControl();

        App.MainWindow?.NavigateToToolPage(typeof(ToolContentPage), new ToolContentPageParam
        {
            ToolId = Id,
            Title = "键盘测试",
            Description = "3D 键盘测试 · 按键聚焦，停留 1 秒自动回到全景",
            Glyph = Glyph,
            Content = keyboardControl,
            OnClose = keyboardControl.Cleanup
        });

        return Task.CompletedTask;
    }
}
