using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

/// <summary>
/// 节日彩蛋弹窗：每个 S 级节日给一个可点的「小动作」。
/// 春节额外展示官网红包图（图里之后会放支付宝口令），下载失败就只保留文案与按钮。
/// </summary>
public sealed partial class FestivalEggDialog : ContentDialog
{
    private readonly FestivalOccasion _occasion;
    private int _actionIndex = -1;

    public FestivalEggDialog(FestivalOccasion occasion)
    {
        InitializeComponent();
        _occasion = occasion;

        Title = FestivalCopy.DialogTitle(occasion);
        PrimaryButtonText = FestivalCopy.AcceptButton();
        CloseButtonText = FestivalCopy.CloseButton();
        BodyText.Text = FestivalCopy.DialogBody(occasion.Definition);
        ActionButton.Content = FestivalCopy.ActionLabel(occasion.Definition);

        if (occasion.Definition.Id == FestivalCatalog.Spring)
        {
            HintText.Text = FestivalCopy.EnvelopeHint();
            EnvelopeLink.Content = FestivalCopy.EnvelopeLinkLabel();
            EnvelopeLink.Visibility = Visibility.Visible;
            ShowEnvelopeLoading();
            LoadEnvelope();
        }
    }

    private Storyboard? _envelopeLoadingStoryboard;

    private void ShowEnvelopeLoading()
    {
        EnvelopeFrame.Visibility = Visibility.Visible;
        EnvelopeImage.Opacity = 0;
        EnvelopeLoading.Visibility = Visibility.Visible;
        EnvelopeLoadingText.Text = FestivalCopy.EnvelopeLoading();
        EnvelopeRing.IsActive = true;
        HintText.Visibility = Visibility.Collapsed;

        var pulse = new DoubleAnimation
        {
            From = 0.16,
            To = 0.42,
            Duration = TimeSpan.FromSeconds(1.2),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(pulse, EnvelopePulse);
        Storyboard.SetTargetProperty(pulse, "Opacity");
        _envelopeLoadingStoryboard = new Storyboard();
        _envelopeLoadingStoryboard.Children.Add(pulse);
        _envelopeLoadingStoryboard.Begin();
    }

    private void LoadEnvelope()
    {
        var cached = FestivalAssets.TryGetCachedSource();
        if (cached is not null)
        {
            ShowEnvelope(cached);
            return;
        }
        _ = LoadEnvelopeAsync();
    }

    private async Task LoadEnvelopeAsync()
    {
        try
        {
            var image = await FestivalAssets.EnsureAsync();
            if (image is not null)
            {
                ShowEnvelope(image);
                return;
            }
            ShowEnvelopeFailed();
        }
        catch
        {
            ShowEnvelopeFailed();
        }
    }

    private void ShowEnvelope(Microsoft.UI.Xaml.Media.ImageSource source)
    {
        _envelopeLoadingStoryboard?.Stop();
        _envelopeLoadingStoryboard = null;
        EnvelopeLoading.Visibility = Visibility.Collapsed;
        EnvelopeRing.IsActive = false;

        EnvelopeImage.Source = source;
        HintText.Text = FestivalCopy.EnvelopeHint();
        HintText.Visibility = Visibility.Visible;

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(260)
        };
        Storyboard.SetTarget(fade, EnvelopeImage);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Begin();
    }

    /// <summary>云端图拿不到时退回随包的立体红包图，并明确说明「图没取到」。</summary>
    private void ShowEnvelopeFailed()
    {
        _envelopeLoadingStoryboard?.Stop();
        _envelopeLoadingStoryboard = null;
        EnvelopeRing.IsActive = false;
        EnvelopeLoading.Visibility = Visibility.Collapsed;

        var fallback = FestivalAssets.TryGetPropSource();
        if (fallback is not null)
        {
            EnvelopeImage.Source = fallback;
            EnvelopeImage.Opacity = 0.55;
        }
        EnvelopeFrame.Visibility = Visibility.Visible;
        HintText.Text = FestivalCopy.EnvelopeLoadFailed();
        HintText.Visibility = Visibility.Visible;
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        _actionIndex++;
        if (_occasion.Definition.Id == FestivalCatalog.NewYear)
        {
            FestivalService.RequestFireworks();
        }

        ResultText.Text = FestivalCopy.ActionResult(_occasion.Definition, _actionIndex);
        ResultText.Visibility = Visibility.Visible;
    }

    /// <summary>红包图里的口令可能很小，给一个在浏览器里看大图的入口。</summary>
    private async void EnvelopeLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(FestivalAssets.RedEnvelopeUrl));
        }
        catch
        {
            // 没有默认浏览器时保持弹窗不动。
        }
    }
}
