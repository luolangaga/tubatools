using System.Text.Json;
using Microsoft.Win32;

namespace TubaWinUi3.Services;

public sealed class HardwareSpooferEntry
{
    public required string KeyPath { get; init; }
    public required string ValueName { get; init; }
    public required RegistryValueKind Kind { get; init; }
    public required string OriginalValue { get; init; }
    public string? CurrentValue { get; set; }
    public bool IsModified => CurrentValue is not null && CurrentValue != OriginalValue;
}

public static class HardwareSpooferService
{
    private static readonly string BackupPath = Path.Combine(
        ConfigManager.GetDataDir(), "hardware_spoofer_backup.json");

    public static bool IsAdmin
    {
        get
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    public static bool HasBackup => File.Exists(BackupPath);

    public static string ReadValue(string keyPath, string valueName, string defaultValue = "")
    {
        try
        {
            var hive = GetHive(keyPath, out var subKey);
            using var key = hive.OpenSubKey(subKey, writable: false);
            if (key is null) return defaultValue;
            var val = key.GetValue(valueName);
            if (val is null) return defaultValue;
            if (val is int i) return i.ToString();
            if (val is string[] arr) return string.Join("|", arr);
            if (val is byte[] bytes) return BitConverter.ToString(bytes).Replace("-", "");
            return val.ToString() ?? defaultValue;
        }
        catch { return defaultValue; }
    }

    public static int ReadDword(string keyPath, string valueName, int defaultValue = 0)
    {
        try
        {
            var hive = GetHive(keyPath, out var subKey);
            using var key = hive.OpenSubKey(subKey, writable: false);
            if (key is null) return defaultValue;
            var val = key.GetValue(valueName);
            if (val is int i) return i;
            return defaultValue;
        }
        catch { return defaultValue; }
    }

    public static bool WriteValue(string keyPath, string valueName, string value, RegistryValueKind kind)
    {
        try
        {
            var hive = GetHive(keyPath, out var subKey);
            using var key = hive.OpenSubKey(subKey, writable: true);
            if (key is null) return false;
            if (kind == RegistryValueKind.DWord)
            {
                if (int.TryParse(value, out var dw))
                    key.SetValue(valueName, dw, RegistryValueKind.DWord);
                else return false;
            }
            else if (kind == RegistryValueKind.MultiString)
            {
                key.SetValue(valueName, new[] { value }, RegistryValueKind.MultiString);
            }
            else
            {
                key.SetValue(valueName, value, kind);
            }
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Writes a GPU name to ALL relevant registry locations for maximum coverage.
    /// Modifies DriverDesc, ProviderName, HardwareInformation.ChipType, SPDIFVendorDesc
    /// in both Control\Video and Control\Class paths.
    /// </summary>
    public static bool WriteGpuName(string gpuName, string? providerName)
    {
        var videoKey = FindPrimaryGpuKey();
        var classKey = FindPrimaryGpuClassKey();
        var anySuccess = false;

        var chipTypeName = gpuName.Contains("Family", StringComparison.OrdinalIgnoreCase)
            ? gpuName
            : gpuName + " Family";

        foreach (var keyPath in new[] { videoKey, classKey })
        {
            if (keyPath is null) continue;
            try
            {
                var hive = GetHive(keyPath, out var subKey);
                using var key = hive.OpenSubKey(subKey, writable: true);
                if (key is null) continue;

                // DriverDesc (String) — WMI Win32_VideoController.Name
                key.SetValue("DriverDesc", gpuName, RegistryValueKind.String);

                // HardwareInformation.ChipType (MultiString) — WMI Win32_VideoController.VideoProcessor
                key.SetValue("HardwareInformation.ChipType", new[] { chipTypeName }, RegistryValueKind.MultiString);

                // HardwareInformation.AdapterString (String) — adapter identifier
                key.SetValue("HardwareInformation.AdapterString", gpuName, RegistryValueKind.String);

                // ProviderName (String)
                if (!string.IsNullOrEmpty(providerName))
                    key.SetValue("ProviderName", providerName, RegistryValueKind.String);

                anySuccess = true;
            }
            catch { }
        }

        return anySuccess;
    }

    /// <summary>
    /// Reads the current GPU description from any available source.
    /// </summary>
    public static string ReadCurrentGpuDesc()
    {
        var videoKey = FindPrimaryGpuKey();
        if (videoKey is not null)
        {
            var val = ReadValue(videoKey, "DriverDesc");
            if (!string.IsNullOrEmpty(val)) return val;
        }
        var classKey = FindPrimaryGpuClassKey();
        if (classKey is not null)
        {
            var val = ReadValue(classKey, "DriverDesc");
            if (!string.IsNullOrEmpty(val)) return val;
        }
        return "";
    }

    public static string ReadCurrentGpuProvider()
    {
        var videoKey = FindPrimaryGpuKey();
        if (videoKey is not null)
        {
            var val = ReadValue(videoKey, "ProviderName");
            if (!string.IsNullOrEmpty(val)) return val;
        }
        var classKey = FindPrimaryGpuClassKey();
        if (classKey is not null)
        {
            var val = ReadValue(classKey, "ProviderName");
            if (!string.IsNullOrEmpty(val)) return val;
        }
        return "";
    }

    public static List<HardwareSpooferEntry> ReadAllCurrent()
    {
        var entries = new List<HardwareSpooferEntry>();

        // CPU
        var cpuKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = cpuKey, ValueName = "ProcessorNameString",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(cpuKey, "ProcessorNameString"),
            CurrentValue = ReadValue(cpuKey, "ProcessorNameString")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = cpuKey, ValueName = "VendorIdentifier",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(cpuKey, "VendorIdentifier"),
            CurrentValue = ReadValue(cpuKey, "VendorIdentifier")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = cpuKey, ValueName = "~MHz",
            Kind = RegistryValueKind.DWord,
            OriginalValue = ReadDword(cpuKey, "~MHz").ToString(),
            CurrentValue = ReadDword(cpuKey, "~MHz").ToString()
        });

        // GPU — just store the display values; actual write uses WriteGpuName
        var gpuDesc = ReadCurrentGpuDesc();
        var gpuProvider = ReadCurrentGpuProvider();
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = "__GPU__", ValueName = "DriverDesc",
            Kind = RegistryValueKind.String,
            OriginalValue = gpuDesc,
            CurrentValue = gpuDesc
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = "__GPU__", ValueName = "ProviderName",
            Kind = RegistryValueKind.String,
            OriginalValue = gpuProvider,
            CurrentValue = gpuProvider
        });

        // System info
        var sysKey = @"SYSTEM\CurrentControlSet\Control\SystemInformation";
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = sysKey, ValueName = "SystemProductName",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(sysKey, "SystemProductName"),
            CurrentValue = ReadValue(sysKey, "SystemProductName")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = sysKey, ValueName = "SystemManufacturer",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(sysKey, "SystemManufacturer"),
            CurrentValue = ReadValue(sysKey, "SystemManufacturer")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = sysKey, ValueName = "SystemFamily",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(sysKey, "SystemFamily"),
            CurrentValue = ReadValue(sysKey, "SystemFamily")
        });

        // BIOS
        var biosKey = @"HARDWARE\DESCRIPTION\System\BIOS";
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = biosKey, ValueName = "BIOSVendor",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(biosKey, "BIOSVendor"),
            CurrentValue = ReadValue(biosKey, "BIOSVendor")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = biosKey, ValueName = "BIOSVersion",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(biosKey, "BIOSVersion"),
            CurrentValue = ReadValue(biosKey, "BIOSVersion")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = biosKey, ValueName = "BaseBoardManufacturer",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(biosKey, "BaseBoardManufacturer"),
            CurrentValue = ReadValue(biosKey, "BaseBoardManufacturer")
        });
        entries.Add(new HardwareSpooferEntry
        {
            KeyPath = biosKey, ValueName = "BaseBoardProduct",
            Kind = RegistryValueKind.String,
            OriginalValue = ReadValue(biosKey, "BaseBoardProduct"),
            CurrentValue = ReadValue(biosKey, "BaseBoardProduct")
        });

        return entries;
    }

    public static string? FindPrimaryGpuKey()
    {
        try
        {
            using var videoKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Video");
            if (videoKey is null) return null;

            foreach (var subKeyName in videoKey.GetSubKeyNames())
            {
                var subPath = $@"SYSTEM\CurrentControlSet\Control\Video\{subKeyName}\0000";
                using var subKey = Registry.LocalMachine.OpenSubKey(subPath, writable: false);
                if (subKey is null) continue;

                var desc = subKey.GetValue("DriverDesc") as string;
                if (string.IsNullOrEmpty(desc)) continue;

                if (IsVirtualGpu(desc)) continue;

                return subPath;
            }
        }
        catch { }

        return null;
    }

    public static string? FindPrimaryGpuClassKey()
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null) return null;

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                if (subKeyName.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                    subKeyName.Equals("Properties", StringComparison.OrdinalIgnoreCase))
                    continue;

                var subPath = $@"SYSTEM\CurrentControlSet\Control\Class\{{4d36e968-e325-11ce-bfc1-08002be10318}}\{subKeyName}";
                using var subKey = Registry.LocalMachine.OpenSubKey(subPath, writable: false);
                if (subKey is null) continue;

                var desc = subKey.GetValue("DriverDesc") as string;
                if (string.IsNullOrEmpty(desc)) continue;

                if (IsVirtualGpu(desc)) continue;

                return subPath;
            }
        }
        catch { }

        return null;
    }

    private static bool IsVirtualGpu(string desc)
    {
        return desc.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("RDPDD", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("VGA Save", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("Virtual Display", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("IddDesk", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("GameViewer", StringComparison.OrdinalIgnoreCase) ||
               desc.Contains("Remote Desktop", StringComparison.OrdinalIgnoreCase);
    }

    public static void SaveBackup(List<HardwareSpooferEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(BackupPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // Also backup the raw GPU registry values for full restore
            var gpuBackup = new Dictionary<string, Dictionary<string, string>>();
            foreach (var keyPath in new[] { FindPrimaryGpuKey(), FindPrimaryGpuClassKey() })
            {
                if (keyPath is null) continue;
                try
                {
                    var hive = GetHive(keyPath, out var subKey);
                    using var key = hive.OpenSubKey(subKey, writable: false);
                    if (key is null) continue;
                    var vals = new Dictionary<string, string>();
                    foreach (var vn in key.GetValueNames())
                    {
                        if (vn.Equals("DriverDesc", StringComparison.OrdinalIgnoreCase) ||
                            vn.Equals("ProviderName", StringComparison.OrdinalIgnoreCase) ||
                            vn.Equals("HardwareInformation.ChipType", StringComparison.OrdinalIgnoreCase) ||
                            vn.Equals("HardwareInformation.AdapterString", StringComparison.OrdinalIgnoreCase))
                        {
                            vals[vn] = ReadValue(keyPath, vn);
                        }
                    }
                    gpuBackup[keyPath] = vals;
                }
                catch { }
            }

            var gpuBackupJson = JsonSerializer.Serialize(gpuBackup, TubaDefaultContext.Default.DictionaryStringDictionaryStringString);
            var backupData = new HardwareSpooferBackupData
            {
                Entries = entries.Select(e => new HardwareSpooferBackupItem { KeyPath = e.KeyPath, ValueName = e.ValueName, Kind = e.Kind, OriginalValue = e.OriginalValue }).ToList(),
                GpuBackup = gpuBackupJson
            };
            var json = JsonSerializer.Serialize(backupData, TubaDefaultIndentedContext.Default.HardwareSpooferBackupData);
            File.WriteAllText(BackupPath, json);
        }
        catch { }
    }

    public static List<HardwareSpooferEntry>? LoadBackup()
    {
        try
        {
            if (!File.Exists(BackupPath)) return null;
            var json = File.ReadAllText(BackupPath);
            var doc = JsonDocument.Parse(json);

            var entriesArray = doc.RootElement.TryGetProperty("Entries", out var entriesEl)
                ? entriesEl
                : doc.RootElement;

            var items = JsonSerializer.Deserialize(entriesArray.GetRawText(), TubaDefaultContext.Default.ListHardwareSpooferBackupItem);
            if (items is null) return null;

            return items.Select(i => new HardwareSpooferEntry
            {
                KeyPath = i.KeyPath,
                ValueName = i.ValueName,
                Kind = i.Kind,
                OriginalValue = i.OriginalValue,
                CurrentValue = i.OriginalValue
            }).ToList();
        }
        catch { return null; }
    }

    public static int ApplyChanges(List<HardwareSpooferEntry> entries)
    {
        if (!HasBackup)
            SaveBackup(entries);

        var count = 0;

        // GPU — use the dedicated multi-path writer
        var gpuDesc = entries.FirstOrDefault(e => e.KeyPath == "__GPU__" && e.ValueName == "DriverDesc");
        var gpuProvider = entries.FirstOrDefault(e => e.KeyPath == "__GPU__" && e.ValueName == "ProviderName");
        if (gpuDesc?.IsModified == true)
        {
            if (WriteGpuName(gpuDesc.CurrentValue!, gpuProvider?.CurrentValue))
                count++;
        }

        // Other entries — standard single-key write
        foreach (var entry in entries)
        {
            if (entry.KeyPath == "__GPU__") continue; // already handled above
            if (entry.CurrentValue is null || entry.CurrentValue == entry.OriginalValue) continue;
            if (WriteValue(entry.KeyPath, entry.ValueName, entry.CurrentValue, entry.Kind))
                count++;
        }
        return count;
    }

    public static int RestoreAll()
    {
        var backup = LoadBackup();
        if (backup is null) return 0;

        var count = 0;

        // GPU restore — use the dedicated multi-path writer
        var gpuDesc = backup.FirstOrDefault(e => e.KeyPath == "__GPU__" && e.ValueName == "DriverDesc");
        var gpuProvider = backup.FirstOrDefault(e => e.KeyPath == "__GPU__" && e.ValueName == "ProviderName");
        if (gpuDesc is not null)
        {
            if (WriteGpuName(gpuDesc.OriginalValue, gpuProvider?.OriginalValue))
                count++;
        }

        // Other entries
        foreach (var entry in backup)
        {
            if (entry.KeyPath == "__GPU__") continue;
            if (WriteValue(entry.KeyPath, entry.ValueName, entry.OriginalValue, entry.Kind))
                count++;
        }

        try { if (File.Exists(BackupPath)) File.Delete(BackupPath); } catch { }

        return count;
    }

    public static void DeleteBackup()
    {
        try { if (File.Exists(BackupPath)) File.Delete(BackupPath); } catch { }
    }

    private static RegistryKey GetHive(string keyPath, out string subKey)
    {
        subKey = keyPath;
        return Registry.LocalMachine;
    }

}
