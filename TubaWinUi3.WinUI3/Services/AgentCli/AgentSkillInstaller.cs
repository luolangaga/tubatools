using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.AgentCli;

internal static class AgentSkillInstaller
{
    internal static readonly string[] Agents = ["codex", "claude-code", "cursor", "opencode"];
    internal static string Export()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Assets", "AgentSkills", "tuba-toolbox");
        if (!File.Exists(Path.Combine(source, "SKILL.md"))) throw new FileNotFoundException("技能资源不完整，请重新安装工具箱。");
        var destination = Path.Combine(ConfigManager.GetDataDir(), "AgentSkills", "tuba-toolbox");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        File.WriteAllText(Path.Combine(destination, "toolbox.json"), JsonSerializer.Serialize(new { executable = Environment.ProcessPath }), new UTF8Encoding(false));
        var doc = Path.Combine(AppContext.BaseDirectory, "Metadata", "CLI工具使用文档.md");
        if (File.Exists(doc)) File.Copy(doc, Path.Combine(destination, "references", "cli-tools.md"), overwrite: true);
        return destination;
    }

    internal static string InstallCommand(string path, string agent)
    {
        if (!Agents.Contains(agent)) throw new ArgumentException("不支持的 Agent。");
        return $"npx --yes skills add '{path.Replace("'", "''")}' --global --agent {agent} --yes --copy";
    }

    internal static async Task<string> InstallAsync(string agent)
    {
        var path = Export();
        var command = InstallCommand(path, agent).Replace("npx --yes", "& npx.cmd --yes", StringComparison.Ordinal);
        var script = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false); $env:DISABLE_TELEMETRY='1'; "
            + "if (!(Get-Command node.exe -ErrorAction SilentlyContinue) -or !(Get-Command npx.cmd -ErrorAction SilentlyContinue)) { Write-Error '请先安装 Node.js 22.20 或更新版本，再重启工具箱。'; exit 1 }; "
            + "$nodeVersion=[version]((& node.exe --version).TrimStart('v')); if ($nodeVersion -lt [version]'22.20') { Write-Error 'Node.js 版本过旧，请升级到 22.20 或更新版本。'; exit 1 }; "
            + command + "; exit $LASTEXITCODE";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Skills 安装程序。");
        ChildProcessJob.Assign(process);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("安装超时。请检查网络，或复制安装命令到终端运行。");
        }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException((error + "\n" + output).Trim()[..Math.Min((error + "\n" + output).Trim().Length, 1800)]);
        return "Skills 已安装到 " + agent + "。请重新打开该 Agent 的会话，再让它使用图吧工具箱检查电脑。";
    }
}
