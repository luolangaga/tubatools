using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

public sealed partial class DashboardContentEditor : UserControl
{
    private readonly IReadOnlyList<DashboardEditorOption> _options;
    private readonly ObservableCollection<DashboardEditorCard> _selected = [];
    private bool _updating;
    private bool _narrow;
    private static string L(string key, string fallback) => LocalizationService.L("Dashboard_" + key, fallback);

    public DashboardContentEditor(IReadOnlyList<DashboardEditorOption> options, IReadOnlyList<HomeDashboardCard> cards,
        HomeDashboardCard? selected, double width, double height)
    {
        InitializeComponent();
        _options = options;
        Resize(width, height);
        LibraryHeading.Text = L("Add", "添加工具");
        SearchBox.PlaceholderText = L("SearchTools", "搜索名称、介绍或分类");
        SelectedHeading.Text = L("HomeContent", "首页内容");
        LibraryTab.Text = LibraryHeading.Text; SelectedTab.Text = SelectedHeading.Text;
        RestoreButton.Content = L("RestoreContent", "恢复默认内容");
        OrderHint.Text = L("FeaturedOrderHint", "勾选添加大卡片，拖动调整顺序；选中后可修改名称和介绍。");
        DetailsHeading.Text = L("EditSelectedCard", "编辑选中的卡片");
        CardTitleBox.Header = L("CardTitleLabel", "名称"); CardDescriptionBox.Header = L("CardDescriptionLabel", "介绍");
        EmptySelectionText.Text = L("NoSelectedTools", "尚未添加工具");
        NoResultsText.Text = L("NoMatchingTools", "没有找到匹配的工具");
        MoveUpButton.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, L("MoveUp", "上移"));
        MoveDownButton.SetValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty, L("MoveDown", "下移"));
        ToolTipService.SetToolTip(MoveUpButton, L("MoveUp", "上移")); ToolTipService.SetToolTip(MoveDownButton, L("MoveDown", "下移"));
        CategoryBox.Items.Add(L("AllTools", "全部工具")); CategoryBox.Items.Add(L("BuiltinTools", "内置工具")); CategoryBox.Items.Add(L("ExternalTools", "外部工具"));
        foreach (var category in options.Select(o => o.Category).Distinct().OrderBy(c => c)) CategoryBox.Items.Add(category);
        SelectedList.ItemsSource = _selected;
        SelectedList.DragItemsCompleted += (_, _) => LoadDetails();
        SelectedList.ContainerContentChanging += (_, args) =>
        {
            if (!args.InRecycleQueue && args.Item is DashboardEditorCard card)
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(args.ItemContainer, card.Title);
        };
        ReplaceCards(cards);
        foreach (var option in options) option.PropertyChanged += Option_PropertyChanged;
        CategoryBox.SelectedIndex = 0;
        SelectedList.SelectedItem = selected is null ? _selected.FirstOrDefault() : _selected.FirstOrDefault(c => c.Option.Target == selected.Target);
        LoadDetails();
    }

    public void Resize(double width, double height)
    {
        Width = width; Height = height;
        _narrow = width < 720;
        PaneSelector.Visibility = _narrow ? Visibility.Visible : Visibility.Collapsed;
        EditorGrid.ColumnDefinitions.Clear();
        EditorGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        if (!_narrow) EditorGrid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(SelectedScroll, _narrow ? 0 : 1);
        double bodyHeight = Math.Max(80, height - (_narrow ? 44 : 0));
        LibraryHeading.Visibility = _narrow ? Visibility.Collapsed : Visibility.Visible;
        LibraryPane.RowDefinitions.Clear();
        if (!_narrow) LibraryPane.RowDefinitions.Add(new() { Height = GridLength.Auto });
        LibraryPane.RowDefinitions.Add(new() { Height = GridLength.Auto });
        LibraryPane.RowDefinitions.Add(new() { Height = GridLength.Auto });
        LibraryPane.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(SearchBox, _narrow ? 0 : 1); Grid.SetRow(FilterRow, _narrow ? 1 : 2);
        Grid.SetRow(LibraryResultsBorder, _narrow ? 2 : 3);
        SelectedHeading.Visibility = OrderHint.Visibility = _narrow ? Visibility.Collapsed : Visibility.Visible;
        LibraryPane.Height = bodyHeight;
        SelectedPane.Height = Math.Max(440, bodyHeight);
        ShowPane();
    }

    private void PaneSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) => ShowPane();
    private void ShowPane()
    {
        if (LibraryPane is null || SelectedScroll is null) return;
        bool selected = SelectedTab.IsSelected;
        LibraryPane.Visibility = !_narrow || !selected ? Visibility.Visible : Visibility.Collapsed;
        SelectedScroll.Visibility = !_narrow || selected ? Visibility.Visible : Visibility.Collapsed;
    }

    public List<HomeDashboardCard> GetCards() => _selected.Select(c => new HomeDashboardCard
    {
        Target = c.Option.Target, Featured = true,
        Title = string.IsNullOrWhiteSpace(c.Title) || c.Title.Trim() == c.Option.Name ? null : c.Title.Trim(),
        Description = c.Description.Trim() == c.Option.Description ? null : c.Description.Trim()
    }).ToList();

    private void ReplaceCards(IReadOnlyList<HomeDashboardCard> cards)
    {
        _updating = true;
        _selected.Clear();
        foreach (var card in cards)
        {
            var option = _options.FirstOrDefault(o => o.Target == card.Target);
            if (option is not null) _selected.Add(new(option, card));
        }
        foreach (var option in _options) option.Included = _selected.Any(c => c.Option == option);
        _updating = false;
        SelectedList.SelectedItem = _selected.FirstOrDefault();
        LoadDetails();
    }

    private void Option_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updating || e.PropertyName != nameof(DashboardEditorOption.Included) || sender is not DashboardEditorOption option) return;
        if (option.Included)
        {
            if (_selected.Count >= HomeDashboardContent.MaxCards) { option.Included = false; return; }
            var card = new DashboardEditorCard(option, new() { Target = option.Target, Featured = true });
            _selected.Add(card); SelectedList.SelectedItem = card; SelectedList.ScrollIntoView(card);
        }
        else
        {
            foreach (var card in _selected.Where(c => c.Option == option).ToArray()) _selected.Remove(card);
            SelectedList.SelectedItem ??= _selected.FirstOrDefault();
        }
        LoadDetails();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Filter();
    private void CategoryBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Filter();
    private void Filter()
    {
        if (_options is null) return;
        string query = SearchBox.Text.Trim(); int scope = CategoryBox.SelectedIndex;
        var results = _options.Where(o => (scope <= 0 || (scope == 1 && !o.External) || (scope == 2 && o.External) ||
                (scope >= 3 && o.Category == (string)CategoryBox.SelectedItem)) &&
            (query.Length == 0 || (o.Name + " " + o.Description + " " + o.Category + " " + o.Target).Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToList();
        LibraryList.ItemsSource = results;
        ResultsText.Text = string.Format(L("ToolCount", "{0} 项"), results.Count);
        NoResultsText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectedList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_updating) LoadDetails(); }
    private void LoadDetails()
    {
        _updating = true;
        var card = SelectedList.SelectedItem as DashboardEditorCard;
        CardTitleBox.IsEnabled = CardDescriptionBox.IsEnabled = card is not null;
        CardTitleBox.Text = card?.Title ?? ""; CardDescriptionBox.Text = card?.Description ?? "";
        MoveUpButton.IsEnabled = card is not null && _selected.IndexOf(card) > 0;
        MoveDownButton.IsEnabled = card is not null && _selected.IndexOf(card) < _selected.Count - 1;
        EmptySelectionText.Visibility = _selected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _updating = false;
    }
    private void CardText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || SelectedList.SelectedItem is not DashboardEditorCard card) return;
        card.Title = CardTitleBox.Text; card.Description = CardDescriptionBox.Text;
        if (SelectedList.ContainerFromItem(card) is ListViewItem container)
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(container, card.Title);
    }
    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not DashboardEditorCard card) return;
        _updating = true; _selected.Remove(card);
        card.Option.Included = _selected.Any(c => c.Option == card.Option); _updating = false;
        SelectedList.SelectedItem ??= _selected.FirstOrDefault(); LoadDetails();
    }
    private void RestoreButton_Click(object sender, RoutedEventArgs e) => ReplaceCards(HomeDashboardContent.Defaults());
    private void MoveUpButton_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDownButton_Click(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int delta)
    {
        if (SelectedList.SelectedItem is not DashboardEditorCard card) return;
        int index = _selected.IndexOf(card); int next = index + delta;
        if (next < 0 || next >= _selected.Count) return;
        _updating = true; _selected.Move(index, next); SelectedList.SelectedItem = card; _updating = false;
        SelectedList.ScrollIntoView(card); LoadDetails();
    }
}

public sealed partial class DashboardEditorOption : INotifyPropertyChanged
{
    public required string Target { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Category { get; init; }
    public required string CategoryLabel { get; init; }
    public required string FallbackGlyph { get; init; }
    public bool External { get; init; }
    public Func<Task<ImageSource?>>? LoadIcon { get; init; }
    private ImageSource? _icon;
    private bool _requested, _included;
    public ImageSource? Icon { get { if (!_requested) { _requested = true; _ = ResolveIconAsync(); } return _icon; } }
    public Visibility FallbackVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;
    public bool Included { get => _included; set { if (_included == value) return; _included = value; PropertyChanged?.Invoke(this, new(nameof(Included))); } }
    private async Task ResolveIconAsync()
    {
        try { if (LoadIcon is not null) _icon = await LoadIcon(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Dashboard editor icon] {ex.Message}"); }
        PropertyChanged?.Invoke(this, new(nameof(Icon))); PropertyChanged?.Invoke(this, new(nameof(FallbackVisibility)));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed partial class DashboardEditorCard(DashboardEditorOption option, HomeDashboardCard card) : INotifyPropertyChanged
{
    public DashboardEditorOption Option { get; } = option;
    private string _title = card.Title ?? option.Name;
    public string Title { get => _title; set { _title = value; PropertyChanged?.Invoke(this, new(nameof(Title))); } }
    public string Description { get; set; } = card.Description ?? option.Description;
    private bool _featured = card.Featured == true;
    public bool Featured
    {
        get => _featured;
        set { _featured = value; PropertyChanged?.Invoke(this, new(nameof(Featured))); PropertyChanged?.Invoke(this, new(nameof(LayoutLabel))); }
    }
    public string LayoutLabel => LocalizationService.L(Featured ? "Dashboard_FeatureCard" : "Dashboard_CompactCard",
        Featured ? "大卡片（顶部）" : "小卡片（常用工具）");
    public event PropertyChangedEventHandler? PropertyChanged;
}
