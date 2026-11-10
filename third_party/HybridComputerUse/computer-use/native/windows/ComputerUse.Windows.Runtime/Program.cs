using System.Windows.Forms;

namespace ComputerUse.Windows.Runtime;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Per-monitor DPI awareness makes window, capture and SendInput
        // coordinates physical pixels. The ApplicationHighDpiMode project
        // property only takes effect through ApplicationConfiguration, and a
        // manifest dpiAwareness entry is rejected by the WinForms analyzer.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        try
        {
            Console.InputEncoding = new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false);
            Console.OutputEncoding = new System.Text.UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false);
            using var server = new ProviderServer();
            server.Run();
            return Environment.ExitCode;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
