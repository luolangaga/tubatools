using System.Reflection;
using System.Text;
using Spectre.Console;

namespace TubaWinUi3.Tui;

internal static class Program
{
    private static readonly string Version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        if (args is ["--version"])
        {
            Console.WriteLine(Version);
            return 0;
        }

        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine("图吧工具箱 TUI 兼容版");
            Console.WriteLine("直接运行进入双栏界面。方向键浏览，Tab 切换栏，Enter 启动工具，/ 搜索，Q 退出。");
            return 0;
        }

        if (args.Length > 0 || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("请在交互式终端中直接运行，或使用 --help 查看用法。");
            return 1;
        }

        AnsiConsole.AlternateScreen(() => new ToolWorkspace(Version).Run());
        return 0;
    }
}
