using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;

namespace TubaWinUi3;

/// <summary>
/// AI 助手「正式上线」引导：侧边栏入口旁的角标 + 首次启动的 TeachingTip。
/// 只在没看过、且当前有网络时展示一次；看过或进过 AI 助手页后角标自动消失。
/// </summary>
public sealed partial class MainWindow
{
    private bool _aiIntroShowing;

    private void InitializeAiAssistantIntro()
    {
        ConfigureAiAssistantIntroTip();
        ApplyAiAssistantBadge();
    }

    /// <summary>侧边栏角标：用 InfoBadge 的小圆点提示「这里多了个新东西」，进过页面就收起。</summary>
    private void ApplyAiAssistantBadge()
    {
        var show = !AiAssistantIntroService.HasVisitedAssistant() && !AiAssistantIntroService.HasSeenIntro();
        AiAssistantNavItem.InfoBadge = show ? new InfoBadge { Value = -1 } : null;
    }

    private void ConfigureAiAssistantIntroTip()
    {
        AiAssistantIntroTip.Title = LocalizationService.L("AiIntro_Title", "AI 助手正式上线");
        AiAssistantIntroTip.Subtitle = LocalizationService.L("AiIntro_Subtitle", "免费、开箱即用");
        AiAssistantIntroBody.Text = LocalizationService.L(
            "AiIntro_Body",
            "不只是问答。蓝屏、卡顿、驱动、设置，一句话查到底；降压超频、装软件、改设置，直接上手帮你操作；做作业、操作浏览器、控制电脑，也都接得住。真能动手，把活干完。");
        AiAssistantIntroTip.ActionButtonContent = LocalizationService.L("AiIntro_Action", "去试试");
        AiAssistantIntroTip.CloseButtonContent = LocalizationService.L("AiIntro_Close", "以后再说");
    }

    /// <summary>
    /// 首次使用工具箱（含刚更新到本版本）时弹一次引导。
    /// 由启动画面淡出后与首次向导关闭后各调用一次，内部自行判重：
    /// 未联网、已看过、已进过 AI 助手页、向导还没结束都不会弹。
    /// </summary>
    public async Task MaybeShowAiAssistantIntroAsync()
    {
        if (_aiIntroShowing) return;
        if (AiAssistantIntroTip.IsOpen) return;
        if (!AiAssistantIntroService.ShouldShow(
                AiAssistantIntroService.HasSeenIntro(),
                AiAssistantIntroService.HasVisitedAssistant(),
                AiAssistantIntroService.IsNetworkAvailable()))
            return;
        if (App.IsExiting || _exitRequested) return;
        if (SplashOverlay.Visibility == Visibility.Visible) return;
        if (AppSettings.Get("SetupCompleted") == null) return;
        if (!AppWindow.IsVisible) return;
        if (NavFrame.Content is AiAssistantPage) return;

        _aiIntroShowing = true;
        try
        {
            // 等分类菜单/默认页完成一次布局，TeachingTip 才能贴着导航项定位
            await Task.Delay(300);
            if (App.IsExiting || _exitRequested || NavFrame.Content is AiAssistantPage) return;
            if (AiAssistantIntroService.HasSeenIntro() || AiAssistantIntroService.HasVisitedAssistant()) return;

            ConfigureAiAssistantIntroTip();
            AiAssistantIntroTip.Target = AiAssistantNavItem;
            AiAssistantIntroTip.IsOpen = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AiIntro] 展示引导失败（已忽略）: {ex.Message}");
        }
        finally
        {
            _aiIntroShowing = false;
        }
    }

    /// <summary>无论点「去试试」「以后再说」还是点空白处收起，都算看过，不再打扰。</summary>
    private void AiAssistantIntroTip_Closed(TeachingTip sender, TeachingTipClosedEventArgs args)
    {
        AiAssistantIntroService.MarkIntroSeen(UpdateService.CurrentVersion);
        ApplyAiAssistantBadge();
    }

    private void AiAssistantIntroTip_ActionClick(TeachingTip sender, object args)
    {
        sender.IsOpen = false;
        if (NavFrame.Content is not AiAssistantPage)
            NavFrame.Navigate(typeof(AiAssistantPage));
    }

    internal void OnAiAssistantVisited()
    {
        AiAssistantIntroService.MarkAssistantVisited();
        ApplyAiAssistantBadge();
        if (AiAssistantIntroTip.IsOpen)
            AiAssistantIntroTip.IsOpen = false;
    }
}
