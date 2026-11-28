using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 托盘图标的鼠标回调语义。Win32 消息循环与 Explorer 在单测里跑不起来，所以这里覆盖的是
/// 「哪个鼠标消息对应哪个动作」这条纯映射 —— 它决定用户点一下托盘图标到底有没有反应：
/// 左键单击必须直接恢复主界面（不能只认双击），右键才是菜单。
/// </summary>
public class TrayClickTests
{
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;

    /// <summary>核心用例：单击一次就该把主界面叫回来，不能要求双击。</summary>
    [Fact]
    public void LeftButtonUp_RestoresMainWindow()
    {
        Assert.Equal(TrayIconService.TrayMouseAction.RestoreMainWindow,
            TrayIconService.TrayMouseActionFor(WM_LBUTTONUP));
    }

    /// <summary>双击继续恢复：双击序列里第一下 WM_LBUTTONUP 已经恢复，这里只是兜底。</summary>
    [Fact]
    public void LeftButtonDoubleClick_RestoresMainWindow()
    {
        Assert.Equal(TrayIconService.TrayMouseAction.RestoreMainWindow,
            TrayIconService.TrayMouseActionFor(WM_LBUTTONDBLCLK));
    }

    [Fact]
    public void RightButtonUp_OpensMenu()
    {
        Assert.Equal(TrayIconService.TrayMouseAction.ShowMenu,
            TrayIconService.TrayMouseActionFor(WM_RBUTTONUP));
    }

    /// <summary>键盘选择托盘图标时系统补发 WM_CONTEXTMENU：菜单里也有「打开主窗口」，可达性不丢。</summary>
    [Fact]
    public void KeyboardContextMenu_OpensMenu()
    {
        Assert.Equal(TrayIconService.TrayMouseAction.ShowMenu,
            TrayIconService.TrayMouseActionFor(WM_CONTEXTMENU));
    }

    [Theory]
    [InlineData(0x0200)] // WM_MOUSEMOVE
    [InlineData(0x0201)] // WM_LBUTTONDOWN
    [InlineData(0x0204)] // WM_RBUTTONDOWN
    [InlineData(0x0000)] // WM_NULL（弹完菜单补发的那条）
    public void OtherMessages_DoNothing(uint message)
    {
        Assert.Equal(TrayIconService.TrayMouseAction.None, TrayIconService.TrayMouseActionFor(message));
    }
}
