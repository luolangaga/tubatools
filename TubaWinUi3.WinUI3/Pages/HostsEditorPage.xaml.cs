using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class HostsEditorPage : Page
{
    // 语义状态色（跟随系统主题，经 ThemeColors 取官方语义色）
    private static Color AccentBlue => ThemeColors.AccentBlue;
    private static Color AccentGreen => ThemeColors.AccentGreen;
    private static Color AccentRed => ThemeColors.AccentRed;
    private static Color AccentOrange => ThemeColors.AccentOrange;
    private static Color AccentPurple => ThemeColors.AccentPurple;

    private List<HostsEntry>? _entries;
    private bool _dirty;

    public HostsEditorPage()
    {
        InitializeComponent();

        PageHeader.Subtitle = HostsEditorService.HostsPath;

        ApplyThemeVisuals();

        // 代码构建的画刷不会随主题自动刷新，切换主题后重渲染
        ActualThemeChanged += (_, _) =>
        {
            ApplyThemeVisuals();
            if (_entries is not null)
                RenderEntries();
        };

        _ = LoadEntriesAsync();
    }

    /// <summary>应用与主题相关的卡片/表头画刷（构造与主题切换时调用）。</summary>
    private void ApplyThemeVisuals()
    {
        // Style stat cards
        StyleStatCard(ActiveCard, ActiveIcon, AccentGreen);
        StyleStatCard(DisabledCard, DisabledIcon, AccentOrange);
        StyleStatCard(AdminCard, AdminIcon, AccentPurple);

        HeaderBorder.Background = new SolidColorBrush(ThemeColors.HeaderBg);
        ListBorder.BorderBrush = new SolidColorBrush(ThemeColors.BorderColor);
    }

    private static void StyleStatCard(Border card, Border icon, Color accent)
    {
        card.Background = new SolidColorBrush(ThemeColors.CardBg);
        card.BorderBrush = new SolidColorBrush(ThemeColors.BorderColor);
        icon.Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B));
        if (icon.Child is FontIcon fi)
            fi.Foreground = new SolidColorBrush(accent);
    }

    private async Task LoadEntriesAsync()
    {
        LoadingRing.IsActive = true;
        LoadingPanel.Visibility = Visibility.Visible;
        ListBorder.Visibility = Visibility.Collapsed;
        HeaderBorder.Visibility = Visibility.Collapsed;

        _entries = await Task.Run(() => HostsEditorService.Load());
        RenderEntries();

        LoadingRing.IsActive = false;
        LoadingPanel.Visibility = Visibility.Collapsed;
        ListBorder.Visibility = Visibility.Visible;
        HeaderBorder.Visibility = Visibility.Visible;
    }

    private void RenderEntries()
    {
        if (_entries is null) return;

        ListContainer.Children.Clear();

        var activeCount = _entries.Count(e => e.Enabled && !e.IsComment && !string.IsNullOrEmpty(e.Address));
        var disabledCount = _entries.Count(e => !e.Enabled && !e.IsComment && !string.IsNullOrEmpty(e.Address));
        ActiveCountText.Text = activeCount.ToString();
        DisabledCountText.Text = disabledCount.ToString();
        AdminText.Text = HostsEditorService.IsAdmin ? "是" : "否";
        AdminText.Foreground = HostsEditorService.IsAdmin
            ? new SolidColorBrush(AccentGreen)
            : new SolidColorBrush(AccentRed);

        foreach (var entry in _entries)
        {
            var row = CreateEntryRow(entry);
            ListContainer.Children.Add(row);
        }
    }

    private Border CreateEntryRow(HostsEntry entry)
    {
        if (entry.IsComment)
        {
            if (string.IsNullOrEmpty(entry.Comment))
            {
                return new Border
                {
                    Height = 6,
                    Background = new SolidColorBrush(ThemeColors.Separator)
                };
            }

            return new Border
            {
                Padding = new Thickness(10, 6, 10, 6),
                BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new TextBlock
                {
                    Text = entry.Comment,
                    FontSize = 12,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = new SolidColorBrush(ThemeColors.DimText),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
        }

        var toggle = new ToggleSwitch
        {
            IsOn = entry.Enabled,
            OnContent = "",
            OffContent = "",
            MinWidth = 80
        };

        var addrText = new TextBlock
        {
            Text = entry.Address,
            FontSize = 14,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(entry.Enabled ? AccentBlue : ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var hostText = new TextBlock
        {
            Text = entry.Hostname,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(entry.Enabled ? ThemeColors.PrimaryText : ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var commentText = new TextBlock
        {
            Text = string.IsNullOrEmpty(entry.Comment) ? "" : $"# {entry.Comment}",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.SecondaryText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var deleteBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            Tag = entry
        };
        deleteBtn.Click += (_, _) =>
        {
            _entries?.Remove(entry);
            _dirty = true;
            RenderEntries();
        };

        var editBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE70F", FontSize = 12 },
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            Tag = entry
        };
        editBtn.Click += async (_, _) =>
        {
            await EditEntryDialog(entry);
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(toggle); Grid.SetColumn(toggle, 0);
        grid.Children.Add(addrText); Grid.SetColumn(addrText, 1);
        grid.Children.Add(hostText); Grid.SetColumn(hostText, 2);
        grid.Children.Add(commentText); Grid.SetColumn(commentText, 3);
        grid.Children.Add(editBtn); Grid.SetColumn(editBtn, 4);
        grid.Children.Add(deleteBtn); Grid.SetColumn(deleteBtn, 5);

        var border = new Border
        {
            Padding = new Thickness(10, 6, 10, 6),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid,
            Tag = entry
        };

        toggle.Toggled += (_, _) =>
        {
            entry.Enabled = toggle.IsOn;
            _dirty = true;
            UpdateEntryVisual(border, entry);
        };

        return border;
    }

    private static void UpdateEntryVisual(Border border, HostsEntry entry)
    {
        var grid = border.Child as Grid;
        if (grid is null) return;

        foreach (var child in grid.Children)
        {
            if (child is TextBlock { FontFamily: not null } tb)
            {
                if (tb.FontSize == 14 && tb.Text == entry.Address)
                    tb.Foreground = new SolidColorBrush(entry.Enabled ? AccentBlue : ThemeColors.DimText);
                else if (tb.FontSize == 14 && tb.Text == entry.Hostname)
                    tb.Foreground = new SolidColorBrush(entry.Enabled ? ThemeColors.PrimaryText : ThemeColors.DimText);
            }
        }

        border.Background = new SolidColorBrush(entry.Enabled
            ? Microsoft.UI.Colors.Transparent
            : ThemeColors.SubtleBg);
    }

    private async Task EditEntryDialog(HostsEntry entry)
    {
        var addrBox = new TextBox
        {
            Text = entry.Address,
            PlaceholderText = "IP 地址，如 127.0.0.1",
            FontFamily = new FontFamily("Consolas"),
            Header = "IP 地址"
        };
        var hostBox = new TextBox
        {
            Text = entry.Hostname,
            PlaceholderText = "主机名，如 example.com",
            FontFamily = new FontFamily("Consolas"),
            Header = "主机名"
        };
        var commentBox = new TextBox
        {
            Text = entry.Comment,
            PlaceholderText = "可选备注",
            Header = "备注"
        };
        var enabledCheck = new CheckBox { Content = "启用此规则", IsChecked = entry.Enabled };

        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(addrBox);
        panel.Children.Add(hostBox);
        panel.Children.Add(commentBox);
        panel.Children.Add(enabledCheck);

        var dlg = new ContentDialog
        {
            Title = "编辑 Hosts 规则",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        var result = await dlg.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            entry.Address = addrBox.Text.Trim();
            entry.Hostname = hostBox.Text.Trim();
            entry.Comment = commentBox.Text.Trim();
            entry.Enabled = enabledCheck.IsChecked ?? true;
            _dirty = true;
            RenderEntries();
        }
    }

    private async void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        var newEntry = new HostsEntry { Enabled = true, Address = "127.0.0.1", Hostname = "", Comment = "" };
        await EditEntryDialog(newEntry);
        if (!string.IsNullOrEmpty(newEntry.Hostname))
        {
            _entries ??= [];
            _entries.Add(newEntry);
            _dirty = true;
            RenderEntries();
        }
    }

    private async void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_entries is null) return;
        try
        {
            await Task.Run(() => HostsEditorService.Save(_entries));
            _dirty = false;
            ShowToast("已保存", "Hosts 文件保存成功", InfoBarSeverity.Success);
        }
        catch (UnauthorizedAccessException)
        {
            ShowToast("权限不足", "请以管理员身份运行本程序", InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            ShowToast("保存失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void BackupBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HostsEditorService.Backup();
            ShowToast("备份成功", "已创建 Hosts 备份文件", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowToast("备份失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void FlushBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HostsEditorService.FlushDns();
            ShowToast("DNS 已刷新", "DNS 缓存已清除", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ShowToast("刷新失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void ReloadBtn_Click(object sender, RoutedEventArgs e)
    {
        _dirty = false;
        await LoadEntriesAsync();
    }

    private async void OnBackRequested(object? sender, EventArgs e)
    {
        if (_dirty)
        {
            var warn = new ContentDialog
            {
                Title = "未保存更改",
                Content = "Hosts 文件已修改但未保存，是否放弃更改？",
                PrimaryButtonText = "放弃",
                CloseButtonText = "继续编辑",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            var result = await warn.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;
        }
        App.MainWindow?.NavigateBack();
    }

    private DispatcherTimer? _toastBarTimer;

    private void ShowToast(string title, string message, InfoBarSeverity severity)
    {
        ToastBar.Title = title;
        ToastBar.Message = message;
        ToastBar.Severity = severity;
        ToastBar.IsOpen = true;

        _toastBarTimer?.Stop();
        _toastBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toastBarTimer.Tick += (s, e) =>
        {
            ToastBar.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _toastBarTimer.Start();
    }
}
