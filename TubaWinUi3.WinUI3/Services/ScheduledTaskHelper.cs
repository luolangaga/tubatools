using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>
/// 计划任务操作的返回结果。失败时带上**真实原因**（schtasks 的报错或退出码），
/// 界面据此提示——不要再把任何失败都猜成"UAC 可能被拒绝"。
/// </summary>
public readonly record struct StartupTaskResult(bool Success, string? Error)
{
    public static StartupTaskResult Ok() => new(true, null);
    public static StartupTaskResult Fail(string error) => new(false, error);
}

/// <summary>
/// schtasks 计划任务的公共部分：任务 XML 落盘、调用与失败原因。
///
/// <para><b>为什么必须 UTF-16</b>：<c>schtasks /create /XML</c> 只认真正的 Unicode
/// （UTF-16）文件。XML 声明写着 <c>encoding="UTF-16"</c>、文件实际却是 UTF-8
/// （<c>File.WriteAllText</c> 不带编码参数的默认行为）时，MSXML 解析到第一个非 ASCII
/// 节点就报「任务 XML 包含意外节点 (5,3):Description」——中文 &lt;Description&gt;
/// 恰好就是第一个。计划任务因此永远建不出来，而调用方只看到 process 正常退出，
/// 于是把失败误写成"UAC 可能被拒绝"（UAC 开不开都一样失败）。</para>
///
/// <para>本机实测（Windows 11 26200，非提权）：
/// UTF-16+BOM → <c>错误: 拒绝访问</c>（XML 已通过解析，走到权限检查）；
/// UTF-8 → <c>错误: 任务 XML 包含意外节点 (5,3):Description</c>。</para>
/// </summary>
internal static class ScheduledTaskHelper
{
    /// <summary>UTF-16LE + BOM，与任务 XML 里的 <c>encoding="UTF-16"</c> 声明严格一致。</summary>
    internal static Encoding TaskXmlEncoding { get; } =
        new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

    /// <summary>
    /// 把外部字符串安全地放进 XML 文本节点。程序路径 / 用户名 / --config 路径都可能带
    /// <c>&amp;</c> 或 <c>&lt;</c>（例如装在 <c>D:\Games &amp; Tools\</c>、机器名自带
    /// <c>&amp;</c>）——不转义就是非法 XML，schtasks 会把整份 XML 退回来，
    /// 又是一次"任务建不出来却看不出原因"。
    /// </summary>
    internal static string Escape(string? value) => SecurityElement.Escape(value) ?? "";

    /// <summary>把任务 XML 写进临时文件（UTF-16 + BOM），返回文件路径。</summary>
    internal static async Task<string> WriteTaskXmlAsync(string xml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"TubaWinUi3Task_{Guid.NewGuid():N}.xml");
        await File.WriteAllTextAsync(path, xml, TaskXmlEncoding);
        return path;
    }

    /// <summary>当前进程是否已提权。UAC 关闭时管理员拿到的是完整令牌 → true。</summary>
    internal static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 构造 schtasks 调用。**已提权时不再套 runas**：本程序（非打包版）启动即自动提权，
    /// 再走一层 ShellExecute 提权没有意义，在关闭 UAC 的机器上还白白多一个可能失败的环节。
    /// 已提权时改为直接启动并重定向 stderr，这样失败原因拿得到。
    /// </summary>
    private static ProcessStartInfo CreateStartInfo(string arguments)
    {
        if (IsElevated())
        {
            return new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
        }

        return new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
    }

    /// <summary>运行 schtasks；失败时返回能直接展示给用户的原因。</summary>
    internal static async Task<StartupTaskResult> RunAsync(string arguments)
    {
        try
        {
            var psi = CreateStartInfo(arguments);
            var redirected = psi.RedirectStandardError;

            using var process = new Process { StartInfo = psi };
            process.Start();

            // 先读干净 stderr 再等退出，避免缓冲区写满导致互等
            var stderr = redirected ? await process.StandardError.ReadToEndAsync() : "";
            await process.WaitForExitAsync();

            if (process.ExitCode == 0) return StartupTaskResult.Ok();

            var reason = string.IsNullOrWhiteSpace(stderr)
                ? $"schtasks 退出码 {process.ExitCode}"
                : stderr.Trim();
            return StartupTaskResult.Fail(reason);
        }
        catch (Exception ex)
        {
            // runas 被取消 / 提权被拒 / schtasks 起不来
            return StartupTaskResult.Fail($"无法运行 schtasks：{ex.Message}");
        }
    }
}
