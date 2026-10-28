using System.Diagnostics;
using System.Text;

namespace TubaWinUi3.Services;

/// <summary>
/// 「连接手机」需要的 Windows 防火墙放行：HttpListener 走 HTTP.sys，真正要放行的是端口
/// （TCP = 手机连进来的 HTTP 端口，UDP = 局域网发现 18764）。首次开启服务时自动创建，
/// 失败只记日志不打断服务（用户仍可手动放行）。
/// </summary>
internal static class PhoneLinkFirewall
{
    internal const int DiscoveryPort = 18764;

    private static int _ensuredPort;

    /// <summary>后台确保规则存在（同一端口本进程只跑一次 PowerShell）。</summary>
    public static void EnsureInBackground(int httpPort)
    {
        if (Interlocked.Exchange(ref _ensuredPort, httpPort) == httpPort) return;
        _ = Task.Run(() => EnsureAsync(httpPort));
    }

    internal static async Task<bool> EnsureAsync(int httpPort, CancellationToken ct = default)
    {
        var tcpName = $"图吧工具箱·连接手机 (TCP {httpPort})";
        var udpName = $"图吧工具箱·连接手机 (UDP {DiscoveryPort})";
        var script = $$"""
$tcp = '{{tcpName}}'
$udp = '{{udpName}}'
$created = @()
if (-not (Get-NetFirewallRule -DisplayName $tcp -ErrorAction SilentlyContinue)) {
  New-NetFirewallRule -DisplayName $tcp -Direction Inbound -Action Allow -Protocol TCP -LocalPort {{httpPort}} -Profile Any -ErrorAction Stop | Out-Null
  $created += 'tcp'
}
if (-not (Get-NetFirewallRule -DisplayName $udp -ErrorAction SilentlyContinue)) {
  New-NetFirewallRule -DisplayName $udp -Direction Inbound -Action Allow -Protocol UDP -LocalPort {{DiscoveryPort}} -Profile Any -ErrorAction Stop | Out-Null
  $created += 'udp'
}
if ($created.Count -eq 0) { 'exists' } else { 'created ' + ($created -join ',') }
""";

        var (ok, output) = await RunPowerShellAsync(script, TimeSpan.FromSeconds(45), ct).ConfigureAwait(false);
        if (ok)
        {
            LogService.Info(LogService.PhoneLink,
                output.Contains("created", StringComparison.Ordinal)
                    ? $"已自动放行防火墙：{output.Trim()}"
                    : "防火墙规则已存在");
        }
        else
        {
            LogService.Warn(LogService.PhoneLink, $"自动放行防火墙失败（可手动放行）：{output.Trim()}");
        }
        return ok;
    }

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
