using System.Diagnostics;
using Microsoft.Win32;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class WingetService
{
    public static List<WingetPackage> GetCatalog()
    {
        return
        [
            new() { Id = "M2Team.NanaZip", Name = "NanaZip", Category = "压缩工具", Glyph = "\uE8D7", Description = "现代化的7-Zip解压缩工具" },
            new() { Id = "Microsoft.PowerToys", Name = "PowerToys", Category = "系统工具", Glyph = "\uE90F", Description = "微软官方系统增强工具集" },
            new() { Id = "OTP.OfficeToolPlus", Name = "Office Tool Plus", Category = "办公工具", Glyph = "\uE8BD", Description = "Office部署与管理工具" },
            new() { Id = "OBSProject.OBSStudio", Name = "OBS Studio", Category = "录屏直播", Glyph = "\uE960", Description = "开源录屏与直播软件" },
            new() { Id = "qBittorrent.qBittorrent", Name = "qBittorrent", Category = "下载工具", Glyph = "\uE896", Description = "开源BitTorrent客户端" },
            new() { Id = "GIMP.GIMP", Name = "GIMP", Category = "图像编辑", Glyph = "\uEB9F", Description = "开源图像编辑器" },
            new() { Id = "VideoLAN.VLC", Name = "VLC Media Player", Category = "媒体播放", Glyph = "\uE8B2", Description = "开源多媒体播放器" },
            new() { Id = "Notepad++.Notepad++", Name = "Notepad++", Category = "文本编辑", Glyph = "\uE8AC", Description = "免费源代码编辑器" },
            new() { Id = "Google.Chrome", Name = "Google Chrome", Category = "浏览器", Glyph = "\uE774", Description = "谷歌浏览器" },
            new() { Id = "Mozilla.Firefox", Name = "Firefox", Category = "浏览器", Glyph = "\uE774", Description = "火狐浏览器" },
            new() { Id = "7zip.7zip", Name = "7-Zip", Category = "压缩工具", Glyph = "\uE8D7", Description = "开源文件压缩工具" },
            new() { Id = "Microsoft.VisualStudioCode", Name = "VS Code", Category = "开发工具", Glyph = "\uE943", Description = "轻量级代码编辑器" },
            new() { Id = "Git.Git", Name = "Git", Category = "开发工具", Glyph = "\uE943", Description = "分布式版本控制系统" },
            new() { Id = "voidtools.Everything", Name = "Everything", Category = "系统工具", Glyph = "\uE721", Description = "极速文件搜索工具" },
            new() { Id = "AutoHotkey.AutoHotkey", Name = "AutoHotkey", Category = "系统工具", Glyph = "\uE92E", Description = "自动化脚本工具" },
            new() { Id = "Figma.Figma", Name = "Figma", Category = "设计工具", Glyph = "\uEB9F", Description = "在线UI设计工具" },
            new() { Id = "Audacity.Audacity", Name = "Audacity", Category = "音频编辑", Glyph = "\uEB9F", Description = "开源音频编辑器" },
            new() { Id = "KDE.Kdenlive", Name = "Kdenlive", Category = "视频编辑", Glyph = "\uE960", Description = "开源视频编辑器" },
            new() { Id = "RARLab.WinRAR", Name = "WinRAR", Category = "压缩工具", Glyph = "\uE8D7", Description = "经典压缩解压软件" },
            new() { Id = "PotPlayer.PotPlayer", Name = "PotPlayer", Category = "媒体播放", Glyph = "\uE8B2", Description = "高性能多媒体播放器" },
        ];
    }

    public static IReadOnlyList<string> GetCategories()
    {
        return GetCatalog()
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static async Task<bool> IsInstalledAsync(string packageId, CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"list --id {packageId} --accept-source-agreements",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return false;

            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            return output.Contains(packageId, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<WingetInstallResult> InstallAsync(string packageId, IProgress<WingetInstallProgress>? progress, CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"install --id {packageId} --accept-package-agreements --accept-source-agreements",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return new WingetInstallResult(false, "无法启动 winget 进程");
            }

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var line = await process.StandardOutput.ReadLineAsync(ct);
                if (line is null) break;

                progress?.Report(new WingetInstallProgress(packageId, line, ParseProgressFromLine(line)));
            }

            await process.WaitForExitAsync(ct);

            var success = process.ExitCode == 0;
            var message = success ? "安装成功" : $"安装失败 (退出代码: {process.ExitCode})";
            return new WingetInstallResult(success, message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WingetInstallResult(false, ex.Message);
        }
    }

    public static async Task<bool> IsWingetAvailableAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static string? FindInstalledExePath(string packageId)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var keyPath in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            })
            {
                using var parentKey = hive.OpenSubKey(keyPath);
                if (parentKey is null) continue;

                foreach (var subKeyName in parentKey.GetSubKeyNames())
                {
                    using var subKey = parentKey.OpenSubKey(subKeyName);
                    if (subKey is null) continue;

                    var displayName = subKey.GetValue("DisplayName") as string;
                    if (displayName is null || !displayName.Contains(packageId, StringComparison.OrdinalIgnoreCase)) continue;

                    var installLocation = subKey.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation)) continue;

                    var exe = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault(f => !Path.GetFileName(f).Equals("Uninstall.exe", StringComparison.OrdinalIgnoreCase)
                                          && !Path.GetFileName(f).Equals("unins000.exe", StringComparison.OrdinalIgnoreCase));
                    return exe;
                }
            }
        }
        return null;
    }

    private static int ParseProgressFromLine(string line)
    {
        if (line.Contains("已成功安装", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase))
            return 100;

        if (line.Contains("正在下载", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
            return 30;

        if (line.Contains("正在安装", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Installing", StringComparison.OrdinalIgnoreCase))
            return 70;

        if (line.Contains("正在验证", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Verifying", StringComparison.OrdinalIgnoreCase))
            return 85;

        if (line.Contains("启动", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Starting", StringComparison.OrdinalIgnoreCase))
            return 10;

        return 0;
    }
}

public sealed record WingetInstallResult(bool Success, string Message);
public sealed record WingetInstallProgress(string PackageId, string StatusLine, int Percent);
