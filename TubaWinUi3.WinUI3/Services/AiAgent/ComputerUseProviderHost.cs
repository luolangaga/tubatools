using System.Runtime.InteropServices;
using ComputerUse.Windows.Runtime;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// Hidden mode that hosts the official Hybrid Computer Use Windows provider inside
/// <c>TubaWinUi3.exe</c>. The Go MCP server (<c>computer-use.exe</c>) is pointed at this
/// executable through <see cref="ProviderPathVariable"/> plus <see cref="InvocationVariable"/>,
/// so the provider shares the app's self-contained runtime instead of shipping a second
/// ~155 MB single-file .NET bundle.
/// </summary>
internal static class ComputerUseProviderHost
{
    internal const string InvocationVariable = "TUBA_COMPUTER_PROVIDER_HOST";
    internal const string ProviderPathVariable = "COMPUTER_USE_WINDOWS_PROVIDER";

    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    private static readonly IntPtr PerMonitorV2 = new(-4);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    internal static bool IsInvocation() =>
        string.Equals(Environment.GetEnvironmentVariable(InvocationVariable), "1", StringComparison.Ordinal);

    internal static int Run()
    {
        // Window, capture and SendInput coordinates are physical pixels only when the
        // process is per-monitor DPI aware before any UI Automation call.
        SetProcessDpiAwarenessContext(PerMonitorV2);
        try
        {
            Console.InputEncoding = new System.Text.UTF8Encoding(false);
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        }
        catch (IOException) { }

        var exitCode = 0;
        var thread = new Thread(() =>
        {
            try
            {
                using var server = new ProviderServer();
                server.Run();
            }
            catch (Exception error)
            {
                try { Console.Error.WriteLine(error); } catch (IOException) { }
                exitCode = 1;
            }
        })
        {
            IsBackground = false,
            Name = "ComputerUse provider"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return exitCode;
    }
}
