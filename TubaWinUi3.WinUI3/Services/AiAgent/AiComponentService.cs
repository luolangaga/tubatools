using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.AiAgent;

public static class AiComponentService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static string Root => Path.Combine(ConfigManager.GetDataDir(), "AiAssistant");
    public static string Executable => Path.Combine(Root, "Component", "opencode.exe");
    public static bool IsInstalled => File.Exists(Executable);
    internal static string DownloadUrl(Architecture architecture) => architecture switch
    {
        Architecture.Arm64 => "https://opencode.ai/update/api/latest/cli/opencode/opencode-windows-arm64.zip",
        Architecture.X64 => "https://opencode.ai/update/api/latest/cli/opencode/opencode-windows-x64.zip",
        _ => throw new PlatformNotSupportedException(AgentText.L("Architecture", "AI 组件需要 x64 或 ARM64 系统。"))
    };

    public static async Task InstallAsync(IProgress<string> progress, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        var temporary = Path.Combine(Root, "install-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (IsInstalled) return;
            Directory.CreateDirectory(temporary);
            progress.Report(AgentText.L("Installing", "正在安装AI组件"));
            using var http = ProxyService.CreateClient(TimeSpan.FromMinutes(10));
            using var response = await http.GetAsync(DownloadUrl(RuntimeInformation.OSArchitecture), System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var archive = Path.Combine(temporary, "component.zip");
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var destination = File.Create(archive))
            {
                var total = response.Content.Headers.ContentLength;
                if (total > 400L * 1024 * 1024) throw new InvalidDataException("AI component too large.");
                long downloaded = 0;
                var buffer = new byte[81920];
                int read;
                int lastPercent = -1;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    downloaded += read;
                    if (downloaded > 400L * 1024 * 1024) throw new InvalidDataException("AI component too large.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                    var percent = total > 0 ? (int)(downloaded * 100 / total.Value) : -1;
                    if (percent != lastPercent) { lastPercent = percent; progress.Report(AgentText.L("Installing", "正在安装AI组件") + $" · {percent}%"); }
                }
            }
            await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(archive);
                var entry = zip.Entries.Single(e => e.Name.Equals("opencode.exe", StringComparison.OrdinalIgnoreCase));
                if (entry.Length is < 1024 or > 700L * 1024 * 1024) throw new InvalidDataException("Invalid AI component.");
                entry.ExtractToFile(Path.Combine(temporary, "opencode.exe"));
            }, ct);
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(Executable)!);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "AiAgent", "COMPONENT-LICENSE.txt"),
                Path.Combine(Path.GetDirectoryName(Executable)!, "LICENSE.txt"), true);
            File.Move(Path.Combine(temporary, "opencode.exe"), Executable);
        }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
            finally { Gate.Release(); }
        }
    }

    internal static ProcessStartInfo StartInfo(string executable, string directory)
    {
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var item in new[] { ("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data"), ("XDG_STATE_HOME", "state"), ("XDG_CACHE_HOME", "cache") })
            info.Environment[item.Item1] = Path.Combine(Root, item.Item2);
        info.Environment["OPENCODE_DISABLE_AUTOUPDATE"] = "true";
        foreach (var provider in AgentProviderStore.Read()) info.Environment[provider.EnvironmentKey] = string.IsNullOrEmpty(provider.ApiKey) ? "local" : provider.ApiKey;
        foreach (var inherited in new[] { "OPENCODE_CONFIG", "OPENCODE_CONFIG_CONTENT", "OPENCODE_CONFIG_DIR", "OPENCODE_DB" })
            info.Environment.Remove(inherited);
        if (ProxyService.HasProxy && Uri.TryCreate(ProxyService.ProxyAddress, UriKind.Absolute, out var address))
        {
            var proxy = new UriBuilder(address);
            if (!string.IsNullOrWhiteSpace(ProxyService.ProxyUsername))
            {
                proxy.UserName = ProxyService.ProxyUsername;
                proxy.Password = ProxyService.ProxyPassword ?? "";
            }
            info.Environment["HTTPS_PROXY"] = info.Environment["HTTP_PROXY"] = proxy.Uri.AbsoluteUri;
            info.Environment["NO_PROXY"] = "localhost,127.0.0.1,::1";
        }
        return info;
    }
}
