using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class SetupWizardDialog : ContentDialog
{
    private int _currentStep;
    private Storyboard? _transition;

    public SetupWizardDialog()
    {
        InitializeComponent();
        LayoutChoices.SelectedIndex = CompactModeService.IsCompactModeEnabled() ? 1 : 0;
        var backdrop = BackdropService.GetBackdropType().ToString();
        BackdropChoices.SelectedItem = BackdropChoices.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == backdrop)
            ?? BackdropChoices.Items[0];
        WizardCloseToTrayToggle.IsOn = CloseToTrayService.IsEnabled;
        UpdateStepUI();
    }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        ResizeContent();
        XamlRoot.Changed += OnXamlRootChanged;
        _ = SearchPicker.InitializeAsync();
    }

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        SearchPicker.CancelPendingLoad();
        _transition?.Stop();
        if (XamlRoot is not null) XamlRoot.Changed -= OnXamlRootChanged;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ResizeContent();

    private void ResizeContent()
    {
        if (XamlRoot is null) return;
        WizardRoot.Width = Math.Clamp(XamlRoot.Size.Width - 80, 240, 560);
        WizardRoot.Height = Math.Clamp(XamlRoot.Size.Height - 180, 420, 540);
    }

    private void UpdateStepUI(bool animate = false, bool forward = true)
    {
        PreferencesPanel.Visibility = _currentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchPicker.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        StepTitleText.Text = _currentStep == 0 ? "欢迎使用图吧工具箱" : "把常用工具，放进 Windows 搜索";
        StepSubtitleText.Text = _currentStep == 0 ? "先选好外观与使用习惯，马上开始。" : "按 Win 键搜索名称，直接打开你勾选的工具。";
        StepCountText.Text = $"{_currentStep + 1} / 2";
        StepProgress.Value = _currentStep + 1;
        PrimaryButtonText = _currentStep == 0 ? "下一步" : "完成设置";
        SecondaryButtonText = _currentStep == 0 ? "" : "上一步";
        CloseButtonText = "跳过设置";
        UpdateSearchState();
        if (animate) AnimateStep(forward);
    }

    private void AnimateStep(bool forward)
    {
        _transition?.Stop();
        StepSlide.X = 0;
        StepHost.Opacity = 1;
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) return;
        var slide = new DoubleAnimation
        {
            From = forward ? -36 : 36, To = 0, Duration = TimeSpan.FromMilliseconds(240),
            EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, StepSlide);
        Storyboard.SetTargetProperty(slide, "X");
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(180) };
        Storyboard.SetTarget(fade, StepHost);
        Storyboard.SetTargetProperty(fade, "Opacity");
        _transition = new Storyboard();
        _transition.Children.Add(slide);
        _transition.Children.Add(fade);
        _transition.Begin();
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_currentStep == 0)
        {
            args.Cancel = true;
            _currentStep = 1;
            UpdateStepUI(animate: true);
            return;
        }
        if (!SearchPicker.CanApply)
        {
            args.Cancel = true;
            UpdateSearchState();
            return;
        }
        var deferral = args.GetDeferral();
        try
        {
            IsPrimaryButtonEnabled = false;
            ApplyPreferences();
            var result = await SearchPicker.ApplySettingsAsync();
            if (result.Errors.Count > 0)
            {
                args.Cancel = true;
                SearchPicker.ShowRegistrationErrors(result.Errors);
            }
            else AppSettings.Set("SetupCompleted", true);
        }
        finally { UpdateSearchState(); deferral.Complete(); }
    }

    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        _currentStep = 0;
        UpdateStepUI(animate: true, forward: false);
    }

    private void OnCloseButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) =>
        AppSettings.Set("SetupCompleted", true);

    private void SearchPicker_StateChanged(object? sender, EventArgs e) => UpdateSearchState();

    private void UpdateSearchState() => IsPrimaryButtonEnabled = _currentStep == 0 || SearchPicker.CanApply;

    private void ApplyPreferences()
    {
        CompactModeService.SetCompactModeEnabled(LayoutChoices.SelectedIndex == 1);
        if (BackdropChoices.SelectedItem is ComboBoxItem { Tag: string value } && Enum.TryParse<BackdropType>(value, out var backdrop))
            BackdropService.SetBackdropType(backdrop);
        CloseToTrayService.SetEnabled(WizardCloseToTrayToggle.IsOn);
    }
}
