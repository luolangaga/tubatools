using TubaWinUi3.Services.DriverStore;

namespace TubaWinUi3.Tests;

/// <summary>
/// 驱动管理（issue #241）：pnputil 枚举输出解析（中英文/未知语言按值模式兜底）、
/// 日期/版本解析、分组与最新版本标记、FileRepository 大小匹配、操作结果分类。
/// 全部为纯逻辑，不触碰系统。
/// </summary>
public class DriverStoreTests
{
    private const string ChineseEnumOutput = """
        Microsoft PnP 工具

        发布名称:            oem0.inf
        原始名称:            prnms009.Inf
        提供程序:            Microsoft
        类:                  打印机
        驱动程序日期和版本:  06/21/2006 10.0.26100.1
        签名人:              Microsoft Windows

        发布名称:            oem14.inf
        原始名称:            nvlt.inf
        提供程序:            NVIDIA
        类:                  显示适配器
        驱动程序日期和版本:  03/30/2023 31.0.15.5222
        签名者:              Microsoft Windows Hardware Compatibility Publisher
        """;

    private const string EnglishEnumOutput = """
        Microsoft PnP tool

        Published Name:      oem14.inf
        Original Name:       nvlt.inf
        Provider Name:       NVIDIA
        Class Name:          Display adapters
        Driver Version:      03/30/2023 31.0.15.5222
        Signer:              Microsoft Windows Hardware Compatibility Publisher

        Published Name:      oem15.inf
        Original Name:       nvlt.inf
        Provider Name:       NVIDIA
        Class Name:          Display adapters
        Driver Version:      03/30/2024 32.0.15.6114
        Signer:              Microsoft Windows Hardware Compatibility Publisher
        """;

    [Fact]
    public void NativeStruct_UsesDismPackedAbi()
        => Assert.Equal(IntPtr.Size == 8 ? 100 : 72, DriverStoreNative.PackageStructSize);

    [Fact]
    public void BuildGroups_DifferentProvidersOrClassesAreNeverCleanupCandidates()
    {
        var items = new List<DriverPackage>
        {
            new() { InfName = "oem1.inf", OriginalInfName = "common.inf", Provider = "A", ClassGuid = "a", Version = "1.0" },
            new() { InfName = "oem2.inf", OriginalInfName = "common.inf", Provider = "B", ClassGuid = "a", Version = "2.0" },
            new() { InfName = "oem3.inf", OriginalInfName = "common.inf", Provider = "A", ClassGuid = "b", Version = "2.0" }
        };
        Assert.Equal(3, DriverStoreService.BuildGroups(items).Count);
        Assert.All(items, p => Assert.True(p.IsNewest));
    }

    [Fact]
    public void EqualVersionAndDate_AllRemainNewest()
    {
        var items = new List<DriverPackage>
        {
            new() { InfName = "oem1.inf", OriginalInfName = "a.inf", Version = "1.0" },
            new() { InfName = "oem2.inf", OriginalInfName = "a.inf", Version = "1.0" }
        };
        DriverStoreService.BuildGroups(items);
        Assert.All(items, p => Assert.True(p.IsNewest));
    }

    [Fact]
    public void DeletePolicy_UnknownUsageOfflineBootCriticalAndBoundDevicesAreProtected()
    {
        var p = new DriverPackage { InfName = "oem1.inf", OriginalInfName = "a.inf" };
        Assert.False(p.CanDelete);
        p.UsageKnown = true;
        Assert.True(p.CanDelete);
        Assert.False((p with { IsOffline = true }).CanDelete);
        Assert.False((p with { BootCritical = true }).CanDelete);
        p.InUse = true;
        Assert.False(p.CanDelete);
        p.InUse = false;
        p.IsNewest = true;
        Assert.False(p.CanDelete);
    }

    [Fact]
    public void Csv_EscapesQuotesNewlinesAndFormulaPrefixes()
    {
        var csv = DriverStoreService.ToCsv([new DriverPackage { InfName = "oem1.inf", OriginalInfName = "a.inf", Provider = "=cmd(1)", ClassName = "a,\"b\n" }]);
        Assert.Contains("\"'=cmd(1)\"", csv);
        Assert.Contains("\"a,\"\"b\n\"", csv);
        Assert.Contains("未知", csv);
    }

    [Fact]
    public void Export_CopiesExactPackageTreeAndRejectsRecursiveDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), "DriverStoreTest-" + Guid.NewGuid());
        var source = Path.Combine(root, "source");
        try
        {
            Directory.CreateDirectory(Path.Combine(source, "zh-CN"));
            var inf = Path.Combine(source, "a.inf");
            File.WriteAllText(inf, "test-inf");
            File.WriteAllText(Path.Combine(source, "zh-CN", "a.dll"), "test-binary");
            var p = new DriverPackage { InfName = "oem1.inf", OriginalInfName = "a.inf", StoreInfPath = inf };
            var target = Path.Combine(root, "backup");
            DriverStoreService.ExportPackage(p, target, default);
            Assert.Equal("test-inf", File.ReadAllText(Path.Combine(target, "oem1_source", "a.inf")));
            Assert.Equal("test-binary", File.ReadAllText(Path.Combine(target, "oem1_source", "zh-CN", "a.dll")));
            Assert.Throws<IOException>(() => DriverStoreService.ExportPackage(p, source, default));
            Assert.Throws<IOException>(() => DriverStoreService.ExportPackage(p, Path.Combine(source, "nested"), default));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // ══════════════════ 枚举解析 ══════════════════

    [Fact]
    public void ParseEnumDrivers_ChineseOutput_ParsesAllFields()
    {
        var packages = DriverStoreService.ParseEnumDrivers(ChineseEnumOutput);

        Assert.Equal(2, packages.Count);
        var prn = packages[0];
        Assert.Equal("oem0.inf", prn.InfName);
        Assert.Equal("prnms009.inf", prn.OriginalInfName);
        Assert.Equal("Microsoft", prn.Provider);
        Assert.Equal("打印机", prn.ClassName);
        Assert.Equal(new DateTime(2006, 6, 21), prn.Date);
        Assert.Equal("10.0.26100.1", prn.Version);

        var nv = packages[1];
        Assert.Equal("oem14.inf", nv.InfName);
        Assert.Equal("nvlt.inf", nv.OriginalInfName);
        Assert.Equal("NVIDIA", nv.Provider);
        Assert.Equal(new DateTime(2023, 3, 30), nv.Date);
        Assert.Equal("31.0.15.5222", nv.Version);
    }

    [Fact]
    public void ParseEnumDrivers_EnglishOutput_ParsesAllFields()
    {
        var packages = DriverStoreService.ParseEnumDrivers(EnglishEnumOutput);

        Assert.Equal(2, packages.Count);
        Assert.Equal("nvlt.inf", packages[0].OriginalInfName);
        Assert.Equal("Display adapters", packages[0].ClassName);
        Assert.Equal("31.0.15.5222", packages[0].Version);
        Assert.Equal("32.0.15.6114", packages[1].Version);
    }

    [Fact]
    public void ParseEnumDrivers_UnknownLocale_StillExtractsInfNamesAndVersions()
    {
        // 任意语言的输出：值模式（oemNN.inf / inf / 日期+版本）仍然成立
        var output = """
            xxxxxxxxxxxxxxx

            xxxxxxx:            oem42.inf
            xxxxxxx:            somegfx.Inf
            xxxxxxxxxxxxxxxx:   11/15/2021 30.0.14.7219
            """;

        var packages = DriverStoreService.ParseEnumDrivers(output);

        var pkg = Assert.Single(packages);
        Assert.Equal("oem42.inf", pkg.InfName);
        Assert.Equal("somegfx.inf", pkg.OriginalInfName);
        Assert.Equal("30.0.14.7219", pkg.Version);
        Assert.Equal(new DateTime(2021, 11, 15), pkg.Date);
    }

    [Fact]
    public void ParseEnumDrivers_EmptyOrGarbage_ReturnsEmptyOrSafeFallback()
    {
        Assert.Empty(DriverStoreService.ParseEnumDrivers(""));
        Assert.Empty(DriverStoreService.ParseEnumDrivers("没有任何驱动内容"));

        // 有 oem 行但没有原始名：原始名回退为发布名，保证分组键存在
        var fallback = DriverStoreService.ParseEnumDrivers("发布名称:  oem7.inf\n提供程序: X");
        var pkg = Assert.Single(fallback);
        Assert.Equal("oem7.inf", pkg.OriginalInfName);
    }

    // ══════════════════ 日期与版本 ══════════════════

    [Theory]
    [InlineData("06/21/2006", 2006, 6, 21)]   // MM/DD/YYYY
    [InlineData("2023/03/30", 2023, 3, 30)]    // YYYY/MM/DD
    [InlineData("2023.03.30", 2023, 3, 30)]    // 点分隔
    public void ParseDriverDate_CommonFormats(string text, int y, int m, int d)
        => Assert.Equal(new DateTime(y, m, d), DriverStoreService.ParseDriverDate(text));

    [Theory]
    [InlineData("31/31/2023")]  // 非法日期
    [InlineData("abc")]
    [InlineData("2023/3")]
    public void ParseDriverDate_Invalid_ReturnsNull(string text)
        => Assert.Null(DriverStoreService.ParseDriverDate(text));

    [Fact]
    public void ReadInfDriverVerText_ExtractsVersion()
    {
        Assert.Equal("31.0.15.5222",
            DriverStoreService.ReadInfDriverVerText("[Version]\r\nDriverVer = 03/30/2023, 31.0.15.5222"));
        Assert.Equal("10.0.26100.1",
            DriverStoreService.ReadInfDriverVerText("DriverVer=06/21/2006,10.0.26100.1"));
        Assert.Equal("", DriverStoreService.ReadInfDriverVerText("[Version]\r\nSignature=\"$WINDOWS NT$\""));
    }

    // ══════════════════ 分组与旧版本标记 ══════════════════

    [Fact]
    public void BuildGroups_GroupsByOriginalInf_AndMarksNewest()
    {
        var packages = DriverStoreService.ParseEnumDrivers(EnglishEnumOutput);
        var groups = DriverStoreService.BuildGroups(packages);

        var nvGroup = Assert.Single(groups);
        Assert.Equal(2, nvGroup.Packages.Count);
        // 2024 版本在前且标记最新；2023 版本为旧版本
        Assert.Equal("32.0.15.6114", nvGroup.Packages[0].Version);
        Assert.True(nvGroup.Packages[0].IsNewest);
        Assert.False(nvGroup.Packages[1].IsNewest);
        Assert.Equal(1, nvGroup.OldCount);
    }

    [Fact]
    public void BuildGroups_NewestDeterminedByVersion_WhenDateMissing()
    {
        var packages = new List<DriverPackage>
        {
            new() { InfName = "oem1.inf", OriginalInfName = "a.inf", Version = "1.0.0" },
            new() { InfName = "oem2.inf", OriginalInfName = "a.inf", Version = "2.0.0" },
        };

        var group = Assert.Single(DriverStoreService.BuildGroups(packages));

        Assert.Equal("2.0.0", group.Packages[0].Version);
        Assert.True(group.Packages[0].IsNewest);
        Assert.Equal("1.0.0", group.Packages[1].Version);
        Assert.False(group.Packages[1].IsNewest);
    }

    // ══════════════════ FileRepository 大小匹配 ══════════════════

    [Fact]
    public void AssignRepositorySizes_ExactVersionMatch_Wins()
    {
        var packages = new List<DriverPackage>
        {
            new() { InfName = "oem1.inf", OriginalInfName = "nvlt.inf", Version = "31.0.15.5222" },
            new() { InfName = "oem2.inf", OriginalInfName = "nvlt.inf", Version = "32.0.15.6114" },
        };
        var dirs = new List<DriverStoreService.RepoDirInfo>
        {
            new("nvlt.inf_amd64_aaa", "nvlt.inf", "31.0.15.5222", 500),
            new("nvlt.inf_amd64_bbb", "nvlt.inf", "32.0.15.6114", 900),
        };

        DriverStoreService.AssignRepositorySizes(packages, dirs);

        Assert.Equal(500, packages[0].SizeBytes);
        Assert.Equal(900, packages[1].SizeBytes);
    }

    [Fact]
    public void AssignRepositorySizes_SingleDirFallback_WhenVersionUnknown()
    {
        var packages = new List<DriverPackage>
        {
            new() { InfName = "oem1.inf", OriginalInfName = "only.inf", Version = "" },
        };
        var dirs = new List<DriverStoreService.RepoDirInfo>
        {
            new("only.inf_amd64_x", "only.inf", "", 777),
        };

        DriverStoreService.AssignRepositorySizes(packages, dirs);

        Assert.Equal(777, packages[0].SizeBytes);
    }

    [Fact]
    public void AssignRepositorySizes_NoMatchingDir_StaysZero()
    {
        var packages = new List<DriverPackage>
        {
            new() { InfName = "oem1.inf", OriginalInfName = "none.inf", Version = "1.0" },
        };
        var dirs = new List<DriverStoreService.RepoDirInfo>
        {
            new("a.inf_amd64_x", "a.inf", "1.0", 100),
            new("b.inf_amd64_y", "b.inf", "1.0", 200),
        };

        DriverStoreService.AssignRepositorySizes(packages, dirs);

        Assert.Equal(0, packages[0].SizeBytes);
    }

    [Theory]
    [InlineData("oem1.inf /force")]
    [InlineData("prefixoem1.inf")]
    [InlineData("oem1.inf\\..\\x")]
    [InlineData("oem1.inf\n")]
    public async Task DeleteAsync_RejectsMalformedNames(string name)
        => await Assert.ThrowsAsync<ArgumentException>(() => DriverStoreService.DeleteAsync(name));

    [Fact]
    public async Task DeleteAsync_RejectsNonOemInf()
    {
        // 拒绝任意路径及参数注入
        await Assert.ThrowsAsync<ArgumentException>(() => DriverStoreService.DeleteAsync("nvlt.inf"));
        await Assert.ThrowsAsync<ArgumentException>(() => DriverStoreService.DeleteAsync("C:\\windows\\x.inf"));
    }
}
