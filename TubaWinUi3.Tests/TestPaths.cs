using System.Runtime.CompilerServices;

namespace TubaWinUi3.Tests;

/// <summary>测试用的仓库路径定位（从编译期嵌入的源码路径或输出目录向上找 TubaWinUi3.sln）。</summary>
internal static class TestPaths
{
    internal static string RepoRoot { get; } = FindRepoRoot();

    internal static string AppRoot { get; } = Path.Combine(RepoRoot, "TubaWinUi3.WinUI3");

    private static string FindRepoRoot([CallerFilePath] string callerFilePath = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(callerFilePath), AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(start)) continue;

            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "TubaWinUi3.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new InvalidOperationException("未找到仓库根目录（TubaWinUi3.sln）");
    }
}
