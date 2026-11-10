using System.Text.RegularExpressions;

namespace Tubatools.Windows11Optimizer;

internal static partial class AppManager
{
    [GeneratedRegex(@"^[A-Za-z0-9._\-]+$")]
    private static partial Regex SafePackageName();

    [GeneratedRegex(@"^[A-Za-z0-9._+\-]+$")]
    private static partial Regex SafeWingetId();

    public static async Task<HashSet<string>> GetInstalledAppxAsync(CancellationToken ct)
    {
        var result = await ShellRunner.RunPowerShellAsync(
            "Get-AppxPackage | ForEach-Object { $_.Name } | Sort-Object -Unique",
            TimeSpan.FromMinutes(2), ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(result.Tail(400) is { Length: > 0 } tail ? tail : "PowerShell 无法读取已安装应用列表。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            names.Add(line.Trim());
        return names;
    }

    public static async Task<string> RemoveAppxAsync(
        IReadOnlyList<AppxEntry> apps,
        bool removeProvisioned,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var names = apps.Select(a => a.PackageName).Where(n => SafePackageName().IsMatch(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) return "没有可卸载的应用。";

        var list = string.Join(",", names.Select(n => "'" + n + "'"));
        var script = $$"""
            $names = @({{list}})
            foreach ($n in $names) {
              $pkgs = @(Get-AppxPackage -Name $n -AllUsers -ErrorAction SilentlyContinue)
              if ($pkgs.Count -eq 0) { $pkgs = @(Get-AppxPackage -Name $n -ErrorAction SilentlyContinue) }
              if ($pkgs.Count -eq 0) { Write-Output "SKIP|$n|未安装"; continue }
              foreach ($p in $pkgs) {
                Write-Output "PROGRESS|卸载 $($p.Name)"
                try {
                  Remove-AppxPackage -Package $p.PackageFullName -AllUsers -ErrorAction Stop
                  Write-Output "OK|$($p.Name)|已从所有用户卸载"
                } catch {
                  try {
                    Remove-AppxPackage -Package $p.PackageFullName -ErrorAction Stop
                    Write-Output "OK|$($p.Name)|已从当前用户卸载"
                  } catch {
                    Write-Output "FAIL|$($p.Name)|$($_.Exception.Message)"
                  }
                }
              }
            }
            if ({{(removeProvisioned ? "$true" : "$false")}}) {
              Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.DisplayName } | ForEach-Object {
                Write-Output "PROGRESS|移除预置副本 $($_.DisplayName)"
                try {
                  Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction Stop
                  Write-Output "PROV|OK|$($_.DisplayName)"
                } catch {
                  Write-Output "PROV|FAIL|$($_.Exception.Message)"
                }
              }
            }
            Write-Output "DONE"
            """;

        progress?.Report("正在调用 PowerShell 卸载预装应用…");
        var result = await ShellRunner.RunPowerShellAsync(script, TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
        return Summarize(result);
    }

    public static async Task<bool> HasWingetAsync(CancellationToken ct)
    {
        try
        {
            var result = await ShellRunner.RunAsync("winget.exe", "--version", TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<string> InstallWingetAsync(WingetApp app, IProgress<string>? progress, CancellationToken ct)
    {
        if (!SafeWingetId().IsMatch(app.Id)) throw new InvalidOperationException("无效的 winget 包 ID。");
        progress?.Report($"正在安装 {app.Title}…");
        ProcessResult result;
        try
        {
            result = await ShellRunner.RunAsync(
                "winget.exe",
                $"install --id {app.Id} -e --source winget --accept-package-agreements --accept-source-agreements --disable-interactivity",
                TimeSpan.FromMinutes(30), ct).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("未找到 winget。请先在 Microsoft Store 安装「应用安装程序」。");
        }

        if (result.ExitCode == 0) return $"{app.Title} 安装完成。";
        var tail = result.Tail(600);
        return $"{app.Title} 安装未完成（退出码 {result.ExitCode}）。{(string.IsNullOrWhiteSpace(tail) ? "" : "\n" + tail)}";
    }

    private static string Summarize(ProcessResult result)
    {
        var ok = 0;
        var failed = 0;
        var skipped = 0;
        var provisioned = 0;
        var details = new List<string>();
        foreach (var raw in result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            var parts = line.Split('|');
            if (parts.Length < 3) continue;
            switch (parts[0])
            {
                case "OK":
                    ok++;
                    details.Add($"✓ {parts[1]}：{parts[2]}");
                    break;
                case "FAIL":
                    failed++;
                    details.Add($"✗ {parts[1]}：{parts[2]}");
                    break;
                case "SKIP":
                    skipped++;
                    break;
                case "PROV" when parts[1] == "OK":
                    provisioned++;
                    break;
                case "PROV" when parts[1] == "FAIL":
                    failed++;
                    details.Add($"✗ 预置 {parts[2]}");
                    break;
            }
        }

        if (ok == 0 && failed == 0) return "没有应用被修改：" + (result.Tail(400) is { Length: > 0 } tail ? tail : "可能全部已卸载或名称未匹配。");
        var summary = $"完成：卸载 {ok} 个，跳过 {skipped} 个，失败 {failed} 个";
        if (provisioned > 0) summary += $"，移除预置副本 {provisioned} 个";
        summary += "。";
        if (details.Count > 0) summary += "\n" + string.Join('\n', details.Take(12));
        return summary;
    }
}
