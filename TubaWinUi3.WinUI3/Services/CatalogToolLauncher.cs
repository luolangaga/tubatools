using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

/// <summary>从当前界面启动目录工具；页面仅提供对话框根和状态提示，无需导航到分类页。</summary>
internal static class CatalogToolLauncher
{
    internal static Task LaunchAsync(ToolItem tool, DispatcherQueue dispatcherQueue,
        Func<XamlRoot?> getXamlRoot, Action<string, string, InfoBarSeverity> showStatus,
        bool runAsAdmin = false) =>
        new LaunchOperation(dispatcherQueue, getXamlRoot, showStatus).LaunchAsync(tool, runAsAdmin);

    private sealed class LaunchOperation(
        DispatcherQueue dispatcherQueue,
        Func<XamlRoot?> getXamlRoot,
        Action<string, string, InfoBarSeverity> showStatus)
    {
        private DispatcherQueue DispatcherQueue => dispatcherQueue;
        private XamlRoot? XamlRoot => getXamlRoot();
        private void ShowStatus(string title, string message, InfoBarSeverity severity) =>
            showStatus(title, message, severity);

        internal async Task LaunchAsync(ToolItem tool, bool runAsAdmin = false)
        {
            try { await LaunchCoreAsync(tool, runAsAdmin); }
            catch (Exception ex)
            {
                ShowStatus(LocalizationService.L("Common_LaunchFailed", "启动失败"), ex.Message, InfoBarSeverity.Error);
            }
        }

        private async Task LaunchCoreAsync(ToolItem tool, bool runAsAdmin)
        {
            if (tool.IsBuiltinLink)
            {
                await LaunchBuiltinToolAsync(tool);
                return;
            }

            if (!string.IsNullOrWhiteSpace(tool.RemoteUrl))
            {
                BrowserPage.Open(tool.RemoteUrl, tool.Name);
                Telemetry.TelemetryService.TrackExternalToolLaunch(tool.Path);
                LaunchHistoryService.RecordLaunch(tool.Path);
                return;
            }

            if (!string.IsNullOrWhiteSpace(tool.DownloadUrl) && !File.Exists(tool.EffectivePath))
            {
                await ShowDownloadDialogAsync(tool);
                return;
            }

            if (!string.IsNullOrWhiteSpace(tool.WingetId) && !File.Exists(tool.EffectivePath))
            {
                await HandleWingetToolAsync(tool);
                return;
            }

            var exePath = tool.EffectivePath;
            if (!File.Exists(exePath))
            {
                ShowStatus(
                    LocalizationService.L("Common_LaunchFailed", "启动失败"),
                    string.Format(LocalizationService.L("HomePage_LaunchFileMissing", "找不到文件：{0}"), exePath),
                    InfoBarSeverity.Error);
                return;
            }

            var ext = Path.GetExtension(exePath);
            if (ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                var command = ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
                    ? $"powershell -ExecutionPolicy Bypass -Command \"& '{exePath}'\""
                    : $"cmd.exe /c \"{exePath}\"";
                ScriptRunnerWindow.ShowAndRun(command, tool.EffectiveWorkingDir, string.Format(LocalizationService.L("HomePage_InstallScriptTitle", "安装 {0}"), tool.Name));
                Telemetry.TelemetryService.TrackExternalToolLaunch(tool.Path);
                LaunchHistoryService.RecordLaunch(tool.Path);
                return;
            }

            try
            {
                ToolProcessLauncher.Launch(exePath, tool.EffectiveWorkingDir, runAsAdmin);

                LaunchHistoryService.RecordLaunch(tool.Path);
            }
            catch (Exception ex)
            {
                ShowStatus(LocalizationService.L("Common_LaunchFailed", "启动失败"), ex.Message, InfoBarSeverity.Error);
            }
        }

        private async Task LaunchBuiltinToolAsync(ToolItem tool)
        {
            var builtinTool = BuiltinToolRegistry.GetById(tool.BuiltinToolId!);
            if (builtinTool is null)
            {
                ShowStatus(
                    LocalizationService.L("Common_LaunchFailed", "启动失败"),
                    LocalizationService.L("HomePage_BuiltinNotFound", "找不到对应的内置工具"),
                    InfoBarSeverity.Error);
                return;
            }

            try
            {
                var context = new BuiltinToolContext
                {
                    XamlRoot = XamlRoot,
                    OnProgress = msg => DispatcherQueue.TryEnqueue(() =>
                        ShowStatus(builtinTool.Name, msg, InfoBarSeverity.Informational))
                };
                MainWindow.ActiveToolName = builtinTool.Name;
                await builtinTool.ExecuteTrackedAsync(context);
                LaunchHistoryService.RecordLaunch(tool.Path);
            }
            catch (Exception ex)
            {
                ShowStatus(LocalizationService.L("Common_LaunchFailed", "启动失败"), ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                MainWindow.ActiveToolName = null;
            }
        }

        private async Task HandleWingetToolAsync(ToolItem tool)
        {
            if (tool.IsWingetInstalling) return;

            if (!tool.IsWingetInstalled)
            {
                var installed = await WingetService.IsInstalledAsync(tool.WingetId!);
                if (installed)
                {
                    tool.IsWingetInstalled = true;
                    LaunchInstalledWingetTool(tool);
                    return;
                }

                tool.IsWingetInstalling = true;
                ShowStatus(
                    LocalizationService.L("Common_InstallingTitle", "正在安装"),
                    string.Format(LocalizationService.L("HomePage_WingetInstallingMessage", "正在通过 winget 安装「{0}」..."), tool.Name),
                    InfoBarSeverity.Informational);

                var progress = new Progress<WingetInstallProgress>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        tool.WingetInstallStatus = p.StatusLine;
                        if (p.Percent > 0) tool.WingetInstallProgress = p.Percent;
                    });
                });

                var result = await WingetService.InstallAsync(tool.WingetId!, progress);
                tool.IsWingetInstalling = false;

                if (result.Success)
                {
                    tool.IsWingetInstalled = true;
                    ShowStatus(
                        LocalizationService.L("Common_InstallDone", "安装完成"),
                        string.Format(LocalizationService.L("HomePage_WingetInstalledMessage", "「{0}」安装成功，点击打开即可使用。"), tool.Name),
                        InfoBarSeverity.Success);
                }
                else
                {
                    ShowStatus(LocalizationService.L("Common_InstallFailed", "安装失败"), result.Message, InfoBarSeverity.Error);
                }
                return;
            }

            LaunchInstalledWingetTool(tool);
        }

        private void LaunchInstalledWingetTool(ToolItem tool)
        {
            var exePath = WingetService.FindInstalledExePath(tool.WingetId!);
            if (exePath is not null && File.Exists(exePath))
            {
                try
                {
                    ToolProcessLauncher.Launch(exePath, Path.GetDirectoryName(exePath));
                }
                catch (Exception ex)
                {
                    ShowStatus(LocalizationService.L("Common_LaunchFailed", "启动失败"), ex.Message, InfoBarSeverity.Error);
                }
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"\" winget run --id {tool.WingetId} --accept-source-agreements",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex)
            {
                ShowStatus(LocalizationService.L("Common_LaunchFailed", "启动失败"), ex.Message, InfoBarSeverity.Error);
            }
        }

        private async Task ShowDownloadDialogAsync(ToolItem tool)
        {
            var toolDir = Path.GetDirectoryName(tool.Path) ?? Path.Combine(ToolCatalog.ToolsRoot, tool.Category, tool.Folder);
            Directory.CreateDirectory(toolDir);

            var dialog = new ToolDownloadDialog(
                tool.Name,
                tool.Description ?? "",
                tool.DownloadUrl!,
                tool.DownloadFilter,
                toolDir) { XamlRoot = XamlRoot };

            await dialog.ShowAsync();
        }

    }
}
