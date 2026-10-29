using System.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>首页与分类页共用的拖放导入流程；仅在所属页面可见时接收文件。</summary>
internal sealed class CustomToolDropHandler
{
    private static readonly HashSet<string> DropImportableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".exe", ".bat", ".cmd", ".ps1", ".vbs"
    };

    private readonly Page _page;
    private readonly Func<Task> _refresh;
    private readonly Action<string, string, InfoBarSeverity> _showStatus;
    private bool _dropEventSubscribed;
    private bool _importing;

    public CustomToolDropHandler(Page page, Func<Task> refresh,
        Action<string, string, InfoBarSeverity> showStatus)
    {
        _page = page;
        _refresh = refresh;
        _showStatus = showStatus;
        page.Loaded += (_, _) => InstallDropHook();
        page.Unloaded += (_, _) => UninstallDropHook();
    }

    private void InstallDropHook()
    {
        // 获取主窗口句柄并安装拖放钩子；此调用依赖 App.MainWindow 的 WinRT 封送，
        // 启动早期可能尚未就绪而抛异常，不能让一个非关键钩子打断启动流程。
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            if (hwnd != IntPtr.Zero) Win32DropHelper.EnsureInstalled(hwnd);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CustomToolDropHandler] 安装拖放钩子失败: {ex}");
        }

        if (!_dropEventSubscribed)
        {
            _dropEventSubscribed = true;
            Win32DropHelper.FilesDropped += OnDropFiles;
        }
    }

    private void UninstallDropHook()
    {
        _dropEventSubscribed = false;
        Win32DropHelper.FilesDropped -= OnDropFiles;
    }

    private void OnDropFiles(IReadOnlyList<string> files)
    {
        var importable = files.FirstOrDefault(f => DropImportableExtensions.Contains(Path.GetExtension(f)));
        if (importable is null) return;

        _page.DispatcherQueue.TryEnqueue(() =>
        {
            if (_dropEventSubscribed) _ = ImportDroppedFileAsync(importable);
        });
    }

    /// <summary>拖入文件后弹出导入弹窗；确认后导入、刷新导航与本页工具列表。</summary>
    private async Task ImportDroppedFileAsync(string filePath)
    {
        if (_importing) return;
        _importing = true;
        try
        {
            await ImportDroppedFileCoreAsync(filePath);
        }
        catch (Exception ex)
        {
            // 兜底：弹窗链路任何异常都不能静默（旧版开独立窗口不依赖页面状态，
            // 弹窗必须挂主窗口 XamlRoot 才能保证任何页面状态下都能弹出）
            ShowStatus(LocalizationService.L("Common_ImportFailed", "导入失败"), ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _importing = false;
        }
    }

    private async Task ImportDroppedFileCoreAsync(string filePath)
    {
        // 弹窗挂主窗口的 XamlRoot（与旧窗口 Content.XamlRoot 等价），
        // 使用当前主窗口的根节点，避免页面切换时根节点不可用。
        var xamlRoot = App.MainWindow?.Content?.XamlRoot ?? _page.XamlRoot;
        if (xamlRoot is null)
        {
            ShowStatus(
                LocalizationService.L("Common_ImportFailed", "导入失败"),
                LocalizationService.L("HomePage_ImportNoWindow", "无法获取窗口，请重试"),
                InfoBarSeverity.Error);
            return;
        }

        var isZip = Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<ImportableExecutable> executables;
        try
        {
            executables = isZip
                ? CustomToolPackageService.GetExecutables(filePath)
                : [new ImportableExecutable(Path.GetFileName(filePath))];
        }
        catch (Exception ex)
        {
            ShowStatus(LocalizationService.L("HomePage_ImportUnreadable", "无法读取文件"), ex.Message, InfoBarSeverity.Error);
            return;
        }

        if (executables.Count == 0)
        {
            await ShowMessageAsync(
                LocalizationService.L("HomePage_ImportNoExeTitle", "未找到可导入工具"),
                LocalizationService.L("HomePage_ImportNoExeMessage", "压缩包里需要至少包含一个 .exe 文件。"));
            return;
        }

        var dialog = new CustomToolImportDialog(xamlRoot, filePath, executables);
        var request = await dialog.ShowImportAsync();
        if (request is null) return;

        try
        {
            var result = isZip
                ? await CustomToolPackageService.ImportAsync(request)
                : await CustomToolPackageService.ImportSingleFileAsync(
                    filePath,
                    request.ToolName,
                    request.Category,
                    request.Description,
                    request.Publisher,
                    request.Tags);

            // 新建分类：记录图标并追加到分类顺序，保持固定位置而非按字母序插入
            if (dialog.IsNewCategory)
            {
                AppSettings.Set($"CategoryGlyph_{request.Category}", dialog.NewCategoryGlyph ?? "\uE8B7");
                ToolCatalog.AppendCategoryOrder(request.Category);
            }

            ToolCatalog.InvalidateTagsCache();
            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.RefreshToolCategories();

            await _refresh();
            ShowStatus(
                LocalizationService.L("Common_ImportSuccess", "导入成功"),
                string.Format(LocalizationService.L("HomePage_ImportSuccessMessage", "已导入 {0}"), Path.GetFileName(result.ToolDirectory)),
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            // 分类下没有工具就删掉：新建分类导入失败时清理留下的空白目录
            if (ToolCatalog.PruneCategoryIfEmpty(request.Category))
            {
                ToolCatalog.InvalidateTagsCache();
                if (App.MainWindow is MainWindow mainWindow)
                    mainWindow.RefreshToolCategories();
            }
            ShowStatus(LocalizationService.L("Common_ImportFailed", "导入失败"), ex.Message, InfoBarSeverity.Error);
        }
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
        => _showStatus(title, message, severity);

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new TubaWinUi3.Controls.FluentContentDialog
        {
            XamlRoot = App.MainWindow?.Content?.XamlRoot ?? _page.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
            CloseButtonText = LocalizationService.L("Common_Confirm", "确定"),
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        await dialog.ShowAsync();
    }
}
