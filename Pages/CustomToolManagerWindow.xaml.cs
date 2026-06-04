using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace TubaWinUi3.Pages;

public sealed partial class CustomToolManagerWindow : Window
{
    private readonly List<CategoryViewModel> _categories = [];
    private readonly Dictionary<string, bool> _expandedStates = new(StringComparer.CurrentCultureIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public string lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_NOCHANGEDIR = 0x00000008;

    public CustomToolManagerWindow()
    {
        InitializeComponent();

        AppWindow.Title = "自定义工具管理";
        AppWindow.Resize(new SizeInt32(900, 680));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        ThemeService.ApplySavedTheme();

        LoadCategories();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LoadCategories()
    {
        _categories.Clear();
        CategoryPanel.Children.Clear();

        var categories = ToolCatalog.GetCategories();
        foreach (var category in categories)
        {
            var tools = ToolCatalog.GetTools(category);
            var vm = new CategoryViewModel(category, tools);
            _categories.Add(vm);
        }

        RebuildUI();
    }

    private void SaveExpandedStates()
    {
        foreach (var child in CategoryPanel.Children)
        {
            if (child is Border border && border.Child is Expander expander && border.Tag is string name)
            {
                _expandedStates[name] = expander.IsExpanded;
            }
        }
    }

    private void RebuildUI()
    {
        SaveExpandedStates();
        CategoryPanel.Children.Clear();

        for (var i = 0; i < _categories.Count; i++)
        {
            var vm = _categories[i];
            var categoryBorder = CreateCategoryBorder(vm, i);
            CategoryPanel.Children.Add(categoryBorder);
        }
    }

    private Border CreateCategoryBorder(CategoryViewModel vm, int index)
    {
        var glyph = MainWindow.GetCategoryGlyphStatic(vm.Name);
        var toolCount = vm.Tools.Count;

        var expander = new Expander
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = _expandedStates.TryGetValue(vm.Name, out var expanded) ? expanded : index == 0
        };

        var headerGrid = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(0)
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        var iconBorder = new Border
        {
            Width = 32,
            Height = 32,
            Background = Application.Current.Resources["SubtleFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Glyph = glyph }
        };
        Grid.SetColumn(iconBorder, 0);
        headerGrid.Children.Add(iconBorder);

        var nameText = new TextBlock
        {
            Text = vm.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameText, 1);
        headerGrid.Children.Add(nameText);

        var countText = new TextBlock
        {
            Text = $"{toolCount} 个工具",
            Opacity = 0.68,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(countText, 2);
        headerGrid.Children.Add(countText);

        var moveUpBtn = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Tag = index,
            Content = new FontIcon { FontSize = 10, Glyph = "\uE70E" }
        };
        moveUpBtn.Click += MoveCategoryUp_Click;
        ToolTipService.SetToolTip(moveUpBtn, "上移");
        if (index == 0) moveUpBtn.IsEnabled = false;
        Grid.SetColumn(moveUpBtn, 3);
        headerGrid.Children.Add(moveUpBtn);

        var moveDownBtn = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Tag = index,
            Content = new FontIcon { FontSize = 10, Glyph = "\uE70D" }
        };
        moveDownBtn.Click += MoveCategoryDown_Click;
        ToolTipService.SetToolTip(moveDownBtn, "下移");
        if (index == _categories.Count - 1) moveDownBtn.IsEnabled = false;
        Grid.SetColumn(moveDownBtn, 4);
        headerGrid.Children.Add(moveDownBtn);

        var deleteBtn = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Tag = vm.Name,
            Content = new FontIcon { FontSize = 10, Glyph = "\uE74D" }
        };
        deleteBtn.Click += DeleteCategory_Click;
        ToolTipService.SetToolTip(deleteBtn, "删除分类");
        Grid.SetColumn(deleteBtn, 5);
        headerGrid.Children.Add(deleteBtn);

        expander.Header = headerGrid;

        var contentPanel = new StackPanel
        {
            Spacing = 6,
            Padding = new Thickness(8, 8, 8, 4)
        };

        var toolGrid = new GridView
        {
            ItemsSource = vm.Tools,
            SelectionMode = ListViewSelectionMode.None,
            CanDragItems = true,
            CanReorderItems = true,
            AllowDrop = true,
            Tag = vm.Name
        };
        toolGrid.DragItemsCompleted += ToolGrid_DragItemsCompleted;

        var toolItemTemplate = CreateToolItemTemplate();
        toolGrid.ItemTemplate = toolItemTemplate;

        contentPanel.Children.Add(toolGrid);

        if (toolCount == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "此分类下暂无工具",
                Opacity = 0.52,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 8)
            };
            contentPanel.Children.Add(emptyText);
        }

        expander.Content = contentPanel;

        var border = new Border
        {
            Padding = new Thickness(0),
            Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = expander,
            Tag = vm.Name,
            AllowDrop = true,
            CanDrag = true
        };

        border.DragStarting += CategoryBorder_DragStarting;
        border.DragOver += CategoryBorder_DragOver;
        border.Drop += CategoryBorder_Drop;

        return border;
    }

    private void CategoryBorder_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string categoryName) return;
        args.Data.SetData("CategoryName", categoryName);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void CategoryBorder_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains("CategoryName"))
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }
    }

    private void CategoryBorder_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains("CategoryName")) return;
        _ = HandleCategoryDrop(sender, e);
    }

    private async Task HandleCategoryDrop(object sender, DragEventArgs e)
    {
        var sourceName = await e.DataView.GetDataAsync("CategoryName") as string;
        if (string.IsNullOrEmpty(sourceName)) return;

        if (sender is not FrameworkElement targetFe || targetFe.Tag is not string targetName) return;
        if (sourceName == targetName) return;

        var sourceIndex = _categories.FindIndex(c => c.Name == sourceName);
        var targetIndex = _categories.FindIndex(c => c.Name == targetName);
        if (sourceIndex < 0 || targetIndex < 0) return;

        var item = _categories[sourceIndex];
        _categories.RemoveAt(sourceIndex);
        _categories.Insert(targetIndex, item);

        SaveCategoryOrder();
        RebuildUI();
    }

    private static DataTemplate CreateToolItemTemplate()
    {
        var xaml = """
            <DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
                <Border
                    Padding='10,8'
                    MinWidth='120'
                    MaxWidth='180'
                    Background='{ThemeResource CardBackgroundFillColorSecondaryBrush}'
                    BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}'
                    BorderThickness='1'
                    CornerRadius='6'>
                    <TextBlock
                        FontSize='13'
                        FontWeight='SemiBold'
                        MaxLines='2'
                        TextTrimming='CharacterEllipsis'
                        TextWrapping='Wrap'
                        Text='{Binding Name}' />
                </Border>
            </DataTemplate>
            """;
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private void MoveCategoryUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not int index) return;
        if (index <= 0) return;

        (_categories[index - 1], _categories[index]) = (_categories[index], _categories[index - 1]);
        SaveCategoryOrder();
        RebuildUI();
    }

    private void MoveCategoryDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not int index) return;
        if (index >= _categories.Count - 1) return;

        (_categories[index], _categories[index + 1]) = (_categories[index + 1], _categories[index]);
        SaveCategoryOrder();
        RebuildUI();
    }

    private async void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string categoryName) return;

        var toolCount = ToolCatalog.GetTools(categoryName).Count;

        var confirmDialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = $"确认删除「{categoryName}」",
            Content = toolCount > 0
                ? $"分类「{categoryName}」下有 {toolCount} 个工具，删除后这些工具的文件将全部被移除！\n\n此操作不可撤销，确定要继续吗？"
                : $"分类「{categoryName}」下没有工具。\n\n确定要删除此分类吗？",
            PrimaryButtonText = "确认删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var categoryDir = Path.Combine(ToolCatalog.ToolsRoot, categoryName);
            if (Directory.Exists(categoryDir))
            {
                await Task.Run(() => Directory.Delete(categoryDir, true));
            }

            AppSettings.Remove($"CategoryGlyph_{categoryName}");
            ToolCatalog.InvalidateTagsCache();
            RefreshMainWindow();
            LoadCategories();
            StatusText.Text = $"已删除分类「{categoryName}」";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"删除失败: {ex.Message}";
        }
    }

    private void ToolGrid_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (sender.Tag is not string categoryName) return;
        var category = _categories.FirstOrDefault(c => c.Name == categoryName);
        if (category is null) return;

        var newOrder = category.Tools.Select(t => t.Name).ToList();
        var json = System.Text.Json.JsonSerializer.Serialize(newOrder);
        AppSettings.Set($"ToolOrder_{categoryName}", json);
    }

    private async void AddCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            Header = "分类名称",
            PlaceholderText = "例如 我的工具"
        };

        var iconOptions = new (string Label, string Glyph)[]
        {
            ("工具", "\uE8B7"),
            ("处理器", "\uEEA1"),
            ("显卡", "\uF211"),
            ("显示器", "\uE7F4"),
            ("硬盘", "\uEDA2"),
            ("内存", "\uEEA0"),
            ("外设", "\uE962"),
            ("游戏", "\uE7FC"),
            ("烤鸡", "\uE9D9"),
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
        };

        var iconGridView = new GridView
        {
            ItemsSource = iconOptions.Select(o => new { o.Label, o.Glyph }).ToList(),
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 200,
            Padding = new Thickness(0, 8, 0, 0)
        };

        iconGridView.ItemTemplate = CreateIconItemTemplate();

        var glyphPreview = new FontIcon
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
            CornerRadius = new CornerRadius(6),
            Child = glyphPreview,
            VerticalAlignment = VerticalAlignment.Center
        };

        var customGlyphBox = new TextBox
        {
            Header = "自定义图标符号",
            PlaceholderText = "例如 E700 或 ",
            Width = 200
        };

        customGlyphBox.TextChanged += (_, _) =>
        {
            var text = customGlyphBox.Text.Trim();
            if (TryParseGlyph(text, out var g))
            {
                glyphPreview.Glyph = g;
                iconGridView.SelectedItem = null;
            }
        };

        iconGridView.SelectionChanged += (_, _) =>
        {
            if (iconGridView.SelectedItem is not null)
            {
                customGlyphBox.Text = "";
                var g = (string)iconGridView.SelectedItem.GetType().GetProperty("Glyph")!.GetValue(iconGridView.SelectedItem)!;
                glyphPreview.Glyph = g;
            }
        };

        var glyphInputRow = new Grid
        {
            ColumnSpacing = 10
        };
        glyphInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        glyphInputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(glyphPreviewBorder, 0);
        glyphInputRow.Children.Add(glyphPreviewBorder);
        Grid.SetColumn(customGlyphBox, 1);
        glyphInputRow.Children.Add(customGlyphBox);

        var docsLink = new HyperlinkButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { FontSize = 12, Glyph = "\uE71B" },
                    new TextBlock { Text = "Segoe Fluent Icons 图标列表", FontSize = 12 }
                }
            },
            NavigateUri = new Uri("https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font"),
            Padding = new Thickness(0),
            Margin = new Thickness(0, -4, 0, 0)
        };

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                nameBox,
                new TextBlock { Text = "选择图标", Opacity = 0.68, FontSize = 12 },
                iconGridView,
                new TextBlock { Text = "或自定义输入", Opacity = 0.68, FontSize = 12 },
                glyphInputRow,
                docsLink
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "新建工具分类",
            Content = content,
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var categoryName = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            await ShowMessageAsync("名称不能为空", "请输入分类名称。");
            return;
        }

        var customText = customGlyphBox.Text.Trim();
        string selectedGlyph;
        if (!string.IsNullOrEmpty(customText) && TryParseGlyph(customText, out var customGlyph))
        {
            selectedGlyph = customGlyph;
        }
        else if (iconGridView.SelectedItem is not null)
        {
            selectedGlyph = (string)iconGridView.SelectedItem.GetType().GetProperty("Glyph")!.GetValue(iconGridView.SelectedItem)!;
        }
        else
        {
            selectedGlyph = "\uE8B7";
        }

        try
        {
            var categoryDir = Path.Combine(ToolCatalog.ToolsRoot, categoryName);
            if (Directory.Exists(categoryDir))
            {
                await ShowMessageAsync("分类已存在", $"分类「{categoryName}」已经存在。");
                return;
            }

            Directory.CreateDirectory(categoryDir);
            AppSettings.Set($"CategoryGlyph_{categoryName}", selectedGlyph);

            _categories.Add(new CategoryViewModel(categoryName, []));
            SaveCategoryOrder();
            RefreshMainWindow();
            LoadCategories();
            StatusText.Text = $"已创建分类「{categoryName}」";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"创建失败: {ex.Message}";
        }
    }

    private async void ImportToolButton_Click(object sender, RoutedEventArgs e)
    {
        var ofn = new OPENFILENAME
        {
            lStructSize = Marshal.SizeOf<OPENFILENAME>(),
            hwndOwner = WinRT.Interop.WindowNative.GetWindowHandle(this),
            lpstrFilter = "压缩包\0*.zip\0所有文件\0*.*\0\0",
            lpstrFile = new string(new char[1024]),
            nMaxFile = 1024,
            lpstrTitle = "选择工具压缩包",
            Flags = OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR,
            nFilterIndex = 1
        };

        if (!GetOpenFileName(ref ofn)) return;

        var packagePath = ofn.lpstrFile.TrimEnd('\0');
        if (string.IsNullOrWhiteSpace(packagePath)) return;

        StatusText.Text = "正在读取压缩包...";

        try
        {
            var executables = CustomToolPackageService.GetExecutables(packagePath);
            if (executables.Count == 0)
            {
                StatusText.Text = "压缩包中没有找到 exe 文件";
                await ShowMessageAsync("未找到可导入工具", "压缩包里需要至少包含一个 .exe 文件。");
                return;
            }

            var request = await ShowImportToolDialogAsync(packagePath, executables);
            if (request is null)
            {
                StatusText.Text = "已取消导入";
                return;
            }

            StatusText.Text = "正在导入工具...";
            var result = await CustomToolPackageService.ImportAsync(request);

            RefreshMainWindow();
            LoadCategories();
            StatusText.Text = $"已导入 {Path.GetFileName(result.ToolDirectory)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"导入失败: {ex.Message}";
        }
    }

    private async Task<CustomToolImportRequest?> ShowImportToolDialogAsync(
        string packagePath,
        IReadOnlyList<ImportableExecutable> executables)
    {
        var primaryComboBox = new ComboBox
        {
            ItemsSource = executables,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var toolNameBox = new TextBox
        {
            Header = "工具名称",
            Text = Path.GetFileNameWithoutExtension(executables[0].FileName),
            PlaceholderText = "例如 CPU-Z"
        };

        var categories = ToolCatalog.GetCategories();
        var categoryBox = new TextBox
        {
            Header = "分类",
            Text = categories.FirstOrDefault() ?? "其他工具",
            PlaceholderText = "例如 处理器工具"
        };

        var categoryComboBox = new ComboBox
        {
            Header = "已有分类",
            ItemsSource = categories,
            SelectedIndex = categories.Count > 0 ? 0 : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        categoryComboBox.SelectionChanged += (_, _) =>
        {
            if (categoryComboBox.SelectedItem is string category)
                categoryBox.Text = category;
        };

        var descriptionBox = new TextBox
        {
            Header = "简介",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            PlaceholderText = "输入工具用途、特点或注意事项"
        };

        var publisherBox = new TextBox
        {
            Header = "作者/发布者",
            PlaceholderText = "可选"
        };

        var tagsBox = new TextBox
        {
            Header = "标签",
            PlaceholderText = "用逗号分隔，例如 CPU, 跑分, 稳定性测试"
        };

        var archComboBox = new ComboBox
        {
            Header = "目标架构",
            ItemsSource = new[] { "自动检测", "x64", "x86", "ARM64" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var variantsList = new ListView
        {
            Header = "多架构文件",
            ItemsSource = executables,
            SelectionMode = ListViewSelectionMode.Multiple,
            MaxHeight = 180
        };

        var content = new ScrollViewer
        {
            MaxHeight = 620,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    toolNameBox,
                    categoryComboBox,
                    categoryBox,
                    new TextBlock { Text = "主程序", Opacity = 0.68, FontSize = 12 },
                    primaryComboBox,
                    archComboBox,
                    variantsList,
                    descriptionBox,
                    publisherBox,
                    tagsBox
                }
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "导入自定义工具",
            Content = content,
            PrimaryButtonText = "导入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return null;

        if (primaryComboBox.SelectedItem is not ImportableExecutable primary)
        {
            await ShowMessageAsync("请选择主程序", "需要指定一个 exe 作为打开工具时运行的主程序。");
            return null;
        }

        var selectedVariants = variantsList.SelectedItems
            .OfType<ImportableExecutable>()
            .Select(item => new ImportArchVariant(item.EntryPath, GuessArch(item.EntryPath)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Arch))
            .ToList();

        var manualArch = archComboBox.SelectedIndex switch
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

        var tags = tagsBox.Text
            .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new CustomToolImportRequest(
            packagePath,
            toolNameBox.Text,
            categoryBox.Text,
            primary.EntryPath,
            descriptionBox.Text,
            publisherBox.Text,
            tags,
            selectedVariants);
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
            var hexPart = text[2..];
            if (int.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber, null, out var code) && code is > 0 and <= 0xFFFF)
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
                    <FontIcon FontSize='22' Glyph='{Binding Glyph}' HorizontalAlignment='Center' VerticalAlignment='Center' />
                </Border>
            </DataTemplate>
            """;
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
    }

    private void SaveCategoryOrder()
    {
        var order = _categories.Select(c => c.Name).ToList();
        var json = System.Text.Json.JsonSerializer.Serialize(order);
        AppSettings.Set("CategoryOrder", json);
    }

    private void SaveToolOrder(string category)
    {
        var cat = _categories.FirstOrDefault(c => c.Name == category);
        if (cat is null) return;
        var order = cat.Tools.Select(t => t.Name).ToList();
        var json = System.Text.Json.JsonSerializer.Serialize(order);
        AppSettings.Set($"ToolOrder_{category}", json);
    }

    private static void RefreshMainWindow()
    {
        if (App.MainWindow is MainWindow mainWindow)
            mainWindow.RefreshToolCategories();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "确定",
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        await dialog.ShowAsync();
    }

}

public sealed class CategoryViewModel
{
    public string Name { get; }
    public List<ToolViewModel> Tools { get; }

    public CategoryViewModel(string name, IReadOnlyList<ToolItem> tools)
    {
        Name = name;
        Tools = tools.Select(t => new ToolViewModel(t)).ToList();
    }
}

public sealed class ToolViewModel
{
    public string Name { get; }
    public string Category { get; }
    public string Path { get; }

    public ToolViewModel(ToolItem item)
    {
        Name = item.Name;
        Category = item.Category;
        Path = item.Path;
    }

    public override string ToString() => Name;
}
