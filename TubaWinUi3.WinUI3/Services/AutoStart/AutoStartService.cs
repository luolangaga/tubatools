using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Microsoft.Windows.AppLifecycle;

using Windows.ApplicationModel;

namespace TubaWinUi3.Services;

internal enum AutoStartState
{
    /// <summary>没有计划任务 / 启动任务已禁用，开机不会自启。</summary>
    Off,
    /// <summary>计划任务存在且指向当前程序、登录触发、最高权限，并带 --autostart 标记。</summary>
    On,
    /// <summary>任务存在但配置不对（路径变了 / 运行级别被改 / 用户不匹配 / 旧版任务缺参数）。</summary>
    NeedsRepair,
    /// <summary>查询或修改失败。</summary>
    Error,
}

internal sealed record AutoStartStatus(AutoStartState State, string? Command = null, string? Detail = null)
{
    public bool TaskExists => State is AutoStartState.On or AutoStartState.NeedsRepair;
}

internal readonly record struct AutoStartResult(bool Success, string Message, AutoStartStatus? Status = null);

/// <summary>
/// 开机自启（内置版，原 Tubatools.AutoStart 插件迁移而来）：
/// <list type="bullet">
/// <item>便携版（非打包）：当前用户的登录计划任务 + HighestAvailable，登录后直接以管理员
/// 令牌运行，不经过 runas，因此不会每次登录弹 UAC。任务参数固定为 <c>--autostart</c>，
/// 主程序据此区分「登录自启」与「手动双击」。</item>
/// <item>商店版（MSIX）：宿主清单声明的 windows.startupTask（TaskId = TubaWinUi3AutoStart），
/// 由 Windows 以当前用户身份激活；是否隐藏界面由设置项 + 激活类型判定。</item>
/// </list>
///
/// XML 必须写成 UTF-16LE + BOM（与 xml 声明一致），复用 <see cref="ScheduledTaskHelper"/>。
/// </summary>
internal static class AutoStartService
{
    internal const string TaskName = "TubaWinUi3AutoStart";
    internal const string TaskUri = @"\TubaWinUi3AutoStart";
    internal const string TaskDescription = "登录 Windows 时以管理员权限启动图吧工具箱（便携版，由内置开机自启创建）。";
    internal const string StartupTaskId = "TubaWinUi3AutoStart";

    /// <summary>是否开启开机自启。</summary>
    internal const string EnabledSettingKey = "AutoStartEnabled";

    /// <summary>登录自启时是否隐藏主窗口（只驻留托盘）。</summary>
    internal const string HideUiSettingKey = "AutoStartHideUi";

    /// <summary>旧插件保存的期望状态；首次升级时迁移到 <see cref="EnabledSettingKey"/>。</summary>
    private const string LegacyPluginDesiredKey = "Tubatools.AutoStart.Desired";

    internal static bool EnabledFromSettings => AppSettings.GetBool(EnabledSettingKey, false);

    internal static bool HideUiOnStartup => AppSettings.GetBool(HideUiSettingKey, false);

    /// <summary>把旧插件的选择迁移到内置设置（只迁一次，之后以新设置为准）。</summary>
    internal static void MigrateLegacySettings()
    {
        if (AppSettings.Get(EnabledSettingKey) is not null) return;
        var legacy = AppSettings.Get(LegacyPluginDesiredKey);
        if (legacy is not { Length: > 0 }) return;
        var desired = legacy == "1" || bool.TryParse(legacy, out var parsed) && parsed;
        AppSettings.Set(EnabledSettingKey, desired);
        AppSettings.Flush();
    }

    /// <summary>
    /// 本次进程是否为「登录自启」：便携版看 --autostart；商店版启动任务不带参数，
    /// 用 AppInstance 的激活类型 StartupTask 判定。判定失败一律按手动启动（会显示界面）。
    /// </summary>
    internal static bool IsStartupLaunch(IReadOnlyList<string> args)
    {
        if (LaunchIntent.HasAutoStart(args)) return true;
        if (!RuntimeHelper.IsPackagedContext) return false;

        try
        {
            return Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs()?.Kind
                == ExtendedActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }

    internal static string CurrentUser()
    {
        try { return WindowsIdentity.GetCurrent().Name; }
        catch { return Environment.UserName; }
    }

    internal static string? CurrentExecutable()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
            path = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path); } catch { return path; }
    }

    /// <summary>登录触发 + 最高权限 + 不限制运行时长；动作参数固定 --autostart。</summary>
    internal static string BuildTaskXml(string exePath, string userId) => $$"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
        <RegistrationInfo>
            <Description>{{ScheduledTaskHelper.Escape(TaskDescription)}}</Description>
            <URI>{{ScheduledTaskHelper.Escape(TaskUri)}}</URI>
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
            <StartWhenAvailable>true</StartWhenAvailable>
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
                <Arguments>--autostart</Arguments>
                <WorkingDirectory>{{ScheduledTaskHelper.Escape(Path.GetDirectoryName(exePath) ?? "")}}</WorkingDirectory>
            </Exec>
        </Actions>
        </Task>
        """;

    internal static async Task<AutoStartStatus> QueryAsync(CancellationToken ct = default)
    {
        if (RuntimeHelper.IsPackagedContext) return await QueryStartupTaskAsync(ct);

        var current = CurrentExecutable();
        var result = await RunSchtasksAsync($"/query /tn \"{TaskName}\" /xml", allowElevation: false, timeoutSeconds: 20, ct);
        if (!result.Started)
            return new(AutoStartState.Error, Detail: result.Error);

        if (result.ExitCode != 0)
        {
            if (IsTaskMissing(result.Error, result.ExitCode)) return new(AutoStartState.Off);
            return new(AutoStartState.Error, Detail: CleanError(result));
        }

        try
        {
            var xml = StripBom(result.Output);
            if (string.IsNullOrWhiteSpace(xml))
                return new(AutoStartState.Error, Detail: "计划任务查询返回了空内容。");

            var doc = XDocument.Parse(xml);
            var command = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value?.Trim().Trim('"') ?? "";
            var arguments = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Arguments")?.Value?.Trim() ?? "";
            var runLevel = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "RunLevel")?.Value?.Trim() ?? "";
            var userId = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "UserId")?.Value?.Trim() ?? "";
            var hasLogon = doc.Descendants().Any(e => e.Name.LocalName == "LogonTrigger");
            var isHighest = runLevel.Equals("HighestAvailable", StringComparison.OrdinalIgnoreCase);
            var sameUser = string.Equals(userId, CurrentUser(), StringComparison.OrdinalIgnoreCase);
            var sameExe = current is not null && SamePath(command, current);
            var hasAutoStartArg = arguments.Contains("--autostart", StringComparison.OrdinalIgnoreCase);

            if (hasLogon && isHighest && sameUser && sameExe && hasAutoStartArg)
                return new(AutoStartState.On, Command: command);

            var why = !hasLogon ? "缺少登录触发器"
                : !isHighest ? "运行级别不是最高权限"
                : !sameUser ? "任务用户与当前用户不一致"
                : !sameExe ? "任务指向的程序路径已变化"
                : "旧版任务缺少 --autostart 参数";
            return new(AutoStartState.NeedsRepair, Command: command, Detail: why);
        }
        catch (Exception ex)
        {
            return new(AutoStartState.NeedsRepair, Detail: "无法解析计划任务 XML：" + ex.Message);
        }
    }

    /// <summary>商店版的启动状态来自宿主清单声明的 StartupTask，由 Windows 按用户持久化。</summary>
    private static async Task<AutoStartStatus> QueryStartupTaskAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (task is null)
                return new(AutoStartState.Error, Detail: "宿主没有声明开机启动任务（windows.startupTask）。");

            return task.State switch
            {
                StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy =>
                    new(AutoStartState.On, Detail: task.State.ToString()),
                StartupTaskState.Disabled =>
                    new(AutoStartState.Off),
                StartupTaskState.DisabledByUser =>
                    new(AutoStartState.Off, Detail: "Windows 已把这个启动任务标记为用户禁用，可在任务管理器 > 启动应用 中重新启用。"),
                StartupTaskState.DisabledByPolicy =>
                    new(AutoStartState.Error, Detail: "系统策略已禁止这个启动任务，无法在应用内开启。"),
                _ => new(AutoStartState.Error, Detail: "未知的启动任务状态：" + task.State),
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new(AutoStartState.Error, Detail: "无法读取商店版启动任务：" + ex.Message);
        }
    }

    /// <summary>开启/关闭开机自启。成功后才把设置项落盘。</summary>
    internal static async Task<AutoStartResult> SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        var result = enabled
            ? await EnableAsync(ct)
            : await DisableAsync(ct);

        if (result.Success)
        {
            AppSettings.Set(EnabledSettingKey, enabled);
            AppSettings.Flush();
        }
        return result;
    }

    /// <summary>保存「自启后隐藏界面」偏好；任务参数不变，下次登录启动时读取。</summary>
    internal static void SetHideUi(bool hide)
    {
        AppSettings.Set(HideUiSettingKey, hide);
        AppSettings.Flush();
    }

    /// <summary>
    /// 启动时对账（后台执行，失败静默）：
    /// 1. 迁移旧插件设置；
    /// 2. 用户期望开启但任务缺失/指向旧路径/旧版缺参数 → 自动重建；
    /// 3. 用户期望关闭但任务还在 → 删除。
    /// 任务管理器里被用户手动禁用的（DisabledByUser）不动，尊重系统设置。
    /// </summary>
    internal static async Task ReconcileOnStartupAsync()
    {
        try
        {
            MigrateLegacySettings();
            var settingMissing = AppSettings.Get(EnabledSettingKey) is null;
            var desired = EnabledFromSettings;
            var status = await QueryAsync();

            // 首次使用：设置项没写过 → 以任务现状为准（迁移旧插件用户），不要误删旧任务。
            if (settingMissing)
            {
                AppSettings.Set(EnabledSettingKey, status.TaskExists);
                AppSettings.Flush();

                if (status.TaskExists && status.State == AutoStartState.NeedsRepair)
                {
                    var repairLegacy = await EnableAsync();
                    LogService.Info(LogService.App, repairLegacy.Success
                        ? "旧版开机自启任务已迁移并补齐 --autostart 参数。"
                        : $"旧版开机自启任务迁移失败：{repairLegacy.Message}");
                }
                return;
            }

            if (desired && status.State == AutoStartState.NeedsRepair)
            {
                var repair = await EnableAsync();
                LogService.Info(LogService.App, repair.Success
                    ? "开机自启任务已按当前路径重建。"
                    : $"开机自启任务重建失败：{repair.Message}");
                return;
            }

            if (!desired && status.TaskExists)
            {
                var remove = await DisableAsync();
                if (!remove.Success) LogService.Warn(LogService.App, $"清理多余的开机自启任务失败：{remove.Message}");
            }
        }
        catch (Exception ex)
        {
            LogService.Warn(LogService.App, $"开机自启对账失败：{ex.Message}");
        }
    }

    private static async Task<AutoStartResult> EnableAsync(CancellationToken ct = default)
    {
        if (RuntimeHelper.IsPackagedContext) return await EnableStartupTaskAsync(ct);

        var exe = CurrentExecutable();
        if (string.IsNullOrWhiteSpace(exe))
            return new(false, "无法定位工具箱程序路径。", new AutoStartStatus(AutoStartState.Error, Detail: "无法定位程序路径"));
        if (!File.Exists(exe))
            return new(false, $"找不到工具箱程序：{exe}", new AutoStartStatus(AutoStartState.Error, Detail: "程序文件不存在"));

        var xml = BuildTaskXml(exe, CurrentUser());
        var xmlPath = await ScheduledTaskHelper.WriteTaskXmlAsync(xml);
        var result = default(SchtasksResult);
        try
        {
            result = await RunSchtasksAsync(
                $"/create /tn \"{TaskName}\" /XML \"{xmlPath}\" /f", allowElevation: true, timeoutSeconds: 90, ct);
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }

        var status = await QueryAsync(ct);
        if (!result.Started || result.ExitCode != 0)
            return new(false, "创建开机自启计划任务失败。" + Environment.NewLine + CleanError(result), status);

        return status.State == AutoStartState.On
            ? new(true, "已开启：登录 Windows 后将以管理员权限自动启动。", status)
            : new(false, "计划任务已提交，但校验未通过：" + (status.Detail ?? "任务配置与预期不一致。"), status);
    }

    private static async Task<AutoStartResult> EnableStartupTaskAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (task is null)
                return new(false, "宿主没有声明开机启动任务（windows.startupTask），无法开启。",
                    new AutoStartStatus(AutoStartState.Error, Detail: "缺少 windows.startupTask 清单扩展"));

            if (task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
                return new(true, "已开启：登录 Windows 后将以当前用户身份自动启动。", await QueryStartupTaskAsync(ct));

            var state = await task.RequestEnableAsync();
            var status = await QueryStartupTaskAsync(ct);
            return state switch
            {
                StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy =>
                    new(true, "已开启：登录 Windows 后将以当前用户身份自动启动。", status),
                StartupTaskState.DisabledByUser =>
                    new(false, "Windows 已把这个启动任务标记为用户禁用。请在任务管理器 > 启动应用 中启用「图吧工具箱CE」，或关闭后再重试。", status),
                StartupTaskState.DisabledByPolicy =>
                    new(false, "系统策略禁止开机启动，无法在应用内开启。", status),
                _ => new(false, "请求开启启动任务失败：" + state, status),
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "Windows 已把这个启动任务标记为用户禁用。请在任务管理器 > 启动应用 中启用「图吧工具箱CE」，或关闭后再重试。",
                new AutoStartStatus(AutoStartState.Off, Detail: "Windows 启动应用设置已禁用此任务"));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new(false, "开启商店版启动任务失败：" + ex.Message,
                new AutoStartStatus(AutoStartState.Error, Detail: ex.Message));
        }
    }

    private static async Task<AutoStartResult> DisableAsync(CancellationToken ct = default)
    {
        if (RuntimeHelper.IsPackagedContext) return await DisableStartupTaskAsync(ct);

        var before = await QueryAsync(ct);
        if (before.State == AutoStartState.Off)
            return new(true, "已关闭：当前没有开机自启任务。", before);
        if (before.State == AutoStartState.Error)
            return new(false, "无法确认计划任务状态：" + (before.Detail ?? "schtasks 查询失败。"), before);

        var result = await RunSchtasksAsync(
            $"/delete /tn \"{TaskName}\" /f", allowElevation: true, timeoutSeconds: 90, ct);
        if ((!result.Started || result.ExitCode != 0) && !IsTaskMissing(result.Error, result.ExitCode))
            return new(false, "删除开机自启计划任务失败。" + Environment.NewLine + CleanError(result), before);

        var after = await QueryAsync(ct);
        return after.State == AutoStartState.Off
            ? new(true, "已关闭：登录 Windows 时不会再自动启动。", after)
            : new(false, "已提交删除，但校验未通过：" + (after.Detail ?? "计划任务仍然存在。"), after);
    }

    private static async Task<AutoStartResult> DisableStartupTaskAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (task is null)
                return new(false, "宿主没有声明开机启动任务（windows.startupTask），无需关闭，但状态无法确认。",
                    new AutoStartStatus(AutoStartState.Error, Detail: "缺少 windows.startupTask 清单扩展"));

            if (task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
                task.Disable();

            var after = await QueryStartupTaskAsync(ct);
            return after.State == AutoStartState.Off
                ? new(true, "已关闭：登录 Windows 时不会再自动启动。", after)
                : new(false, "已提交关闭，但校验未通过：" + (after.Detail ?? "启动任务仍然启用。"), after);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new(false, "关闭商店版启动任务失败：" + ex.Message,
                new AutoStartStatus(AutoStartState.Error, Detail: ex.Message));
        }
    }

    private static string StripBom(string value)
    {
        var text = value.TrimStart();
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..].TrimStart() : text;
    }

    private static bool SamePath(string actual, string expected)
    {
        static string Normalize(string value)
        {
            var path = value.Trim().Trim('"').Replace('/', '\\');
            try { path = Path.GetFullPath(path); } catch { }
            return path.TrimEnd('\\');
        }
        return string.Equals(Normalize(actual), Normalize(expected), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTaskMissing(string error, int exitCode)
    {
        var text = error.ToLowerInvariant();
        if (text.Contains("找不到") || text.Contains("cannot find") || text.Contains("specified file") ||
            text.Contains("不存在") || text.Contains("no such") || text.Contains("not exist"))
            return true;
        return exitCode == 1 && string.IsNullOrWhiteSpace(error);
    }

    private static string CleanError(SchtasksResult result)
    {
        var text = result.Error.Trim();
        if (text.Length == 0) text = result.Output.Trim();
        if (text.Length == 0) text = $"schtasks 退出码 {result.ExitCode}";
        return text;
    }

    private static async Task<SchtasksResult> RunSchtasksAsync(string arguments, bool allowElevation, int timeoutSeconds, CancellationToken ct)
    {
        if (allowElevation && !ScheduledTaskHelper.IsElevated())
            return await RunElevatedAsync(arguments, timeoutSeconds, ct);

        var psi = new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = ConsoleEncoding.Value,
        };

        Process? process = null;
        try
        {
            process = Process.Start(psi);
            if (process is null) return StartFailure("无法启动 schtasks。");

            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            try
            {
                await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), ct);
            }
            catch (TimeoutException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return StartFailure("调用 schtasks 超时。");
            }

            return new(true, process.ExitCode, await stdout, await stderr);
        }
        catch (Exception ex)
        {
            return StartFailure("无法运行 schtasks：" + ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static async Task<SchtasksResult> RunElevatedAsync(string arguments, int timeoutSeconds, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var process = Process.Start(psi);
            if (process is null) return StartFailure("无法启动 schtasks（提权）。");
            try
            {
                await process.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), ct);
            }
            catch (TimeoutException)
            {
                try { process.Kill(); } catch { }
                return StartFailure("等待提权操作超时。");
            }
            return new(true, process.ExitCode, "", "");
        }
        catch (Exception ex)
        {
            return StartFailure("提权运行 schtasks 失败：" + ex.Message);
        }
    }

    private static SchtasksResult StartFailure(string error) => new(false, -1, "", error);

    private sealed record SchtasksResult(bool Started, int ExitCode, string Output, string Error);

    private static readonly Lazy<Encoding> ConsoleEncoding = new(() =>
    {
        try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
        catch
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage); }
            catch { return Encoding.UTF8; }
        }
    });
}
