using Microsoft.UI.Xaml.Settings;

namespace TubaWinUi3;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // These process-wide switches lock at Application.Start; App() is already too late.
        // Keep an explicit list so SDK updates do not silently opt into unrelated changes.
        XamlOptionalChanges.EnableChange(XamlChangeId.IconNoGridOptimization);
        XamlOptionalChanges.EnableChange(XamlChangeId.OptimizeApplyStyles);
        XamlOptionalChanges.EnableChange(XamlChangeId.DefaultStyleOptimizations);

        // Reuse WinUI's generated COM, dispatcher and App initialization for every launch mode.
        XamlGeneratedProgram.XamlGeneratedMain();
    }
}
