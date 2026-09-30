using System.Diagnostics;
using System.Security.Principal;

namespace TubaWinUi3.Services.ActiveIntercept;

/// <summary>
/// 主动拦截后端开机自启（管理员计划任务）。
/// 后端需要管理员权限（写入 HKLM 屏蔽右键菜单），因此不能用普通用户 Run 键，
/// 走「登录时以最高权限运行」的计划任务，直接拉起 NativeAOT 后端（--config 已隐藏控制台窗口）。
/// </summary>
public static class ActiveInterceptStartupService
{
    public const string ScheduleTaskName = "TubaWinUi3ActiveInterceptBackend";

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
    internal static string BuildTaskXml(string exePath, string arguments, string userId) => $$"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
        <RegistrationInfo>
            <Description>登录时启动图吧工具箱CE主动拦截后端（流氓软件拦截器），需管理员权限以屏蔽第三方右键菜单。</Description>
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
            <Priority>5</Priority>
        </Settings>
        <Actions Context="Author">
            <Exec>
                <Command>{{ScheduledTaskHelper.Escape(exePath)}}</Command>
                <Arguments>{{ScheduledTaskHelper.Escape(arguments)}}</Arguments>
            </Exec>
        </Actions>
        </Task>
        """;

    /// <summary>创建（或覆盖）管理员计划任务：登录时以最高权限启动主动拦截后端。失败时返回真实原因。</summary>
    public static async Task<StartupTaskResult> CreateAdminScheduleTaskAsync()
    {
        var exePath = ActiveInterceptService.BackEndExePath;
        if (!File.Exists(exePath)) return StartupTaskResult.Fail($"找不到后端程序：{exePath}");

        // 先把后端配置写好，计划任务启动时后端直接读取（不依赖主程序进程）。
        ActiveInterceptService.EnsureConfigWritten();

        var arguments = $"\"--config\" \"{ActiveInterceptService.ConfigPath}\"";
        var xmlPath = await ScheduledTaskHelper.WriteTaskXmlAsync(
            BuildTaskXml(exePath, arguments, WindowsIdentity.GetCurrent().Name));

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
}
