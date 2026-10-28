using System.IO;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class DiskSpdResultParserTests
{
    private static string Output(string marker = "", double seconds = 3) => $"""
        actual test time: {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}s
        thread count: 1
        Total IO
        thread | bytes | I/Os | MiB/s | I/O per s | file
        total: 898588672 | 219382 | 285.66 | 73128.00
        Read IO
        thread | bytes | I/Os | MiB/s | I/O per s | file
        total: 898588672 | 219382 | 285.66 | 73128.00
        Write IO
        thread | bytes | I/Os | MiB/s | I/O per s | file
        total: 0 | 0 | 0.00 | 0.00
        {marker}
        """;

    [Fact]
    public void UpstreamZeroExit_WithCompleteTables_IsAccepted()
        => DiskSpdResultParser.ValidateCompletion(Output(), "", 0, 3);

    [Theory]
    [InlineData(50930810u)]
    [InlineData(3000000000u)]
    public void CrystalDiskMarkScoreExit_WithMatchingMarker_IsAccepted(uint score)
        => DiskSpdResultParser.ValidateCompletion(Output($"Score: {score}"), "", unchecked((int)score), 3);

    [Fact]
    public void WarningAboutAdminPrivileges_DoesNotInvalidateCompletedMeasurement()
        => DiskSpdResultParser.ValidateCompletion(Output("Score: 50930810"), "WARNING: Could not set privileges", 50930810, 3);

    [Theory]
    [InlineData(1)]
    [InlineData(-1073741510)]
    public void ErrorOrInterruptedExit_IsRejectedEvenWithTables(int code)
        => Assert.Throws<InvalidDataException>(() => DiskSpdResultParser.ValidateCompletion(Output("Score: 50930810"), "error", code, 3));

    [Fact]
    public void ScoreExitWithoutCompletionMarker_IsRejected()
        => Assert.Throws<InvalidDataException>(() => DiskSpdResultParser.ValidateCompletion(Output(), "", 50930810, 3));

    [Fact]
    public void TooShortMeasurement_IsRejectedEvenIfScoreMatchesExit()
        => Assert.Throws<InvalidDataException>(() => DiskSpdResultParser.ValidateCompletion(Output("Score: 50930810", 1), "", 50930810, 3));

    [Fact]
    public void MissingSummary_IsRejectedEvenWithNormalExit()
        => Assert.Throws<InvalidDataException>(() => DiskSpdResultParser.ValidateCompletion(Output().Replace("Write IO", "incomplete"), "", 0, 3));

    [Fact]
    public void ReadMeasurements_KeepOriginalValuesAndUnits()
    {
        Assert.Equal(285.66, DiskSpdResultParser.Measurement(Output(), write: false, iops: false));
        Assert.Equal(73128.00, DiskSpdResultParser.Measurement(Output(), write: false, iops: true));
    }

    [Fact]
    public void WindowsLineEndings_AreAccepted()
    {
        string output = Output("Score: 50930810").Replace("\r\n", "\n").Replace("\n", "\r\n");
        DiskSpdResultParser.ValidateCompletion(output, "", 50930810, 3);
        Assert.Equal(73128, DiskSpdResultParser.Measurement(output, write: false, iops: true));
    }

    [Fact]
    public void WriteMeasurements_UseWriteSummary()
    {
        string output = Output().Replace("Read IO", "Swap IO").Replace("Write IO", "Read IO").Replace("Swap IO", "Write IO");
        Assert.Equal(285.66, DiskSpdResultParser.Measurement(output, write: true, iops: false));
        Assert.Equal(73128, DiskSpdResultParser.Measurement(output, write: true, iops: true));
    }

    [Fact]
    public void ZeroRequestedMeasurement_IsRejected()
        => Assert.Throws<InvalidDataException>(() => DiskSpdResultParser.Measurement(Output(), write: true, iops: false));

    [Fact]
    public void MissingReadSummary_DoesNotFallBackToAnotherTable()
        => Assert.Throws<InvalidDataException>(() => DiskSpdResultParser.Measurement(Output().Replace("Read IO", "incomplete"), write: false, iops: false));
}
