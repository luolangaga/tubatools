using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Tubatools.AiXamlCard;

public sealed record CardCompileResult(bool Success, Assembly? Assembly, Type? Controller, string Error)
{
    public static CardCompileResult Fail(string error) => new(false, null, null, error);
}

/// <summary>
/// 用 Roslyn 把模型写的 C# 代码后置编译成可卸载的程序集。
/// 两个硬约束：只引用白名单里的宿主程序集（保证与宿主共享同一份 WinUI 类型标识），
/// 且编译产物里必须存在一个继承 <see cref="CardControllerBase"/> 的具体类作为入口。
/// </summary>
public static class CardCompiler
{
    public const string EntryFullName = "Tubatools.AiXamlCard.Generated.CardController";
    public const int MaxSourceCharacters = 96 * 1024;

    /// <summary>模型不用自己写 using：这些命名空间默认已导入。</summary>
    private static readonly string[] ImplicitUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.Linq",
        "System.Threading",
        "System.Threading.Tasks",
        "Microsoft.UI.Xaml",
        "Microsoft.UI.Xaml.Controls",
        "Microsoft.UI.Xaml.Media",
        "Microsoft.UI.Xaml.Shapes",
        "Windows.UI",
        "Tubatools.AiXamlCard"
    ];

    /// <summary>
    /// 编译期可引用的程序集白名单。只给界面与 BCL 基础类型：没有 System.IO、没有网络、
    /// 没有 Diagnostics.Process，因此「卡片能做哪些事」是编译期就定死的。
    /// </summary>
    private static readonly HashSet<string> AllowedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Private.CoreLib", "System.Runtime", "System.Runtime.Extensions",
        "System.Collections", "System.Collections.Concurrent", "System.Linq",
        "System.ObjectModel", "System.ComponentModel", "System.ComponentModel.Primitives",
        "System.Threading", "System.Threading.Tasks", "System.Threading.Thread",
        "System.Text.RegularExpressions", "System.Text.Json", "System.Memory",
        "netstandard", "System.Console", "System.Private.Uri",
        // Microsoft.ui.xaml 是原生/混合实现（WinUI 的 XAML 运行时本体），不含托管元数据，
        // 不能作为 Roslyn 引用；托管表面在 Microsoft.WinUI 里。
        "Microsoft.WinUI", "Microsoft.Windows.SDK.NET", "WinRT.Runtime",
        "TubaWinUi3.PluginSdk", "Tubatools.AiXamlCard"
    };

    public static CardCompileResult Compile(string code, string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(code)) return CardCompileResult.Fail("代码后置是空的。");
        if (code.Length > MaxSourceCharacters) return CardCompileResult.Fail($"代码超过 {MaxSourceCharacters / 1024} KB 上限。");

        var source = WithImplicitUsings(code);
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        // 策略只审「模型自己写的部分」：隐式 using 由插件注入，不算越界。
        var userTree = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest));
        // 核心库本身就带 System.IO / Environment / Activator，程序集白名单管不住，
        // 因此能力边界必须落在语法层。
        if (CardCodePolicy.Check(userTree) is { } violation) return CardCompileResult.Fail(violation);
        var references = ResolveReferences();
        if (references.Count == 0) return CardCompileResult.Fail("找不到可引用的宿主程序集，请重启工具箱后重试。");

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, allowUnsafe: false));

        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        if (!emitted.Success) return CardCompileResult.Fail(Describe(emitted));

        stream.Position = 0;
        var context = new CardLoadContext(assemblyName);
        var assembly = context.LoadFromStream(stream);
        var controller = FindController(assembly);
        if (controller is null)
            return CardCompileResult.Fail("代码里需要一个继承 CardControllerBase 的具体类，例如 public sealed class CardController : CardControllerBase。");
        return new CardCompileResult(true, assembly, controller, "");
    }

    /// <summary>
    /// 入口类型不强制命名空间：模型写在全局命名空间或因模板多套了一层都接受，
    /// 只要它是继承基类的唯一具体类。多个候选时优先用约定的 <see cref="EntryFullName"/>。
    /// </summary>
    private static Type? FindController(System.Reflection.Assembly assembly)
    {
        var candidates = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(CardControllerBase).IsAssignableFrom(t))
            .ToList();
        return candidates.FirstOrDefault(t => t.FullName == EntryFullName) ?? candidates.FirstOrDefault();
    }

    /// <summary>
    /// 在片段前面补上 global using。模型若自己写了 using / namespace / class 也不会冲突：
    /// 重复的 global using 会自动去重，而命名空间与类原样保留。
    /// </summary>
    private static string WithImplicitUsings(string code)
    {
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in code.Split('\n'))
        {
            var text = line.Trim();
            if (text.StartsWith("using ", StringComparison.Ordinal) && text.EndsWith(';'))
                existing.Add(text[6..^1].Trim());
        }
        var header = string.Join("\n", ImplicitUsings.Where(u => !existing.Contains(u)).Select(u => $"global using {u};"));
        return header + "\n\n" + code.Trim() + "\n";
    }

    private static string Describe(Microsoft.CodeAnalysis.Emit.EmitResult result)
    {
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Take(8)
            .Select(d => $"{d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}")
            .ToList();
        return errors.Count == 0 ? "代码编译失败。" : string.Join("\n", errors);
    }

    /// <summary>
    /// 引用集合 ＝ 已加载的白名单程序集 ∪ 应用目录里同名却尚未加载的白名单程序集。
    /// 后者很关键：<c>Microsoft.Windows.SDK.NET</c>（TypedEventHandler 等 WinRT 投影类型）
    /// 在宿主里通常还没被触碰过，只靠 AppDomain 扫描会让卡片编译失败。
    /// </summary>
    internal static List<MetadataReference> ResolveReferences()
    {
        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            var name = assembly.GetName().Name;
            if (name is null || !AllowedAssemblies.Contains(name) || !seen.Add(name)) continue;
            if (assembly.Location.Length == 0) continue;
            // Microsoft.ui.xaml.dll 是原生/混合映像，Roslyn 读不了它的「元数据」。
            if (!IsManagedAssembly(assembly.Location)) continue;
            try { references.Add(MetadataReference.CreateFromFile(assembly.Location)); }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or ArgumentException) { }
        }

        // 补齐尚未加载的同名程序集（WinUI / WinRT 投影的类型常在这些文件里）。
        foreach (var directory in CandidateDirectories())
        {
            foreach (var name in AllowedAssemblies)
            {
                if (seen.Contains(name)) continue;
                var path = Path.Combine(directory, name + ".dll");
                if (!File.Exists(path)) continue;
                try
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                    seen.Add(name);
                }
                // Microsoft.ui.xaml.dll 之类是原生/混合映像，Roslyn 读不了；
                // 这类失败必须整体忽略，否则会连累同一批次里的合法引用。
                catch (Exception ex) when (ex is IOException or BadImageFormatException or ArgumentException or InvalidOperationException) { }
            }
        }

        // 本插件自己的程序集必须可引用，否则无法继承基类与使用调色板。
        var self = typeof(CardControllerBase).Assembly;
        if (seen.Add(self.GetName().Name!) && self.Location.Length > 0)
            references.Add(MetadataReference.CreateFromFile(self.Location));
        return references;
    }

    /// <summary>插件所在目录与宿主程序目录——两者都可能放着 WinUI/WinRT 程序集。</summary>
    private static IEnumerable<string> CandidateDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { Path.GetDirectoryName(typeof(CardControllerBase).Assembly.Location), AppContext.BaseDirectory })
        {
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && seen.Add(dir)) yield return dir;
        }
    }

    /// <summary>粗略判断是否为含托管元数据的 PE 映像（原生 WinUI 组件会返回 false）。</summary>
    private static bool IsManagedAssembly(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
            return reader.HasMetadata;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>可卸载上下文：卡片关闭后不会把用户代码永久留在进程里。</summary>
    private sealed class CardLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        // 一律回落到默认上下文：编译期已按白名单绑定，运行期共享宿主类型。
        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }
}
