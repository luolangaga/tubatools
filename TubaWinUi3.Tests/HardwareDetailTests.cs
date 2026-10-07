using System.Text.RegularExpressions;
using TubaWinUi3.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 硬件详情页重构的回归：自适应卡片布局数学、Windows 系统信息映射、分区图标资源与本地化键。
/// </summary>
public class HardwareDetailTests
{
    #region 自适应卡片布局

    [Theory]
    [InlineData(1600, 4)]
    [InlineData(1200, 3)]
    [InlineData(1000, 2)]
    [InlineData(700, 2)]
    [InlineData(340, 1)]
    [InlineData(200, 1)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void ResolveColumnCount_FollowsAvailableWidth(double width, int expected)
    {
        Assert.Equal(expected, CardLayoutMath.ResolveColumnCount(width, 336, 14));
    }

    [Fact]
    public void ResolveColumnCount_UndefinedWidth_FallsBackToOneColumn()
    {
        Assert.Equal(1, CardLayoutMath.ResolveColumnCount(double.NaN, 336, 14));
        Assert.Equal(1, CardLayoutMath.ResolveColumnCount(double.PositiveInfinity, 336, 14));
    }

    [Fact]
    public void ResolveColumnCount_RespectsHardMinimum()
    {
        // 列宽下限被硬性夹到 120：传入 10 也不会算出上千列
        Assert.Equal(10, CardLayoutMath.ResolveColumnCount(1200, 10, 0));
    }

    private const double ColumnWidth = 320;
    private const double ColumnGap = 16;

    private static List<CardPlacement> Arrange(params BoardItem[] items) =>
        CardLayoutMath.Arrange(items, ColumnWidth, 3, ColumnGap, ColumnGap);

    [Fact]
    public void Arrange_StacksCardsInTheirOwnColumn()
    {
        // 三张卡片各自在自己列里从顶向下堆叠
        var placements = Arrange(
            new BoardItem(0, 1, 100),
            new BoardItem(1, 1, 150),
            new BoardItem(2, 1, 80));

        Assert.Equal([0d, 0d, 0d], placements.Select(p => p.Rect.Y).ToArray());
        Assert.Equal([0, 1, 2], placements.Select(p => p.Column).ToArray());
        Assert.Equal(0, placements[0].Rect.X);
        Assert.Equal(ColumnWidth + ColumnGap, placements[1].Rect.X);
    }

    [Fact]
    public void Arrange_KeepsColumnEvenWhenAnotherColumnIsShorter()
    {
        // 关键行为：卡片被指定在某列就**不会**因为别处更矮而搬家（这是不闪动的根源）
        var placements = Arrange(
            new BoardItem(1, 1, 400),   // 第一张放在第 1 列
            new BoardItem(-1, 1, 50));  // 第二张自动分列 → 第 0 列（第 1 列已有 400 高）

        Assert.Equal(1, placements[0].Column);
        Assert.Equal(0, placements[1].Column);
    }

    [Fact]
    public void Arrange_AutoColumnPicksShortestCoveredColumn()
    {
        var placements = Arrange(
            new BoardItem(0, 1, 300),
            new BoardItem(1, 1, 100),
            new BoardItem(2, 1, 200),
            new BoardItem(-1, 1, 50));

        Assert.Equal(1, placements[^1].Column);
        Assert.Equal(100 + ColumnGap, placements[^1].Rect.Y);
    }

    [Fact]
    public void Arrange_SpannedCardPushesEveryCoveredColumn()
    {
        var placements = Arrange(
            new BoardItem(0, 2, 100),
            new BoardItem(0, 1, 40),
            new BoardItem(1, 1, 40),
            new BoardItem(2, 1, 40));

        // 跨列卡片占满 0/1 列，下面的卡片都被推到它下方
        Assert.Equal(ColumnWidth * 2 + ColumnGap, placements[0].Rect.Width);
        Assert.Equal(100 + ColumnGap, placements[1].Rect.Y);
        Assert.Equal(100 + ColumnGap, placements[2].Rect.Y);
        Assert.Equal(0, placements[3].Rect.Y);
    }

    [Fact]
    public void Arrange_ClampsColumnWhenSpanNoLongerFits()
    {
        // 卡片原来在第 2 列，跨列变成 2 后装不下 → 贴到最右可用列（第 1 列）
        var placements = Arrange(new BoardItem(2, 2, 100));

        Assert.Equal(1, placements[0].Column);
    }

    [Fact]
    public void ShortestColumn_TiesGoToLeftmost()
    {
        Assert.Equal(0, CardLayoutMath.ShortestColumn([0, 0, 0], 1));
        Assert.Equal(0, CardLayoutMath.ShortestColumn([30, 30.2, 30.4], 1));
        Assert.Equal(1, CardLayoutMath.ShortestColumn([100, 20, 50], 1));
        Assert.Equal(1, CardLayoutMath.ShortestColumn([100, 20, 50], 2));
        Assert.Equal(0, CardLayoutMath.ShortestColumn([5], 4));
        Assert.Equal(0, CardLayoutMath.ShortestColumn([], 2));
    }

    #endregion

    #region 铺满拉伸（缺口补偿）

    [Fact]
    public void DistributeFill_SpreadsDeficitWithinEachColumn()
    {
        // 两列各一张单列卡：每列缺口全部补给本列卡片
        var items = new[] { new BoardItem(0, 1, 100), new BoardItem(1, 1, 60) };
        var placements = CardLayoutMath.Arrange(items, 100, 2, 10, 10);

        var extras = CardLayoutMath.DistributeFill(items, placements, 2, 160);

        Assert.Equal(60, extras[0], 1);
        Assert.Equal(100, extras[1], 1);
    }

    [Fact]
    public void DistributeFill_SplitsEvenlyAmongSingleSpanCards()
    {
        var items = new[] { new BoardItem(0, 1, 50), new BoardItem(0, 1, 50) };
        var placements = CardLayoutMath.Arrange(items, 100, 1, 10, 10);

        var extras = CardLayoutMath.DistributeFill(items, placements, 1, 200);

        Assert.Equal(45, extras[0], 1);
        Assert.Equal(45, extras[1], 1);
    }

    [Fact]
    public void DistributeFill_LeavesSpanCardsUntouched()
    {
        // 跨列卡片不参与拉伸（否则会同时影响两列，破坏对齐），缺口由单列卡片均摊
        var items = new[] { new BoardItem(0, 2, 100), new BoardItem(0, 1, 40), new BoardItem(1, 1, 40) };
        var placements = CardLayoutMath.Arrange(items, 100, 2, 10, 10);

        var extras = CardLayoutMath.DistributeFill(items, placements, 2, 200);

        Assert.Equal(0, extras[0], 1);
        Assert.Equal(50, extras[1], 1);
        Assert.Equal(50, extras[2], 1);
    }

    [Fact]
    public void DistributeFill_NoOpWhenTargetAlreadyMet()
    {
        var items = new[] { new BoardItem(0, 1, 100) };
        var placements = CardLayoutMath.Arrange(items, 100, 1, 10, 10);

        Assert.Equal(0, CardLayoutMath.DistributeFill(items, placements, 1, 100)[0], 1);
        Assert.Equal(0, CardLayoutMath.DistributeFill(items, placements, 1, 0)[0], 1);
        Assert.Empty(CardLayoutMath.DistributeFill([], [], 2, 100));
    }

    #endregion

    #region Windows 系统信息映射

    [Theory]
    [InlineData("26100", "4946", "26100.4946")]
    [InlineData("26100", null, "26100")]
    [InlineData("26100", "", "26100")]
    [InlineData(null, "4946", null)]
    public void FormatWindowsBuild_CombinesBuildAndUbr(string? build, string? ubr, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.FormatWindowsBuild(build, ubr));
    }

    [Theory]
    [InlineData("Microsoft Windows 11 专业版", "Windows 11 专业版")]
    [InlineData("Microsoft Windows 10 家庭版", "Windows 10 家庭版")]
    [InlineData("Windows 11 专业版", "Windows 11 专业版")]
    [InlineData(null, null)]
    public void CleanProductName_StripsMicrosoftPrefix(string? raw, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.CleanProductName(raw));
    }

    [Theory]
    [InlineData(0, "未授权")]
    [InlineData(1, "已激活")]
    [InlineData(2, "初始宽限期")]
    [InlineData(5, "通知模式")]
    public void MapLicenseStatus_TranslatesKnownCodes(int status, string expected)
    {
        Assert.Equal(expected, HardwareInfoService.MapLicenseStatus(status));
    }

    [Fact]
    public void MapLicenseStatus_UnknownCodeReturnsNull()
    {
        Assert.Null(HardwareInfoService.MapLicenseStatus(99));
    }

    [Theory]
    [InlineData("Retail", "零售版")]
    [InlineData("OEM", "OEM 预装")]
    [InlineData("OEM:DM", "OEM:DM")]
    [InlineData("Volume:MAK", "批量授权 (MAK)")]
    [InlineData("Volume:GVLK", "批量授权 (KMS 客户端)")]
    [InlineData(null, null)]
    public void MapProductKeyChannel_TranslatesChannels(string? channel, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.MapProductKeyChannel(channel));
    }

    [Theory]
    [InlineData(0, "未启用")]
    [InlineData(1, "已启用（未运行）")]
    [InlineData(2, "已启用并运行")]
    public void MapVbsStatus_TranslatesStatus(int status, string expected)
    {
        Assert.Equal(expected, HardwareInfoService.MapVbsStatus(status));
    }

    [Theory]
    [InlineData(533320, ".NET Framework 4.8.1")]
    [InlineData(528040, ".NET Framework 4.8")]
    [InlineData(461808, ".NET Framework 4.7.2")]
    [InlineData(394254, ".NET Framework 4.6.1")]
    [InlineData(378389, ".NET Framework 4.5")]
    public void MapDotNetFrameworkRelease_MapsKnownReleases(int release, string expected)
    {
        Assert.Equal(expected, HardwareInfoService.MapDotNetFrameworkRelease(release));
    }

    [Fact]
    public void MapDotNetFrameworkRelease_UnknownOrMissingReturnsNull()
    {
        Assert.Null(HardwareInfoService.MapDotNetFrameworkRelease(null));
        Assert.Null(HardwareInfoService.MapDotNetFrameworkRelease(1));
    }

    [Theory]
    [InlineData("4.09.00.0904", 26100, "DirectX 12 (4.09.00.0904)")]
    [InlineData("4.09.00.0904", 7601, "DirectX 9.0c (4.09.00.0904)")]
    [InlineData("6.01.7600.16385", 7601, "DirectX 11 (6.01.7600.16385)")]
    [InlineData("6.00.6002.18005", 6002, "DirectX 10 (6.00.6002.18005)")]
    [InlineData(null, 26100, "DirectX 12")]
    public void FormatDirectXVersion_DisambiguatesLegacyVersions(string? raw, int osBuild, string expected)
    {
        Assert.Equal(expected, HardwareInfoService.FormatDirectXVersion(raw, osBuild));
    }

    [Theory]
    [InlineData(96, "100%")]
    [InlineData(120, "125%")]
    [InlineData(144, "150%")]
    [InlineData(192, "200%")]
    public void FormatDisplayScaling_ConvertsDpiToPercent(int dpi, string expected)
    {
        Assert.Equal(expected, HardwareInfoService.FormatDisplayScaling(dpi));
    }

    [Fact]
    public void FormatDisplayScaling_InvalidDpiReturnsNull()
    {
        Assert.Null(HardwareInfoService.FormatDisplayScaling(0));
        Assert.Null(HardwareInfoService.FormatDisplayScaling(null));
    }

    [Fact]
    public void DecodeChassisTypes_TranslatesKnownCodes()
    {
        Assert.Equal("笔记本", HardwareInfoService.DecodeChassisTypes([9]));
        Assert.Equal("台式机", HardwareInfoService.DecodeChassisTypes([3]));
        Assert.Equal("一体机", HardwareInfoService.DecodeChassisTypes([0, 13]));
        Assert.Null(HardwareInfoService.DecodeChassisTypes([]));
        Assert.Null(HardwareInfoService.DecodeChassisTypes(null));
    }

    [Fact]
    public void MapInstallLanguageCode_ConvertsLcidHex()
    {
        var simplified = HardwareInfoService.MapInstallLanguageCode("0804");
        Assert.NotNull(simplified);
        Assert.DoesNotContain("0804", simplified);

        // 无法解析的输入原样返回，不抛异常
        Assert.Equal("zz", HardwareInfoService.MapInstallLanguageCode("zz"));
        Assert.Null(HardwareInfoService.MapInstallLanguageCode(null));
    }

    [Theory]
    [InlineData(0, 0, 0, "0 分钟")]
    [InlineData(0, 0, 45, "45 分钟")]
    [InlineData(0, 1, 30, "1 小时 30 分钟")]
    [InlineData(1, 6, 0, "1 天 6 小时 0 分钟")]
    [InlineData(3, 0, 15, "3 天 0 小时 15 分钟")]
    public void FormatUptimeSpan_FormatsByMagnitude(int days, int hours, int minutes, string expected)
    {
        Assert.Equal(expected, HardwareInfoService.FormatUptimeSpan(new TimeSpan(days, hours, minutes, 0)));
    }

    [Fact]
    public void FormatUptimeSpan_NegativeClampsToZero()
    {
        Assert.Equal("0 分钟", HardwareInfoService.FormatUptimeSpan(TimeSpan.FromMinutes(-5)));
    }

    [Theory]
    [InlineData(1, "固态（无转速）")]
    [InlineData(7200, "7200 RPM")]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    public void FormatRotationRate_HandlesSsdSentinel(long rate, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.FormatRotationRate(rate));
    }

    [Theory]
    [InlineData("0", "MBR")]
    [InlineData("1", "GPT")]
    [InlineData("2", "RAW")]
    [InlineData(null, null)]
    public void MapPartitionStyle_TranslatesStyleCodes(string? raw, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.MapPartitionStyle(raw));
    }

    [Theory]
    [InlineData("OK", "正常")]
    [InlineData("Pred Fail", "预测故障")]
    [InlineData("Custom", "Custom")]
    [InlineData("", null)]
    public void MapDeviceStatus_TranslatesStatuses(string raw, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.MapDeviceStatus(raw));
    }

    [Theory]
    [InlineData("SN12345678", "SN12345678")]
    [InlineData("00000000", null)]
    [InlineData("FFFFFFFF", null)]
    [InlineData("To be filled by O.E.M.", null)]
    [InlineData("Default string", null)]
    [InlineData("", null)]
    public void CleanSerial_RejectsPlaceholderValues(string raw, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.CleanSerial(raw));
    }

    [Theory]
    [InlineData("3", "6", "3.6")]
    [InlineData("2", "0", "2.0")]
    [InlineData(null, null, null)]
    public void FormatSmbiosVersion_CombinesMajorMinor(string? major, string? minor, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.FormatSmbiosVersion(major, minor));
    }

    [Theory]
    [InlineData(2, "已连接")]
    [InlineData(7, "媒体已断开")]
    [InlineData(99, null)]
    public void MapNetworkConnectionStatus_TranslatesStatus(int status, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.MapNetworkConnectionStatus(status));
    }

    [Fact]
    public void JoinAddresses_FiltersLinkLocalAndEmpty()
    {
        Assert.Equal("192.168.1.5", HardwareInfoService.JoinAddresses(["192.168.1.5", "fe80::abcd", "0.0.0.0", ""]));
        Assert.Null(HardwareInfoService.JoinAddresses(["fe80::1"]));
        Assert.Null(HardwareInfoService.JoinAddresses(null));
    }

    [Theory]
    [InlineData(2021, 12, "2021 年第 12 周")]
    [InlineData(2021, 0, "2021 年")]
    [InlineData(0, 5, null)]
    public void FormatMonitorMadeDate_FormatsYearWeek(int year, int week, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.FormatMonitorMadeDate(year, week));
    }

    #endregion

    #region 分区图标与本地化键

    private static string HwIconDir => Path.Combine(TestPaths.AppRoot, "Assets", "HwIcons");

    [Fact]
    public void HardwareIcons_FollowFluentSpecifications()
    {
        Assert.True(Directory.Exists(HwIconDir), $"缺少图标目录: {HwIconDir}");
        Assert.Empty(IconAssetValidation.ValidateAll(HwIconDir));
        Assert.Empty(IconAssetValidation.ValidateRasterizes(HwIconDir));
    }

    [Fact]
    public void HardwareIcons_CoverEverySection()
    {
        // 直接从页面源码里取分区定义的图标 id，避免测试与实现各维护一份清单
        var pageSource = File.ReadAllText(Path.Combine(TestPaths.AppRoot, "Pages", "HardwareDetailPage.xaml.cs"));
        var icons = Regex.Matches(pageSource, "new\\(\"[^\"]+\",\\s*HardwareDetailPart\\.\\w+,\\s*\"([^\"]+)\"\\s*,")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.True(icons.Count >= 14, $"解析到的分区图标过少（{icons.Count}），检查分区定义写法是否变了");

        var missing = icons.Where(icon => !File.Exists(Path.Combine(HwIconDir, icon + ".svg"))).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void SectionDefinitions_CoverEveryDetailPart()
    {
        // 采集侧新增一个数据块后，界面必须同时补上分区定义，否则那块数据永远不显示
        var pageSource = File.ReadAllText(Path.Combine(TestPaths.AppRoot, "Pages", "HardwareDetailPage.xaml.cs"));
        var mapped = Regex.Matches(pageSource, "HardwareDetailPart\\.(\\w+)")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = Enum.GetNames<HardwareDetailPart>().Where(name => !mapped.Contains(name)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void HwResourceKeys_UsedInCode_AreDefinedInBothLanguages()
    {
        var zh = LoadResourceKeys("zh-CN");
        var en = LoadResourceKeys("en-US");

        var sources = new[]
        {
            Path.Combine(TestPaths.AppRoot, "Services", "LocalizationService.cs"),
            Path.Combine(TestPaths.AppRoot, "Pages", "HardwareDetailPage.xaml.cs")
        };

        var used = sources
            .SelectMany(source => Regex.Matches(File.ReadAllText(source), "L\\(\\s*\"(Hw[A-Za-z_0-9]*)\"")
                .Select(match => match.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.True(used.Count > 100, $"解析到的本地化键过少（{used.Count}）");
        Assert.DoesNotContain(used, key => !zh.Contains(key));
        Assert.DoesNotContain(used, key => !en.Contains(key));
    }

    private static HashSet<string> LoadResourceKeys(string language)
    {
        var path = Path.Combine(TestPaths.AppRoot, "Strings", language, "Resources.resw");
        var document = System.Xml.Linq.XDocument.Load(path);
        return document.Root!
            .Elements("data")
            .Select(element => (string)element.Attribute("name")!)
            .ToHashSet(StringComparer.Ordinal);
    }

    #endregion
}
