using Microsoft.UI.Xaml.Media.Animation;
using TubaWinUi3.Pages;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3;

public sealed partial class MainWindow
{
    /// <summary>The AI conversation page is the visible navigation page.</summary>
    internal bool IsAssistantPageActive => NavFrame.Content is AiAssistantPage;

    public void OpenAssistantInteraction(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            RestoreFromTray();
            NavFrame.Navigate(typeof(AiAssistantPage), new AgentAssistantFocusRequest(sessionId), new DrillInNavigationTransitionInfo());
            ActivateFromExternal();
        });
    }
}
