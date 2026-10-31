using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AgentCli;

namespace TubaWinUi3.Pages;

public sealed partial class SettingsPage
{
    private string SelectedSkillAgent => (AgentSkillsTarget.SelectedItem as ComboBoxItem)?.Tag as string ?? "codex";

    private async void InstallAgentSkills_Click(object sender, RoutedEventArgs e)
    {
        AgentSkillsInstall.IsEnabled = false;
        AgentSkillsStatus.IsOpen = true;
        AgentSkillsStatus.Severity = InfoBarSeverity.Informational;
        AgentSkillsStatus.Message = "正在准备技能并通过 Node.js Skills 管理器安装，请稍候…";
        try
        {
            var agent = SelectedSkillAgent;
            AgentSkillsStatus.Message = await Task.Run(() => AgentSkillInstaller.InstallAsync(agent));
            AgentSkillsStatus.Severity = InfoBarSeverity.Success;
        }
        catch (Exception ex) { ShowAgentSkillsError(ex); }
        finally { AgentSkillsInstall.IsEnabled = true; }
    }

    private void CopyAgentSkillsCommand_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = AgentSkillInstaller.Export();
            if (!ClipboardService.TrySetText(AgentSkillInstaller.InstallCommand(path, SelectedSkillAgent)).Success)
                throw new InvalidOperationException("剪贴板正被占用，请稍后重试。");
            AgentSkillsStatus.IsOpen = true;
            AgentSkillsStatus.Severity = InfoBarSeverity.Success;
            AgentSkillsStatus.Message = "已复制。打开 PowerShell，粘贴后按回车即可安装。需要 Node.js 22.20 或更新版本。";
        }
        catch (Exception ex) { ShowAgentSkillsError(ex); }
    }

    private async void ExportAgentSkills_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = AgentSkillInstaller.Export();
            await Windows.System.Launcher.LaunchFolderPathAsync(path);
        }
        catch (Exception ex) { ShowAgentSkillsError(ex); }
    }

    private void ShowAgentSkillsError(Exception ex)
    {
        AgentSkillsStatus.IsOpen = true;
        AgentSkillsStatus.Severity = InfoBarSeverity.Error;
        AgentSkillsStatus.Message = ex.Message;
    }
}
