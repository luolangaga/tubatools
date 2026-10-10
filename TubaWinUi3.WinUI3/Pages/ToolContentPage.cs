using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Controls;

namespace TubaWinUi3.Pages;

public sealed class ToolContentPageParam
{
    public string ToolId { get; init; } = "";
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public string Glyph { get; init; } = "";
    public required UIElement Content { get; init; }
    public Action? OnClose { get; init; }
}

/// <summary>
/// Hosts tool UIs that are built in code (no dedicated XAML page).
/// The tool content fills the page below the standard <see cref="ToolPageHeader"/>.
/// </summary>
public sealed partial class ToolContentPage : Page
{
    private readonly ToolPageHeader _header;
    private readonly Grid _contentHost;
    private Action? _onClose;

    public ToolContentPage()
    {
        InitializeComponent();

        _header = new ToolPageHeader();

        _contentHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_header, 0);
        Grid.SetRow(_contentHost, 1);
        root.Children.Add(_header);
        root.Children.Add(_contentHost);

        Content = root;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is ToolContentPageParam param)
        {
            _onClose = param.OnClose;
            _header.ToolId = param.ToolId;
            _header.Title = param.Title;
            _header.Subtitle = param.Description;
            _header.Glyph = param.Glyph;
            _contentHost.Children.Add(param.Content);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Detach();
    }

    /// <summary>
    /// 执行与 OnNavigatedFrom 相同的清理逻辑。宿主（如独立工具窗口）在窗口关闭时
    /// 调用它，因为关闭窗口不会触发 Frame 的导航事件。
    /// </summary>
    public void Detach()
    {
        _onClose?.Invoke();
        _onClose = null;
        _contentHost.Children.Clear();
    }
}
