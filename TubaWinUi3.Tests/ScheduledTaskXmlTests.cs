using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TubaWinUi3.Services;
using TubaWinUi3.Services.ActiveIntercept;

namespace TubaWinUi3.Tests;

/// <summary>
/// 开机自启计划任务的 XML 落盘编码。
///
/// 回归场景：用户开「后台节流省电神器 → 开机自启」，界面提示"创建计划任务失败 (UAC 可能被拒绝)"，
/// 但 UAC 开不开都一样失败。真因是编码：<c>schtasks /create /XML</c> 只认真正的 Unicode
/// （UTF-16）文件，而 XML 声明写着 <c>encoding="UTF-16"</c>、内容却按 UTF-8 落盘
/// （<c>File.WriteAllText</c> 不带编码参数的默认行为）——MSXML 解析到第一个非 ASCII 节点
/// 就报「任务 XML 包含意外节点 (5,3):Description」，中文 &lt;Description&gt; 恰好就是第一个。
/// schtasks 进程本身是正常退出的，调用方只看"任务没建出来"，于是把失败误记成 UAC 拒绝。
///
/// 本机实测（Windows 11 26200，非提权）：UTF-16+BOM → 「错误: 拒绝访问」（XML 已通过解析，
/// 走到权限检查）；UTF-8 → 「错误: 任务 XML 包含意外节点 (5,3):Description」。
/// </summary>
public class ScheduledTaskXmlTests
{
    private static readonly Regex DeclaredEncoding =
        new("encoding=\"(?<enc>[^\"]+)\"", RegexOptions.Compiled);

    public static TheoryData<string, string> TaskXmls => new()
    {
        {
            "EnergyStar",
            EnergyStarStartupService.BuildTaskXml(@"C:\App\TubaWinUi3.exe", @"PC\user")
        },
        {
            "ActiveIntercept",
            ActiveInterceptStartupService.BuildTaskXml(
                @"C:\App\TubaWinUi3.BackEnd.exe", "\"--config\" \"C:\\cfg.json\"", @"PC\user")
        },
    };

    [Theory]
    [MemberData(nameof(TaskXmls))]
    public async Task TaskXml_IsWrittenInTheEncodingItDeclares(string name, string xml)
    {
        var declared = DeclaredEncoding.Match(xml).Groups["enc"].Value;
        Assert.False(string.IsNullOrEmpty(declared), $"{name}: XML 声明里没有 encoding");

        var path = await ScheduledTaskHelper.WriteTaskXmlAsync(xml);
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);

            Assert.True(bytes.Length > 2, $"{name}: 写出来的文件是空的");

            // 声明的编码必须与真实字节一致 —— 声明 UTF-16 却写 UTF-8 正是本次线上故障
            var actual = bytes[0] == 0xFF && bytes[1] == 0xFE ? "UTF-16"
                       : bytes[0] == 0xEF && bytes[1] == 0xBB ? "UTF-8"
                       : "unknown";
            Assert.Equal(declared, actual);

            // 按声明编码读回来必须与原文逐字一致（中文描述不能变问号）
            Assert.Equal(xml, await File.ReadAllTextAsync(path, Encoding.Unicode));
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    [Theory]
    [MemberData(nameof(TaskXmls))]
    public void TaskXml_ContainsNonAsciiText_SoEncodingMismatchIsFatal(string name, string xml)
    {
        // 这个断言解释"为什么编码错了就必炸"：描述里只要有中文，
        // 编码不匹配时 MSXML 一定在第一个非 ASCII 节点停下。
        Assert.Contains(xml, c => c > 127);
    }

    [Fact]
    public void EnergyStarTaskXml_CarriesExePathAndSilentSwitch()
    {
        var xml = EnergyStarStartupService.BuildTaskXml(@"C:\App\TubaWinUi3.exe", @"PC\user");

        Assert.Contains(@"C:\App\TubaWinUi3.exe", xml, StringComparison.Ordinal);
        Assert.Contains(EnergyStarStartupService.SilentArg, xml, StringComparison.Ordinal);
        Assert.Contains(EnergyStarStartupService.ScheduleTaskName, xml, StringComparison.Ordinal);
        Assert.Contains(@"PC\user", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveInterceptTaskXml_CarriesBackendPathAndConfigArguments()
    {
        var xml = ActiveInterceptStartupService.BuildTaskXml(
            @"C:\App\TubaWinUi3.BackEnd.exe", "\"--config\" \"C:\\cfg.json\"", @"PC\user");

        Assert.Contains(@"C:\App\TubaWinUi3.BackEnd.exe", xml, StringComparison.Ordinal);
        Assert.Contains("--config", xml, StringComparison.Ordinal);
        Assert.Contains(ActiveInterceptStartupService.ScheduleTaskName, xml, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------
    // XML 转义：路径 / 用户名 / --config 路径里的 & < " ' 不能直出
    // ---------------------------------------------------------------------

    public static TheoryData<string> UnsafePaths => new()
    {
        @"C:\plain\app.exe",
        @"D:\Games & Tools\TubaWinUi3.exe",   // & 直出 → XML 非法
        @"C:\A<B>C\app.exe",                  // < > 直出 → XML 非法
        @"C:\quote""d\app.exe",
        @"C:\apos'd\app.exe",
        @"D:\中文 目录 & 空格\图吧工具箱.exe",
    };

    [Theory]
    [MemberData(nameof(UnsafePaths))]
    public void EnergyStarTaskXml_StaysWellFormedAndRoundTripsUnsafePath(string exePath)
    {
        var xml = EnergyStarStartupService.BuildTaskXml(exePath, @"PC & Co\user");

        // 转义漏掉任何一个，这里就会抛 XmlException
        var doc = XDocument.Parse(xml);
        var ns = doc.Root!.GetDefaultNamespace();

        Assert.Equal(exePath, doc.Descendants(ns + "Command").Single().Value);
        Assert.Equal(@"PC & Co\user", doc.Descendants(ns + "UserId").Single().Value);
    }

    [Theory]
    [MemberData(nameof(UnsafePaths))]
    public void ActiveInterceptTaskXml_StaysWellFormedAndRoundTripsUnsafePath(string exePath)
    {
        var arguments = $"\"--config\" \"{exePath}.json\"";

        var xml = ActiveInterceptStartupService.BuildTaskXml(exePath, arguments, @"PC & Co\user");

        var doc = XDocument.Parse(xml);
        var ns = doc.Root!.GetDefaultNamespace();

        Assert.Equal(exePath, doc.Descendants(ns + "Command").Single().Value);
        Assert.Equal(arguments, doc.Descendants(ns + "Arguments").Single().Value);
    }

    [Fact]
    public void TaskXml_EmitsEscapedEntity_NotTheRawCharacter()
    {
        // 直接盯住转义本身，避免"解析得通"靠的是解析器宽容
        var xml = EnergyStarStartupService.BuildTaskXml(@"D:\Games & Tools\TubaWinUi3.exe", @"PC\user");

        Assert.Contains(@"D:\Games &amp; Tools\TubaWinUi3.exe", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Games & Tools", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Escape_HandlesAllXmlSignificantCharacters()
    {
        Assert.Equal("&amp;", ScheduledTaskHelper.Escape("&"));
        Assert.Equal("&lt;", ScheduledTaskHelper.Escape("<"));
        Assert.Equal("&gt;", ScheduledTaskHelper.Escape(">"));
        Assert.Equal("&quot;", ScheduledTaskHelper.Escape("\""));
        Assert.Equal("&apos;", ScheduledTaskHelper.Escape("'"));
        Assert.Equal("", ScheduledTaskHelper.Escape(null));
        Assert.Equal(@"C:\plain\app.exe", ScheduledTaskHelper.Escape(@"C:\plain\app.exe"));
    }
}
