using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TubaWinUi3.Services;

public sealed class DefenderTool : IBuiltinTool
{
    public string Id => "defender-control";
    public string Name => "Defender 控制";
    public string Description => "使用 dControl 一键关闭/开启 Windows Defender 实时保护。";
    public string Glyph => "\uE72E";
    public string Category => "安全工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var exePath = FindDControl();
        if (exePath is null)
        {
            if (context.ShowDialog is not null)
            {
                context.ShowDialog("未找到工具", "未找到 dControl.exe，请确认 remotedefender 文件夹完整。");
            }
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (Exception ex)
        {
            if (context.ShowDialog is not null)
            {
                context.ShowDialog("启动失败", $"无法启动 dControl：{ex.Message}");
            }
        }

        await Task.CompletedTask;
    }

    private static string? FindDControl()
    {
        var appDir = ToolCatalog.AppDirectory;
        var candidates = new[]
        {
            Path.Combine(appDir, "remotedefender", "dControl.exe"),
            Path.Combine(appDir, "..", "remotedefender", "dControl.exe"),
        };

        foreach (var p in candidates)
        {
            var full = Path.GetFullPath(p);
            if (File.Exists(full)) return full;
        }

        var dir = new DirectoryInfo(appDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "remotedefender", "dControl.exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
