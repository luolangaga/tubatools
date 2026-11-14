using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tubatools.AiXamlCard;

/// <summary>
/// 编译前的语法级能力检查。
///
/// 为什么需要这一层：.NET 10 的 <c>System.Private.CoreLib</c> 里就包含 <c>System.IO.File</c>、
/// <c>Environment</c>、<c>Activator</c> 等类型，只要引用了核心库，程序集白名单就管不住它们。
/// 所以真正的边界只能落在源码这一层：拒绝越界命名空间与危险成员。
///
/// 这**不是安全沙箱**——同进程代码终究有办法绕过名字检查（例如反射拼接字符串）。
/// 它是一道「别让模型顺手写出删文件/开进程的代码」的护栏，配合插件只有用户显式安装才存在，
/// 与「每条聊天消息静默执行代码」是两件事。
/// </summary>
public static partial class CardCodePolicy
{
    /// <summary>整段禁止的命名空间前缀。</summary>
    private static readonly string[] ForbiddenNamespaces =
    [
        "System.IO",
        "System.Net",
        "System.Diagnostics",
        "System.Reflection",
        "System.Runtime.InteropServices",
        "System.Security",
        "System.Management",
        "System.ServiceProcess",
        "Microsoft.Win32",
        "Windows.Storage",
        "Windows.System",
        "Windows.ApplicationModel",
        "Windows.Security",
        "Windows.Management",
        "Windows.Media",
        "Windows.Devices",
        "Windows.UI.Notifications",
        "Windows.Foundation.Diagnostics"
    ];

    /// <summary>禁止直接出现的类型或成员名（防住核心库里的高危入口）。</summary>
    private static readonly string[] ForbiddenIdentifiers =
    [
        "Environment", "AppDomain", "Activator", "Assembly", "Process", "ProcessStartInfo",
        "File", "Directory", "FileInfo", "DirectoryInfo", "FileStream",
        "StreamReader", "StreamWriter", "BinaryReader", "BinaryWriter",
        "HttpClient", "WebClient", "Socket", "TcpClient", "UdpClient",
        "Registry", "RegistryKey", "Marshal", "DllImport"
    ];

    public static string? Check(SyntaxTree tree)
    {
        var root = tree.GetRoot();

        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var name = usingDirective.Name?.ToString() ?? "";
            if (IsForbiddenNamespace(name)) return $"不能引用命名空间 {name}：卡片只做界面与纯计算。";
        }

        foreach (var name in root.DescendantNodes().OfType<NameSyntax>())
        {
            var text = name.ToString();
            if (IsForbiddenNamespace(text)) return $"不能使用 {text}：卡片只做界面与纯计算。";
        }

        foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var text = identifier.Identifier.ValueText;
            if (FindForbiddenIdentifier(text) is { } hit) return $"不能使用 {hit}：卡片只做界面与纯计算。";
        }

        return null;
    }

    private static bool IsForbiddenNamespace(string text) =>
        ForbiddenNamespaces.Any(ns => text == ns || text.StartsWith(ns + ".", StringComparison.Ordinal));

    private static string? FindForbiddenIdentifier(string text) =>
        ForbiddenIdentifiers.FirstOrDefault(name => string.Equals(name, text, StringComparison.Ordinal));
}
