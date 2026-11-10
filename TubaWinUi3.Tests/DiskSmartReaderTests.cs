using System;
using System.Collections.Generic;
using System.Linq;
using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>SMART 解析与健康判定（CrystalDiskInfo CheckDiskStatus 移植）单元测试。</summary>
public class DiskSmartReaderTests
{
    private static byte[] MakeAtaBuffer(params (byte Id, byte Current, byte Worst, byte[] Raw)[] attrs)
    {
        var buf = new byte[512];
        buf[0] = 0x0A; // revision 低字节 (0x0C0A)
        buf[1] = 0x0C;
        for (var i = 0; i < attrs.Length; i++)
        {
            var off = 2 + i * 12;
            buf[off] = attrs[i].Id;
            buf[off + 1] = 0x02; // status flags (LE)
            buf[off + 2] = 0x00;
            buf[off + 3] = attrs[i].Current;
            buf[off + 4] = attrs[i].Worst;
            for (var j = 0; j < 6; j++)
                buf[off + 5 + j] = attrs[i].Raw[j];
        }
        return buf;
    }

    private static byte[] MakeThresholdBuffer(params (byte Id, byte Threshold)[] ths)
    {
        var buf = new byte[512];
        buf[0] = 0x0A;
        buf[1] = 0x0C;
        for (var i = 0; i < ths.Length; i++)
        {
            var off = 2 + i * 12;
            buf[off] = ths[i].Id;
            buf[off + 1] = ths[i].Threshold;
        }
        return buf;
    }

    // ─── 解析层 ───

    [Theory]
    [InlineData(42, 18, 33, 42)]
    [InlineData(0, 0, 34, 34)]
    [InlineData(255, 0, 35, 35)]
    [InlineData(0, 0, 0, null)]
    public void AtaTemperature_UsesCurrentByteAndFallsBackToAirflow(byte current, byte extra, byte airflow, int? expected)
    {
        var attrs = DiskSmartReader.ParseAttributes(MakeAtaBuffer(
            (0xC2, 100, 100, [current, extra, 0, 0, 0, 0]),
            (0xBE, 100, 100, [airflow, 0, 0, 0, 0, 0])));
        Assert.Equal(expected, DiskSmartReader.ParseAtaTemperature(attrs));
    }

    [Fact]
    public void ParseAttributes_ReadsFieldsAndSkipsZeroIds()
    {
        var raw = MakeAtaBuffer(
            (0x09, 100, 90, [0x34, 0x12, 0, 0, 1, 2]),
            (0, 0, 0, new byte[6]));
        var attrs = DiskSmartReader.ParseAttributes(raw);

        var attr = Assert.Single(attrs);
        Assert.Equal(0x09, attr.Id);
        Assert.Equal(100, attr.CurrentValue);
        Assert.Equal(90, attr.WorstValue);
        Assert.Equal(0x1234, attr.Raw16);
        Assert.Equal(new byte[] { 0x34, 0x12, 0, 0, 1, 2 }, attr.RawValue);
    }

    [Fact]
    public void ParseThresholds_ReadsThresholdValues()
    {
        var raw = MakeThresholdBuffer((0x05, 3), (0xC5, 0));
        var ths = DiskSmartReader.ParseThresholds(raw);

        Assert.Equal(2, ths.Count);
        Assert.Equal(3, ths[0].ThresholdValue);
        Assert.Equal(0, ths[1].ThresholdValue);
    }

    // ─── ATA 判定 ───

    [Fact]
    public void CheckAtaStatus_HealthySsd_FromLifeAttribute()
    {
        var attrs = DiskSmartReader.ParseAttributes(MakeAtaBuffer((0xE9, 95, 90, [1, 2, 3, 0, 0, 0])));
        var ths = DiskSmartReader.ParseThresholds(MakeThresholdBuffer());

        var (status, life) = DiskSmartReader.CheckAtaStatus(attrs, ths, isSsd: true, isThresholdCorrect: false, thresholdFf: 10);

        Assert.Equal(DiskStatus.Good, status);
        Assert.Equal(95, life);
    }

    [Fact]
    public void CheckAtaStatus_LowLife_IsCaution()
    {
        var attrs = DiskSmartReader.ParseAttributes(MakeAtaBuffer((0xE9, 5, 5, [0, 0, 0, 0, 0, 0])));
        var (status, life) = DiskSmartReader.CheckAtaStatus(attrs, [], isSsd: true, isThresholdCorrect: false, thresholdFf: 10);

        Assert.Equal(DiskStatus.Caution, status);
        Assert.Equal(5, life);
    }

    [Fact]
    public void CheckAtaStatus_ZeroLife_IsBad()
    {
        var attrs = DiskSmartReader.ParseAttributes(MakeAtaBuffer((0xE9, 0, 0, [0, 0, 0, 0, 0, 0])));
        var (status, _) = DiskSmartReader.CheckAtaStatus(attrs, [], isSsd: true, isThresholdCorrect: false, thresholdFf: 10);

        Assert.Equal(DiskStatus.Bad, status);
    }

    [Fact]
    public void CheckAtaStatus_HddWithoutThreshold_IsUnknown()
    {
        var attrs = DiskSmartReader.ParseAttributes(MakeAtaBuffer((0x05, 200, 200, new byte[6])));
        var (status, _) = DiskSmartReader.CheckAtaStatus(attrs, [], isSsd: false, isThresholdCorrect: false, thresholdFf: 10);

        Assert.Equal(DiskStatus.Unknown, status);
    }

    [Fact]
    public void CheckAtaStatus_HddCriticalCurrentBelowThreshold_IsBad()
    {
        var attrs = DiskSmartReader.ParseAttributes(MakeAtaBuffer((0x05, 50, 40, new byte[6])));
        var ths = DiskSmartReader.ParseThresholds(MakeThresholdBuffer((0x05, 100)));

        var (status, _) = DiskSmartReader.CheckAtaStatus(attrs, ths, isSsd: false, isThresholdCorrect: true, thresholdFf: 10);

        Assert.Equal(DiskStatus.Bad, status);
    }

    [Fact]
    public void CheckAtaStatus_HddBadSectorRaw_IsCaution()
    {
        // 0x05 原始值低 4 字节 = 1（≥ 阈值 1）→ 机械盘报 Caution
        var attrs = DiskSmartReader.ParseAttributes(MakeAtaBuffer((0x05, 200, 200, [1, 0, 0, 0, 0, 0])));
        var ths = DiskSmartReader.ParseThresholds(MakeThresholdBuffer((0x05, 100)));

        var (status, _) = DiskSmartReader.CheckAtaStatus(attrs, ths, isSsd: false, isThresholdCorrect: true, thresholdFf: 10);

        Assert.Equal(DiskStatus.Caution, status);
    }

    [Fact]
    public void CheckAtaStatus_HddHealthy_IsGood()
    {
        var attrs = DiskSmartReader.ParseAttributes(MakeAtaBuffer((0x05, 200, 200, [0, 0, 0, 0, 0, 0])));
        var ths = DiskSmartReader.ParseThresholds(MakeThresholdBuffer((0x05, 100)));

        var (status, _) = DiskSmartReader.CheckAtaStatus(attrs, ths, isSsd: false, isThresholdCorrect: true, thresholdFf: 10);

        Assert.Equal(DiskStatus.Good, status);
    }

    [Fact]
    public void CheckAtaStatus_DuplicateAttributeId_IsUnknown()
    {
        var attrs = DiskSmartReader.ParseAttributes(MakeAtaBuffer(
            (0x07, 100, 100, new byte[6]),
            (0x07, 100, 100, new byte[6])));
        var (status, _) = DiskSmartReader.CheckAtaStatus(attrs, [], isSsd: true, isThresholdCorrect: false, thresholdFf: 10);

        Assert.Equal(DiskStatus.Unknown, status);
    }

    [Fact]
    public void ComputeLife_WdcRawValue_SpecialCase()
    {
        var raw = MakeAtaBuffer((0xE6, 100, 100, [0, 3, 0, 0, 0, 0]));
        var attrs = DiskSmartReader.ParseAttributes(raw);

        Assert.Equal(97, DiskSmartReader.ComputeLife(attrs[0]));
    }

    // ─── NVMe 判定 ───

    [Fact]
    public void CheckNvmeStatus_CriticalWarning_IsBad()
    {
        var raw = new byte[512];
        raw[0] = 0x01; // Critical Warning
        var attrs = ParseNvmeAttrsForTest(raw);

        var status = DiskSmartReader.CheckNvmeStatus(attrs, 95, "Samsung NVMe", 10);

        Assert.Equal(DiskStatus.Bad, status);
    }

    [Fact]
    public void CheckNvmeStatus_SpareBelowThreshold_IsBad()
    {
        var raw = new byte[512];
        raw[3] = 5;   // Available Spare
        raw[4] = 10;  // Spare Threshold
        var attrs = ParseNvmeAttrsForTest(raw);

        var status = DiskSmartReader.CheckNvmeStatus(attrs, 95, "Samsung NVMe", 10);

        Assert.Equal(DiskStatus.Bad, status);
    }

    [Fact]
    public void CheckNvmeStatus_SpareEqualsThreshold_IsCaution()
    {
        var raw = new byte[512];
        raw[3] = 10;
        raw[4] = 10;
        var attrs = ParseNvmeAttrsForTest(raw);

        var status = DiskSmartReader.CheckNvmeStatus(attrs, 95, "Samsung NVMe", 10);

        Assert.Equal(DiskStatus.Caution, status);
    }

    [Fact]
    public void CheckNvmeStatus_LowLife_IsCaution()
    {
        var raw = new byte[512];
        raw[3] = 100;
        raw[4] = 10;
        var attrs = ParseNvmeAttrsForTest(raw);

        var status = DiskSmartReader.CheckNvmeStatus(attrs, 8, "Samsung NVMe", 10);

        Assert.Equal(DiskStatus.Caution, status);
    }

    [Fact]
    public void CheckNvmeStatus_Healthy_IsGood()
    {
        var raw = new byte[512];
        raw[3] = 100;
        raw[4] = 10;
        var attrs = ParseNvmeAttrsForTest(raw);

        var status = DiskSmartReader.CheckNvmeStatus(attrs, 95, "Samsung NVMe", 10);

        Assert.Equal(DiskStatus.Good, status);
    }

    [Fact]
    public void CheckNvmeStatus_VirtualMachine_IsUnknown()
    {
        var raw = new byte[512];
        raw[3] = 100;
        raw[4] = 10;
        var attrs = ParseNvmeAttrsForTest(raw);

        var status = DiskSmartReader.CheckNvmeStatus(attrs, 95, "QEMU NVMe Controller", 10);

        Assert.Equal(DiskStatus.Unknown, status);
    }

    private static List<DiskSmartReader.SmartAttribute> ParseNvmeAttrsForTest(byte[] raw)
    {
        // 对应 DiskSmartReader 的 NVMe 属性映射：Id=1 CriticalWarning / Id=3 Spare / Id=4 Threshold
        return
        [
            new DiskSmartReader.SmartAttribute(1, 0, 0, 0, [raw[0], 0, 0, 0, 0, 0]),
            new DiskSmartReader.SmartAttribute(3, 0, 0, 0, [raw[3], 0, 0, 0, 0, 0]),
            new DiskSmartReader.SmartAttribute(4, 0, 0, 0, [raw[4], 0, 0, 0, 0, 0]),
        ];
    }

    // ─── 关键属性范围 ───

    [Theory]
    [InlineData(0x01)]  // Raw Read Error Rate
    [InlineData(0x0D)]  // 边界值
    [InlineData(0xC5)]  // Current Pending Sector
    [InlineData(0xE7)]  // SSD Wear Leveling
    public void IsCriticalId_CommonIdsReturnTrue(byte id) => Assert.True(DiskSmartReader.IsCriticalId(id));

    [Theory]
    [InlineData(0x11)]  // 非关键中间区间
    [InlineData(0x99)]  // 非关键
    public void IsCriticalId_MidRangeReturnsFalse(byte id) => Assert.False(DiskSmartReader.IsCriticalId(id));
}