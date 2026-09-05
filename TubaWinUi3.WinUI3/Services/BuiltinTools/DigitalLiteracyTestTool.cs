using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class DigitalLiteracyTestTool : IBuiltinTool
{
    public string Id => "digital-literacy-test";
    public string Name => "电子文盲测试";
    public string Description => "测试你的电脑基础知识水平，看看你是不是「电子文盲」！共 25 道选择题，满分 100 分，答对得分。";
    public string Glyph => "\uE9CE";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        App.MainWindow?.NavigateToToolPage(typeof(DigitalLiteracyTestPage));
        return Task.CompletedTask;
    }
}
