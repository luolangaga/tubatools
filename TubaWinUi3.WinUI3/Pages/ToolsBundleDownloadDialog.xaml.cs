using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class ToolsBundleDownloadDialog : ContentDialog
{
    // 等已有对话框关闭的最长时间：超时放弃展示（静默流程不打扰用户，也不会崩）。
    private static readonly TimeSpan ShowTimeout = TimeSpan.FromSeconds(30);

    private ToolsBundleUpdateInfo? _updateInfo;
    private bool _isBusy;
    private bool _selectedLite;

    /// <summary>本次操作是否已把内核包加入后台下载队列（调用方据此刷新状态文案）。</summary>
    public bool DownloadEnqueued { get; private set; }

    public ToolsBundleDownloadDialog()
    {
        InitializeComponent();
        TubaWinUi3.Services.FluentDialogLayout.Attach(this);
        XamlRoot = App.MainWindow?.Content?.XamlRoot;
    }

    public void SetDescription(string text)
    {
        DescText.Text = text;
    }

    /// <summary>
    /// 弹出下载对话框。info 可为 null（内部自行检查更新），也可直接传入已解析的
    /// 更新信息（含 HasUpdate=false 的场景：精简版用户升级到完整版）。
    /// 确认下载后对话框立即关闭，下载与解压全程交给后台下载队列
    /// （标题栏下载按钮看进度，完成/失败有系统通知），不再驻留前台。
    /// </summary>
    public async Task ShowDownloadAsync(ToolsBundleUpdateInfo? info = null)
    {
        // 队列里已有内核包任务：不再提供第二个下载入口
        // （同一目标目录并发解压/替换会互相破坏），只提示进度查看位置。
        if (ToolsBundleService.HasPendingBundleDownload())
        {
            ShowPendingDownloadState();
        }
        else if (info is not null)
        {
            ApplyInfo(info);
        }
        else
        {
            ResolvingSection.Visibility = Visibility.Visible;
            _ = ResolveAndShowAsync();
        }

        // 已有对话框在展示时（如静默更新提示）等它关闭再弹：
        // 并发 ShowAsync 会抛 COMException「只能有一个 ContentDialog」，静默流程下
        // 该异常无人接管，会直接触发全局崩溃上报。
        await ContentDialogGuard.ShowWhenIdleAsync(this, ShowTimeout);
    }

    private void ApplyInfo(ToolsBundleUpdateInfo info)
    {
        _updateInfo = info;
        var kind = ToolsBundleService.GetInstalledKind();

        // 有更新时提供「跳过此版本」：记住该版本，静默通道不再提示
        SecondaryButtonText = info.HasUpdate ? "跳过此版本" : null;

        // 完整版已是最新：无事可做（完整版不可降级到精简版，也不重复下载）
        if (kind == ToolsBundleService.KindFull && !info.HasUpdate)
        {
            DescText.Text = "当前完整版内核已是最新版本，无需下载。";
            IsPrimaryButtonEnabled = false;
            return;
        }

        UpdateDescriptionFromInfo(info);
        ShowVariantSelection(info, kind);
    }

    /// <summary>队列中已有内核包任务时的只读状态：只给「知道了」，不再提供下载入口。</summary>
    private void ShowPendingDownloadState()
    {
        TitleText.Text = "内核正在下载中";
        DescText.Text = "内核包已在后台下载队列中，可在标题栏的下载按钮查看进度或取消，安装完成后自动生效。";
        ResolvingSection.Visibility = Visibility.Collapsed;
        SourceSection.Visibility = Visibility.Collapsed;
        VariantSection.Visibility = Visibility.Collapsed;
        SecondaryButtonText = null;
        PrimaryButtonText = null;
        CloseButtonText = "知道了";
    }

    /// <summary>「跳过此版本」：记录当前内核版本，此后启动/静默检查不再提示该版本的更新。</summary>
    private void OnSecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_isBusy)
        {
            args.Cancel = true;
            return;
        }

        if (_updateInfo is not null && _updateInfo.HasUpdate)
            ToolsBundleService.SetSkippedVersion(_updateInfo.Version);

        Hide();
    }

    private void UpdateDescriptionFromInfo(ToolsBundleUpdateInfo info)
    {
        if (info.HasUpdate)
        {
            var sizeStr = info.Size > 0 ? $"（完整版约 {ToolsBundleService.FormatSize(info.Size)}）" : "";
            DescText.Text = $"发现内核新版本 v{info.Version}{sizeStr}，请选择要下载的版本。";
        }
        else
        {
            DescText.Text = $"当前内核版本 v{info.Version}，可选择切换到完整版内核。";
        }
    }

    /// <summary>
    /// 展示 精简版/完整版 选择：
    /// - 已安装完整版：精简版选项禁用（不支持降级）；
    /// - 精简版已是最新：精简版禁用，仅可升级完整版；
    /// - 首次安装：两者均可选，默认完整版。
    /// </summary>
    private void ShowVariantSelection(ToolsBundleUpdateInfo info, string? kind)
    {
        VariantSection.Visibility = Visibility.Visible;
        IsPrimaryButtonEnabled = true;

        var liteSelectable = info.HasLiteAsset &&
                             kind != ToolsBundleService.KindFull &&
                             info.HasUpdate;

        LiteRadio.IsEnabled = liteSelectable;
        LiteRadioSub.Text = !info.HasLiteAsset
            ? "该版本未提供精简包"
            : kind == ToolsBundleService.KindFull
                ? "已安装完整版，不可降级"
                : !info.HasUpdate
                    ? (kind == ToolsBundleService.KindLite ? "当前已是最新" : "已内置精简工具，无需下载")
                    : info.LiteSize > 0
                        ? $"约 {ToolsBundleService.FormatSize(info.LiteSize)}"
                        : "";

        FullRadio.IsEnabled = true;
        FullRadioSub.Text = info.Size > 0 ? $"约 {ToolsBundleService.FormatSize(info.Size)}" : "";
        FullRadio.IsChecked = true;

        string? hint = (kind, info.HasUpdate) switch
        {
            (ToolsBundleService.KindFull, true) => "已安装完整版内核，不支持降级到精简版。",
            (ToolsBundleService.KindLite, false) => "当前精简版内核已是最新，可升级到完整版获得全部工具。",
            (null, false) => "已内置精简工具集，可下载完整版内核获得全部工具。",
            _ => null
        };
        if (hint is null)
        {
            VariantHintText.Visibility = Visibility.Collapsed;
        }
        else
        {
            VariantHintText.Text = hint;
            VariantHintText.Visibility = Visibility.Visible;
        }

        // 两个下载源同时竞赛，无需用户手动选择
        SourceSection.Visibility = Visibility.Visible;
    }

    private async Task ResolveAndShowAsync()
    {
        try
        {
            var info = await ToolsBundleService.CheckForToolsUpdateAsync();
            ResolvingSection.Visibility = Visibility.Collapsed;

            if (info is null)
            {
                DescText.Text = $"无法获取内核信息，请检查网络连接后重试。";
                return;
            }

            ApplyInfo(info);
        }
        catch (Exception ex)
        {
            ResolvingSection.Visibility = Visibility.Collapsed;
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }

    private void OnVariantChecked(object sender, RoutedEventArgs e)
    {
        if (sender == LiteRadio) _selectedLite = true;
        else if (sender == FullRadio) _selectedLite = false;
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_isBusy)
        {
            args.Cancel = true;
            return;
        }

        var deferral = args.GetDeferral();
        args.Cancel = true;

        try
        {
            await StartDownloadAsync();
        }
        finally
        {
            try { deferral.Complete(); } catch { }
        }
    }

    /// <summary>
    /// 把所选变种内核包交给后台下载队列后立即关闭对话框：下载/解压期间不占用窗口，
    /// 用户可继续使用应用，进度在标题栏下载队列里查看。
    /// </summary>
    private async Task StartDownloadAsync()
    {
        if (_updateInfo is null)
        {
            _updateInfo = await ToolsBundleService.CheckForToolsUpdateAsync();
            if (_updateInfo is null)
            {
                ErrorBar.Message = $"未找到可用的内核更新。";
                ErrorBar.IsOpen = true;
                return;
            }
            ApplyInfo(_updateInfo);
        }

        if (ToolsBundleService.HasPendingBundleDownload())
        {
            ShowPendingDownloadState();
            return;
        }

        _isBusy = true;

        var lite = _selectedLite;
        var version = _updateInfo.Version;

        // MSIX 解压到 LocalAppData 内核目录；精简版便携已内置 Tools 时就地升级
        var toolsDir = ToolsBundleService.GetInstallTargetDir();

        var resolver = ToolsBundleService.CreateUrlResolver(_updateInfo, preferGitCode: true, lite: lite);

        DownloadQueueService.EnqueueWithResolver(
            displayName: $"{(lite ? "精简版" : "完整版")}内核 " + (version ?? ""),
            urlResolver: resolver,
            destinationPath: toolsDir,
            postProcessor: new ToolsBundleExtractProcessor(version, lite ? ToolsBundleService.KindLite : ToolsBundleService.KindFull),
            description: lite ? "图吧工具箱CE精简版内核" : "图吧工具箱CE完整版内核",
            glyph: "\uE896",
            fallbackUrl: _updateInfo.FallbackUrl(lite));

        DownloadEnqueued = true;
        Hide();
    }
}
