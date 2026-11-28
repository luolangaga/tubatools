using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class HardwareInfoServiceTests
{
    [Theory]
    [InlineData("Intel(R) Core(TM) i7-12700K", "intel")]
    [InlineData("AMD Ryzen 9 7950X", "amd")]
    [InlineData("Apple M1 Pro", "apple")]
    [InlineData("Apple M2", "apple")]
    [InlineData("Apple M3 Max", "apple")]
    [InlineData("Apple M4", "apple")]
    [InlineData("Qualcomm Snapdragon X Elite", "qualcomm")]
    [InlineData("Snapdragon 8 Gen 3", "qualcomm")]
    [InlineData("Unknown CPU", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void DetectCpuBrand_DetectsCorrectBrand(string? cpuName, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.DetectCpuBrand(cpuName));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3080", "nvidia")]
    [InlineData("GeForce GTX 1080", "nvidia")]
    [InlineData("RTX 4090", "nvidia")]
    [InlineData("GTX 1660 Super", "nvidia")]
    [InlineData("AMD Radeon RX 7900 XTX", "amd")]
    [InlineData("Radeon RX 6800 XT", "amd")]
    [InlineData("Intel Arc A770", "intel")]
    [InlineData("Intel UHD Graphics 770", "intel")]
    [InlineData("Intel Iris Xe", "intel")]
    [InlineData("Apple M1 GPU", "apple")]
    [InlineData("Qualcomm Adreno 730", "qualcomm")]
    [InlineData("Unknown GPU", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void DetectGpuBrand_DetectsCorrectBrand(string? gpuName, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.DetectGpuBrand(gpuName));
    }

    [Theory]
    [InlineData(18, "DDR")]
    [InlineData(19, "DDR2")]
    [InlineData(20, "DDR2 FB-DIMM")]
    [InlineData(24, "DDR3")]
    [InlineData(25, "DDR3L")]
    [InlineData(26, "DDR4")]
    [InlineData(27, "LPDDR")]
    [InlineData(28, "LPDDR2")]
    [InlineData(29, "LPDDR3")]
    [InlineData(30, "LPDDR4")]
    [InlineData(34, "DDR5")]
    [InlineData(35, "LPDDR5")]
    [InlineData(36, "HBM3")]
    [InlineData(0, "")]
    [InlineData(99, "")]
    public void GetMemoryTypeLabel_MapsCorrectly(int type, string expected)
    {
        Assert.Equal(expected, HardwareInfoService.GetMemoryTypeLabel(type));
    }

    [Theory]
    [InlineData("KINGSTON", "金士顿(Kingston)")]
    [InlineData("Kingston Technology", "金士顿(Kingston)")]
    [InlineData("CORSAIR", "海盗船(Corsair)")]
    [InlineData("CRUCIAL", "英睿达(Crucial)")]
    [InlineData("SAMSUNG", "三星(Samsung)")]
    [InlineData("SK HYNIX", "海力士(SK Hynix)")]
    [InlineData("HYNIX", "海力士(SK Hynix)")]
    [InlineData("MICRON", "美光(Micron)")]
    [InlineData("ADATA", "威刚(ADATA)")]
    [InlineData("G.SKILL", "芝奇(G.Skill)")]
    [InlineData("TEAMGROUP", "十铨(TeamGroup)")]
    [InlineData("Kingbank Technology", "金百达(Kingbank)")]
    [InlineData("KINGBANK", "金百达(Kingbank)")]
    // 十六进制 JEDEC ID 走全表解码（不再原样显示 hex）
    [InlineData("0746", "光威(Gloway)")]
    [InlineData("120B", "金百达(Kingbank)")]
    [InlineData("0B2E", "爱国者(aigo)")]
    // 字符串形式的品牌名（CPU-Z 数据源 / BIOS 直接给字符串）
    [InlineData("Asgard", "阿斯加特(Asgard)")]
    [InlineData("JUHOR", "玖合(JUHOR)")]
    [InlineData("Teclast", "台电(Teclast)")]
    [InlineData("Maxsun", "铭瑄(Maxsun)")]
    [InlineData("Kimtigo", "金泰克(Kimtigo)")]
    [InlineData("aigo", "爱国者(aigo)")]
    [InlineData("Avexir", "宇帷(Avexir)")]
    [InlineData("TwinMOS", "勤茂(TwinMOS)")]
    [InlineData("Neo Forza", "凌航(Neo Forza)")]
    [InlineData("A-DATA Technology", "威刚(ADATA)")]
    // SMBIOS/SPD 占位串（Issue #193：DDR5 主板 BIOS 未填厂商，界面显示 "Unknown"）视为无数据
    [InlineData("Unknown", null)]
    [InlineData("To be filled by O.E.M.", null)]
    [InlineData("Default string", null)]
    [InlineData("Not Specified", null)]
    [InlineData("N/A", null)]
    [InlineData("UnknownBrand", "UnknownBrand")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    public void CleanMemManufacturer_CleansCorrectly(string? raw, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.CleanMemManufacturer(raw));
    }

    [Theory]
    // JEP106 page 0：0x2C=美光、0x4E=三星（0xCE 为 0x4E 带奇校验）、0x18=东芝内存(Kioxia)
    [InlineData("2C", "美光(Micron)")]
    [InlineData("CE", "三星(Samsung)")]
    [InlineData("18", "东芝内存(Kioxia)")]
    // 奇校验位：0x92 与 0x12 必须解到同一厂商（Kingbank 的 vendor byte 0x12）
    [InlineData("92", null)] // 单字节无法区分是哪一 bank 的 vendor，返回 null 让上层走字符串路径
    [InlineData("FF", null)]
    public void DecodeJedecManufacturer_2DigitHex(string raw, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.DecodeJedecManufacturer(raw));
    }

    [Theory]
    [InlineData("0x2C", "美光(Micron)")]
    [InlineData("0xCE", "三星(Samsung)")]
    [InlineData("0x92", null)] // 单字节厂商码去校验后是 0x12，表中无匹配
    public void DecodeJedecManufacturer_0xPrefix2Digit(string raw, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.DecodeJedecManufacturer(raw));
    }

    [Theory]
    // Kingbank 的 JEP106 完整编码是 Bank11 + vendor 0x12，十六进制 0x0B12。
    // Windows 实际返回的两种字节序都要正确解码：
    [InlineData("0B12", "金百达(Kingbank)")] // 大端：[continuation][vendor]
    [InlineData("120B", "金百达(Kingbank)")] // 小端：[vendor][continuation]
    [InlineData("0x0B12", "金百达(Kingbank)")]
    [InlineData("0x120B", "金百达(Kingbank)")]
    // Silicon Power：Bank6 + 0x53
    [InlineData("0653", "广颖电通(Silicon Power)")]
    [InlineData("5306", "广颖电通(Silicon Power)")]
    // Bank11 + 0x12 带奇校验位：高位字节 = 0x8B，低位 = 0x92
    [InlineData("8B92", "金百达(Kingbank)")]
    [InlineData("928B", "金百达(Kingbank)")]
    // 主流大厂真实 SPD 编码：三星 = page0+0x4E（带校验 0xCE，continuation 0x00）
    [InlineData("00CE", "三星(Samsung)")]
    [InlineData("CE00", "三星(Samsung)")]
    // 金士顿 = page1+0x18（带校验 0x98）
    [InlineData("0118", "金士顿(Kingston)")]
    [InlineData("1801", "金士顿(Kingston)")]
    [InlineData("0198", "金士顿(Kingston)")]
    // 英睿达 = page5+0x1B
    [InlineData("051B", "英睿达(Crucial)")]
    // 全表适配（JEP106BL）：此前未收录的国产 / 常见模组品牌
    [InlineData("044B", "威刚(ADATA)")] // Bank4 + 0x4B
    [InlineData("0616", "宇帷(Avexir)")] // Bank6 + 0x16
    [InlineData("066B", "勤茂(TwinMOS)")] // Bank6 + 0x6B
    [InlineData("066D", "全何(V-Color)")] // Bank6 + 0x6D
    [InlineData("0746", "光威(Gloway)")] // Bank7 + 0x46
    [InlineData("0813", "光威(Gloway)")] // Bank8 + 0x13
    [InlineData("0871", "阿斯加特(Asgard)")] // Bank8 + 0x71
    [InlineData("0875", "玖合(JUHOR)")] // Bank8 + 0x75
    [InlineData("091B", "长江存储(YMTC)")] // Bank9 + 0x1B
    [InlineData("0921", "台电(Teclast)")] // Bank9 + 0x21
    [InlineData("0922", "铭瑄(Maxsun)")] // Bank9 + 0x22
    [InlineData("092D", "凌航(Neo Forza)")] // Bank9 + 0x2D
    [InlineData("0968", "金泰克(Kimtigo)")] // Bank9 + 0x68
    [InlineData("0B2E", "爱国者(aigo)")] // Bank11 + 0x2E
    [InlineData("1003", "JoulWatt Technology Co Ltd")] // Bank16：全表最高 bank 也要能解
    public void DecodeJedecManufacturer_4DigitHex(string raw, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.DecodeJedecManufacturer(raw));
    }

    [Fact]
    public void JedecVendorFromCode_KnownCodes_ReturnCorrectVendor()
    {
        Assert.Equal("美光(Micron)", HardwareInfoService.JedecVendorFromCode(0x2C));
        Assert.Equal("三星(Samsung)", HardwareInfoService.JedecVendorFromCode(0x4E));
        Assert.Equal("海力士(SK Hynix)", HardwareInfoService.JedecVendorFromCode(0x2D));
        Assert.Equal("三菱(Mitsubishi)", HardwareInfoService.JedecVendorFromCode(0x1C));
        Assert.Null(HardwareInfoService.JedecVendorFromCode(0xFF));
    }

    [Theory]
    // 回归：曾经伪造的 0x80-0x95 映射必须全部移除，避免误识别
    [InlineData((byte)0x80, null)]
    [InlineData((byte)0x83, null)] // 旧版被误映射到 Kingston
    [InlineData((byte)0x92, null)] // 旧版被误映射到 Silicon Power（= Kingbank vendor 0x12 带校验）
    [InlineData((byte)0x95, null)] // 旧版被误映射到 CXMT
    public void JedecVendorFromCode_FabricatedHighRange_NoLongerMapped(byte code, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.JedecVendorFromCode(code));
    }

    [Theory]
    [InlineData(0x0B12, "金百达(Kingbank)")] // Bank11 + 0x12 = Kingbank
    [InlineData(0x653, "广颖电通(Silicon Power)")] // Bank6 + 0x53 = Silicon Power
    [InlineData(0x0118, "金士顿(Kingston)")] // Bank1 + 0x18
    [InlineData(0x051B, "英睿达(Crucial)")] // Bank5 + 0x1B
    [InlineData(0x044D, "芝奇(G.Skill)")] // Bank4 + 0x4D
    [InlineData(0x0818, "科赋(Klevv/Essencore)")] // Bank8 + 0x18
    [InlineData(0x002C, "美光(Micron)")] // Bank0 + 0x2C
    // 全表适配：新收录的 bank / 厂商
    [InlineData(0x044B, "威刚(ADATA)")]
    [InlineData(0x0616, "宇帷(Avexir)")]
    [InlineData(0x0746, "光威(Gloway)")]
    [InlineData(0x0921, "台电(Teclast)")]
    [InlineData(0x0968, "金泰克(Kimtigo)")]
    [InlineData(0x0B2E, "爱国者(aigo)")]
    [InlineData(0x1003, "JoulWatt Technology Co Ltd")] // Bank16 最高 bank
    [InlineData(0x0B7F, null)] // 未注册的 vendor code（bank 11 最大 id 为 126）；bank 17 同理不存在
    public void JedecVendorFromExtendedCode_MultiByte(int fullCode, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.JedecVendorFromExtendedCode(fullCode));
    }

    [Theory]
    [InlineData("ASUS", "华硕(ASUS)")]
    [InlineData("ASUSTeK COMPUTER INC.", "华硕(ASUS)")]
    [InlineData("MSI", "微星(MSI)")]
    [InlineData("GIGABYTE", "技嘉(Gigabyte)")]
    [InlineData("ASROCK", "华擎(ASRock)")]
    [InlineData("LENOVO", "联想(Lenovo)")]
    [InlineData("DELL", "戴尔(Dell)")]
    [InlineData("HP", "惠普(HP)")]
    [InlineData("UnknownBoard", "UnknownBoard")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void CleanBoardManufacturer_CleansCorrectly(string? raw, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.CleanBoardManufacturer(raw));
    }

    [Theory]
    [InlineData("ASU", "华硕(ASUS)")]
    [InlineData("AUS", "华硕(ASUS)")]
    [InlineData("LEN", "联想(Lenovo)")]
    [InlineData("DEL", "Dell(戴尔)")]
    [InlineData("HWP", "HP(惠普)")]
    [InlineData("SAM", "三星(Samsung)")]
    [InlineData("BOE", "京东方(BOE)")]
    [InlineData("AUO", "友达(AU Optronics)")]
    [InlineData("CSO", "华星光电(CSOT)")]
    [InlineData("CMN", "奇美(Chimei InnoLux)")]
    [InlineData("HEC", "海信(Hisense)")]
    [InlineData("HIT", "日立(Hitachi)")]
    [InlineData("HTC", "日立(Hitachi)")]
    [InlineData("HRE", "海尔(Haier)")]
    [InlineData("MEL", "三菱(Mitsubishi)")]
    [InlineData("MAG", "美格(MAG)")]
    [InlineData("MSI", "微星(MSI)")]
    [InlineData("TOL", "TCL")]
    [InlineData("CSW", "华星光电(CSOT)")]
    [InlineData("HHT", "鸿合(Hitevision)")]
    [InlineData("XXX", "XXX")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void ResolveManufacturer_ResolvesCorrectly(string? code, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.ResolveManufacturer(code));
    }

    [Theory]
    [InlineData("DISPLAY\\SAM1234\\1", "SAM1234")]
    [InlineData("MONITOR\\DEL1234\\0", "DEL1234")]
    [InlineData("DISPLAY#AUO5678#1", "AUO5678")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ExtractMonitorPnpCode_ExtractsCorrectly(string? deviceId, string expected)
    {
        Assert.Equal(expected, HardwareInfoService.ExtractMonitorPnpCode(deviceId));
    }

    [Theory]
    [InlineData("Hello World", new[] { "Hello" }, true)]
    [InlineData("Hello World", new[] { "xyz" }, false)]
    [InlineData("Hello World", new[] { "WORLD" }, true)]
    [InlineData(null, new[] { "test" }, false)]
    [InlineData("", new[] { "test" }, false)]
    [InlineData("Hello", new string[] { }, false)]
    public void ContainsAny_DetectsCorrectly(string? value, string[] needles, bool expected)
    {
        Assert.Equal(expected, HardwareInfoService.ContainsAny(value, needles));
    }

    [Theory]
    [InlineData("3600", "3.6 GHz")]
    [InlineData("800", "800 MHz")]
    [InlineData("0", null)]
    [InlineData(null, null)]
    [InlineData("-100", null)]
    public void FormatMhz_FormatsCorrectly(string? value, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.FormatMhz(value));
    }

    [Theory]
    [InlineData("8192", "8 MB")]
    [InlineData("512", "512 KB")]
    [InlineData("0", null)]
    [InlineData(null, null)]
    public void FormatCacheSize_FormatsCorrectly(string? value, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.FormatCacheSize(value));
    }

    [Theory]
    [InlineData("0", "x86")]
    [InlineData("5", "ARM")]
    [InlineData("9", "x64")]
    [InlineData("12", "ARM64")]
    [InlineData("6", "Itanium")]
    [InlineData("99", null)]
    [InlineData(null, "x86")]
    public void MapCpuArchitecture_MapsCorrectly(string? value, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.MapCpuArchitecture(value));
    }

    [Theory]
    [InlineData("20240115000000.000000+000", "2024-01-15")]
    [InlineData("20231231000000", "2023-12-31")]
    [InlineData("19000101", "19000101")]
    [InlineData("18991231", "18991231")]
    [InlineData("short", "short")]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void FormatBiosDate_FormatsCorrectly(string? value, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.FormatBiosDate(value));
    }

    [Theory]
    [InlineData(null, "NVMe", null, 0, "SSD")]
    [InlineData(null, "IDE", null, 0, null)]
    [InlineData(null, null, "Samsung SSD 870", 0, "SSD")]
    [InlineData(null, null, "WD Blue HDD", 0, null)]
    [InlineData(null, null, null, 7200, "HDD")]
    [InlineData(null, null, null, 1, "SSD")]
    [InlineData("Solid State Drive", null, null, 0, "SSD")]
    [InlineData("Fixed hard disk media", null, null, 0, null)]
    [InlineData("Hard Disk Drive", null, null, 0, "HDD")]
    [InlineData(null, null, "NVMe Controller", 0, "SSD")]
    [InlineData(null, null, null, 0, null)]
    [InlineData("Fixed hard disk media", "SCSI", "Samsung 990 PRO", 0, "SSD")]
    [InlineData("Fixed hard disk media", "IDE", null, 0, null)]
    [InlineData(null, "SCSI", null, 0, "SSD")]
    [InlineData(null, "USB", null, 0, null)]
    [InlineData(null, "1394", null, 0, null)]
    public void DetermineMediaType_DeterminesCorrectly(
        string? mediaType, string? interfaceType, string? model, long rotationRate, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.DetermineMediaType(mediaType, interfaceType, model, rotationRate));
    }

    [Theory]
    [InlineData("IDE", "IDE/PATA")]
    [InlineData("SCSI", "SCSI")]
    [InlineData("1394", "IEEE 1394")]
    [InlineData("NVMe", "NVMe")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void MapInterfaceType_MapsCorrectly(string? value, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.MapInterfaceType(value));
    }

    [Theory]
    [InlineData("PCI\\VEN_8086&DEV_A1B2&SUBSYS_12345678\\3&1234", null)]
    [InlineData("USBSTOR\\DISK&VEN_SAMSUNG&PROD_SSD", "USB")]
    [InlineData("SCSI\\DISK&VEN_WDC", "SCSI")]
    [InlineData("NVME\\VEN_8086", "NVMe")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("SOMETHING_ELSE", null)]
    public void InferDiskInterfaceFromPnpId_InfersCorrectly(string? pnpId, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.InferDiskInterfaceFromPnpId(pnpId));
    }

    [Theory]
    [InlineData(1_000_000_000, "1 Gbps")]
    [InlineData(10_000_000_000, "10 Gbps")]
    [InlineData(1_000_000, "1 Mbps")]
    [InlineData(100_000_000, "100 Mbps")]
    [InlineData(1_000, "1 Kbps")]
    [InlineData(500, "500 bps")]
    public void FormatNetworkSpeed_FormatsCorrectly(long bps, string expected)
    {
        Assert.Equal(expected, HardwareInfoService.FormatNetworkSpeed(bps));
    }

    [Theory]
    [InlineData("8", "DIMM")]
    [InlineData("DIMM", "DIMM")]
    [InlineData("12", "SO-DIMM")]
    [InlineData("SODIMM", "SO-DIMM")]
    [InlineData("13", "FB-DIMM")]
    [InlineData("Unknown", "Unknown")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void MapFormFactor_MapsCorrectly(string? value, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.MapFormFactor(value));
    }

    [Theory]
    [InlineData(1073741824, "1 GB")]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    public void FormatCapacity_FormatsCorrectly(long bytes, string? expected)
    {
        Assert.Equal(expected, HardwareInfoService.FormatCapacity(bytes));
    }

    [Fact]
    public void CoresThreadsLabel_HyperThreading_ShowsCoresAndThreads()
    {
        var cpuz = new CpuzInfo { CpuCores = 8, CpuThreads = 16 };
        Assert.Equal("8C/16T", HardwareInfoService.CoresThreadsLabel(cpuz));
    }

    [Fact]
    public void CoresThreadsLabel_NoHyperThreading_ShowsOnlyCores()
    {
        var cpuz = new CpuzInfo { CpuCores = 4, CpuThreads = 4 };
        Assert.Equal("4C", HardwareInfoService.CoresThreadsLabel(cpuz));
    }

    [Fact]
    public void CoresThreadsLabel_ZeroCores_ReturnsEmpty()
    {
        var cpuz = new CpuzInfo { CpuCores = 0, CpuThreads = 0 };
        Assert.Equal("", HardwareInfoService.CoresThreadsLabel(cpuz));
    }

    [Fact]
    public void BuildCpuzMemoryLabel_CombinesAllParts()
    {
        var cpuz = new CpuzInfo
        {
            MemoryType = "DDR5",
            MemorySize = "32768 MBytes",
            MemorySpeed = "4800 MHz"
        };
        var label = HardwareInfoService.BuildCpuzMemoryLabel(cpuz);
        Assert.Equal("DDR5 32768 MBytes 4800 MHz", label);
    }

    [Fact]
    public void BuildCpuzMemoryLabel_WithManufacturer_PrependsManufacturer()
    {
        var cpuz = new CpuzInfo
        {
            MemoryType = "DDR5",
            MemorySize = "32768 MBytes",
            MemDevices =
            [
                new CpuzMemDevice { Manufacturer = "KINGSTON" }
            ]
        };
        var label = HardwareInfoService.BuildCpuzMemoryLabel(cpuz);
        Assert.StartsWith("金士顿(Kingston)", label);
    }

    [Fact]
    public void BuildCpuzMemoryLabel_EmptyInfo_ReturnsEmpty()
    {
        var cpuz = new CpuzInfo();
        Assert.Equal("", HardwareInfoService.BuildCpuzMemoryLabel(cpuz));
    }

    [Fact]
    public void ComposeDetailRows_LoadedRowsFirstThenPendingPlaceholders()
    {
        var items = new HardwareInfoItem?[11];
        var completed = new bool[11];

        // 只完成「内存」和「主板」：已完成的按规范顺序在前，其余必备项以占位行排在末尾。
        items[2] = new HardwareInfoItem { Label = "内存", Value = "32 GB" };
        completed[2] = true;
        items[0] = new HardwareInfoItem { Label = "主板", Value = "B650" };
        completed[0] = true;

        var rows = HardwareInfoService.ComposeDetailRows(items, completed);

        Assert.Equal(new[] { "主板", "内存" }, rows.Take(2).Select(row => row.Label));
        Assert.All(rows.Skip(2), row => Assert.True(row.IsPending));
        // 可选行（NPU / 电池）还没结果时不生成占位行。
        Assert.DoesNotContain(rows, row => row.Label is "NPU" or "电池");
        // 未完成的必备项都在，且顺序与规范顺序一致。
        Assert.Equal(
            new[] { "处理器", "显卡", "显示器", "硬盘", "声卡", "网卡", "接口支持" },
            rows.Skip(2).Select(row => row.Label));
    }

    [Fact]
    public void ComposeDetailRows_AllCompleted_HasNoPendingRows()
    {
        var items = new HardwareInfoItem?[11];
        var completed = new bool[11];
        for (var i = 0; i < items.Length; i++)
        {
            completed[i] = true;
            // 可选行没有结果：直接不展示。
            if (i is 4 or 7) continue;
            items[i] = new HardwareInfoItem { Label = $"行{i}", Value = "v" };
        }

        var rows = HardwareInfoService.ComposeDetailRows(items, completed);

        Assert.Equal(9, rows.Count);
        Assert.DoesNotContain(rows, row => row.IsPending);
    }
}
