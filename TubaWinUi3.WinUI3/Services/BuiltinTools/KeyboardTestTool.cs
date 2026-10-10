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
        var keyboardControl = new KeyboardTestControl();

        var tipText = new TextBlock
        {
            Text = "点击下方键盘区域后开始按键测试，按键会高亮显示，已按过的键会留有浅色标记；大键盘含数字小键盘区，小键盘为无数字区的紧凑布局，可区分左右 Shift/Ctrl/Alt，支持 Copilot 键",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        };

        var root = new StackPanel { Spacing = 14, MaxWidth = 1040 };
        root.Children.Add(tipText);
        root.Children.Add(keyboardControl);

        var content = new ScrollViewer
        {
            Content = root,
            MaxWidth = 1080,
            Padding = new Thickness(24, 0, 24, 24),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        App.MainWindow?.NavigateToToolPage(typeof(ToolContentPage), new ToolContentPageParam
        {
            ToolId = Id,
            Title = "键盘测试",
            Description = "依次按下键盘上的按键，检测每个键位是否正常工作",
            Glyph = Glyph,
            Content = content,
            OnClose = keyboardControl.Cleanup
        });

        return Task.CompletedTask;
    }
}
