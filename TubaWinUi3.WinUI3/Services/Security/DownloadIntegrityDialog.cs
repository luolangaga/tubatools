using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Controls;

namespace TubaWinUi3.Services;

internal static class DownloadIntegrityDialog
{
    internal static async Task<bool> ConfirmAsync(DownloadIntegrityMismatch mismatch, CancellationToken ct)
    {
        var window = App.MainWindow;
        if (window is null || ct.IsCancellationRequested) return false;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!window.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                window.ActivateFromExternal();
                if (ct.IsCancellationRequested || window.Content?.XamlRoot is not { } root)
                { completion.TrySetResult(false); return; }
                var acknowledgement = new CheckBox { Content = new TextBlock
                { Text = "我已了解文件可能损坏或被篡改，仍决定安装本次文件。", TextWrapping = TextWrapping.Wrap } };
                var confirmation = new TextBox { Header = "输入“仍然安装”确认本次继续", PlaceholderText = "仍然安装", MaxLength = 12 };
                var content = new StackPanel { Spacing = 10, MaxWidth = 500 };
                content.Children.Add(new TextBlock { Text = "文件：" + mismatch.FileName, TextWrapping = TextWrapping.Wrap });
                content.Children.Add(new TextBlock { Text = "文件校验与发布信息不一致。建议取消并重新下载；继续安装可能带来风险。", TextWrapping = TextWrapping.Wrap });
                AddHash(content, "发布方提供的 SHA-256", mismatch.ExpectedSha256);
                AddHash(content, "下载文件实际的 SHA-256", mismatch.ActualSha256);
                if (mismatch.ExpectedBytes is { } size && size != mismatch.ActualBytes)
                    content.Children.Add(new TextBlock { Text = $"文件大小也不符：发布方 {size:N0} 字节，实际 {mismatch.ActualBytes:N0} 字节", TextWrapping = TextWrapping.Wrap });
                content.Children.Add(acknowledgement); content.Children.Add(confirmation);
                var dialog = new FluentContentDialog
                {
                    Title = "文件校验不一致", Content = new ScrollViewer { Content = content, MaxHeight = 430 },
                    PrimaryButtonText = "仍然安装本次文件", CloseButtonText = "取消安装",
                    DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false,
                    XamlRoot = root, RequestedTheme = ThemeService.CurrentElementTheme
                };
                void Refresh() => dialog.IsPrimaryButtonEnabled = DownloadIntegrityPolicy.IsStrongConfirmation(acknowledgement.IsChecked == true, confirmation.Text);
                acknowledgement.Checked += (_, _) => Refresh();
                acknowledgement.Unchecked += (_, _) => Refresh();
                confirmation.TextChanged += (_, _) => Refresh();
                bool approved = false;
                dialog.PrimaryButtonClick += (_, args) =>
                {
                    approved = DownloadIntegrityPolicy.IsStrongConfirmation(acknowledgement.IsChecked == true, confirmation.Text);
                    args.Cancel = !approved;
                };
                using var cancellation = ct.Register(() => window.DispatcherQueue.TryEnqueue(() =>
                { try { dialog.Hide(); } catch { } }));
                var displayed = await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(30));
                LogService.Warn(LogService.App, $"文件校验不一致：{mismatch.FileName}，发布值={mismatch.ExpectedSha256}，实际值={mismatch.ActualSha256}，本次强制安装确认={displayed && approved && !ct.IsCancellationRequested}");
                completion.TrySetResult(displayed && approved && !ct.IsCancellationRequested);
            }
            catch { completion.TrySetResult(false); }
        })) return false;
        try { return await completion.Task.WaitAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private static void AddHash(StackPanel content, string title, string hash)
    {
        content.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = hash, FontFamily = new FontFamily("Consolas"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
    }
}
