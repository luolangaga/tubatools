using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services.AiPlayground;

namespace TubaWinUi3.Services.AiRuntime;

/// <summary>
/// AI 工具的统一「运行库门」：确保 AI 运行库已下载安装并完成动态加载
/// （含 EP 注册）后才允许触碰 AI 类型。
///
/// 关键约束（类型加载安全）：调用方方法体内不得在通过本门之前出现任何 AI 类型
/// （Windows ML / ORT GenAI）——CLR 在 JIT 方法时会解析其引用的类型，
/// 运行库缺失时会直接抛 FileNotFoundException。因此内置工具入口用
/// [MethodImpl(NoInlining)] 包一层「开门后再 new 页面」。
/// </summary>
public static class AiRuntimeGate
{
    /// <summary>
    /// 确保运行库就绪：已安装则直接加载 + 注册 EP 返回 true；
    /// 未安装则弹出带进度条的下载对话框，完成后返回 true；用户取消 / 失败返回 false。
    /// </summary>
    public static async Task<bool> EnsureReadyAsync(XamlRoot xamlRoot)
    {
        var installed = AiRuntimeBundleService.IsReady;

        if (AiRuntimeBundleService.Rid is null)
        {
            await ShowMessageAsync(xamlRoot, "AI 运行库",
                "当前处理器架构不支持 AI 运行库，本地AI试炼场不可用。");
            return false;
        }

        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"本地推理需要运行组件（Windows ML / ONNX Runtime），" +
                   $"首次使用需从 NuGet 官方源下载 {AiRuntimeBundleService.GetSizeHint()}，完成后可离线使用。\n" +
                   "下载会出现在「下载队列」中，支持断点续传与暂停。",
        };
        var bar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed,
        };
        var detail = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Visibility = Visibility.Collapsed,
        };
        var panel = new StackPanel { Spacing = 12, MinWidth = 440 };
        panel.Children.Add(text);
        panel.Children.Add(bar);
        panel.Children.Add(detail);

        var dialog = new TubaWinUi3.Controls.FluentContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "初始化 AI 运行库",
            Content = panel,
            PrimaryButtonText = "下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };

        var ready = false;

        async Task LoadAsync()
        {
            dialog.PrimaryButtonText = "加载中…";
            dialog.IsPrimaryButtonEnabled = false;
            dialog.CloseButtonText = "";
            text.Text = "正在准备本地 AI 试炼场，请稍候。";
            bar.Visibility = Visibility.Visible;
            bar.IsIndeterminate = true;
            detail.Visibility = Visibility.Visible;
            detail.Text = "正在加载运行组件…";
            var loadProgress = new Progress<string>(message => detail.Text = message);
            await Task.Run(() => AiRuntimeLoader.EnsureLoaded(loadProgress));
            detail.Text = "正在注册硬件加速执行提供程序…";
            await Task.Run(() => AiRuntimeService.EnsureProvidersRegisteredAsync());
            bar.IsIndeterminate = false;
            bar.Value = 100;
            detail.Text = "运行库已就绪";
            ready = true;
            dialog.Hide();
        }

        if (installed)
        {
            dialog.PrimaryButtonText = "";
            dialog.CloseButtonText = "";
            text.Text = "正在准备本地 AI 试炼场，请稍候。";
            bar.Visibility = Visibility.Visible;
            detail.Visibility = Visibility.Visible;
            detail.Text = "正在检查运行组件…";
            dialog.Opened += async (_, _) =>
            {
                try { await LoadAsync(); }
                catch (Exception ex)
                {
                    bar.IsIndeterminate = false;
                    detail.Text = $"加载失败：{ex.Message}";
                    dialog.CloseButtonText = "关闭";
                }
            };
        }
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                // 保持对话框打开，把下载/解包进度画在里面
                args.Cancel = true;
                dialog.PrimaryButtonText = "下载中…";
                dialog.IsPrimaryButtonEnabled = false;
                bar.Visibility = Visibility.Visible;
                bar.IsIndeterminate = true;
                detail.Visibility = Visibility.Visible;
                detail.Text = "正在解析下载地址…";

                var progress = new Progress<(int Percent, string Message)>(p =>
                {
                    if (p.Percent < 0)
                    {
                        bar.IsIndeterminate = true;
                    }
                    else
                    {
                        bar.IsIndeterminate = false;
                        bar.Value = p.Percent;
                    }
                    detail.Text = p.Message;
                });

                await AiRuntimeBundleService.EnsureInstalledAsync(progress, CancellationToken.None);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                bar.IsIndeterminate = false;
                detail.Text = $"下载失败：{ex.Message}\n可点击「重试」再次尝试（已完成的部分会自动跳过）。";
                dialog.CloseButtonText = "取消";
                dialog.PrimaryButtonText = "重试";
                dialog.IsPrimaryButtonEnabled = true;
            }
            finally
            {
                deferral.Complete();
            }
        };

        // 下载期间用户关闭对话框 = 放弃本次安装：取消队列项（半成品保留，下次续传）
        dialog.Closed += (_, _) =>
        {
            if (ready) return;
            var item = AiRuntimeBundleService.ActiveInstallItem;
            if (item is null) return;
            if (item.State is DownloadItemState.Queued or DownloadItemState.Resolving
                or DownloadItemState.Downloading or DownloadItemState.Processing)
            {
                CancelItem(item);
            }
        };

        await dialog.ShowAsync();
        return ready;
    }

    private static void CancelItem(DownloadItem item)
    {
        try
        {
            DownloadQueueService.Cancel(item.Id);
        }
        catch
        {
        }
    }

    /// <summary>尝试加载运行库（已就绪的快速路径）：加载 + EP 注册，失败返回 false。</summary>
    public static async Task<bool> TryLoadRuntimeAsync()
    {
        try
        {
            AiRuntimeLoader.EnsureLoaded();
            await AiRuntimeService.EnsureProvidersRegisteredAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string message)
    {
        try
        {
            var dialog = new TubaWinUi3.Controls.FluentContentDialog
            {
                XamlRoot = xamlRoot,
                Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "知道了",
                RequestedTheme = ThemeService.CurrentElementTheme,
            };
            await dialog.ShowAsync();
        }
        catch
        {
        }
    }
}
