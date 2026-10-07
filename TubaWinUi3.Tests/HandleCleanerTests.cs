using System.Diagnostics;
using System.Text;
using TubaWinUi3.Services.HandleCleaner;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 「句柄清理」纯逻辑回归：安全判定（关键进程 / 删除态 / 路径兜底）、疑似泄漏分级、
/// 诊断报告与状态持久化。真实扫描（NtQuerySystemInformation）另有冒烟用例。
/// </summary>
public class HandleCleanerTests
{
    // ---------- 关键进程硬名单 ----------

    [Theory]
    [InlineData(0, "Idle", true)]
    [InlineData(4, "System", true)]
    [InlineData(632, "smss", true)]
    [InlineData(700, "csrss", true)]
    [InlineData(800, "lsass", true)]
    [InlineData(900, "services", true)]
    [InlineData(1000, "winlogon", true)]
    [InlineData(1100, "dwm", true)]
    [InlineData(1200, "MsMpEng", true)]
    public void IsCriticalProcess_BlocksHardListedProcesses(int pid, string name, bool expected)
        => Assert.Equal(expected, HandleCleanerPolicy.IsCriticalProcess(pid, name));

    [Theory]
    [InlineData(4321, "explorer")]
    [InlineData(5555, "chrome")]
    [InlineData(6666, "TubaWinUi3")]
    [InlineData(7777, "svchost")]
    public void IsCriticalProcess_AllowsOrdinaryProcesses(int pid, string name)
        => Assert.False(HandleCleanerPolicy.IsCriticalProcess(pid, name));

    [Fact]
    public void IsCriticalProcess_IsCaseInsensitive()
    {
        Assert.True(HandleCleanerPolicy.IsCriticalProcess(123, "LSASS"));
        Assert.True(HandleCleanerPolicy.IsCriticalProcess(123, "Msmpeng"));
    }

    // ---------- 判定：标准信息（DeletePending 主路径） ----------

    [Fact]
    public void DecideFromStandardInfo_DeletePending_IsCandidate()
        => Assert.Equal(HandleVerdict.Deleted, HandleCleanerPolicy.DecideFromStandardInfo(true, true));

    [Fact]
    public void DecideFromStandardInfo_NormalFile_IsNormal()
        => Assert.Equal(HandleVerdict.Normal, HandleCleanerPolicy.DecideFromStandardInfo(true, false));

    [Fact]
    public void DecideFromStandardInfo_Unavailable_IsUnknown()
        => Assert.Equal(HandleVerdict.Unknown, HandleCleanerPolicy.DecideFromStandardInfo(false, true));

    // ---------- 判定：路径兜底 ----------

    [Fact]
    public void DecideFromPath_DeletedPseudoPath_IsCandidate()
        => Assert.Equal(HandleVerdict.Deleted,
            HandleCleanerPolicy.DecideFromPath(@"C:\$Extend\$Deleted\0129000000003BFB", null));

    [Fact]
    public void DecideFromPath_MissingFile_IsCandidate()
        => Assert.Equal(HandleVerdict.Deleted,
            HandleCleanerPolicy.DecideFromPath(@"C:\Temp\gone.tmp", true));

    [Fact]
    public void DecideFromPath_ExistingFile_IsNormal()
        => Assert.Equal(HandleVerdict.Normal,
            HandleCleanerPolicy.DecideFromPath(@"C:\Temp\alive.tmp", false));

    [Fact]
    public void DecideFromPath_IndeterminateExistence_IsUnknown()
        => Assert.Equal(HandleVerdict.Unknown,
            HandleCleanerPolicy.DecideFromPath(@"C:\Temp\maybe.tmp", null));

    [Fact]
    public void DecideFromPath_UnresolvedPath_IsUnknown()
        => Assert.Equal(HandleVerdict.Unknown, HandleCleanerPolicy.DecideFromPath(null, true));

    [Fact]
    public void DecideFromPath_UnmappedDevicePath_IsUnknown()
        => Assert.Equal(HandleVerdict.Unknown,
            HandleCleanerPolicy.DecideFromPath(@"\Device\HarddiskVolume9\orphan.bin", true));

    [Fact]
    public void IsDeletedPseudoPath_IsCaseInsensitive()
    {
        Assert.True(HandleCleanerPolicy.IsDeletedPseudoPath(@"C:\$extend\$deleted\abc"));
        Assert.False(HandleCleanerPolicy.IsDeletedPseudoPath(@"C:\Temp\abc"));
    }

    // ---------- 疑似泄漏分级 ----------

    [Theory]
    [InlineData(0, HandleLeakLevel.Normal)]
    [InlineData(49_999, HandleLeakLevel.Normal)]
    [InlineData(50_000, HandleLeakLevel.Warning)]
    [InlineData(199_999, HandleLeakLevel.Warning)]
    [InlineData(200_000, HandleLeakLevel.Critical)]
    [InlineData(5_000_000, HandleLeakLevel.Critical)]
    public void ClassifyLeakLevel_ThresholdBoundaries(int handles, HandleLeakLevel expected)
        => Assert.Equal(expected, HandleCleanerPolicy.ClassifyLeakLevel(handles));

    // ---------- 类型显示名 ----------

    [Theory]
    [InlineData("File")]
    [InlineData("Event")]
    [InlineData("Mutant")]
    [InlineData("Semaphore")]
    [InlineData("Section")]
    [InlineData("Key")]
    [InlineData("Process")]
    [InlineData("Thread")]
    [InlineData("Token")]
    [InlineData("Timer")]
    [InlineData("IoCompletion")]
    public void TypeLabel_KnownTypes_AreLocalized(string kernelName)
    {
        var label = HandleCleanerPolicy.TypeLabel(kernelName);
        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.NotEqual(kernelName, label);
    }

    [Fact]
    public void TypeLabel_UnknownType_FallsBackToOther()
    {
        var other = HandleCleanerPolicy.TypeLabel("Other");
        Assert.Equal(other, HandleCleanerPolicy.TypeLabel(null));
        Assert.Equal(other, HandleCleanerPolicy.TypeLabel("WaitCompletionPacket"));
    }

    // ---------- 扫描结果聚合 ----------

    [Fact]
    public void CandidateScanResult_AggregatesCounts()
    {
        var result = new CandidateScanResult
        {
            Groups =
            [
                new CandidateProcessGroup
                {
                    ProcessId = 10,
                    Name = "a",
                    Handles = [new CandidateHandle { HandleValue = 1 }, new CandidateHandle { HandleValue = 2 }],
                    UnknownCount = 3,
                },
                new CandidateProcessGroup
                {
                    ProcessId = 20,
                    Name = "b",
                    Handles = [new CandidateHandle { HandleValue = 3 }],
                },
                new CandidateProcessGroup
                {
                    ProcessId = 30,
                    Name = "c",
                    UnknownCount = 1,
                },
            ],
        };

        Assert.Equal(3, result.TotalCandidates);
        Assert.Equal(4, result.TotalUnknown);
        Assert.Equal(2, result.AffectedProcessCount);
    }

    // ---------- 诊断报告 ----------

    [Fact]
    public void BuildReport_ContainsOverviewAndScanFacts()
    {
        var overview = new HandleCleanerOverview
        {
            TotalHandles = 1_234_567,
            ProcessCount = 321,
            SuspectedLeakCount = 2,
            FileHandles = 45_678,
            Processes =
            [
                new ProcessHandleSummary
                {
                    ProcessId = 4321,
                    Name = "leaky-app",
                    TotalHandles = 98_765,
                    FileHandles = 12_345,
                },
            ],
        };

        var scan = new CandidateScanResult
        {
            ScannedFileHandles = 20_000,
            SkippedProcesses = 3,
            Groups =
            [
                new CandidateProcessGroup
                {
                    ProcessId = 4321,
                    Name = "leaky-app",
                    Handles = [new CandidateHandle { HandleValue = 1 }],
                },
            ],
        };

        var report = HandleCleanerService.BuildReport(overview, scan, isAdmin: true);

        Assert.Contains("1234567", report);
        Assert.Contains("leaky-app", report);
        Assert.Contains("4321", report);
        Assert.Contains("98765", report);
        Assert.Contains("20000", report);
    }

    [Fact]
    public void BuildReport_WithoutScan_MentionsNoScan()
    {
        var report = HandleCleanerService.BuildReport(new HandleCleanerOverview(), null, isAdmin: false);
        Assert.False(string.IsNullOrWhiteSpace(report));
    }

    // ---------- 状态持久化 ----------

    [Fact]
    public void State_SaveAndLoad_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TubaHandleCleanerTests_" + Guid.NewGuid().ToString("N"));
        HandleCleanerState.DataDirOverride = dir;
        try
        {
            var state = new HandleCleanerState
            {
                LastCleanUtc = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc),
                LastFreedHandles = 42,
                LastProcessCount = 3,
            };
            state.Save();

            var loaded = HandleCleanerState.Load();
            Assert.NotNull(loaded.LastCleanUtc);
            Assert.Equal(42, loaded.LastFreedHandles);
            Assert.Equal(3, loaded.LastProcessCount);
        }
        finally
        {
            HandleCleanerState.DataDirOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch { /* 清理失败不影响断言 */ }
        }
    }

    [Fact]
    public void State_ParseGarbage_ReturnsDefaults()
    {
        var state = HandleCleanerState.Parse("not json at all");
        Assert.Null(state.LastCleanUtc);
        Assert.Equal(0, state.LastFreedHandles);
    }

    // ---------- 真实句柄表冒烟 ----------

    [Fact]
    public void ScanOverview_ReadsRealHandleTable()
    {
        var overview = HandleCleanerService.ScanOverview(CancellationToken.None);

        Assert.True(overview.TotalHandles > 0, "系统句柄总数应大于 0");
        Assert.True(overview.ProcessCount > 0, "至少应看到一个进程");
        Assert.All(overview.Processes, p => Assert.True(p.TotalHandles > 0));
    }

    [Fact]
    public void ScanOverview_CanceledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => HandleCleanerService.ScanOverview(cts.Token));
    }

    // ---------- 深度模式：风险分级 / 访问权限 / 复核决策 ----------

    [Theory]
    [InlineData("File", true, HandleRisk.Low)]
    [InlineData("File", false, HandleRisk.Medium)]
    [InlineData("File", null, HandleRisk.Medium)]
    [InlineData("Event", null, HandleRisk.High)]
    [InlineData("Mutant", null, HandleRisk.High)]
    [InlineData("Semaphore", null, HandleRisk.High)]
    [InlineData("Key", null, HandleRisk.Medium)]
    [InlineData("Section", null, HandleRisk.Medium)]
    [InlineData("WaitCompletionPacket", null, HandleRisk.Medium)]
    public void ClassifyRisk_ByTypeAndDeletionState(string type, bool? deleted, HandleRisk expected)
        => Assert.Equal(expected, HandleCleanerPolicy.ClassifyRisk(type, deleted));

    [Fact]
    public void DescribeHandleAccess_DecodesFileBits()
    {
        // 0x0012019F = GENERIC_READ|GENERIC_WRITE|SYNCHRONIZE（读写打开的常见取值）
        var text = HandleCleanerPolicy.DescribeHandleAccess("File", 0x0012019F);
        Assert.Contains("READ", text);
        Assert.Contains("WRITE", text);
        Assert.Contains("SYNCHRONIZE", text);
        Assert.Contains("0x", text);   // 未识别位保留十六进制
    }

    [Fact]
    public void DescribeHandleAccess_NonFileTypes_StayHex()
        => Assert.Equal("0x001F0003", HandleCleanerPolicy.DescribeHandleAccess("Event", 0x001F0003));

    [Fact]
    public void DecideForceClose_CoversRecyclingCases()
    {
        // 句柄值已不在句柄表：已自行释放
        Assert.Equal(ForceCloseDecision.Gone, HandleCleanerPolicy.DecideForceClose(38, null, null, null));
        // 类型对不上：已被回收成别的对象
        Assert.Equal(ForceCloseDecision.Recycled, HandleCleanerPolicy.DecideForceClose(38, 15, null, null));
        // 类型一致、无路径（非文件）：放行
        Assert.Equal(ForceCloseDecision.Allow, HandleCleanerPolicy.DecideForceClose(38, 38, null, null));
        // 文件：路径一致（忽略大小写）放行；不一致跳过；解析失败保守跳过
        Assert.Equal(ForceCloseDecision.Allow, HandleCleanerPolicy.DecideForceClose(38, 38, @"C:\a.txt", @"c:\A.TXT"));
        Assert.Equal(ForceCloseDecision.Recycled, HandleCleanerPolicy.DecideForceClose(38, 38, @"C:\a.txt", @"C:\b.txt"));
        Assert.Equal(ForceCloseDecision.Unverifiable, HandleCleanerPolicy.DecideForceClose(38, 38, @"C:\a.txt", null));
    }

    [Fact]
    public void IsForceCloseTargetAllowed_BlocksCriticalAndSelf()
    {
        Assert.False(HandleCleanerPolicy.IsForceCloseTargetAllowed(4, "System"));
        Assert.False(HandleCleanerPolicy.IsForceCloseTargetAllowed(700, "csrss"));
        Assert.False(HandleCleanerPolicy.IsForceCloseTargetAllowed(Environment.ProcessId, "TubaWinUi3"));
        Assert.True(HandleCleanerPolicy.IsForceCloseTargetAllowed(4321, "explorer"));
    }

    // ---------- 端到端：子进程持有「已删除文件的句柄」→ 检出 → 释放 ----------

    [Fact]
    public async Task ScanCandidates_DetectsAndCleansDeletedFileHandleInChildProcess()
    {
        var readyPath = Path.Combine(Path.GetTempPath(), "hc_ready_" + Guid.NewGuid().ToString("N") + ".txt");
        var script = $"""
            $p = Join-Path $env:TEMP 'hc_probe_{Guid.NewGuid():N}.txt'
            Set-Content -LiteralPath $p -Value 'x'
            $fs = [System.IO.File]::Open($p, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, ([System.IO.FileShare]::Delete))
            Remove-Item -LiteralPath $p
            Set-Content -LiteralPath '{readyPath}' -Value 'ready'
            Start-Sleep -Seconds 120
            $fs.Dispose()
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        using var child = Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        try
        {
            // 等子进程把「已删除但仍被持有」的句柄准备好（它写好就绪文件后才开始 sleep）。
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!File.Exists(readyPath) && DateTime.UtcNow < deadline)
                await Task.Delay(200);
            Assert.True(File.Exists(readyPath), "子进程未能在超时内就绪");
            await Task.Delay(300);

            var scan = await HandleCleanerService.ScanCandidatesAsync(child.Id, CancellationToken.None);
            Assert.True(scan.TotalCandidates >= 1, "应至少检出一个指向已删除文件的句柄");

            var outcome = await HandleCleanerService.CleanAsync(scan.Groups);
            Assert.True(outcome.Freed >= 1, $"应至少释放一个句柄（实际 {outcome.Freed}）");

            var rescan = await HandleCleanerService.ScanCandidatesAsync(child.Id, CancellationToken.None);
            Assert.True(rescan.TotalCandidates < scan.TotalCandidates, "清理后该进程的失效句柄数应减少");
        }
        finally
        {
            try { child.Kill(entireProcessTree: true); } catch { /* 子进程可能已退出 */ }
            try { File.Delete(readyPath); } catch { /* 清理失败不影响断言 */ }
        }
    }

    // ---------- 端到端：深度模式（枚举 / 按路径查找 / 强制关闭既存文件句柄） ----------

    [Fact]
    public async Task DeepCleanup_EnumeratesAndForceClosesExistingFileHandleInChildProcess()
    {
        var readyPath = Path.Combine(Path.GetTempPath(), "hc_deep_ready_" + Guid.NewGuid().ToString("N") + ".txt");
        var targetPath = Path.Combine(Path.GetTempPath(), "hc_deep_target_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(targetPath, "locked");

        // 子进程打开（并持有）一个**存在**的文件；FILE_SHARE_DELETE 仅用于兼容，不删除它。
        var script = $"""
            $fs = [System.IO.File]::Open('{targetPath}', [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
            Set-Content -LiteralPath '{readyPath}' -Value 'ready'
            Start-Sleep -Seconds 120
            $fs.Dispose()
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        using var child = Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!File.Exists(readyPath) && DateTime.UtcNow < deadline)
                await Task.Delay(200);
            Assert.True(File.Exists(readyPath), "子进程未能在超时内就绪");
            await Task.Delay(300);

            // 1) 句柄浏览器枚举：应能看到子进程持有的该文件句柄（File 类型，路径能对上）。
            var list = await HandleCleanerService.EnumerateHandlesAsync(child.Id, null, CancellationToken.None);
            Assert.Contains(list.Handles, h => h.TypeName == "File"
                && h.ObjectName is not null
                && h.ObjectName.Contains(Path.GetFileName(targetPath), StringComparison.OrdinalIgnoreCase));

            // 2) 按路径查找占用：应至少找到子进程的一个句柄。
            var scan = await HandleCleanerService.FindHandlesForPathAsync(targetPath, child.Id, CancellationToken.None);
            Assert.Equal(PathHandleScanError.None, scan.Error);
            Assert.True(scan.TotalHandles >= 1, "应至少找到一个持有该路径的句柄");

            // 3) 强制关闭：释放句柄 → 复扫不再找到；文件本身保留。
            var groups = scan.Groups.Select(g => new ForceCloseGroup
            {
                ProcessId = g.ProcessId,
                Name = g.Name,
                ImagePath = g.ImagePath,
                StartTimeUtc = g.StartTimeUtc,
                Handles = g.Handles.Select(h => new ForceCloseHandleRequest
                {
                    HandleValue = h.HandleValue,
                    ExpectedTypeIndex = h.TypeIndex,
                    IsFile = true,
                    ExpectedPath = h.DisplayPath.Length > 0 ? h.DisplayPath : null,
                }).ToList(),
            }).ToList();

            var outcome = await HandleCleanerService.ForceCloseAsync(groups, CancellationToken.None);
            Assert.True(outcome.Freed >= 1, $"应至少强制关闭一个句柄（实际 {outcome.Freed}）");

            var rescan = await HandleCleanerService.FindHandlesForPathAsync(targetPath, child.Id, CancellationToken.None);
            Assert.Equal(0, rescan.TotalHandles);
            Assert.True(File.Exists(targetPath), "文件本身不应被删除");
        }
        finally
        {
            try { child.Kill(entireProcessTree: true); } catch { /* 子进程可能已退出 */ }
            try { File.Delete(readyPath); } catch { /* 清理失败不影响断言 */ }
            try { File.Delete(targetPath); } catch { /* 清理失败不影响断言 */ }
        }
    }
}
