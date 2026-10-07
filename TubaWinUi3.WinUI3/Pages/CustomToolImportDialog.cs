using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>
/// 导入自定义工具弹窗（替代已删除的 CustomToolManagerWindow 的导入对话框）。
/// 由 HomePage 拖入文件后弹出；分类从 ToolCatalog（Tools 目录 + tools.json 排序）读取，
/// 可选择已有分类或新建分类（含图标）。
/// </summary>
public sealed class CustomToolImportDialog : ContentDialog
{
    /// <summary>分类下拉框里的「新建分类」哨兵项。</summary>
    private sealed class NewCategoryItem
    {
        public override string ToString() => "＋ 新建分类…";
    }

    private static readonly string[] ArchOptions = ["自动检测", "x64", "x86", "ARM64"];

    private static readonly (string Label, string Glyph)[] CategoryIcons =
    [
        ("工具", "\uE8B7"),
        ("处理器", "\uEEA1"),
        ("显卡", "\uF211"),
        ("显示器", "\uE7F4"),
        ("硬盘", "\uEDA2"),
        ("内存", "\uEEA0"),
        ("外设", "\uE962"),
        ("游戏", "\uE7FC"),
        ("烤鸡", "\uEC4A"),
        ("声卡", "\uE7F5"),
        ("网卡", "\uEDA3"),
        ("综合", "\uEC4E"),
        ("其他", "\uE712"),
        ("文件夹", "\uE8B7"),
        ("星标", "\uE734"),
        ("齿轮", "\uE713"),
        ("代码", "\uE943"),
        ("下载", "\uE896"),
        ("上传", "\uE898"),
        ("保存", "\uE74E"),
        ("编辑", "\uE70F"),
        ("搜索", "\uE721"),
        ("终端", "\uE756"),
        ("数据库", "\uEFC6"),
        ("安全", "\uE730"),
        ("网络", "\uE968"),
        ("系统", "\uE977"),
        ("磁盘", "\uEDA2"),
        ("USB", "\uE88E"),
        ("电源", "\uE83E"),
    ];

    private readonly string _packagePath;
    private readonly IReadOnlyList<ImportableExecutable> _executables;

    private readonly TextBox _toolNameBox;
    private readonly ComboBox _categoryComboBox;
    private readonly TextBox _categoryBox;
    private readonly StackPanel _newCategoryPanel;
    private readonly GridView _iconGridView;
    private readonly TextBox _customGlyphBox;
    private readonly FontIcon _glyphPreview;
    private readonly ComboBox _primaryComboBox;
    private readonly ComboBox _archComboBox;
    private readonly ListView _variantsList;
    private readonly TextBox _descriptionBox;
    private readonly TextBox _publisherBox;
    private readonly TextBox _tagsBox;

    /// <summary>确认后为 true：本次导入将使用新建分类。</summary>
    public bool IsNewCategory { get; private set; }

    /// <summary>新建分类时选定的图标字形（未选时返回默认文件夹图标）。</summary>
    public string? NewCategoryGlyph { get; private set; }

    public CustomToolImportDialog(XamlRoot xamlRoot, string packagePath, IReadOnlyList<ImportableExecutable> executables)
    {
        _packagePath = packagePath;
        _executables = executables;

        XamlRoot = xamlRoot;
        Title = "导入自定义工具";
        PrimaryButtonText = "导入";
        CloseButtonText = "取消";
        DefaultButton = ContentDialogButton.Primary;
        RequestedTheme = ThemeService.CurrentElementTheme;

        _toolNameBox = new TextBox
        {
            Header = "工具名称",
            Text = Path.GetFileNameWithoutExtension(executables[0].FileName),
            PlaceholderText = "例如 CPU-Z"
        };

        var categories = ToolCatalog.GetCategories();
        _categoryBox = new TextBox
        {
            Header = "分类",
            Text = categories.FirstOrDefault() ?? "其他工具",
            PlaceholderText = "例如 处理器工具"
        };

        var comboItems = categories.Cast<object>().ToList();
        comboItems.Add(new NewCategoryItem());

        _categoryComboBox = new ComboBox
        {
            Header = "已有分类",
            ItemsSource = comboItems,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var iconItems = CategoryIcons.Select(o => new { o.Label, o.Glyph }).ToList();
        _iconGridView = new GridView
        {
            ItemsSource = iconItems,
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 140,
            Padding = new Thickness(0, 8, 0, 0)
        };
        _iconGridView.ItemTemplate = CreateIconItemTemplate();

        _glyphPreview = new FontIcon
        {
            FontSize = 24,
            Glyph = "\uE8B7",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var glyphPreviewBorder = new Border
        {
            Width = 40,
            Height = 40,
            Background = Application.Current.Resources["SubtleFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
            CornerRadius = new CornerRadius(8),
            Child = _glyphPreview,
            VerticalAlignment = VerticalAlignment.Center
        };

        _customGlyphBox = new TextBox
        {
            Header = "自定义图标符号",
            PlaceholderText = "例如 E700 或 工具",
            Width = 200
        };
        _customGlyphBox.TextChanged += (_, _) =>
        {
            var text = _customGlyphBox.Text.Trim();
            if (TryParseGlyph(text, out var g))
            {
                _glyphPreview.Glyph = g;
                _iconGridView.SelectedItem = null;
            }
        };

        _iconGridView.SelectionChanged += (_, _) =>
        {
            if (_iconGridView.SelectedItem is not null)
            {
                _customGlyphBox.Text = "";
                var glyph = (string)_iconGridView.SelectedItem.GetType().GetProperty("Glyph")!.GetValue(_iconGridView.SelectedItem)!;
                _glyphPreview.Glyph = glyph;
            }
        };

        var glyphInputRow = new Grid { ColumnSpacing = 8 };
        glyphInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        glyphInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(glyphPreviewBorder, 0);
        glyphInputRow.Children.Add(glyphPreviewBorder);
        Grid.SetColumn(_customGlyphBox, 1);
        glyphInputRow.Children.Add(_customGlyphBox);

        _newCategoryPanel = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed,
            Children =
            {
                new TextBlock { Text = "选择图标", FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] },
                _iconGridView,
                new TextBlock { Text = "或自定义输入", FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] },
                glyphInputRow
            }
        };

        // 事件订阅与初始选中放在所有控件构建之后：SelectedIndex 赋值会同步触发
        // SelectionChanged（含「新建分类」哨兵分支），此时字段必须已就绪
        _categoryComboBox.SelectionChanged += (_, _) =>
        {
            if (_categoryComboBox.SelectedItem is string category)
            {
                _categoryBox.Text = category;
                HideNewCategoryPanel();
            }
            else if (_categoryComboBox.SelectedItem is NewCategoryItem)
            {
                // 新建分类：分类名由下方文本框输入，并展开图标选择
                _categoryBox.Text = "";
                _categoryBox.PlaceholderText = "输入新分类名称";
                _newCategoryPanel.Visibility = Visibility.Visible;
                _categoryBox.Focus(FocusState.Programmatic);
            }
        };
        // 已有分类默认选中第一个；没有任何分类时（items 里只有哨兵项）自动进入「新建分类」模式
        _categoryComboBox.SelectedIndex = 0;

        _primaryComboBox = new ComboBox
        {
            ItemsSource = executables,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _archComboBox = new ComboBox
        {
            Header = "目标架构",
            ItemsSource = ArchOptions,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        _variantsList = new ListView
        {
            Header = "多架构文件",
            ItemsSource = executables,
            SelectionMode = ListViewSelectionMode.Multiple,
            MaxHeight = 180
        };

        _descriptionBox = new TextBox
        {
            Header = "简介",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            PlaceholderText = "输入工具用途、特点或注意事项"
        };

        _publisherBox = new TextBox
        {
            Header = "作者/发布者",
            PlaceholderText = "可选"
        };

        _tagsBox = new TextBox
        {
            Header = "标签",
            PlaceholderText = "用逗号分隔，例如 CPU, 跑分, 稳定性测试"
        };

        var content = new ScrollViewer
        {
            MaxHeight = 620,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    _toolNameBox,
                    _categoryComboBox,
                    _categoryBox,
                    _newCategoryPanel,
                    new TextBlock { Text = "主程序", FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] },
                    _primaryComboBox,
                    _archComboBox,
                    _variantsList,
                    _descriptionBox,
                    _publisherBox,
                    _tagsBox
                }
            }
        };

        Content = content;
    }

    private void HideNewCategoryPanel()
    {
        _newCategoryPanel.Visibility = Visibility.Collapsed;
        _categoryBox.PlaceholderText = "例如 处理器工具";
    }

    /// <summary>显示弹窗并收集输入；取消或校验失败时返回 null。</summary>
    public async Task<CustomToolImportRequest?> ShowImportAsync()
    {
        if (await ShowAsync() != ContentDialogResult.Primary)
            return null;

        if (_categoryComboBox.SelectedItem is NewCategoryItem)
        {
            IsNewCategory = true;
            NewCategoryGlyph = ResolveGlyph();
        }

        var category = _categoryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            await ShowMessageAsync("请填写分类", "需要指定工具放入的分类，或选择「＋ 新建分类…」输入新分类名称。");
            return null;
        }

        if (_primaryComboBox.SelectedItem is not ImportableExecutable primary)
        {
            await ShowMessageAsync("请选择主程序", "需要指定一个 exe 作为打开工具时运行的主程序。");
            return null;
        }

        var selectedVariants = _variantsList.SelectedItems
            .OfType<ImportableExecutable>()
            .Select(item => new ImportArchVariant(item.EntryPath, GuessArch(item.EntryPath)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Arch))
            .ToList();

        var manualArch = _archComboBox.SelectedIndex switch
        {
            1 => "x64",
            2 => "x86",
            3 => "ARM64",
            _ => null
        };

        if (manualArch is not null && !selectedVariants.Any(v => v.EntryPath.Equals(primary.EntryPath, StringComparison.OrdinalIgnoreCase)))
        {
            selectedVariants.Add(new ImportArchVariant(primary.EntryPath, manualArch));
        }

        var tags = _tagsBox.Text
            .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new CustomToolImportRequest(
            _packagePath,
            _toolNameBox.Text,
            category,
            primary.EntryPath,
            _descriptionBox.Text,
            _publisherBox.Text,
            tags,
            selectedVariants);
    }

    private string ResolveGlyph()
    {
        var customText = _customGlyphBox.Text.Trim();
        if (!string.IsNullOrEmpty(customText) && TryParseGlyph(customText, out var customGlyph))
            return customGlyph;

        if (_iconGridView.SelectedItem is not null)
            return (string)_iconGridView.SelectedItem.GetType().GetProperty("Glyph")!.GetValue(_iconGridView.SelectedItem)!;

        return "\uE8B7";
    }

    private static string GuessArch(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Contains("arm64", StringComparison.OrdinalIgnoreCase))
            return "ARM64";
        if (name.Contains("x64", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("64", StringComparison.OrdinalIgnoreCase))
            return "x64";
        if (name.Contains("x86", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("32", StringComparison.OrdinalIgnoreCase))
            return "x86";
        return "";
    }

    private static bool TryParseGlyph(string text, out string glyph)
    {
        glyph = "";
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("U+", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("\\u", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out var code) && code is > 0 and <= 0xFFFF)
            {
                glyph = char.ConvertFromUtf32(code);
                return true;
            }
            return false;
        }

        if (int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var directCode) && directCode is > 0 and <= 0xFFFF)
        {
            glyph = char.ConvertFromUtf32(directCode);
            return true;
        }

        if (text.Length == 1)
        {
            glyph = text;
            return true;
        }

        return false;
    }

    private static DataTemplate CreateIconItemTemplate()
    {
        var xaml = """
            <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <Border Width='48' Height='48' Background='{ThemeResource SubtleFillColorSecondaryBrush}' CornerRadius='8' Padding='8'>
                    <FontIcon FontSize='24' Glyph='{Binding Glyph}' HorizontalAlignment='Center' VerticalAlignment='Center' />
                </Border>
            </DataTemplate>
            """;
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "确定",
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        await dialog.ShowAsync();
    }
}