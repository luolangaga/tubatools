using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

/// <summary>
/// 内置工具页统一页头：左上角返回按钮 + 图标瓦片 + 标题/副标题 + 右侧操作区。
/// 返回按钮采用 WinUI 3 官方独立返回按钮规范（TitleBarBackButtonStyle）；
/// 在 BuiltinToolWindow 独立窗口中不显示（窗口自身标题栏即可关闭）。
/// </summary>
public sealed partial class ToolPageHeader : UserControl
{
    /// <summary>独立工具窗口扩展标题栏（TitleBarHeightOption.Tall）的高度，页头据此避让。</summary>
    private const double ToolWindowTitleBarInset = 48;

    private readonly Brush? _defaultIconBackground;
    private readonly Brush? _defaultIconForeground;

    public ToolPageHeader()
    {
        InitializeComponent();
        _defaultIconBackground = IconTile.Background;
        _defaultIconForeground = IconGlyph.Foreground;
        ActualThemeChanged += (_, _) => ApplyIcon();
    }

    /// <summary>右侧操作区（刷新/重新检测等页面级按钮）。</summary>
    public UIElementCollection Actions => ActionsPanel.Children;

    public static readonly DependencyProperty ToolIdProperty =
        DependencyProperty.Register(nameof(ToolId), typeof(string), typeof(ToolPageHeader),
            new PropertyMetadata("", OnToolIdChanged));

    /// <summary>稳定的内置工具 id；与列表共用彩色 SVG，不依赖标题或当前语言。</summary>
    public string ToolId
    {
        get => (string)GetValue(ToolIdProperty);
        set => SetValue(ToolIdProperty, value);
    }

    private static void OnToolIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ToolPageHeader)d).ApplyIcon();

    private void ApplyIcon()
    {
        // 高对比度使用可跟随系统前景的字体图标。
        var source = ThemeColors.IsSystemHighContrast()
            ? null : BuiltinIconService.Get(ToolId);
        ColorIcon.Source = source;
        ColorIcon.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        IconGlyph.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
        IconTile.Background = source is null ? IconBackground ?? _defaultIconBackground : _defaultIconBackground;
    }

    /// <summary>页面自定义返回逻辑（如未保存更改确认）；未订阅时默认调用 MainWindow.NavigateBack()。</summary>
    public event EventHandler? BackRequested;

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ToolPageHeader),
            new PropertyMetadata("", OnTitleChanged));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(ToolPageHeader),
            new PropertyMetadata("", OnSubtitleChanged));

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(ToolPageHeader),
            new PropertyMetadata("", OnGlyphChanged));

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>图标瓦片底色；留空则使用主题强调色。</summary>
    public static readonly DependencyProperty IconBackgroundProperty =
        DependencyProperty.Register(nameof(IconBackground), typeof(Brush), typeof(ToolPageHeader),
            new PropertyMetadata(null, OnIconBackgroundChanged));

    public Brush? IconBackground
    {
        get => (Brush?)GetValue(IconBackgroundProperty);
        set => SetValue(IconBackgroundProperty, value);
    }

    /// <summary>图标前景色；留空则使用强调色前景（TextOnAccent）。</summary>
    public static readonly DependencyProperty IconForegroundProperty =
        DependencyProperty.Register(nameof(IconForeground), typeof(Brush), typeof(ToolPageHeader),
            new PropertyMetadata(null, OnIconForegroundChanged));

    public Brush? IconForeground
    {
        get => (Brush?)GetValue(IconForegroundProperty);
        set => SetValue(IconForegroundProperty, value);
    }

    /// <summary>页头内边距；页面自身已有整页 Padding 时可设为 0 避免双重缩进。</summary>
    public static readonly DependencyProperty HeaderPaddingProperty =
        DependencyProperty.Register(nameof(HeaderPadding), typeof(Thickness), typeof(ToolPageHeader),
            new PropertyMetadata(new Thickness(24, 24, 24, 20), OnHeaderPaddingChanged));

    public Thickness HeaderPadding
    {
        get => (Thickness)GetValue(HeaderPaddingProperty);
        set => SetValue(HeaderPaddingProperty, value);
    }

    private static void OnHeaderPaddingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ToolPageHeader)d).ApplyHostLayout();

    /// <summary>是否显示页头底部 1px 分隔线。</summary>
    public static readonly DependencyProperty ShowDividerProperty =
        DependencyProperty.Register(nameof(ShowDivider), typeof(bool), typeof(ToolPageHeader),
            new PropertyMetadata(false, OnShowDividerChanged));

    public bool ShowDivider
    {
        get => (bool)GetValue(ShowDividerProperty);
        set => SetValue(ShowDividerProperty, value);
    }

    /// <summary>是否允许显示返回按钮（独立窗口模式下始终不显示）。</summary>
    public static readonly DependencyProperty ShowBackButtonProperty =
        DependencyProperty.Register(nameof(ShowBackButton), typeof(bool), typeof(ToolPageHeader),
            new PropertyMetadata(true, OnShowBackButtonChanged));

    public bool ShowBackButton
    {
        get => (bool)GetValue(ShowBackButtonProperty);
        set => SetValue(ShowBackButtonProperty, value);
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ToolPageHeader)d).TitleText.Text = e.NewValue as string ?? "";

    private static void OnSubtitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ToolPageHeader)d).SubtitleText.Text = e.NewValue as string ?? "";

    private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ToolPageHeader)d).IconGlyph.Glyph = e.NewValue as string ?? "";

    private static void OnIconBackgroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var header = (ToolPageHeader)d;
        header.ApplyIcon();
    }

    private static void OnIconForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var header = (ToolPageHeader)d;
        header.IconGlyph.Foreground = e.NewValue as Brush ?? header._defaultIconForeground;
    }

    private static void OnShowDividerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ToolPageHeader)d).RootGrid.BorderThickness = (bool)e.NewValue ? new Thickness(0, 0, 0, 1) : new Thickness(0);

    private static void OnShowBackButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ToolPageHeader)d).ApplyHostLayout();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyHostLayout();
        ApplyIcon();
    }

    private void OnColorIconFailed(object sender, ExceptionRoutedEventArgs e)
    {
        ColorIcon.Visibility = Visibility.Collapsed;
        IconGlyph.Visibility = Visibility.Visible;
    }

    private void OnLogoEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => FluentMotion.AnimateIcon(IconTile, true);

    private void OnLogoExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => FluentMotion.AnimateIcon(IconTile, false);

    private void OnLogoClick(object sender, RoutedEventArgs e)
        => FluentMotion.AnimateIcon(IconTile, false, celebrate: true);

    private void OnHeaderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 900;
        Grid.SetRow(ActionsPanel, compact ? 1 : 0);
        Grid.SetColumn(ActionsPanel, compact ? 0 : 3);
        Grid.SetColumnSpan(ActionsPanel, compact ? 4 : 1);
        ActionsPanel.Margin = compact && Actions.Count > 0 ? new Thickness(0, 16, 0, 0) : new Thickness(0);
    }

    /// <summary>
    /// 按宿主调整页头：独立工具窗口隐藏返回按钮（交给窗口标题栏），
    /// 并整体下移避让扩展出的 Tall 标题栏，避免页头与系统标题按钮重叠。
    /// </summary>
    private void ApplyHostLayout()
    {
        var inToolWindow = BuiltinToolWindow.IsInToolWindow(this);

        BackButton.Visibility = ShowBackButton && !inToolWindow
            ? Visibility.Visible
            : Visibility.Collapsed;

        var padding = HeaderPadding;
        RootGrid.Padding = inToolWindow
            ? new Thickness(padding.Left, padding.Top + ToolWindowTitleBarInset, padding.Right, padding.Bottom)
            : padding;
    }

    private void OnBackButtonClick(object sender, RoutedEventArgs e)
    {
        if (BackRequested is not null)
            BackRequested(this, EventArgs.Empty);
        else
            App.MainWindow?.NavigateBack();
    }
}
