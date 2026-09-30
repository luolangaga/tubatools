// EnergyStar auto-start helper for the unpackaged (non-MSIX) build.
// Ported from EnergyStarX StartupService (https://github.com/JasonWei512/EnergyStarX)
// — only the admin scheduled-task path is kept, because TubaWinUi3 is unpackaged
// (WindowsPackageType=None) and its app already runs as admin (auto-elevates via
// App.OnLaunched). MSIX StartupTask correspondence therefore does not apply here.

using System.Diagnostics;
using System.Security.Principal;

namespace TubaWinUi3.Services;

/// <summary>Schedule EnergyStar to start (throttling) at Windows logon, as admin.</summary>
public static class EnergyStarStartupService
{
    public const string ScheduleTaskName = "TubaWinUi3EnergyStarStartupTask";
    public const string SilentArg = "--energystar-silent";

    public enum StartupType { None, Admin }

    public static async Task<bool> GetAdminScheduleTaskExistsAsync()
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/query /tn \"{ScheduleTaskName}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        try
        {
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<StartupType> GetStartupTypeAsync()
    {
        return await GetAdminScheduleTaskExistsAsync() ? StartupType.Admin : StartupType.None;
    }

    /// <summary>
    /// 计划任务 XML。internal 以便单测锁定「encoding 声明（UTF-16）与落盘编码一致」——
    /// 声明 UTF-16 却按 UTF-8 写盘，会让 schtasks 解析到中文 &lt;Description&gt; 直接失败，
    /// 详见 <see cref="ScheduledTaskHelper"/>。
    /// </summary>
    internal static string BuildTaskXml(string exePath, string userId) => $$"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
        <RegistrationInfo>
            <Description>开机自启 TubaWinUi3 后台节能 (EcoQoS 效率模式)。</Description>
            <URI>\{{ScheduleTaskName}}</URI>
        </RegistrationInfo>
        <Triggers>
            <LogonTrigger>
                <Enabled>true</Enabled>
                <UserId>{{ScheduledTaskHelper.Escape(userId)}}</UserId>
            </LogonTrigger>
        </Triggers>
        <Principals>
            <Principal id="Author">
                <LogonType>InteractiveToken</LogonType>
                <RunLevel>HighestAvailable</RunLevel>
            </Principal>
        </Principals>
        <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>false</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <IdleSettings>
                <StopOnIdleEnd>false</StopOnIdleEnd>
                <RestartOnIdle>false</RestartOnIdle>
            </IdleSettings>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <Hidden>false</Hidden>
            <RunOnlyIfIdle>false</RunOnlyIfIdle>
            <WakeToRun>false</WakeToRun>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>7</Priority>
        </Settings>
        <Actions Context="Author">
            <Exec>
                <Command>{{ScheduledTaskHelper.Escape(exePath)}}</Command>
                <Arguments>{{SilentArg}}</Arguments>
            </Exec>
        </Actions>
        </Task>
        """;

    /// <summary>创建（或替换）管理员计划任务。失败时返回真实原因，不再是「UAC 可能被拒绝」。</summary>
    public static async Task<StartupTaskResult> CreateAdminScheduleTaskAsync()
    {
        var exePath = GetExecutablePath();
        if (string.IsNullOrEmpty(exePath)) return StartupTaskResult.Fail("拿不到程序自身路径");

        var xmlPath = await ScheduledTaskHelper.WriteTaskXmlAsync(
            BuildTaskXml(exePath, WindowsIdentity.GetCurrent().Name));

        try
        {
            var result = await ScheduledTaskHelper.RunAsync(
                $"/create /tn \"{ScheduleTaskName}\" /XML \"{xmlPath}\" /f");
            if (!result.Success) return result;

            return await GetAdminScheduleTaskExistsAsync()
                ? StartupTaskResult.Ok()
                : StartupTaskResult.Fail("schtasks 报告成功，但计划任务未出现在系统中");
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    /// <summary>删除计划任务。</summary>
    public static async Task<StartupTaskResult> DeleteAdminScheduleTaskAsync()
    {
        var result = await ScheduledTaskHelper.RunAsync($"/delete /tn \"{ScheduleTaskName}\" /f");
        if (!result.Success) return result;

        return await GetAdminScheduleTaskExistsAsync()
            ? StartupTaskResult.Fail("计划任务仍然存在（删除未生效）")
            : StartupTaskResult.Ok();
    }

    public static async Task<StartupTaskResult> SetStartupEnabledAsync(bool enabled)
    {
        if (enabled)
        {
            return await CreateAdminScheduleTaskAsync();
        }
        return await DeleteAdminScheduleTaskAsync();
    }

    private static string GetExecutablePath()
    {
        return Process.GetCurrentProcess().MainModule?.FileName ?? "";
    }
}
