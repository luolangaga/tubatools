using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// Stages the official Microsoft Hybrid Computer Use Go MCP server next to the private AI
/// runtime. The heavy Windows provider is not shipped: it runs as a hidden mode of
/// <c>TubaWinUi3.exe</c> (see <see cref="ComputerUseProviderHost"/>), sharing the app runtime.
/// </summary>
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

    /// <summary>Older builds staged a ~155 MB self-contained provider; reclaim it.</summary>
    private static string LegacyProvider => Path.Combine(Root, "computer-use-windows-provider.exe");

    internal static async Task EnsureInstalledAsync(CancellationToken ct)
    {
        var source = Path.Combine(SourceDirectory, "computer-use.exe");
        if (!File.Exists(source))
            throw new FileNotFoundException(AgentText.L("ComputerUseMissing", "电脑控制组件缺失。请重新安装图吧工具箱。"));
        if (IsReady()) { RemoveLegacyProvider(); return; }

        await Gate.WaitAsync(ct);
        try
        {
            if (IsReady()) { RemoveLegacyProvider(); return; }
            Directory.CreateDirectory(Root);
            await CopyFileAsync(source, Executable, ct);
            await File.WriteAllTextAsync(Path.Combine(Root, "VERSION"), Version, ct);
            RemoveLegacyProvider();
        }
        finally { Gate.Release(); }
    }

    private static bool IsReady() => File.Exists(Executable) &&
        string.Equals(ReadVersion(), Version, StringComparison.Ordinal);

    private static string ReadVersion()
    {
        try { return File.ReadAllText(Path.Combine(Root, "VERSION")).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    private static void RemoveLegacyProvider()
    {
        try { File.Delete(LegacyProvider); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        const int bufferSize = 1024 * 1024;
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, bufferSize, ct);
    }
}
