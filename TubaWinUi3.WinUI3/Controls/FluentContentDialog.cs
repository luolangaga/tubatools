using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3.Controls;

/// <summary>Native ContentDialog with the toolbox's shared responsive presentation.</summary>
public class FluentContentDialog : ContentDialog
{
    public FluentContentDialog()
    {
        DefaultStyleKey = typeof(ContentDialog);
        Services.FluentDialogLayout.Attach(this);
    }
}
