using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>
/// 社区工具提交（单窗口）：表单 → 校验 → 提交进度 → 成功/失败。
/// plugin.json 由 CommunityToolService.BuildPluginJson 单源生成，预览与上传内容完全一致。
/// </summary>
public sealed partial class CommunitySubmitWindow : Window
{
    private CancellationTokenSource _cts = new();
    private string _errorDetail = "";
    private string? _userName;
    private bool _submitting;

    // 表单状态（跨视图保留）
    private string? _packagePath;
    private string? _iconFilePath;
    private bool _urlVerified;
    private IReadOnlyList<ImportableExecutable> _executables = [];
    private CommunityPluginDraft? _pendingDraft;

    // 页头强调色（随状态变化），供主题切换后按当前状态重刷
    private Color _headerAccent = ThemeColors.AccentBlue;

    public event Action? SubmitSucceeded;

    public CommunitySubmitWindow()
    {
        InitializeComponent();

        AppWindow.Title = "图吧工具箱CE - 提交社区工具";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var screenArea = displayArea.WorkArea;
        var width = (int)(screenArea.Width * 0.6);
        var height = (int)(screenArea.Height * 0.8);
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            (screenArea.Width - width) / 2,
            (screenArea.Height - height) / 2));

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter is not null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }

        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = ThemeService.CurrentElementTheme;
            root.ActualThemeChanged += (_, _) => ApplyHeaderAccent(_headerAccent);
        }

        PopulateCategories();
        _ = LoadAccountAsync();
    }

    private async Task LoadAccountAsync()
    {
        try { _userName = (await GitHubAuthService.GetCurrentUserAsync())?.Login; }
        catch { }

        AccountText.Text = string.IsNullOrWhiteSpace(_userName)
            ? "未登录 GitHub（提交需要登录）"
            : $"已登录：{_userName}";
    }

    private void PopulateCategories()
    {
        var existingCategories = ToolCatalog.GetCategories();
        var standardCategories = new[]
        {
            "处理器工具", "显卡工具", "内存工具", "硬盘工具", "显示器工具", "声卡工具",
            "网卡工具", "外设工具", "综合工具", "系统工具", "游戏工具", "其他工具"
        };

        var allCategories = existingCategories
            .Concat(standardCategories)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var category in allCategories)
            CategoryComboBox.Items.Add(category);

        CategoryComboBox.SelectedIndex = 0;
    }

    /// <summary>页头图标/瓦片强调色由代码构建，集中一处便于主题联动与状态切换。</summary>
    private void ApplyHeaderAccent(Color accent)
    {
        _headerAccent = accent;
        HeaderIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(accent);
        IconBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            Color.FromArgb(51, accent.R, accent.G, accent.B));
    }

    // ---------- 表单：上传方式 / 压缩包 / 下载链接 ----------

    private void MethodRadio_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var isZip = MethodRadio.SelectedIndex == 0;
        ZipPanel.Visibility = isZip ? Visibility.Visible : Visibility.Collapsed;
        UrlPanel.Visibility = isZip ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void PickPackageButton_Click(object sender, RoutedEventArgs e)
    {
        var picked = Win32Dialogs.PickOpen("压缩包\0*.zip\0所有文件\0*.*\0\0", "选择工具压缩包");
        if (string.IsNullOrWhiteSpace(picked)) return;

        PickPackageButton.IsEnabled = false;
        _packagePath = null;
        SetExecutables([]);
        PackageInfoText.Text = "";
        try
        {
            var fi = new FileInfo(picked);
            if (fi.Length > CommunityToolService.MaxUploadSizeBytes)
            {
                ShowFormError($"压缩包大小不能超过 {CommunityToolService.MaxUploadSizeBytes / 1024 / 1024} MB（当前 {FormatSize(fi.Length)}）");
                return;
            }

            var inspection = await Task.Run(() =>
            {
                var success = CustomToolPackageService.TryGetExecutables(picked, out var files, out var error);
                return (success, files, error);
            });
            if (!inspection.success)
            {
                ShowFormError(inspection.error!);
                return;
            }
            var exes = inspection.files;
            if (exes.Count == 0)
            {
                ShowFormError("压缩包里需要至少包含一个 .exe 文件。");
                return;
            }

            _packagePath = picked;
            SetExecutables(exes);
            PackageInfoText.Text = $"{Path.GetFileName(picked)}  ·  {FormatSize(fi.Length)}  ·  {exes.Count} 个可执行文件";

            if (string.IsNullOrWhiteSpace(NameBox.Text))
                NameBox.Text = Path.GetFileNameWithoutExtension(exes[0].FileName);

            ClearFormError();
        }
        catch (InvalidDataException)
        {
            ShowFormError("无法读取 ZIP 压缩包：文件可能损坏、下载不完整，或并非 ZIP 格式。请重新下载或重新打包后选择。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ShowFormError($"无法读取压缩包，请确认文件仍存在且有读取权限：{ex.Message}");
        }
        finally { PickPackageButton.IsEnabled = true; }
    }

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        var url = DownloadUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowFormError("请输入下载链接");
            return;
        }

        VerifyButton.IsEnabled = false;
        VerifyProgress.Visibility = Visibility.Visible;
        VerifyStatusText.Visibility = Visibility.Visible;
        VerifyStatusText.Text = "正在解析下载链接...";
        _urlVerified = false;

        var verifyDir = Path.Combine(Path.GetTempPath(), $"TubaCommunityVerify_{Guid.NewGuid():N}");
        try
        {
            var filter = DownloadFilterBox.Text.Trim();

            if (!url.StartsWith("gh:", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                VerifyStatusText.Text = "链接需要以 https:// 或 gh: 开头";
                return;
            }

            var info = await ToolDownloaderService.ResolveDownloadUrlAsync(
                url, string.IsNullOrWhiteSpace(filter) ? null : filter);
            if (info is null)
            {
                VerifyStatusText.Text = "无法解析链接（gh: 位置不存在或链接无效）";
                return;
            }

            Directory.CreateDirectory(verifyDir);
            VerifyStatusText.Text = $"正在下载 {info.FileName}...";

            var progress = new Progress<ToolDownloadProgress>(p => DispatcherQueue.TryEnqueue(() =>
            {
                VerifyProgress.IsIndeterminate = false;
                if (p.Percentage > 0) VerifyProgress.Value = p.Percentage;
                VerifyStatusText.Text = $"正在下载... {p.Percentage:F0}%  {ToolDownloaderService.FormatSpeed(p.SpeedMbps)}";
            }));

            var filePath = await ToolDownloaderService.DownloadToFileAsync(info.DownloadUrl, verifyDir, info.FileName, progress);

            VerifyStatusText.Text = "正在检查文件...";

            if (filePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var extractDir = Path.Combine(verifyDir, "extracted");
                Directory.CreateDirectory(extractDir);
                await ToolDownloaderService.ExtractArchiveAsync(filePath, extractDir);

                var exes = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories)
                    .Select(f => new ImportableExecutable(Path.GetRelativePath(extractDir, f).Replace('\\', '/')))
                    .OrderBy(x => x.EntryPath, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                if (exes.Count == 0)
                {
                    VerifyStatusText.Text = "下载的压缩包里没有 .exe 文件";
                    return;
                }

                SetExecutables(exes);
                if (string.IsNullOrWhiteSpace(NameBox.Text))
                    NameBox.Text = Path.GetFileNameWithoutExtension(info.FileName);
                VerifyStatusText.Text = $"下载完成，发现 {exes.Count} 个可执行文件";
            }
            else
            {
                var exes = new List<ImportableExecutable> { new(info.FileName) };
                SetExecutables(exes);
                if (string.IsNullOrWhiteSpace(NameBox.Text))
                    NameBox.Text = Path.GetFileNameWithoutExtension(info.FileName);
                VerifyStatusText.Text = "下载完成";
            }

            _urlVerified = true;
            ClearFormError();
        }
        catch (Exception ex)
        {
            VerifyStatusText.Text = $"验证失败：{ex.InnerException?.Message ?? ex.Message}";
        }
        finally
        {
            // 验证用的临时下载不再需要（提交只记录链接，不上传文件）
            try { if (Directory.Exists(verifyDir)) Directory.Delete(verifyDir, true); } catch { }

            VerifyButton.IsEnabled = true;
            VerifyProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void SetExecutables(IReadOnlyList<ImportableExecutable> exes)
    {
        _executables = exes;
        PrimaryComboBox.ItemsSource = exes;
        VariantsList.ItemsSource = exes;
        if (exes.Count > 0 && PrimaryComboBox.SelectedIndex < 0)
            PrimaryComboBox.SelectedIndex = 0;
    }

    private void IconPickButton_Click(object sender, RoutedEventArgs e)
    {
        var picked = Win32Dialogs.PickOpen(
            "图标文件\0*.png;*.ico;*.jpg;*.jpeg;*.bmp\0所有文件\0*.*\0\0", "选择工具图标");
        if (string.IsNullOrWhiteSpace(picked) || !File.Exists(picked)) return;

        _iconFilePath = picked;
        IconNameText.Text = Path.GetFileName(picked);
        try
        {
            IconPreviewImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(picked));
            IconPreviewImage.Visibility = Visibility.Visible;
            IconFallbackGlyph.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    // ---------- 预览 ----------

    private void PreviewExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args)
    {
        var draft = TryBuildDraft(out _);
        PreviewJsonText.Text = draft is null
            ? "（完善必填信息后即可预览）"
            : CommunityToolService.BuildPluginJson(draft, _userName ?? "(未登录)");
    }

    // ---------- 提交 ----------

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_submitting) return;

        var draft = TryBuildDraft(out var error);
        if (draft is null)
        {
            ShowFormError(error ?? "请完善信息");
            return;
        }

        ClearFormError();
        _pendingDraft = draft;
        BeginSubmit(draft);
    }

    private CommunityPluginDraft? TryBuildDraft(out string? error)
    {
        error = null;
        var isZipMode = MethodRadio.SelectedIndex == 0;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameBox.Focus(FocusState.Programmatic);
            error = "请填写工具名称（必填）";
            return null;
        }

        if (isZipMode)
        {
            if (string.IsNullOrWhiteSpace(_packagePath))
            {
                PickPackageButton.Focus(FocusState.Programmatic);
                error = "请选择工具压缩包（包含 .exe 的 .zip）";
                return null;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(DownloadUrlBox.Text))
            {
                DownloadUrlBox.Focus(FocusState.Programmatic);
                error = "请填写下载链接";
                return null;
            }
            if (!_urlVerified)
            {
                VerifyButton.Focus(FocusState.Programmatic);
                error = "请先点击「解析并检查」确认链接可用";
                return null;
            }
        }

        if (_executables.Count == 0)
        {
            error = isZipMode ? "请选择包含 .exe 的压缩包" : "请先解析下载链接";
            return null;
        }

        var primary = PrimaryComboBox.SelectedItem as ImportableExecutable;
        if (primary is null)
        {
            PrimaryComboBox.Focus(FocusState.Programmatic);
            error = "请选择主程序";
            return null;
        }

        var category = CategoryComboBox.SelectedItem as string ?? "其他工具";
        var launchTarget = string.IsNullOrWhiteSpace(LaunchTargetBox.Text)
            ? primary.FileName
            : LaunchTargetBox.Text.Trim();

        var archVariants = VariantsList.SelectedItems
            .OfType<ImportableExecutable>()
            .Select(item => new ImportArchVariant(item.EntryPath, GuessArch(item.EntryPath)))
            .Where(v => !string.IsNullOrWhiteSpace(v.Arch))
            .ToList();

        return new CommunityPluginDraft(
            NameBox.Text.Trim(),
            DescBox.Text.Trim(),
            category,
            CommunityToolService.ParseTagList(TagsBox.Text),
            isZipMode ? _packagePath : null,
            launchTarget,
            string.IsNullOrWhiteSpace(PublisherBox.Text) ? null : PublisherBox.Text.Trim(),
            string.IsNullOrWhiteSpace(HomepageBox.Text) ? null : HomepageBox.Text.Trim(),
            string.IsNullOrWhiteSpace(VersionBox.Text) ? null : VersionBox.Text.Trim(),
            _iconFilePath,
            isZipMode ? null : DownloadUrlBox.Text.Trim(),
            isZipMode || string.IsNullOrWhiteSpace(DownloadFilterBox.Text) ? null : DownloadFilterBox.Text.Trim(),
            archVariants);
    }

    private void BeginSubmit(CommunityPluginDraft draft)
    {
        _submitting = true;
        _cts = new CancellationTokenSource();

        FormPanel.Visibility = Visibility.Collapsed;
        ProgressCard.Visibility = Visibility.Visible;
        SuccessCard.Visibility = Visibility.Collapsed;
        ErrorCard.Visibility = Visibility.Collapsed;

        HeaderIcon.Glyph = "\uE898";
        ApplyHeaderAccent(ThemeColors.AccentBlue);
        TitleText.Text = "正在提交";
        SubtitleText.Text = "正在将你的工具提交到社区仓库...";
        SubmitProgressBar.IsIndeterminate = true;
        ProgressText.Text = "正在 Fork 仓库...";

        SubmitButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        CloseButton.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Collapsed;

        var progress = new Progress<string>(msg => DispatcherQueue.TryEnqueue(() =>
        {
            try { ProgressText.Text = msg; } catch { }
        }));

        _ = Task.Run(async () =>
        {
            try
            {
                var prUrl = await CommunityToolService.SubmitPluginAsync(draft, progress, _cts.Token);
                DispatcherQueue.TryEnqueue(() => ShowSuccess(prUrl));
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() => ShowError("已取消提交"));
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (string.IsNullOrWhiteSpace(msg)) msg = ex.GetType().Name;
                if (ex.InnerException is { } inner && !string.IsNullOrWhiteSpace(inner.Message))
                    msg += $"\n{inner.Message}";
                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                    msg += $"\n\n{ex.StackTrace}";
                DispatcherQueue.TryEnqueue(() => ShowError(msg));
            }
        }, _cts.Token);
    }

    private void ShowSuccess(string? prUrl)
    {
        _submitting = false;

        HeaderIcon.Glyph = "\uE73E";
        ApplyHeaderAccent(ThemeColors.AccentGreen);
        TitleText.Text = "提交成功";
        SubtitleText.Text = "你的工具已成功提交到社区仓库";

        ProgressCard.Visibility = Visibility.Collapsed;
        SuccessCard.Visibility = Visibility.Visible;
        ErrorCard.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(prUrl))
        {
            try
            {
                PrLink.NavigateUri = new Uri(prUrl);
                PrLink.Visibility = Visibility.Visible;
            }
            catch { }
        }

        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;

        SubmitSucceeded?.Invoke();
    }

    private void ShowError(string error)
    {
        _submitting = false;
        _errorDetail = error;

        HeaderIcon.Glyph = "\uE783";
        ApplyHeaderAccent(ThemeColors.AccentRed);
        TitleText.Text = "提交失败";
        SubtitleText.Text = "提交过程中出现错误，请查看详情";

        ProgressCard.Visibility = Visibility.Collapsed;
        SuccessCard.Visibility = Visibility.Collapsed;
        ErrorCard.Visibility = Visibility.Visible;
        ErrorText.Text = error;

        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Visible;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDraft is null)
        {
            Close();
            return;
        }

        _errorDetail = "";
        BeginSubmit(_pendingDraft);
    }

    private void CopyErrorButton_Click(object sender, RoutedEventArgs e)
    {
        // 剪贴板写入失败不得让窗口本身崩溃（详见 ClipboardService）
        var result = ClipboardService.TrySetText(_errorDetail);
        CopyErrorButtonText.Text = result.Success ? "已复制" : "复制失败，请重试";
    }

    // ---------- 工具方法 ----------

    private void ShowFormError(string message)
    {
        FormErrorBar.Message = message;
        FormErrorBar.IsOpen = true;
    }

    private void ClearFormError() => FormErrorBar.IsOpen = false;

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

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{(double)bytes / (1L << 30):F2} GB";
        if (bytes >= 1L << 20) return $"{(double)bytes / (1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{(double)bytes / (1L << 10):F1} KB";
        return $"{bytes} B";
    }
}
