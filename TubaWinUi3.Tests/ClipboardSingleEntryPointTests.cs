using System.Runtime.CompilerServices;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 剪贴板写入「唯一入口」守卫（回归：硬件信息页点「运行时间」卡片闪退）。
///
/// 背景：<c>Clipboard.SetContent</c> 需要打开共享剪贴板，另一个进程正持有时抛
/// COMException（0x800401D0 / CLIPBRD_E_CANT_OPEN）。这个异常从 Tapped/Click
/// 处理器里逃出去就是未处理异常。全应用因此统一走 <c>ClipboardService</c>
/// （占用重试 + 异常收口 + 失败留痕）。
///
/// 规则本身没有任何编译器保障——迁移期间就漏过 4 处（--copy-path 后台复制、
/// 局域网分享链接、Wi-Fi 密码、GitHub 授权码），全都是"点了没反应"或闪退。
/// 这里用文本扫描把口子焊死：新增直连调用会立刻让测试变红。
/// </summary>
public class ClipboardSingleEntryPointTests
{
    private static readonly string AppRoot = Path.Combine(FindRepoRoot(), "TubaWinUi3.WinUI3");

    /// <summary>向上查找 TubaWinUi3.sln 定位仓库根（同 LocalizationTests，兼容 BaseOutputPath）。</summary>
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

    /// <summary>唯一允许直连 WinRT 剪贴板 API 的文件（重试与异常收口都在这里）。</summary>
    private static readonly string ClipboardServiceRelativePath =
        Path.Combine("Services", "ClipboardService.cs");

    /// <summary>会抛 COMException 的 WinRT 剪贴板写入 API。</summary>
    private static readonly string[] ForbiddenApis =
    [
        "Clipboard.SetContent",
        "Clipboard.SetBitmap",
        "Clipboard.Flush",
        "Clipboard.Clear",
    ];

    [Fact]
    public void OnlyClipboardServiceWritesToTheClipboard()
    {
        var offenders = new List<string>();

        foreach (var file in EnumerateSourceFiles())
        {
            var relative = Path.GetRelativePath(AppRoot, file);
            if (string.Equals(relative, ClipboardServiceRelativePath, StringComparison.OrdinalIgnoreCase))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // 只看代码行：注释里提到 API 名字是正常的（本测试的文档注释就提了）
                if (IsCommentLine(lines[i])) continue;

                foreach (var api in ForbiddenApis)
                {
                    if (lines[i].Contains(api, StringComparison.Ordinal))
                        offenders.Add($"{relative}:{i + 1} → {api}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "剪贴板写入必须统一走 ClipboardService（占用重试、异常不外抛、失败留痕）。" +
            "直连会闪退：\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// 扫描面本身也要有保障：路径找错会导致"零命中"的假通过。
    /// </summary>
    [Fact]
    public void ScanCoversTheWholeAppSource()
    {
        var files = EnumerateSourceFiles().ToList();

        Assert.True(files.Count > 100, $"扫描到的源文件只有 {files.Count} 个，疑似仓库根定位错误");

        var servicePath = Path.Combine(AppRoot, ClipboardServiceRelativePath);
        Assert.Contains(servicePath, files, StringComparer.OrdinalIgnoreCase);

        // 崩溃现场所在文件必须在扫描面内（本测试存在的理由）
        var hardwarePage = Path.Combine(AppRoot, "Pages", "HardwarePage.xaml.cs");
        Assert.Contains(hardwarePage, files, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCommentLine(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("/*", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal);
    }

    /// <summary>枚举应用源码（跳过 bin/obj 生成物）。</summary>
    private static IEnumerable<string> EnumerateSourceFiles()
    {
        var pending = new Stack<string>();
        pending.Push(AppRoot);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase))
                    continue;
                pending.Push(sub);
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
                yield return file;
        }
    }
}
