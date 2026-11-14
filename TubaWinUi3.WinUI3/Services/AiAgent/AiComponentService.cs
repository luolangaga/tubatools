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

    private const long MaxPackageBytes = 400L * 1024 * 1024;

    /// <summary>
    /// 首选来源 = 自建 GitCode 镜像（tubatoolr release 12 的 opencode.zip，包内只有一个 x64 的 opencode.exe，
    /// Windows 11 ARM64 靠 x64 模拟运行）。镜像不可用（网络/404/包损坏）时自动回退到下面的官方更新接口。
    /// </summary>
    internal const string MirrorUrl = "https://gitcode.com/luolangaga/tubatoolr/releases/download/12/opencode.zip";

    /// <summary>官方更新接口（兜底来源，按架构取包）。</summary>
    internal static string OfficialUrl(Architecture architecture) => architecture switch
    {
        Architecture.Arm64 => "https://opencode.ai/update/api/latest/cli/opencode/opencode-windows-arm64.zip",
        Architecture.X64 => "https://opencode.ai/update/api/latest/cli/opencode/opencode-windows-x64.zip",
        _ => throw new PlatformNotSupportedException(AgentText.L("Architecture", "AI 组件需要 x64 或 ARM64 系统。"))
    };

    /// <summary>按顺序尝试的来源：自建镜像 → 官方更新接口。</summary>
    internal static IReadOnlyList<string> DownloadSources(Architecture architecture) => [MirrorUrl, OfficialUrl(architecture)];

    public static async Task InstallAsync(IProgress<string> progress, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (IsInstalled) return;
            Exception? firstFailure = null;
            var sources = DownloadSources(RuntimeInformation.OSArchitecture);
            for (var index = 0; index < sources.Count; index++)
            {
                var temporary = Path.Combine(Root, "install-" + Guid.NewGuid().ToString("N"));
                var label = index == 0
                    ? AgentText.L("Installing", "正在安装AI组件")
                    : AgentText.L("InstallingFallback", "镜像不可用，正在从官方源下载AI组件");
                try
                {
                    Directory.CreateDirectory(temporary);
                    progress.Report(label);
                    await InstallFromAsync(sources[index], temporary, label, progress, ct);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    firstFailure ??= ex;
                    LogService.Warn(LogService.App, $"AI component download failed: {sources[index]} · {ex.Message}");
                }
                finally
                {
                    try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            throw firstFailure ?? new InvalidDataException("AI component unavailable.");
        }
        finally { Gate.Release(); }
    }

    private static async Task InstallFromAsync(string url, string temporary, string label, IProgress<string> progress, CancellationToken ct)
    {
        using var http = ProxyService.CreateClient(TimeSpan.FromMinutes(10));
        using var response = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        if (total > MaxPackageBytes) throw new InvalidDataException("AI component too large.");
        var archive = Path.Combine(temporary, "component.zip");
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var destination = File.Create(archive))
        {
            long downloaded = 0;
            var buffer = new byte[81920];
            int read;
            int lastPercent = -1;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                downloaded += read;
                if (downloaded > MaxPackageBytes) throw new InvalidDataException("AI component too large.");
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
                var percent = total > 0 ? (int)(downloaded * 100 / total.Value) : -1;
                if (percent != lastPercent) { lastPercent = percent; progress.Report(label + $" · {percent}%"); }
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
        var componentDirectory = Path.GetDirectoryName(Executable)!;
        Directory.CreateDirectory(componentDirectory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "AiAgent", "COMPONENT-LICENSE.txt"),
            Path.Combine(componentDirectory, "LICENSE.txt"), true);
        File.Move(Path.Combine(temporary, "opencode.exe"), Executable);
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
