using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class WindowsImagePage : Page, ILocalizablePage
{
    private List<WindowsImageEntry>? _allEntries;
    private string _filter = "";
    private string _categoryFilter = "全部";
    private string _langFilter = "全部语言";
    private WindowsImageEntry? _msResolvedEntry;

    // ===== UUP Dump（JSON API 三步向导） =====
    private List<UupBuildInfo>? _uupAllBuilds;
    private UupBuildInfo? _uupSelectedBuild;
    private UupLanguageInfo? _uupSelectedLanguage;
    private UupEditionInfo? _uupSelectedEdition;
    private UupFileSetInfo? _uupFilePreview;
    private List<UupVirtualEditionInfo>? _uupVirtualEditions;
    private int _uupPreviewSeq;
    private CancellationTokenSource? _uupBuildCts;
    private CancellationTokenSource? _uupLangCts;
    private CancellationTokenSource? _uupEditionCts;
    private bool _isPageAlive = true;

    public WindowsImagePage()
    {
        InitializeComponent();

        HeaderBorder.Background = new SolidColorBrush(ThemeColors.HeaderBg);
        ListBorder.BorderBrush = new SolidColorBrush(ThemeColors.BorderColor);

        InitUupArchCombo();
        UpdateUupLocationText();
        ApplyCommunityRiskText();

        Unloaded += (_, _) =>
        {
            _isPageAlive = false;
            _uupBuildCts?.Cancel();
            _uupLangCts?.Cancel();
            _uupEditionCts?.Cancel();
        };

        LoadMsEditions();
        _ = LoadDataAsync();
        _ = LoadUupBuildsAsync(null);
    }



    /// <summary>语言切换后刷新静态文案（社区镜像 Run 段落、架构下拉、向导提示与列表卡片）。</summary>
    public void ApplyLocalization()
    {
        ApplyCommunityRiskText();
        InitUupArchCombo();
        UpdateUupLocationText();
        if (_uupSelectedBuild is null)
            ResetUupSelection();
        ApplyFilter();
    }

    /// <summary>社区镜像页的加粗 Run 段落（Run 不支持 Uid，只能代码赋值）。</summary>
    private void ApplyCommunityRiskText()
    {
        CommunitySourceRun.Text = LocalizationService.L("WindowsImage_CommunitySource", "来源：GitHub ILLKX/Windows 社区项目");
        CommunityRiskRun.Text = LocalizationService.L("WindowsImage_CommunityRisk", "⚠ 风险提醒：");
        CommunityRiskBodyRun.Text = LocalizationService.L("WindowsImage_CommunityRiskBody",
            "社区镜像非微软官方提供，虽经社区验证，但无法保证文件完整性与安全性。建议下载后校验 SHA256，或优先使用「微软官方下载」标签页获取原版镜像。");
    }

    private void LoadMsEditions()
    {
        var editions = MicrosoftOfficialService.GetAvailableEditions();
        MsEditionCombo.ItemsSource = editions;
        MsEditionCombo.DisplayMemberPath = "Name";
    }

    /// <summary>把底层英文异常（超时/域名解析失败等）转成对用户友好的中文描述。</summary>
    private static string FriendlyTimeoutMessage(Exception ex, string fallback)
    {
        var msg = ex.Message;
        if (ex is TaskCanceledException || msg.Contains("HttpClient.Timeout", StringComparison.OrdinalIgnoreCase))
            return string.Format(LocalizationService.L("WindowsImage_ErrTimeout", "{0}：请求超时，请检查网络连接后重试。"), fallback);
        if (msg.Contains("No such host", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("not resolved", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("远程名称无法解析", StringComparison.OrdinalIgnoreCase))
            return string.Format(LocalizationService.L("WindowsImage_ErrDns", "{0}：无法解析服务器地址，请检查网络或 DNS 设置。"), fallback);
        return string.Format(LocalizationService.L("WindowsImage_ErrGeneric", "{0}：{1}"), fallback, msg);
    }

    private async Task LoadDataAsync()
    {
        LoadingRing.IsActive = true;
        LoadingPanel.Visibility = Visibility.Visible;
        ListBorder.Visibility = Visibility.Collapsed;
        HeaderBorder.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Collapsed;

        try
        {
            _allEntries = await WindowsImageService.LoadAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusInfoBar.Title = LocalizationService.L("WindowsImage_LoadFailed", "加载失败");
            StatusInfoBar.Message = FriendlyTimeoutMessage(ex, LocalizationService.L("WindowsImage_FailLoadList", "获取镜像列表失败"));
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.IsOpen = true;
        }
        finally
        {
            LoadingRing.IsActive = false;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyFilter()
    {
        if (_allEntries is null) return;

        var filtered = _allEntries.AsEnumerable();

        if (_categoryFilter != "全部")
            filtered = filtered.Where(e => e.Category == _categoryFilter);

        if (_langFilter != "全部语言")
            filtered = filtered.Where(e => e.Language == _langFilter);

        if (!string.IsNullOrWhiteSpace(_filter))
        {
            var f = _filter.Trim();
            filtered = filtered.Where(e =>
                e.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.FileName.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.Language.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                e.Arch.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                (e.Updated ?? "").Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();
        RenderList(list);

        EmptyPanel.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListBorder.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeaderBorder.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderList(List<WindowsImageEntry> entries)
    {
        ListContainer.Children.Clear();
        foreach (var entry in entries)
            ListContainer.Children.Add(CreateRow(entry));
    }

    private Border CreateRow(WindowsImageEntry entry)
    {
        var nameText = new TextBlock
        {
            Text = entry.DisplayName,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 420
        };

        var fileNameText = new TextBlock
        {
            Text = entry.FileName,
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var sizeText = new TextBlock
        {
            Text = entry.SizeDisplay,
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText),
            VerticalAlignment = VerticalAlignment.Center
        };

        var langBadge = MakeBadge(entry.Language, entry.Language == "简体中文"
            ? ThemeColors.AccentBlue
            : entry.Language == "English"
                ? ThemeColors.AccentGreen
                : ThemeColors.AccentPurple);

        var archBadge = MakeBadge(entry.Arch, ThemeColors.AccentOrange);

        var downloadBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE896", FontSize = 11 },
                    new TextBlock { Text = LocalizationService.L("WindowsImage_BtnDownload", "下载"), FontSize = 12 }
                }
            },
            Padding = new Thickness(10, 4, 10, 4),
            Tag = entry
        };
        downloadBtn.Click += DownloadBtn_Click;

        var convertBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new FontIcon { Glyph = "\uE898", FontSize = 11 },
                    new TextBlock { Text = LocalizationService.L("WindowsImage_BtnDownloadConvert", "下载并转ISO"), FontSize = 12 }
                }
            },
            Padding = new Thickness(10, 4, 10, 4),
            Tag = entry,
            Visibility = entry.IsEsd ? Visibility.Visible : Visibility.Collapsed
        };
        convertBtn.Click += DownloadAndConvertBtn_Click;

        var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        actionPanel.Children.Add(downloadBtn);
        actionPanel.Children.Add(convertBtn);

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(nameText); Grid.SetColumn(nameText, 0);
        grid.Children.Add(fileNameText); Grid.SetColumn(fileNameText, 1);
        grid.Children.Add(sizeText); Grid.SetColumn(sizeText, 2);
        grid.Children.Add(langBadge); Grid.SetColumn(langBadge, 3);
        grid.Children.Add(archBadge); Grid.SetColumn(archBadge, 4);
        grid.Children.Add(actionPanel); Grid.SetColumn(actionPanel, 5);

        var tip = new ToolTip { Content = string.Format(LocalizationService.L("WindowsImage_TipSize", "{0}\n{1}\n大小: {2}"), entry.DisplayName, entry.FileName, entry.SizeDisplay) };
        if (entry.Sha256 is not null) tip.Content += $"\nSHA256: {entry.Sha256[..16]}...";
        if (entry.Updated is not null) tip.Content += string.Format(LocalizationService.L("WindowsImage_TipUpdated", "\n更新: {0}"), entry.Updated);
        ToolTipService.SetToolTip(grid, tip);

        return new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
    }

    private static Border MakeBadge(string text, Color color)
    {
        return new Border
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private void DownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WindowsImageEntry entry } btn) return;
        EnqueueDownload(entry, btn);
    }

    private void EnqueueDownload(WindowsImageEntry entry, FrameworkElement? target = null)
    {
        var destDir = WindowsImageService.GetDownloadDir();
        DownloadQueueService.Enqueue(
            entry.DisplayName,
            entry.DownloadUrl,
            destDir,
            postProcessor: null,
            description: $"{entry.Language} | {entry.Arch} | {entry.SizeDisplay}",
            glyph: "\uE896");

        StatusInfoBar.Title = LocalizationService.L("WindowsImage_Queued", "已加入下载队列");
        StatusInfoBar.Message = string.Format(LocalizationService.L("WindowsImage_QueuedMsgDownload", "{0} 正在下载至 {1}"), entry.DisplayName, destDir);
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;

        ShowQueueTip(entry.DisplayName, target);
    }

    private void ShowQueueTip(string name, FrameworkElement? target)
    {
        QueueTeachingTip.Title = "已加入下载队列";
        QueueTeachingTip.Subtitle = string.Format(LocalizationService.L("WindowsImage_QueueTipSubtitle", "{0}\n点击主页搜索框旁的下载按钮可查看进度"), name);
        QueueTeachingTip.IconSource = new SymbolIconSource { Symbol = Symbol.Download };
        QueueTeachingTip.Target = target;
        QueueTeachingTip.IsOpen = true;
    }

    private async void DownloadAndConvertBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: WindowsImageEntry entry } btn) return;

        if (!WindowsImageService.IsUltraIsoAvailable)
        {
            var dialog = new ContentDialog
            {
                Title = LocalizationService.L("WindowsImage_UltraIsoRequired", "需要 UltraISO"),
                Content = LocalizationService.L("WindowsImage_UltraIsoContent", "ESD 转 ISO 需要安装 UltraISO。") + "\n\n" + LocalizationService.L("WindowsImage_UltraIsoContentAsk", "是否前往下载页面？"),
                PrimaryButtonText = LocalizationService.L("WindowsImage_UltraIsoGo", "前往下载"),
                CloseButtonText = LocalizationService.L("WindowsImage_UltraIsoCancel", "取消"),
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                try { _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.ezbsystems.com/ultraiso/")); } catch { }
            }
            return;
        }

        var destDir = WindowsImageService.GetDownloadDir();
        var isoFileName = Path.ChangeExtension(entry.FileName, ".iso");

        var postProcessor = new DelegatePostProcessor(LocalizationService.L("WindowsImage_PostProcessorName", "ESD 转 ISO"), async (file, dest, progress, ct) =>
        {
            progress?.Report(LocalizationService.L("WindowsImage_WaitingDownload", "正在等待下载完成..."));
            var esdFile = Path.Combine(dest, Path.GetFileName(file));
            if (!File.Exists(esdFile)) esdFile = file;

            var isoPath = Path.Combine(dest, isoFileName);

            await WindowsImageService.ConvertEsdToIsoAsync(esdFile, isoPath, progress, ct);

            if (File.Exists(esdFile) && File.Exists(isoPath))
            {
                try { File.Delete(esdFile); } catch { }
            }
        });

        DownloadQueueService.Enqueue(
            entry.DisplayName + LocalizationService.L("WindowsImage_EsdIsoSuffix", " (ESD→ISO)"),
            entry.DownloadUrl,
            destDir,
            postProcessor,
            description: $"{entry.Language} | {entry.Arch} | {entry.SizeDisplay} → ISO",
            glyph: "\uE898");

        StatusInfoBar.Title = LocalizationService.L("WindowsImage_Queued", "已加入下载队列");
        StatusInfoBar.Message = string.Format(LocalizationService.L("WindowsImage_QueuedMsgConvert", "{0} 下载完成后将自动转换为 ISO"), entry.DisplayName);
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;

        ShowQueueTip(entry.DisplayName + LocalizationService.L("WindowsImage_EsdIsoSuffix", " (ESD→ISO)"), btn);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        _filter = sender.Text;
        ApplyFilter();
    }

    private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryCombo.SelectedItem is ComboBoxItem { Tag: string s })
            _categoryFilter = s;
        ApplyFilter();
    }

    private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LangCombo.SelectedItem is ComboBoxItem { Tag: string s })
            _langFilter = s;
        ApplyFilter();
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private void CancelConvertBtn_Click(object sender, RoutedEventArgs e)
    {
    }

    private void SourcePivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private async void MsEditionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MsEditionCombo.SelectedItem is not MicrosoftEdition edition) return;

        MsLangCombo.IsEnabled = false;
        MsLangCombo.ItemsSource = null;
        MsFetchBtn.IsEnabled = false;
        MsResultPanel.Visibility = Visibility.Collapsed;

        MsProgressRing.Visibility = Visibility.Visible;
        MsStatusText.Visibility = Visibility.Visible;
        MsStatusText.Text = LocalizationService.L("WindowsImage_MsInitSession", "正在初始化会话...");

        try
        {
            var sessionId = await MicrosoftOfficialService.InitSessionAsync();

            MsStatusText.Text = LocalizationService.L("WindowsImage_FetchingLanguages", "正在获取语言列表...");

            var skuId = edition.SkuIds[0];
            var languages = await MicrosoftOfficialService.GetLanguagesAsync(skuId, sessionId);

            if (edition.SkuIds.Length > 1)
            {
                var sessionId2 = await MicrosoftOfficialService.InitSessionAsync();
                var languages2 = await MicrosoftOfficialService.GetLanguagesAsync(edition.SkuIds[1], sessionId2);
                foreach (var lang in languages2)
                {
                    if (!languages.Any(l => l.Name == lang.Name))
                        languages.Add(lang);
                }
            }

            MsLangCombo.ItemsSource = languages;
            MsLangCombo.DisplayMemberPath = "Name";
            MsLangCombo.IsEnabled = languages.Count > 0;
            MsFetchBtn.IsEnabled = languages.Count > 0;

            MsStatusText.Text = string.Format(LocalizationService.L("WindowsImage_MsLanguagesCount", "已获取 {0} 种语言"), languages.Count);
        }
        catch (Exception ex)
        {
            MsStatusText.Text = FriendlyTimeoutMessage(ex, LocalizationService.L("WindowsImage_FailLoadLanguages", "获取语言列表失败"));
        }
        finally
        {
            MsProgressRing.Visibility = Visibility.Collapsed;
        }
    }

    private async void MsFetchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MsEditionCombo.SelectedItem is not MicrosoftEdition edition) return;
        if (MsLangCombo.SelectedItem is not MicrosoftLanguage language) return;

        MsFetchBtn.IsEnabled = false;
        MsResultPanel.Visibility = Visibility.Collapsed;
        MsProgressRing.Visibility = Visibility.Visible;
        MsStatusText.Visibility = Visibility.Visible;
        MsStatusText.Text = LocalizationService.L("WindowsImage_MsFetchingLink", "正在获取下载链接...");

        try
        {
            _msResolvedEntry = await MicrosoftOfficialService.ResolveDownloadEntryAsync(edition, language);

            if (_msResolvedEntry is not null)
            {
                MsResultTitle.Text = _msResolvedEntry.DisplayName;
                MsResultInfo.Text = string.Format(LocalizationService.L("WindowsImage_MsResultInfo", "架构: {0} | 文件: {1}"), _msResolvedEntry.Arch, _msResolvedEntry.FileName);
                MsResultPanel.Visibility = Visibility.Visible;
                MsStatusText.Text = LocalizationService.L("WindowsImage_MsLinkOk", "下载链接获取成功（24 小时内有效）");
            }
            else
            {
                MsStatusText.Text = LocalizationService.L("WindowsImage_MsLinkFail", "未能获取下载链接，请稍后重试");
            }
        }
        catch (Exception ex)
        {
            MsStatusText.Text = FriendlyTimeoutMessage(ex, LocalizationService.L("WindowsImage_FailFetchLink", "获取下载链接失败"));
        }
        finally
        {
            MsProgressRing.Visibility = Visibility.Collapsed;
            MsFetchBtn.IsEnabled = true;
        }
    }

    private void MsDownloadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_msResolvedEntry is null) return;
        EnqueueDownload(_msResolvedEntry, sender as FrameworkElement);
    }

    private async void MsOpenBrowserBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_msResolvedEntry is null) return;
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_msResolvedEntry.DownloadUrl));
        }
        catch { }
    }

    private void MsCopyLinkBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_msResolvedEntry is null) return;
        try
        {
            // 失败重试与诊断由 ClipboardService 统一负责（剪贴板被占用时不得崩溃）
            if (!ClipboardService.TrySetText(_msResolvedEntry.DownloadUrl).Success)
            {
                StatusInfoBar.Title = LocalizationService.L("Hw_CopyFailed", "复制失败");
                StatusInfoBar.Message = LocalizationService.L("Hw_CopyBusyRetry", "复制失败：剪贴板被其他程序占用，请稍后重试");
                StatusInfoBar.Severity = InfoBarSeverity.Warning;
                StatusInfoBar.IsOpen = true;
                return;
            }

            StatusInfoBar.Title = LocalizationService.L("WindowsImage_Copied", "已复制");
            StatusInfoBar.Message = LocalizationService.L("WindowsImage_CopiedMsg", "下载链接已复制到剪贴板");
            StatusInfoBar.Severity = InfoBarSeverity.Success;
            StatusInfoBar.IsOpen = true;
        }
        catch { }
    }

    // ==================== UUP Dump：三步向导 ====================

    /// <summary>刷新「保存位置」显示；该设置对本页全部下载（微软官方/社区镜像/UUP）生效。</summary>
    private void UpdateUupLocationText()
    {
        var custom = AppSettings.Get("WindowsImageDownloadDir");
        var isCustom = !string.IsNullOrWhiteSpace(custom);

        UupSaveLocationText.Text = UupDumpService.GetDownloadDir();
        ToolTipService.SetToolTip(UupSaveLocationText,
            LocalizationService.L("WindowsImage_SaveLocationTip", "微软官方与社区镜像直接保存到所选目录；UUP 下载包保存在其中的 UUPDump 子目录。") + "\n" +
            string.Format(LocalizationService.L("WindowsImage_SaveLocationCurrent", "当前保存根目录：{0}"), isCustom ? custom!.Trim() : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")));
        UupResetDirBtn.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void UupBrowseDirBtn_Click(object sender, RoutedEventArgs e)
    {
        var dir = await Win32Dialogs.PickFolderAsync();
        if (string.IsNullOrEmpty(dir)) return;

        AppSettings.Set("WindowsImageDownloadDir", dir);
        UpdateUupLocationText();

        StatusInfoBar.Title = LocalizationService.L("WindowsImage_SaveLocationChanged", "保存位置已更改");
        StatusInfoBar.Message = string.Format(LocalizationService.L("WindowsImage_SaveLocationChangedMsg", "此页面所有下载（微软官方/社区镜像/UUP）将保存到 {0}"), dir);
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;
    }

    private void UupResetDirBtn_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Remove("WindowsImageDownloadDir");
        UpdateUupLocationText();
    }

    private void InitUupArchCombo()
    {
        var suggested = UupDumpService.GetSuggestedArch();
        List<ComboBoxItem> archItems =
        [
            new ComboBoxItem { Content = string.Format(LocalizationService.L("WindowsImage_ArchFollowSystem", "跟随系统 ({0})"), suggested), Tag = suggested },
            new ComboBoxItem { Content = LocalizationService.L("WindowsImage_ArchAll", "全部架构"), Tag = "" },
            new ComboBoxItem { Content = "amd64 (x64)", Tag = "amd64" },
            new ComboBoxItem { Content = "arm64", Tag = "arm64" },
            new ComboBoxItem { Content = LocalizationService.L("WindowsImage_ArchX86", "x86 (32 位)"), Tag = "x86" },
        ];
        UupArchCombo.ItemsSource = archItems;
        UupArchCombo.SelectedIndex = 0;
    }

    private async Task LoadUupBuildsAsync(string? search)
    {
        _uupBuildCts?.Cancel();
        _uupBuildCts = new CancellationTokenSource();
        var ct = _uupBuildCts.Token;

        UupBuildLoadingPanel.Visibility = Visibility.Visible;
        UupBuildStatusText.Visibility = Visibility.Visible;
        UupBuildStatusText.Text = LocalizationService.L("WindowsImage_UupFetchingBuilds", "正在获取构建列表（网络较慢时首次可能需要数秒）…");
        UupBuildList.ItemsSource = null;
        ResetUupSelection();

        try
        {
            var builds = await UupDumpService.GetKnownBuildsAsync(search, ct);
            if (ct.IsCancellationRequested || !_isPageAlive) return;
            _uupAllBuilds = builds;
            ApplyUupBuildFilter();

            if (builds.Count == 0)
                ShowUupBuildStatus(LocalizationService.L("WindowsImage_UupNoBuilds", "没有找到匹配的构建。试试其他关键词，例如 26100 或 24H2。"));
        }
        catch (OperationCanceledException) { }
        catch (UupDumpApiException ex)
        {
            ShowUupBuildStatus(ex.Message);
        }
        catch (Exception ex)
        {
            ShowUupBuildStatus(FriendlyTimeoutMessage(ex, LocalizationService.L("WindowsImage_FailLoadBuilds", "获取构建列表失败")));
        }
        finally
        {
            if (!ct.IsCancellationRequested && _isPageAlive)
                UupBuildLoadingPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowUupBuildStatus(string message)
    {
        UupBuildStatusText.Text = message;
        UupBuildStatusText.Visibility = Visibility.Visible;
        UupBuildLoadingPanel.Visibility = Visibility.Collapsed;
    }

    private void ApplyUupBuildFilter()
    {
        if (_uupAllBuilds is null) return;

        var channel = (UupChannelCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "全部";
        var arch = (UupArchCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

        var filtered = _uupAllBuilds.AsEnumerable();
        if (channel != "全部")
            filtered = filtered.Where(b => b.Channel == channel);
        if (!string.IsNullOrEmpty(arch))
            filtered = filtered.Where(b => b.Architecture == arch);

        var list = filtered.ToList();
        UupBuildList.ItemsSource = list;

        // 保留仍可见的已选构建，避免切换筛选时丢失选择状态
        if (_uupSelectedBuild is not null)
        {
            var match = list.FirstOrDefault(b => b.UpdateId == _uupSelectedBuild.UpdateId);
            if (match is not null)
                UupBuildList.SelectedItem = match;
            else
                ResetUupSelection();
        }

        if (list.Count == 0 && UupBuildStatusText.Visibility != Visibility.Visible)
            ShowUupBuildStatus(LocalizationService.L("WindowsImage_UupNoBuildsFiltered", "当前筛选条件下没有构建，可把架构切换为「全部架构」。"));
        else if (list.Count > 0)
            UupBuildStatusText.Visibility = Visibility.Collapsed;
    }

    private void ResetUupSelection()
    {
        _uupSelectedBuild = null;
        _uupSelectedLanguage = null;
        _uupSelectedEdition = null;
        _uupFilePreview = null;
        _uupVirtualEditions = null;

        UupSelectedBuildText.Text = LocalizationService.L("WindowsImage_UupSelectBuildHint", "请先在上方第 1 步中选择一个系统版本");
        UupLangCombo.ItemsSource = null;
        UupLangCombo.IsEnabled = false;
        UupEditionRadio.ItemsSource = null;
        UupVirtualEditionPanel.Children.Clear();
        UupVirtualEditionExpander.Visibility = Visibility.Collapsed;
        UupStartBtn.IsEnabled = false;
        UupSummaryText.Text = LocalizationService.L("WindowsImage_UupSummaryHint", "完成前两步后，这里会显示下载内容摘要。");
    }

    private async void UupBuildList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UupBuildList.SelectedItem is not UupBuildInfo build) return;
        if (build.UpdateId == _uupSelectedBuild?.UpdateId) return;
        await UupSelectBuildAsync(build);
    }

    private async Task UupSelectBuildAsync(UupBuildInfo build)
    {
        _uupLangCts?.Cancel();
        _uupEditionCts?.Cancel();
        _uupLangCts = new CancellationTokenSource();
        var ct = _uupLangCts.Token;

        _uupSelectedBuild = build;
        _uupSelectedLanguage = null;
        _uupSelectedEdition = null;
        _uupFilePreview = null;

        UupSelectedBuildText.Text = string.Format(LocalizationService.L("WindowsImage_UupSelected", "已选择：{0}"), build.Title);
        UupLangCombo.ItemsSource = null;
        UupLangCombo.IsEnabled = false;
        UupEditionRadio.ItemsSource = null;
        UupStartBtn.IsEnabled = false;
        UupSummaryText.Text = LocalizationService.L("WindowsImage_FetchingLanguages", "正在获取语言列表...");
        UupLangProgress.Visibility = Visibility.Visible;

        try
        {
            var langs = await UupDumpService.GetLanguagesAsync(build.UpdateId, ct);
            if (!_isPageAlive) return;
            if (_uupSelectedBuild?.UpdateId != build.UpdateId) return;

            UupLangCombo.ItemsSource = langs;
            UupLangCombo.IsEnabled = langs.Count > 0;
            if (langs.Count > 0)
            {
                // 默认选中简体中文（无则选第一个）；SelectedItem 触发的
                // SelectionChanged 会接着加载该语言的版本列表
                UupLangCombo.SelectedItem = langs.FirstOrDefault(l => l.Code == "zh-cn") ?? langs[0];
            }
            else
            {
                UupSummaryText.Text = LocalizationService.L("WindowsImage_UupNoLanguages", "该构建没有可选语言。");
            }
        }
        catch (OperationCanceledException) { }
        catch (UupDumpApiException ex)
        {
            UupSummaryText.Text = ex.Message;
        }
        catch (Exception ex)
        {
            UupSummaryText.Text = FriendlyTimeoutMessage(ex, LocalizationService.L("WindowsImage_FailLoadLanguages", "获取语言列表失败"));
        }
        finally
        {
            if (_isPageAlive)
                UupLangProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void UupLangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UupLangCombo.SelectedItem is not UupLanguageInfo lang) return;
        await UupLoadEditionsAsync(lang);
    }

    private async Task UupLoadEditionsAsync(UupLanguageInfo lang)
    {
        var build = _uupSelectedBuild;
        if (build is null) return;

        _uupEditionCts?.Cancel();
        _uupEditionCts = new CancellationTokenSource();
        var ct = _uupEditionCts.Token;

        _uupSelectedLanguage = lang;
        _uupSelectedEdition = null;
        _uupFilePreview = null;

        UupEditionRadio.ItemsSource = null;
        UupStartBtn.IsEnabled = false;
        UupSummaryText.Text = string.Format(LocalizationService.L("WindowsImage_UupFetchingEditions", "正在获取「{0}」的可用版本..."), lang.DisplayName);
        UupEditionProgress.Visibility = Visibility.Visible;

        try
        {
            var editions = await UupDumpService.GetEditionsAsync(build.UpdateId, lang.Code, ct);
            if (!_isPageAlive) return;
            if (_uupSelectedBuild?.UpdateId != build.UpdateId || _uupSelectedLanguage?.Code != lang.Code) return;

            UupEditionRadio.ItemsSource = editions;
            if (editions.Count > 0)
            {
                UupEditionRadio.SelectedItem = editions.FirstOrDefault(x => x.Id == "PROFESSIONAL") ?? editions[0];
            }
            else
            {
                UupSummaryText.Text = LocalizationService.L("WindowsImage_UupNoEditions", "该语言下没有可用版本。");
            }
        }
        catch (OperationCanceledException) { }
        catch (UupDumpApiException ex)
        {
            UupSummaryText.Text = ex.Message;
        }
        catch (Exception ex)
        {
            UupSummaryText.Text = FriendlyTimeoutMessage(ex, LocalizationService.L("WindowsImage_FailLoadEditions", "获取版本列表失败"));
        }
        finally
        {
            if (_isPageAlive)
                UupEditionProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void UupEditionRadio_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UupEditionRadio.SelectedItem is not UupEditionInfo edition) return;
        _uupSelectedEdition = edition;
        _uupFilePreview = null;
        UupStartBtn.IsEnabled = true;
        UupRenderVirtualEditions(edition.Id);
        UpdateUupSummary();
        await UupLoadFilePreviewAsync();
    }

    /// <summary>根据基础版本渲染可合成的附加版本勾选项；无可用附加版本时隐藏该区域。</summary>
    private void UupRenderVirtualEditions(string baseEditionId)
    {
        UupVirtualEditionPanel.Children.Clear();
        _uupVirtualEditions = UupDumpService.GetVirtualEditionsForBase(baseEditionId);

        if (_uupVirtualEditions.Count == 0)
        {
            UupVirtualEditionExpander.Visibility = Visibility.Collapsed;
            UupVirtualEditionExpander.IsExpanded = false;
            return;
        }

        UupVirtualEditionExpander.Visibility = Visibility.Visible;
        foreach (var ve in _uupVirtualEditions)
        {
            var cb = new CheckBox
            {
                Content = ve.DisplayName,
                Tag = ve.Name,
                Margin = new Thickness(0, 2, 0, 2)
            };
            cb.Checked += (_, _) => UpdateUupSummary();
            cb.Unchecked += (_, _) => UpdateUupSummary();
            UupVirtualEditionPanel.Children.Add(cb);
        }
    }

    private List<string> CollectUupVirtualEditions()
    {
        var selected = new List<string>();
        foreach (var child in UupVirtualEditionPanel.Children)
        {
            if (child is CheckBox { IsChecked: true, Tag: string name })
                selected.Add(name);
        }
        return selected;
    }

    private string GetVirtualEditionDisplayName(string name)
    {
        return _uupVirtualEditions?.FirstOrDefault(v => v.Name == name)?.DisplayName ?? name;
    }

    private async Task UupLoadFilePreviewAsync()
    {
        var build = _uupSelectedBuild;
        var lang = _uupSelectedLanguage;
        var edition = _uupSelectedEdition;
        if (build is null || lang is null || edition is null) return;

        // noLinks 通道不触发接口限流，仅用于展示文件数与总大小；结果过期直接丢弃
        var seq = ++_uupPreviewSeq;
        try
        {
            var preview = await UupDumpService.GetFilesAsync(build.UpdateId, lang.Code, edition.Id, withLinks: false);
            if (!_isPageAlive || seq != _uupPreviewSeq) return;
            if (_uupSelectedBuild?.UpdateId != build.UpdateId ||
                _uupSelectedLanguage?.Code != lang.Code ||
                _uupSelectedEdition?.Id != edition.Id) return;

            _uupFilePreview = preview;
            UpdateUupSummary();
        }
        catch
        {
            // 大小预览失败不影响主流程，摘要保持现状
        }
    }

    private void UpdateUupSummary()
    {
        var build = _uupSelectedBuild;
        var lang = _uupSelectedLanguage;
        var edition = _uupSelectedEdition;
        if (build is null || lang is null || edition is null) return;

        var summary = $"{build.Title}\n{lang.DisplayName} · {edition.DisplayName} · {build.Architecture}";

        var ves = CollectUupVirtualEditions();
        if (ves.Count > 0)
            summary += string.Format(LocalizationService.L("WindowsImage_UupVirtualList", "\n附加版本：{0}"), string.Join(LocalizationService.L("WindowsImage_ListSeparator", "、"), ves.Select(GetVirtualEditionDisplayName)));

        if (_uupFilePreview is { } p && p.Files.Count > 0)
            summary += string.Format(LocalizationService.L("WindowsImage_UupFileSummary", "\n共 {0} 个文件，约 {1}（下载时间取决于网速）"), p.Files.Count, DownloadQueueService.FormatSize(p.TotalSize));

        UupSummaryText.Text = summary;
    }

    private void UupChannelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyUupBuildFilter();
    }

    private void UupArchCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyUupBuildFilter();
    }

    private async void UupSearchBtn_Click(object sender, RoutedEventArgs e)
    {
        await LoadUupBuildsAsync(string.IsNullOrWhiteSpace(UupSearchBox.Text) ? null : UupSearchBox.Text.Trim());
    }

    private async void UupSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await LoadUupBuildsAsync(string.IsNullOrWhiteSpace(sender.Text) ? null : sender.Text.Trim());
    }

    private void UupNoConvertCheck_Changed(object sender, RoutedEventArgs e)
    {
        // 仅下载文件集时不涉及转换，附加版本与转换选项一并禁用
        var converting = UupNoConvertCheck.IsChecked != true;
        UupVirtualEditionExpander.IsEnabled = converting;
        UupOptAddUpdates.IsEnabled = converting;
        UupOptCleanup.IsEnabled = converting;
        UupOptNetFx3.IsEnabled = converting;
        UupOptEsd.IsEnabled = converting;
        UupOptApps.IsEnabled = converting;
    }

    private void UupStartBtn_Click(object sender, RoutedEventArgs e)
    {
        var build = _uupSelectedBuild;
        var lang = _uupSelectedLanguage;
        var edition = _uupSelectedEdition;
        if (build is null || lang is null || edition is null) return;

        var noConvert = UupNoConvertCheck.IsChecked == true;
        var (pkgDir, uupsDir) = UupDumpService.GetPackageDirs(build.Build, lang.Code, edition.Id);
        var title = $"{build.Title} {lang.DisplayName} {edition.DisplayName}";

        var virtualEditions = CollectUupVirtualEditions();
        var options = new UupConvertOptions
        {
            AddUpdates = UupOptAddUpdates.IsChecked == true,
            Cleanup = UupOptCleanup.IsChecked == true,
            NetFx3 = UupOptNetFx3.IsChecked == true,
            Wim2Esd = UupOptEsd.IsChecked == true,
            SkipApps = UupOptApps.IsChecked != true,
            VirtualEditions = noConvert ? [] : virtualEditions,
        };

        // 入队时按所选选项生成官方格式的 ConvertConfig.ini；
        // 下载期间用户可手动编辑，转换脚本启动时以该文件为准
        if (!noConvert)
            UupDumpService.WriteConvertConfigIni(pkgDir, options);

        var resolver = UupDumpService.CreateMultiFileResolver(build.UpdateId, lang.Code, edition.Id);
        IDownloadPostProcessor? post = noConvert ? null : UupDumpService.CreateIsoPostProcessor(title);

        var sizeDesc = _uupFilePreview is { } p && p.Files.Count > 0
            ? string.Format(LocalizationService.L("WindowsImage_UupApproxSize", "约 {0}"), DownloadQueueService.FormatSize(p.TotalSize))
            : LocalizationService.L("WindowsImage_UupSizeUnknown", "文件较多，耗时取决于网速");
        var editionDesc = options.HasVirtualEditions
            ? string.Format(LocalizationService.L("WindowsImage_UupEditionWithVirtual", "{0} + 附加版本（{1}）"), edition.DisplayName, string.Join(LocalizationService.L("WindowsImage_ListSeparator", "、"), virtualEditions.Select(GetVirtualEditionDisplayName)))
            : edition.DisplayName;
        var displayName = noConvert
            ? string.Format(LocalizationService.L("WindowsImage_UupFileSetName", "{0} UUP 文件集"), build.Title)
            : string.Format(LocalizationService.L("WindowsImage_UupIsoName", "{0} ISO（{1}）"), build.Title, edition.DisplayName);

        DownloadQueueService.EnqueueMultiFile(
            displayName,
            resolver,
            uupsDir,
            post,
            description: $"{lang.DisplayName} · {editionDesc} · {build.Architecture} · {sizeDesc}",
            glyph: "\uE896");

        StatusInfoBar.Title = LocalizationService.L("WindowsImage_Queued", "已加入下载队列");
        StatusInfoBar.Message = noConvert
            ? string.Format(LocalizationService.L("WindowsImage_UupStartedMsg", "{0} 开始下载，文件保存在 {1}"), displayName, uupsDir)
            : string.Format(LocalizationService.L("WindowsImage_UupStartedMsgConvert", "{0} 下载完成后将自动转换为 ISO，最终文件位于 {1}"), displayName, pkgDir);
        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.IsOpen = true;

        ShowQueueTip(displayName, null);
    }
}
