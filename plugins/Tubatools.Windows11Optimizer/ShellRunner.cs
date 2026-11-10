using System.Diagnostics;
using System.Text;

namespace Tubatools.Windows11Optimizer;

internal sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public string Combined => string.IsNullOrWhiteSpace(Error) ? Output : Output + Environment.NewLine + Error;
    public string Tail(int max = 1200) => Combined.Length <= max ? Combined.Trim() : Combined[^max..].Trim();
}

internal static class ShellRunner
{
    public static Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken ct,
        bool utf8Output = false)
        => RunCoreAsync(fileName, arguments, timeout, ct, utf8Output);

    /// <summary>Runs a PowerShell script through -EncodedCommand so Chinese paths never depend on the ANSI code page.</summary>
    public static Task<ProcessResult> RunPowerShellAsync(string script, TimeSpan timeout, CancellationToken ct)
    {
        const string prelude = "$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.Encoding]::UTF8;$OutputEncoding=[Text.Encoding]::UTF8;";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(prelude + script));
        return RunCoreAsync("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
            timeout, ct, utf8Output: true);
    }

    public static Task<ProcessResult> RunPowerCfgAsync(string arguments, CancellationToken ct)
        => RunAsync("powercfg.exe", arguments, TimeSpan.FromSeconds(30), ct);

    public static void StartDetached(string fileName, string? arguments = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(fileName, arguments ?? "")
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
            })?.Dispose();
        }
        catch
        {
            // Launching a system app is best effort; callers report their own status.
        }
    }

    private static async Task<ProcessResult> RunCoreAsync(
        string fileName, string arguments, TimeSpan timeout, CancellationToken ct, bool utf8Output)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.SystemDirectory
        };
        if (utf8Output)
        {
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException($"无法启动 {fileName}。");

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var stdout = process.StandardOutput.ReadToEndAsync(linked.Token);
        var stderr = process.StandardError.ReadToEndAsync(linked.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            throw new TimeoutException($"{Path.GetFileName(fileName)} 执行超过 {timeout.TotalSeconds:0} 秒，已终止。");
        }

        var output = await SafeResultAsync(stdout).ConfigureAwait(false);
        var error = await SafeResultAsync(stderr).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static async Task<string> SafeResultAsync(Task<string> task)
    {
        try { return await task.ConfigureAwait(false); }
        catch { return ""; }
    }
}
