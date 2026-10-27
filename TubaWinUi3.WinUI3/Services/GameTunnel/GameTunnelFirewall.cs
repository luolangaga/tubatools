using System.Diagnostics;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>联机相关操作的结果（防火墙等）。</summary>
public sealed record TunnelActionResult(bool Ok, string Message);

/// <summary>
/// 联机组件需要的 Windows 防火墙规则。
/// 只为 <c>easytier-core.exe</c> 放行入站（程序级），不对游戏端口或网卡做任何放行。
/// </summary>
public static class GameTunnelFirewall
{
    public const string RuleName = "图吧工具箱·游戏联机组件";

    /// <summary>老版本（Tailscale 时期）按端口创建、带空格后缀的规则。</summary>
    private const string LegacyRulePattern = "图吧工具箱·游戏联机 *";

    public static async Task<TunnelActionResult> EnsureComponentRuleAsync(
        string exePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return new TunnelActionResult(false, "联机组件还没下载好，无法放行防火墙");

        var script = $$"""
$n = '{{Escape(RuleName)}}'
$exe = '{{Escape(exePath)}}'
if (-not (Get-NetFirewallRule -DisplayName $n -ErrorAction SilentlyContinue)) {
  New-NetFirewallRule -DisplayName $n -Direction Inbound -Action Allow -Program $exe -Profile Any -ErrorAction Stop | Out-Null
  'created'
} else {
  'exists'
}
""";

        var (ok, output) = await RunPowerShellAsync(script, TimeSpan.FromSeconds(45), ct).ConfigureAwait(false);
        if (!ok) return new TunnelActionResult(false, Explain(output));

        return new TunnelActionResult(true,
            output.Contains("created", StringComparison.Ordinal) ? "已放行联机组件" : "防火墙规则已存在");
    }

    private static bool _legacyCleaned;

    /// <summary>本进程内只清理一次老规则；之后每次建房/加入不再跑 PowerShell。</summary>
    public static async Task CleanLegacyOnceAsync(CancellationToken ct = default)
    {
        if (_legacyCleaned) return;
        _legacyCleaned = true;
        await RemoveLegacyRulesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>清理 Tailscale 时期留下的按端口规则（升级后不残留）。</summary>
    public static async Task RemoveLegacyRulesAsync(CancellationToken ct = default)
    {
        var script = $"Remove-NetFirewallRule -DisplayName '{Escape(LegacyRulePattern)}' -ErrorAction SilentlyContinue; 'done'";
        await RunPowerShellAsync(script, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
    }

    public static async Task<string> ListRulesAsync(CancellationToken ct = default)
    {
        var script = $"Get-NetFirewallRule -DisplayName '图吧工具箱·游戏联机*' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty DisplayName";
        var (_, output) = await RunPowerShellAsync(script, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        return output;
    }

    public static async Task<TunnelActionResult> RemoveAllAsync(CancellationToken ct = default)
    {
        const string script = "Remove-NetFirewallRule -DisplayName '图吧工具箱·游戏联机*' -ErrorAction SilentlyContinue; 'done'";
        var (ok, output) = await RunPowerShellAsync(script, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        return new TunnelActionResult(ok, ok ? "已清理本工具添加的防火墙规则" : output);
    }

    private static string Explain(string output)
    {
        if (output.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
        {
            return "没有权限修改防火墙，请以管理员身份运行";
        }
        if (output.Contains("No MSFT_NetFirewallRule", StringComparison.OrdinalIgnoreCase))
        {
            return "系统不支持按程序放行防火墙，请手动在 Windows 防火墙里放行 easytier-core.exe";
        }
        return "防火墙放行失败（不影响已有规则）";
    }

    private static string Escape(string value) => value.Replace("'", "''");

    private static async Task<(bool Ok, string Output)> RunPowerShellAsync(
        string script, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script })
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return (false, "无法启动 PowerShell");

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                return (false, "操作超时");
            }

            var outText = await stdout.ConfigureAwait(false);
            var errText = await stderr.ConfigureAwait(false);
            var ok = process.ExitCode == 0;
            return (ok, ok ? outText : string.IsNullOrWhiteSpace(errText) ? outText : errText);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
