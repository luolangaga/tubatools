using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Tubatools.ClassicTheme;

internal static class ClassicThemePalette
{
    private static Color C(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    public static readonly Color White = C(255, 255, 255, 255);
    public static readonly Color Transparent = C(0, 0, 0, 0);
    public static readonly Color TextSecondary = C(230, 255, 255, 255);
    public static readonly Color TextTertiary = C(179, 255, 255, 255);
    public static readonly Color TextDisabled = C(102, 255, 255, 255);
    public static readonly Color CardFill = C(48, 255, 255, 255);
    public static readonly Color CardFillSecondary = C(34, 255, 255, 255);
    public static readonly Color CardStroke = C(64, 255, 255, 255);
    public static readonly Color ControlFill = C(38, 255, 255, 255);
    public static readonly Color ControlFillSecondary = C(26, 255, 255, 255);
    public static readonly Color ControlStroke = C(64, 255, 255, 255);
    public static readonly Color ControlStrokeSecondary = C(38, 255, 255, 255);
    public static readonly Color Divider = C(51, 255, 255, 255);

    public static LinearGradientBrush RootGradient() => new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(1, 1),
        GradientStops =
        {
            new GradientStop { Color = C(255, 11, 95, 165), Offset = 0 },
            new GradientStop { Color = C(255, 14, 127, 192), Offset = 0.55 },
            new GradientStop { Color = C(255, 44, 181, 240), Offset = 1 }
        }
    };

    public static LinearGradientBrush TitleGradient() => new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(1, 0),
        GradientStops =
        {
            new GradientStop { Color = C(255, 14, 127, 192), Offset = 0 },
            new GradientStop { Color = C(255, 41, 182, 246), Offset = 1 }
        }
    };

    public static LinearGradientBrush NavGradient() => new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops =
        {
            new GradientStop { Color = C(255, 10, 79, 140), Offset = 0 },
            new GradientStop { Color = C(255, 30, 136, 229), Offset = 1 }
        }
    };

    public static SolidColorBrush Brush(Color color) => new(color);

    public static ResourceDictionary CreateResources()
    {
        var resources = new ResourceDictionary();
        var light = CreateBrushes();
        var dark = CreateBrushes();
        var highContrast = CreateBrushes();
        resources.ThemeDictionaries["Light"] = light;
        resources.ThemeDictionaries["Dark"] = dark;
        resources.ThemeDictionaries["HighContrast"] = highContrast;
        foreach (var key in light.Keys) resources[key] = light[key];
        return resources;
    }

    private static ResourceDictionary CreateBrushes()
    {
        var d = new ResourceDictionary();
        void Solid(string key, Color color) => d[key] = new SolidColorBrush(color);
        void Gradient(string key, LinearGradientBrush brush) => d[key] = brush;

        Solid("ApplicationPageBackgroundThemeBrush", Transparent);

        Solid("TextFillColorPrimaryBrush", White);
        Solid("TextFillColorSecondaryBrush", TextSecondary);
        Solid("TextFillColorTertiaryBrush", TextTertiary);
        Solid("TextFillColorDisabledBrush", TextDisabled);
        Solid("TextFillColorInverseBrush", C(255, 8, 52, 92));

        Solid("AccentTextFillColorPrimaryBrush", C(255, 225, 245, 254));
        Solid("AccentTextFillColorSecondaryBrush", C(255, 179, 229, 252));
        Solid("AccentTextFillColorTertiaryBrush", C(255, 129, 212, 250));
        Solid("TextOnAccentFillColorPrimaryBrush", White);
        Solid("TextOnAccentFillColorSecondaryBrush", TextSecondary);
        Solid("TextOnAccentFillColorDisabledBrush", TextDisabled);

        Solid("CardBackgroundFillColorDefaultBrush", CardFill);
        Solid("CardBackgroundFillColorSecondaryBrush", CardFillSecondary);
        Solid("CardStrokeColorDefaultBrush", CardStroke);

        Solid("ControlFillColorDefaultBrush", ControlFill);
        Solid("ControlFillColorSecondaryBrush", ControlFillSecondary);
        Solid("ControlFillColorTertiaryBrush", C(13, 255, 255, 255));
        Solid("ControlFillColorDisabledBrush", C(13, 255, 255, 255));
        Solid("ControlFillColorTransparentBrush", Transparent);
        Solid("ControlFillColorInputActiveBrush", C(51, 255, 255, 255));
        Solid("ControlStrokeColorDefaultBrush", ControlStroke);
        Solid("ControlStrokeColorSecondaryBrush", ControlStrokeSecondary);
        Solid("ControlStrokeColorTertiaryBrush", C(26, 255, 255, 255));
        Solid("ControlStrongStrokeColorDefaultBrush", C(102, 255, 255, 255));
        Solid("ControlStrongFillColorDefaultBrush", C(51, 255, 255, 255));

        Solid("ButtonForeground", White);
        Solid("ButtonForegroundPointerOver", White);
        Solid("ButtonForegroundPressed", White);
        Solid("ButtonForegroundDisabled", TextDisabled);
        Solid("ButtonBackground", C(38, 255, 255, 255));
        Solid("ButtonBackgroundPointerOver", C(51, 255, 255, 255));
        Solid("ButtonBackgroundPressed", C(64, 255, 255, 255));
        Solid("ButtonBackgroundDisabled", C(13, 255, 255, 255));
        Solid("ButtonBorderBrush", ControlStroke);
        Solid("ButtonBorderBrushPointerOver", ControlStroke);
        Solid("ButtonBorderBrushPressed", ControlStroke);
        Solid("ButtonBorderBrushDisabled", Transparent);

        Solid("TextControlForeground", White);
        Solid("TextControlForegroundPointerOver", White);
        Solid("TextControlForegroundFocused", White);
        Solid("TextControlForegroundDisabled", TextDisabled);
        Solid("TextControlBackground", C(38, 255, 255, 255));
        Solid("TextControlBackgroundPointerOver", C(51, 255, 255, 255));
        Solid("TextControlBackgroundFocused", C(38, 255, 255, 255));
        Solid("TextControlBackgroundDisabled", C(13, 255, 255, 255));
        Solid("TextControlBorderBrush", ControlStroke);
        Solid("TextControlBorderBrushPointerOver", C(89, 255, 255, 255));
        Solid("TextControlBorderBrushFocused", C(140, 255, 255, 255));
        Solid("TextControlBorderBrushDisabled", Transparent);
        Solid("TextControlPlaceholderForeground", C(153, 255, 255, 255));
        Solid("TextControlPlaceholderForegroundPointerOver", C(179, 255, 255, 255));
        Solid("TextControlPlaceholderForegroundFocused", C(179, 255, 255, 255));

        Solid("SubtleFillColorSecondaryBrush", C(26, 255, 255, 255));
        Solid("SubtleFillColorTertiaryBrush", C(13, 255, 255, 255));
        Solid("SubtleFillColorTransparentBrush", Transparent);
        Solid("SubtleFillColorDisabledBrush", C(13, 255, 255, 255));

        Solid("DividerStrokeColorDefaultBrush", Divider);
        Solid("LayerFillColorDefaultBrush", C(26, 255, 255, 255));
        Solid("LayerFillColorAltBrush", C(20, 0, 0, 0));

        Solid("SolidBackgroundFillColorBaseBrush", C(255, 8, 58, 102));
        Solid("SolidBackgroundFillColorSecondaryBrush", C(255, 10, 78, 138));
        Solid("SolidBackgroundFillColorTertiaryBrush", C(255, 14, 111, 184));
        Solid("SolidBackgroundFillColorQuarternaryBrush", C(255, 6, 46, 82));
        Solid("SolidBackgroundFillColorTransparentBrush", Transparent);

        Solid("AcrylicBackgroundFillColorDefaultBrush", C(230, 10, 78, 138));
        Solid("AcrylicBackgroundFillColorBaseBrush", C(230, 8, 58, 102));
        Solid("SmokeFillColorDefaultBrush", C(102, 0, 31, 58));

        Solid("FocusStrokeColorOuterBrush", White);
        Solid("FocusStrokeColorInnerBrush", C(255, 8, 52, 92));

        Gradient("NavigationViewDefaultPaneBackground", NavGradient());
        Gradient("NavigationViewExpandedPaneBackground", NavGradient());
        Gradient("NavigationViewTopPaneBackground", NavGradient());
        Solid("NavigationViewContentBackground", Transparent);
        Solid("NavigationViewItemForeground", White);
        Solid("NavigationViewItemForegroundPointerOver", White);
        Solid("NavigationViewItemForegroundPressed", White);
        Solid("NavigationViewItemForegroundSelected", White);
        Solid("NavigationViewItemForegroundSelectedPointerOver", White);
        Solid("NavigationViewItemForegroundSelectedPressed", White);
        Solid("NavigationViewItemForegroundDisabled", C(102, 255, 255, 255));
        Solid("NavigationViewItemBackground", Transparent);
        Solid("NavigationViewItemBackgroundPointerOver", C(31, 255, 255, 255));
        Solid("NavigationViewItemBackgroundPressed", C(51, 255, 255, 255));
        Solid("NavigationViewItemBackgroundSelected", C(26, 255, 255, 255));
        Solid("NavigationViewItemBackgroundSelectedPointerOver", C(36, 255, 255, 255));
        Solid("NavigationViewItemBackgroundSelectedPressed", C(36, 255, 255, 255));
        Solid("NavigationViewItemBackgroundDisabled", Transparent);
        Solid("NavigationViewItemSeparatorForeground", C(51, 255, 255, 255));
        Solid("NavigationViewSelectionIndicatorForeground", Transparent);

        Solid("TitleBarBackground", Transparent);
        Solid("TitleBarForeground", White);
        Solid("TitleBarInactiveBackground", Transparent);
        Solid("TitleBarInactiveForeground", C(179, 255, 255, 255));
        Solid("TitleBarButtonBackground", Transparent);
        Solid("TitleBarButtonForeground", White);
        Solid("TitleBarButtonHoverBackground", C(51, 255, 255, 255));
        Solid("TitleBarButtonHoverForeground", White);
        Solid("TitleBarButtonPressedBackground", C(77, 255, 255, 255));
        Solid("TitleBarButtonPressedForeground", White);
        Solid("TitleBarButtonInactiveBackground", Transparent);
        Solid("TitleBarButtonInactiveForeground", C(179, 255, 255, 255));
        Solid("TitleBarBackButtonBackground", Transparent);
        Solid("TitleBarBackButtonForeground", White);
        Solid("TitleBarBackButtonHoverBackground", C(51, 255, 255, 255));
        Solid("TitleBarBackButtonHoverForeground", White);
        Solid("TitleBarBackButtonPressedBackground", C(77, 255, 255, 255));
        Solid("TitleBarBackButtonPressedForeground", White);

        Solid("ContentDialogBackground", C(242, 10, 78, 138));
        Solid("ContentDialogBorderBrush", C(64, 255, 255, 255));
        Solid("ContentDialogTopOverlay", Transparent);
        Solid("ContentDialogSeparatorBorderBrush", Divider);
        Solid("FlyoutPresenterBackground", C(242, 10, 78, 138));
        Solid("FlyoutPresenterBorderBrush", C(64, 255, 255, 255));
        Solid("MenuFlyoutPresenterBackground", C(242, 10, 78, 138));
        Solid("MenuFlyoutPresenterBorderBrush", C(64, 255, 255, 255));
        Solid("MenuFlyoutItemBackgroundPointerOver", C(31, 255, 255, 255));
        Solid("MenuFlyoutItemBackgroundPressed", C(51, 255, 255, 255));
        Solid("ToolTipBackground", C(242, 8, 58, 102));
        Solid("ToolTipForeground", White);
        Solid("ToolTipBorderBrush", C(64, 255, 255, 255));

        return d;
    }
}
