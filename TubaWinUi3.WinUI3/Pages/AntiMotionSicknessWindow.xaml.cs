using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class AntiMotionSicknessWindow : Page
{
    private AntiMotionSicknessConfig _cfg;
    private bool _suppressEvents = true;

    public AntiMotionSicknessWindow()
    {
        InitializeComponent();

        _cfg = AntiMotionSicknessConfig.Load();
        LoadConfigToUI();
        UpdateOverlayStatus(AntiMotionSicknessOverlay.IsRunning);

        _suppressEvents = false;

        // 代码构建的画刷不会随主题自动刷新，切换主题后按当前状态重渲染
        ActualThemeChanged += (_, _) => UpdateOverlayStatus(AntiMotionSicknessOverlay.IsRunning);

        Unloaded += OnPageUnloaded;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (!App.IsLiteMode)
        {
            AntiMotionSicknessOverlay.CloseOverlay();
        }
        SaveConfigFromUI();
    }

    private void LoadConfigToUI()
    {
        OpacitySlider.Value = _cfg.Opacity;
        OpacityText.Text = $"{(int)_cfg.Opacity}%";

        ShowCenterToggle.IsOn = _cfg.ShowCenter;
        ShowTopToggle.IsOn = _cfg.ShowTop;
        ShowBottomToggle.IsOn = _cfg.ShowBottom;
        ShowLeftToggle.IsOn = _cfg.ShowLeft;
        ShowRightToggle.IsOn = _cfg.ShowRight;

        CenterColorPicker.Color = _cfg.CenterColor;
        CenterSizeSlider.Value = _cfg.CenterSize;
        CenterSizeText.Text = ((int)_cfg.CenterSize).ToString();
        CenterThicknessSlider.Value = _cfg.CenterThickness;
        CenterThicknessText.Text = ((int)_cfg.CenterThickness).ToString();
        CenterStyleCombo.SelectedIndex = (int)_cfg.CenterStyle;

        EdgeColorPicker.Color = _cfg.EdgeColor;
        EdgeSizeSlider.Value = _cfg.EdgeSize;
        EdgeSizeText.Text = ((int)_cfg.EdgeSize).ToString();
        EdgeShapeCombo.SelectedIndex = (int)_cfg.EdgeShape;

        ForceTopmostToggle.IsOn = AppSettings.GetBool("AntiMotionSickness_ForceTopmost", false);
    }

    private void SaveConfigFromUI()
    {
        try
        {
            _cfg.Opacity = OpacitySlider.Value;

            _cfg.ShowCenter = ShowCenterToggle.IsOn;
            _cfg.ShowTop = ShowTopToggle.IsOn;
            _cfg.ShowBottom = ShowBottomToggle.IsOn;
            _cfg.ShowLeft = ShowLeftToggle.IsOn;
            _cfg.ShowRight = ShowRightToggle.IsOn;

            _cfg.CenterColor = CenterColorPicker.Color;
            _cfg.CenterSize = CenterSizeSlider.Value;
            _cfg.CenterThickness = CenterThicknessSlider.Value;
            _cfg.CenterStyle = (CrosshairStyle)CenterStyleCombo.SelectedIndex;

            _cfg.EdgeColor = EdgeColorPicker.Color;
            _cfg.EdgeSize = EdgeSizeSlider.Value;
            _cfg.EdgeShape = (EdgeMarkerShape)EdgeShapeCombo.SelectedIndex;

            _cfg.Save();

            if (AntiMotionSicknessOverlay.IsRunning)
                AntiMotionSicknessOverlay.RefreshVisuals();
        }
        catch { }
    }

    private void Slider_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressEvents) return;

        OpacityText.Text = $"{(int)OpacitySlider.Value}%";
        CenterSizeText.Text = ((int)CenterSizeSlider.Value).ToString();
        CenterThicknessText.Text = ((int)CenterThicknessSlider.Value).ToString();
        EdgeSizeText.Text = ((int)EdgeSizeSlider.Value).ToString();

        SaveConfigFromUI();
    }

    private void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        SaveConfigFromUI();
    }

    private void Color_Changed(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_suppressEvents) return;
        SaveConfigFromUI();
    }

    private void Combo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        SaveConfigFromUI();
    }

    private void OverlayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        if (OverlayToggle.IsOn)
            StartOverlay();
        else
            StopOverlay();
    }

    private void ToggleOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (AntiMotionSicknessOverlay.IsRunning)
            StopOverlay();
        else
            StartOverlay();
    }

    private void StartOverlay()
    {
        SaveConfigFromUI();

        var forceTopmost = AppSettings.GetBool("AntiMotionSickness_ForceTopmost", false);
        AntiMotionSicknessOverlay.ForceTopmostMode = forceTopmost;

        try
        {
            AntiMotionSicknessOverlay.ShowOverlay();
        }
        catch { }
        UpdateOverlayStatus(AntiMotionSicknessOverlay.IsRunning);
    }

    private void StopOverlay()
    {
        try
        {
            AntiMotionSicknessOverlay.CloseOverlay();
        }
        catch { }
        UpdateOverlayStatus(false);
    }

    private void UpdateOverlayStatus(bool running)
    {
        _suppressEvents = true;
        OverlayToggle.IsOn = running;
        _suppressEvents = false;

        if (running)
        {
            StatusIcon.Glyph = "\uE73E";
            StatusIcon.Foreground = new SolidColorBrush(ThemeColors.AccentGreen);
            StatusTitle.Text = "辅助器运行中";
            StatusDesc.Text = "屏幕准星和标记已开启，不影响鼠标键盘操作";
            ToggleOverlayIcon.Glyph = "\uE71A";
            ToggleOverlayText.Text = "关闭防晕3D辅助";
        }
        else
        {
            StatusIcon.Glyph = "\uE894";
            StatusIcon.Foreground = new SolidColorBrush(ThemeColors.Neutral);
            StatusTitle.Text = "辅助器已关闭";
            StatusDesc.Text = "点击下方按钮开启屏幕准星辅助";
            ToggleOverlayIcon.Glyph = "\uE73E";
            ToggleOverlayText.Text = "开启防晕3D辅助";
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _cfg = new AntiMotionSicknessConfig();
        _cfg.Save();

        _suppressEvents = true;
        LoadConfigToUI();
        _suppressEvents = false;

        if (AntiMotionSicknessOverlay.IsRunning)
            AntiMotionSicknessOverlay.RefreshVisuals();
    }

    private void ForceTopmostToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;

        var forceTopmost = ForceTopmostToggle.IsOn;
        AppSettings.Set("AntiMotionSickness_ForceTopmost", forceTopmost);
        AntiMotionSicknessOverlay.ForceTopmostMode = forceTopmost;

        if (AntiMotionSicknessOverlay.IsRunning)
        {
            AntiMotionSicknessOverlay.CloseOverlay();
            AntiMotionSicknessOverlay.ShowOverlay();
        }
    }

    private void LiteModeButton_Click(object sender, RoutedEventArgs e)
    {
        App.IsLiteMode = true;

        if (!AntiMotionSicknessOverlay.IsRunning)
        {
            StartOverlay();
        }

        if (AntiMotionSicknessOverlay.IsRunning)
        {
            SaveConfigFromUI();
            TrayIconService.Show("游戏防晕3D", AntiMotionSicknessOverlay.CloseOverlay);
            App.MainWindow?.Close();
        }
    }
}
