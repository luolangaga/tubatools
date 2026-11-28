using System.Text.Json;

namespace Tubatools.MacOSInstaller.Core;

internal sealed class UsbWriteResult
{
    public bool Success { get; set; }
    public string VolumeRoot { get; set; } = "";
    public string Error { get; set; } = "";
    public string LogPath { get; set; } = "";
    public int ExitCode { get; set; }
}

internal static class UsbService
{
    private const long EfiOnlyMinimum = 2L * 1024 * 1024 * 1024;
    private const long PackingSlack = 64L * 1024 * 1024;
    private const int Fat32MaxMb = 32768;

    private const string ListScript = """
        $ErrorActionPreference = 'SilentlyContinue'
        [Console]::OutputEncoding = [Text.Encoding]::UTF8
        $d = @(Get-CimInstance -Namespace root/Microsoft/Windows/Storage -ClassName MSFT_Disk |
            Where-Object { $_.BusType -eq 7 } |
            Select-Object Number,FriendlyName,SerialNumber,UniqueId,
                @{n='Size';e={[uint64]$_.Size}},IsBoot,IsSystem,IsReadOnly,IsOffline,PartitionStyle)
        if ($d.Count -eq 0) {
            $d = @(Get-CimInstance Win32_DiskDrive | Where-Object { $_.InterfaceType -eq 'USB' } | ForEach-Object {
                [pscustomobject]@{ Number = [int]$_.Index; FriendlyName = $_.Model; SerialNumber = $_.SerialNumber;
                    UniqueId = $_.PNPDeviceID; Size = [uint64]$_.Size; IsBoot = $false; IsSystem = $false;
                    IsReadOnly = $false; IsOffline = $false; PartitionStyle = '' }
            })
        }
        if ($d.Count -eq 0) { '[]' } else { @($d) | ConvertTo-Json -Depth 4 -Compress }
        """;

    public static async Task<List<UsbDisk>> ListAsync(ProcessRunner runner, Action<string> log, CancellationToken ct)
    {
        var result = await runner.RunPowerShellScriptAsync(ListScript, "usb-list", requireAdmin: false, killOnCancel: true, log, ct);
        if (result.Canceled) throw new OperationCanceledException(ct);
        if (!result.Success)
            throw new InvalidOperationException("枚举 USB 磁盘失败：" + HardwareScanner.FirstError(result));

        string json = HardwareScanner.ExtractJson(result.Output);
        using var doc = JsonDocument.Parse(json);
        var list = new List<UsbDisk>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in doc.RootElement.EnumerateArray())
                Add(list, e);
        }
        else
        {
            Add(list, doc.RootElement);
        }
        return list;
    }

    private static void Add(List<UsbDisk> list, JsonElement e)
    {
        string name = e.TryGetProperty("FriendlyName", out var n) ? n.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(name)) return;
        list.Add(new UsbDisk
        {
            Number = e.TryGetProperty("Number", out var num) ? num.GetInt32() : 0,
            Name = name.Trim(),
            Serial = e.TryGetProperty("SerialNumber", out var sn) ? sn.GetString()?.Trim() ?? "" : "",
            UniqueId = e.TryGetProperty("UniqueId", out var uid) ? uid.GetString()?.Trim() ?? "" : "",
            Size = e.TryGetProperty("Size", out var size) && size.ValueKind == JsonValueKind.Number ? size.GetUInt64() : 0,
            IsBoot = e.TryGetProperty("IsBoot", out var b) && b.ValueKind == JsonValueKind.True,
            IsSystem = e.TryGetProperty("IsSystem", out var s) && s.ValueKind == JsonValueKind.True,
            IsReadOnly = e.TryGetProperty("IsReadOnly", out var ro) && ro.ValueKind == JsonValueKind.True,
            IsOffline = e.TryGetProperty("IsOffline", out var off) && off.ValueKind == JsonValueKind.True,
            PartitionStyle = e.TryGetProperty("PartitionStyle", out var ps) ? ps.GetString() ?? "" : ""
        });
    }

    public static async Task<UsbWriteResult> WriteAsync(
        ProcessRunner runner,
        UsbDisk disk,
        string efiDirectory,
        string recoveryDirectory,
        string dataDirectory,
        Action<string> log,
        Action<string, double?> progress,
        CancellationToken ct)
    {
        var outcome = new UsbWriteResult();
        string? scriptPath = null;
        try
        {
            ValidateEfiDirectory(efiDirectory);
            ValidateRecoveryDirectory(recoveryDirectory);
            if (disk.Size < EfiOnlyMinimum)
                throw new InvalidOperationException("U 盘小于 2 GB，无法制作安装盘。");
            if (disk.IsBoot || disk.IsSystem)
                throw new InvalidOperationException("这块磁盘是系统盘或启动盘，已拒绝写入。");
            if (disk.IsReadOnly)
                throw new InvalidOperationException("这块磁盘是只读的，请先清除只读属性。");
            if (disk.IsOffline)
                throw new InvalidOperationException("这块磁盘处于离线状态，请先在磁盘管理中联机。");

            long requiredBytes = DirectorySize(efiDirectory)
                + (string.IsNullOrWhiteSpace(recoveryDirectory) ? 0 : DirectorySize(recoveryDirectory))
                + PackingSlack;
            long availableMb = (long)disk.Size / (1024 * 1024) - 64;
            int partitionMb = (int)Math.Min(Fat32MaxMb, availableMb);
            long partitionBytes = (long)partitionMb * 1024 * 1024;
            if (partitionMb < 1800 || partitionBytes < requiredBytes)
                throw new InvalidOperationException(
                    $"U 盘空间不足：需要约 {FormatSize(requiredBytes)}，这块盘最多只能提供 {FormatSize(partitionBytes)} 的 FAT32 分区。");

            progress("重新核对 U 盘身份…", null);
            List<UsbDisk> current = await ListAsync(runner, log, ct);
            UsbDisk? found = current.FirstOrDefault(d => d.Number == disk.Number);
            if (found is null)
                throw new InvalidOperationException("磁盘编号已经变化，请刷新 U 盘列表后重试。");
            if (found.Size != disk.Size
                || (!string.IsNullOrEmpty(disk.UniqueId) && found.UniqueId != disk.UniqueId)
                || (!string.IsNullOrEmpty(disk.Serial) && found.Serial != disk.Serial))
            {
                throw new InvalidOperationException("U 盘身份与选择时不一致（容量或序列号已变化），为安全起见已停止。");
            }

            string letter = FindFreeLetter();
            scriptPath = Path.Combine(Path.GetTempPath(), "TubatoolsMacOS", Guid.NewGuid().ToString("N") + ".txt");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            string diskpart = $"""
                select disk {disk.Number}
                attributes disk clear readonly noerr
                clean
                convert gpt
                create partition primary size={partitionMb}
                format fs=fat32 quick label=OCUSB
                assign letter={letter}
                exit
                """;
            await File.WriteAllTextAsync(scriptPath, diskpart, new System.Text.ASCIIEncoding(), ct);

            string ps = $"""
                $ErrorActionPreference = 'Continue'
                [Console]::OutputEncoding = [Text.Encoding]::UTF8
                chcp 65001 > $null
                $p = Start-Process -FilePath 'diskpart.exe' -ArgumentList '/s "{scriptPath}"' -NoNewWindow -Wait -PassThru
                Write-Output ("DISKPART_EXIT=" + $p.ExitCode)
                """;
            progress("正在重建分区并格式化为 FAT32…", null);
            var partitionResult = await runner.RunPowerShellScriptAsync(ps, "usb-format", requireAdmin: true, killOnCancel: false, log, ct);
            outcome.LogPath = partitionResult.LogPath;
            outcome.ExitCode = partitionResult.ExitCode;
            if (partitionResult.Canceled)
                throw new InvalidOperationException("格式化被取消。写盘过程中请不要取消。");
            if (partitionResult.ExitCode != 0 || !partitionResult.Output.Contains("DISKPART_EXIT=0"))
            {
                string detail = (partitionResult.Output + "\n" + partitionResult.StdErr).Trim();
                throw new InvalidOperationException("diskpart 格式化失败。" + (detail.Length > 0 ? "\n" + Tail(detail, 12) : ""));
            }

            string volume = letter + @":\";
            for (int i = 0; i < 40 && !Directory.Exists(volume); i++)
                await Task.Delay(250, ct);
            if (!Directory.Exists(volume))
                throw new InvalidOperationException($"格式化完成，但没有找到卷 {volume}。");

            long totalBytes = DirectorySize(efiDirectory)
                + (string.IsNullOrWhiteSpace(recoveryDirectory) ? 0 : DirectorySize(recoveryDirectory));
            var counter = new CopyCounter(totalBytes);

            progress("复制 OpenCore EFI…", null);
            await CopyDirectoryAsync(efiDirectory, Path.Combine(volume, "EFI"), "复制 EFI", counter, progress, ct);

            if (!string.IsNullOrWhiteSpace(recoveryDirectory) && Directory.Exists(recoveryDirectory))
            {
                progress("复制 macOS 恢复镜像（可能需要几分钟）…", null);
                await CopyDirectoryAsync(
                    recoveryDirectory,
                    Path.Combine(volume, "com.apple.recovery.boot"),
                    "复制恢复镜像",
                    counter,
                    progress,
                    ct);
            }

            outcome.Success = true;
            outcome.VolumeRoot = volume;
            progress($"U 盘已就绪：{volume}", 1.0);
            return outcome;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            outcome.Error = ex.Message;
            progress("", null);
            log("制作 U 盘失败：" + ex.Message);
            return outcome;
        }
        finally
        {
            if (scriptPath is not null)
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }
    }

    private static void ValidateEfiDirectory(string efiDirectory)
    {
        if (string.IsNullOrWhiteSpace(efiDirectory) || !Directory.Exists(efiDirectory))
            throw new InvalidOperationException("EFI 目录不存在，请先构建 EFI。");
        if (!File.Exists(Path.Combine(efiDirectory, "OC", "config.plist")))
            throw new InvalidOperationException("EFI 目录里没有 OC/config.plist，拒绝写入。");
        if (!File.Exists(Path.Combine(efiDirectory, "BOOT", "BOOTx64.efi")))
            throw new InvalidOperationException("EFI 目录里没有 BOOT/BOOTx64.efi，拒绝写入。");
    }

    private static void ValidateRecoveryDirectory(string recoveryDirectory)
    {
        if (string.IsNullOrWhiteSpace(recoveryDirectory)) return;
        if (!Directory.Exists(recoveryDirectory))
            throw new InvalidOperationException("恢复镜像目录不存在。");
        string dmg = Path.Combine(recoveryDirectory, "BaseSystem.dmg");
        string chunklist = Path.Combine(recoveryDirectory, "BaseSystem.chunklist");
        if (!File.Exists(dmg) || new FileInfo(dmg).Length < 1L * 1024 * 1024)
            throw new InvalidOperationException("恢复镜像 BaseSystem.dmg 不存在或不完整，请重新下载。");
        if (!File.Exists(chunklist) || new FileInfo(chunklist).Length < 0x24)
            throw new InvalidOperationException("恢复镜像缺少 BaseSystem.chunklist，无法确认镜像完整性。");
    }

    private static string FindFreeLetter()
    {
        var used = DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])).ToHashSet();
        for (char c = 'D'; c <= 'Z'; c++)
        {
            if (!used.Contains(c)) return c.ToString();
        }
        throw new InvalidOperationException("没有可用的盘符。");
    }

    private static string Tail(string text, int lines)
    {
        string[] all = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join('\n', all.Skip(Math.Max(0, all.Length - lines)));
    }

    private static long DirectorySize(string directory)
    {
        long total = 0;
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }
        }
        catch
        {
        }
        return total;
    }

    private sealed class CopyCounter(long total)
    {
        private long _copied;
        private long _lastReport;
        private readonly long _total = Math.Max(1, total);

        public void Add(int bytes)
        {
            Interlocked.Add(ref _copied, bytes);
        }

        public double Fraction => Math.Clamp((double)Interlocked.Read(ref _copied) / _total, 0, 1);

        public bool ShouldReport()
        {
            long now = Interlocked.Read(ref _copied);
            long last = Interlocked.Read(ref _lastReport);
            if (now - last < 8L * 1024 * 1024 && now < _total) return false;
            Interlocked.Exchange(ref _lastReport, now);
            return true;
        }

        public long Copied => Interlocked.Read(ref _copied);
        public long Total => _total;
    }

    private static async Task CopyDirectoryAsync(
        string source,
        string destination,
        string label,
        CopyCounter counter,
        Action<string, double?> progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            ct.ThrowIfCancellationRequested();
            string name = Path.GetFileName(file);
            if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
                continue;
            await CopyFileAsync(file, Path.Combine(destination, name), label, counter, progress, ct);
        }
        foreach (string dir in Directory.EnumerateDirectories(source))
        {
            ct.ThrowIfCancellationRequested();
            await CopyDirectoryAsync(dir, Path.Combine(destination, Path.GetFileName(dir)), label, counter, progress, ct);
        }
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        string label,
        CopyCounter counter,
        Action<string, double?> progress,
        CancellationToken ct)
    {
        const int bufferSize = 1024 * 1024;
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.SequentialScan);
        byte[] buffer = new byte[bufferSize];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int read = await input.ReadAsync(buffer, ct);
            if (read <= 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            counter.Add(read);
            if (counter.ShouldReport())
                progress($"{label}  {FormatSize(counter.Copied)} / {FormatSize(counter.Total)}", counter.Fraction);
        }
        progress($"{label}  {FormatSize(counter.Copied)} / {FormatSize(counter.Total)}", counter.Fraction);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.00} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.0} MB";
        if (bytes >= 1L << 10) return $"{bytes / 1024.0:0.0} KB";
        return bytes + " B";
    }
}
