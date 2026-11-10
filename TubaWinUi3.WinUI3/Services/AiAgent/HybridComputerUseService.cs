using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>Stages the official Microsoft Hybrid Computer Use Windows bundle next to the private AI runtime.</summary>
internal static class HybridComputerUseService
{
    // This value matches third_party/HybridComputerUse/VERSION and the bundled official binaries.
    internal const string Version = "1.0.0";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static string ArchitectureDirectory => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.Arm64 => "win-arm64",
        _ => throw new PlatformNotSupportedException(AgentText.L("Architecture", "电脑控制需要 x64 或 ARM64 系统。"))
    };

    private static string SourceDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "HybridComputerUse", ArchitectureDirectory);
    internal static string Root => Path.Combine(AiComponentService.Root, "ComputerUse", Version);
    internal static string Executable => Path.Combine(Root, "computer-use.exe");
    private static string Provider => Path.Combine(Root, "computer-use-windows-provider.exe");

    internal static async Task EnsureInstalledAsync(CancellationToken ct)
    {
        var source = SourceDirectory;
        if (!File.Exists(Path.Combine(source, "computer-use.exe")) ||
            !File.Exists(Path.Combine(source, "computer-use-windows-provider.exe")))
            throw new FileNotFoundException(AgentText.L("ComputerUseMissing", "电脑控制组件缺失。请重新安装图吧工具箱。"));
        if (IsReady()) return;

        await Gate.WaitAsync(ct);
        try
        {
            if (IsReady()) return;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var destination = Path.Combine(Root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await CopyFileAsync(file, destination, ct);
            }
            await File.WriteAllTextAsync(Path.Combine(Root, "VERSION"), Version, ct);
        }
        finally { Gate.Release(); }
    }

    private static bool IsReady() => File.Exists(Executable) && File.Exists(Provider) &&
        string.Equals(ReadVersion(), Version, StringComparison.Ordinal);

    private static string ReadVersion()
    {
        try { return File.ReadAllText(Path.Combine(Root, "VERSION")).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        const int bufferSize = 1024 * 1024;
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, bufferSize, ct);
    }
}
