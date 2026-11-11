using System.Text.Json;
using System.Text.RegularExpressions;
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

/// <summary>
/// 一张可独立修改的显卡。把 Enum\PCI、Control\Class、Control\Video 三处属于同一块卡的键归到一组，
/// 多显卡（核显 + 独显）时各自修改、各自恢复，互不影响。
/// </summary>
public sealed class HardwareSpooferGpu
{
    /// <summary>PCI 实例名（如 VEN_10DE&amp;DEV_2684&amp;SUBSYS_...），改名后依旧稳定，用来区分同型号的多张卡。</summary>
    public required string HardwareId { get; init; }
    public required string EnumKeyPath { get; init; }
    public string? ClassKeyPath { get; init; }
    public IReadOnlyList<string> VideoKeyPaths { get; init; } = [];
    public required string DisplayName { get; init; }
    public required string ProviderName { get; init; }
    public string NewName { get; set; } = "";
    public string NewProvider { get; set; } = "";
    public bool IsModified =>
        !string.Equals(NewName, DisplayName, StringComparison.Ordinal) ||
        !string.Equals(NewProvider, ProviderName, StringComparison.Ordinal);
}

public static class HardwareSpooferService
{
    private const string DisplayClassGuid = "4d36e968-e325-11ce-bfc1-08002be10318";
    private const string DisplayClassRoot =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string VideoRoot = @"SYSTEM\CurrentControlSet\Control\Video";

    private static readonly Regex GpuClassInstancePattern = new(@"^00\d+", RegexOptions.Compiled);
    private static readonly Regex VideoInstancePattern = new(@"^(\d{4}|Video)$", RegexOptions.Compiled);

    private static readonly string[] GpuEnumValueNames = ["FriendlyName", "DeviceDesc"];

    private static readonly string[] GpuClassValueNames =
        ["DriverDesc", "ProviderName", "HardwareInformation.ChipType", "HardwareInformation.AdapterString"];

    private static readonly string[] GpuVideoValueNames = ["DriverDesc", "DeviceDesc", "Description", "FriendlyName"];

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

    private static readonly string[] GpuNameKeywords =
        ["nvidia", "geforce", "gtx", "rtx", "amd", "radeon", "arc", "iris", "uhd graphics", "hd graphics"];

    private static readonly string[] GpuExcludeKeywords =
        ["usb", "controller", "host", "xhci", "ehci", "uhci", "chipset", "smbus", "audio", "sound"];

    private static bool ContainsKeyword(string? text, string[] keywords)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var lower = text.ToLowerInvariant();
        return keywords.Any(kw => lower.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGpuName(string? text) => ContainsKeyword(text, GpuNameKeywords);

    /// <summary>
    /// Enumerates Enum\PCI display adapter instances for NVIDIA / AMD / Intel vendors.
    /// This is the location Windows actually reads for device display names
    /// (Device Manager / Task Manager / WMI Win32_VideoController).
    /// </summary>
    public static List<string> FindGpuEnumKeyPaths()
    {
        var results = new List<string>();
        try
        {
            using var enumKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI");
            if (enumKey is null) return results;

            foreach (var deviceName in enumKey.GetSubKeyNames())
            {
                var deviceUpper = deviceName.ToUpperInvariant();
                if (!deviceUpper.Contains("VEN_10DE") &&
                    !deviceUpper.Contains("VEN_1002") &&
                    !deviceUpper.Contains("VEN_8086"))
                    continue;

                var devicePath = $@"SYSTEM\CurrentControlSet\Enum\PCI\{deviceName}";
                using var deviceKey = Registry.LocalMachine.OpenSubKey(devicePath);
                if (deviceKey is null) continue;

                foreach (var instanceName in deviceKey.GetSubKeyNames())
                {
                    var instancePath = $@"{devicePath}\{instanceName}";
                    using var instanceKey = Registry.LocalMachine.OpenSubKey(instancePath);
                    if (instanceKey is null) continue;

                    var deviceDesc = instanceKey.GetValue("DeviceDesc") as string;
                    var friendlyName = instanceKey.GetValue("FriendlyName") as string;

                    if (ContainsKeyword(deviceDesc, GpuExcludeKeywords) ||
                        ContainsKeyword(friendlyName, GpuExcludeKeywords))
                        continue;

                    var isGpu = false;
                    var classGuid = instanceKey.GetValue("ClassGUID") as string;
                    if (classGuid is not null &&
                        classGuid.Trim().Trim('{', '}').Equals(DisplayClassGuid, StringComparison.OrdinalIgnoreCase))
                        isGpu = true;
                    if (!isGpu)
                        isGpu = IsGpuName(deviceDesc) || IsGpuName(friendlyName);

                    if (isGpu)
                        results.Add(instancePath);
                }
            }
        }
        catch { }

        return results;
    }

    /// <summary>
    /// Reads every editable GPU with its own registry key group. Each physical adapter
    /// (integrated or discrete) becomes one entry, so they can be renamed independently.
    /// </summary>
    public static List<HardwareSpooferGpu> ReadGpuDevices()
    {
        var devices = new List<HardwareSpooferGpu>();
        var usedEnumPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedVideoRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var enumPath in FindGpuEnumKeyPaths())
        {
            if (!usedEnumPaths.Add(enumPath)) continue;

            try
            {
                var friendly = ReadValue(enumPath, "FriendlyName");
                var deviceDesc = ReadValue(enumPath, "DeviceDesc");
                var displayName = StripDisplayPrefix(string.IsNullOrWhiteSpace(friendly) ? deviceDesc : friendly);
                if (string.IsNullOrWhiteSpace(displayName)) continue;
                if (IsVirtualGpu(displayName)) continue;

                var classKeyPath = ResolveGpuClassKey(enumPath);
                var videoKeyPaths = ResolveGpuVideoKeys(enumPath, displayName, usedVideoRoots);

                var provider = classKeyPath is not null ? ReadValue(classKeyPath, "ProviderName") : "";
                if (string.IsNullOrEmpty(provider) && videoKeyPaths.Count > 0)
                    provider = ReadValue(videoKeyPaths[0], "ProviderName");

                devices.Add(new HardwareSpooferGpu
                {
                    HardwareId = GetEnumDeviceId(enumPath),
                    EnumKeyPath = enumPath,
                    ClassKeyPath = classKeyPath,
                    VideoKeyPaths = videoKeyPaths,
                    DisplayName = displayName,
                    ProviderName = provider,
                    NewName = displayName,
                    NewProvider = provider
                });
            }
            catch { }
        }

        return devices;
    }

    /// <summary>
    /// Extracts the PCI device id (VEN_xxxx&amp;DEV_xxxx&amp;SUBSYS_...) from an Enum instance path.
    /// </summary>
    private static string GetEnumDeviceId(string enumPath)
    {
        var lastSlash = enumPath.LastIndexOf('\\');
        if (lastSlash <= 0) return enumPath;
        var previousSlash = enumPath.LastIndexOf('\\', lastSlash - 1);
        return previousSlash < 0
            ? enumPath[(lastSlash + 1)..]
            : enumPath[(previousSlash + 1)..lastSlash];
    }

    /// <summary>
    /// Resolves the Control\Class instance key that belongs to one Enum\PCI device.
    /// The device's "Driver" value points at it directly; MatchingDeviceId is the fallback.
    /// </summary>
    private static string? ResolveGpuClassKey(string enumPath)
    {
        var driver = ReadValue(enumPath, "Driver");
        if (!string.IsNullOrEmpty(driver))
        {
            var separator = driver.IndexOf('\\');
            if (separator > 0)
            {
                var classGuid = driver[..separator].Trim().Trim('{', '}');
                var instance = driver[(separator + 1)..].Trim();
                if (classGuid.Equals(DisplayClassGuid, StringComparison.OrdinalIgnoreCase))
                {
                    var candidate = $@"{DisplayClassRoot}\{instance}";
                    if (KeyExists(candidate)) return candidate;
                }
            }
        }

        try
        {
            using var classRoot = Registry.LocalMachine.OpenSubKey(DisplayClassRoot);
            if (classRoot is null) return null;

            var hardwareId = "PCI\\" + GetEnumDeviceId(enumPath);
            foreach (var subName in classRoot.GetSubKeyNames())
            {
                if (!GpuClassInstancePattern.IsMatch(subName)) continue;
                var candidate = $@"{DisplayClassRoot}\{subName}";
                var matchingId = ReadValue(candidate, "MatchingDeviceId");
                if (!string.IsNullOrEmpty(matchingId) &&
                    hardwareId.StartsWith(matchingId, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Resolves this adapter's Control\Video instance keys. Device Parameters\VideoID gives the
    /// adapter GUID; when it is missing, the GUID is matched by its current display name.
    /// </summary>
    private static List<string> ResolveGpuVideoKeys(
        string enumPath, string displayName, HashSet<string> usedRoots)
    {
        var results = new List<string>();
        string? root = null;

        var videoId = ReadValue($@"{enumPath}\Device Parameters", "VideoID").Trim();
        if (!string.IsNullOrEmpty(videoId))
        {
            var candidate = $@"{VideoRoot}\{videoId}";
            if (KeyExists(candidate)) root = candidate;
        }

        if (root is null)
        {
            try
            {
                using var videoRoot = Registry.LocalMachine.OpenSubKey(VideoRoot);
                if (videoRoot is null) return results;

                foreach (var guid in videoRoot.GetSubKeyNames())
                {
                    var candidate = $@"{VideoRoot}\{guid}";
                    if (usedRoots.Contains(candidate)) continue;
                    if (!VideoRootMatches(candidate, displayName)) continue;
                    root = candidate;
                    break;
                }
            }
            catch { }
        }

        if (root is null) return results;
        usedRoots.Add(root);

        try
        {
            using var rootKey = Registry.LocalMachine.OpenSubKey(root);
            if (rootKey is null) return results;

            foreach (var subName in rootKey.GetSubKeyNames())
            {
                if (!VideoInstancePattern.IsMatch(subName)) continue;
                var subPath = $@"{root}\{subName}";
                if (VideoKeyHasName(subPath)) results.Add(subPath);
            }

            if (results.Count == 0)
            {
                foreach (var fallback in new[] { "Video", "0000" })
                {
                    var subPath = $@"{root}\{fallback}";
                    if (KeyExists(subPath)) results.Add(subPath);
                }
            }
        }
        catch { }

        return results;
    }

    private static bool VideoRootMatches(string root, string displayName)
    {
        try
        {
            using var rootKey = Registry.LocalMachine.OpenSubKey(root);
            if (rootKey is null) return false;

            foreach (var subName in rootKey.GetSubKeyNames())
            {
                if (!VideoInstancePattern.IsMatch(subName)) continue;
                if (VideoKeyMatchesName($@"{root}\{subName}", displayName)) return true;
            }
        }
        catch { }

        return false;
    }

    private static bool VideoKeyHasName(string keyPath)
    {
        return GpuVideoValueNames.Any(name => !string.IsNullOrEmpty(ReadValue(keyPath, name)));
    }

    private static bool VideoKeyMatchesName(string keyPath, string displayName)
    {
        foreach (var name in GpuVideoValueNames)
        {
            var value = ReadValue(keyPath, name);
            if (string.IsNullOrEmpty(value)) continue;
            if (StripDisplayPrefix(value).Equals(displayName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool KeyExists(string keyPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key is not null;
        }
        catch { return false; }
    }

    /// <summary>
    /// Finds the primary (discrete, non-integrated) GPU Enum\PCI instance key.
    /// </summary>
    public static string? FindPrimaryGpuEnumKey()
    {
        var gpuKeys = FindGpuEnumKeyPaths();
        if (gpuKeys.Count == 0) return null;

        foreach (var keyPath in gpuKeys)
        {
            if (IsIntegratedGpu(keyPath)) continue;
            return keyPath;
        }
        return gpuKeys[0];
    }

    private static bool IsIntegratedGpu(string instancePath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(instancePath);
            var location = key?.GetValue("LocationInformation") as string;
            if (string.IsNullOrEmpty(location)) return false;
            var lower = location.ToLowerInvariant();
            return lower.Contains("internal") || lower.Contains("on board") || lower.Contains("bus 0");
        }
        catch { return false; }
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

    /// <summary>
    /// Writes a GPU name to the registry keys of one specific adapter only:
    /// 1. Enum\PCI — where Windows caches the device display name
    ///    (DeviceDesc, FriendlyName) shown in Device Manager / Task Manager / WMI.
    /// 2. Control\Class — this adapter's driver instance key (DriverDesc, HardwareInformation.*).
    /// 3. Control\Video — this adapter's legacy display keys (DriverDesc, DeviceDesc, Description, FriendlyName).
    /// </summary>
    public static bool WriteGpuName(HardwareSpooferGpu gpu, string gpuName, string? providerName)
    {
        var anySuccess = false;

        var chipTypeName = gpuName.Contains("Family", StringComparison.OrdinalIgnoreCase)
            ? gpuName
            : gpuName + " Family";

        // 1. Enum\PCI — the key location for the displayed GPU name
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(gpu.EnumKeyPath, writable: true);
            if (key is not null)
            {
                key.SetValue("FriendlyName", gpuName, RegistryValueKind.String);

                var deviceDesc = key.GetValue("DeviceDesc") as string;
                key.SetValue("DeviceDesc", BuildSpoofedDeviceDesc(deviceDesc, gpuName), RegistryValueKind.String);

                anySuccess = true;
            }
        }
        catch { }

        // 2. Control\Class — this adapter's driver instance key
        if (gpu.ClassKeyPath is { } classPath)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(classPath, writable: true);
                if (key is not null)
                {
                    key.SetValue("DriverDesc", gpuName, RegistryValueKind.String);
                    key.SetValue("HardwareInformation.ChipType", new[] { chipTypeName }, RegistryValueKind.MultiString);
                    key.SetValue("HardwareInformation.AdapterString", gpuName, RegistryValueKind.String);
                    if (!string.IsNullOrEmpty(providerName))
                        key.SetValue("ProviderName", providerName, RegistryValueKind.String);

                    anySuccess = true;
                }
            }
            catch { }
        }

        // 3. Control\Video — this adapter's legacy display keys
        foreach (var videoPath in gpu.VideoKeyPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(videoPath, writable: true);
                if (key is null) continue;

                key.SetValue("DriverDesc", gpuName, RegistryValueKind.String);
                key.SetValue("DeviceDesc", gpuName, RegistryValueKind.String);
                key.SetValue("Description", gpuName, RegistryValueKind.String);
                key.SetValue("FriendlyName", gpuName, RegistryValueKind.String);

                anySuccess = true;
            }
            catch { }
        }

        return anySuccess;
    }

    /// <summary>
    /// Keeps the INF/token prefix of a device description intact
    /// (e.g. "@oem30.inf,%token%;Old Name" -> "@oem30.inf,%token%;New Name").
    /// </summary>
    private static string BuildSpoofedDeviceDesc(string? original, string gpuName)
    {
        if (string.IsNullOrEmpty(original)) return gpuName;
        var idx = original.IndexOf(';');
        return idx >= 0 ? original[..(idx + 1)] + gpuName : gpuName;
    }

    /// <summary>
    /// Reads the current GPU description of the first detected adapter.
    /// </summary>
    public static string ReadCurrentGpuDesc() => ReadGpuDevices().FirstOrDefault()?.DisplayName ?? "";

    /// <summary>
    /// Strips the hardware-ID / localized-INF prefix from a display name
    /// (e.g. "@oem30.inf,%token%;Intel(R) Arc(TM) B390 GPU" -> "Intel(R) Arc(TM) B390 GPU").
    /// </summary>
    internal static string StripDisplayPrefix(string value)
    {
        var idx = value.LastIndexOf(';');
        if (idx < 0) return value;
        var result = value[(idx + 1)..].Trim();
        return string.IsNullOrEmpty(result) ? value : result;
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

    public static void SaveBackup(List<HardwareSpooferEntry> entries, List<HardwareSpooferGpu> gpus)
    {
        try
        {
            var dir = Path.GetDirectoryName(BackupPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(new
            {
                Entries = entries.Select(e => new { e.KeyPath, e.ValueName, e.Kind, e.OriginalValue }).ToList(),
                GpuValues = CaptureGpuValues(gpus)
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(BackupPath, json);
        }
        catch { }
    }

    /// <summary>
    /// Snapshots every registry value that a GPU rename touches, per key, so a restore can put
    /// each adapter's own original values back instead of copying one GPU's name onto all of them.
    /// </summary>
    internal static List<GpuValueBackup> CaptureGpuValues(List<HardwareSpooferGpu> gpus)
    {
        var result = new List<GpuValueBackup>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Capture(string keyPath, string valueName)
        {
            if (!seen.Add(keyPath + "|" + valueName)) return;

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath, writable: false);
                if (key is null) return;

                var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value is null)
                {
                    result.Add(new GpuValueBackup
                    {
                        KeyPath = keyPath, ValueName = valueName, Exists = false
                    });
                    return;
                }

                var kind = key.GetValueKind(valueName);
                if (value is string[] multi)
                {
                    result.Add(new GpuValueBackup
                    {
                        KeyPath = keyPath, ValueName = valueName, Kind = kind, Values = multi
                    });
                }
                else
                {
                    result.Add(new GpuValueBackup
                    {
                        KeyPath = keyPath, ValueName = valueName, Kind = kind, Value = value.ToString()
                    });
                }
            }
            catch { }
        }

        foreach (var gpu in gpus)
        {
            foreach (var name in GpuEnumValueNames)
                Capture(gpu.EnumKeyPath, name);

            if (gpu.ClassKeyPath is { } classPath)
            {
                foreach (var name in GpuClassValueNames)
                    Capture(classPath, name);
            }

            foreach (var videoPath in gpu.VideoKeyPaths)
            {
                foreach (var name in GpuVideoValueNames)
                    Capture(videoPath, name);
            }
        }

        return result;
    }

    /// <summary>
    /// Makes sure the backup contains per-key GPU snapshots before anything is written.
    /// A backup from 1.8.x only holds the primary adapter, so it is upgraded in place: its exact
    /// original values are kept and the remaining adapters get snapshotted as they are now.
    /// </summary>
    private static void EnsureBackupForApply(List<HardwareSpooferEntry> entries, List<HardwareSpooferGpu> gpus)
    {
        if (!HasBackup)
        {
            SaveBackup(entries, gpus);
            return;
        }

        try
        {
            var json = File.ReadAllText(BackupPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("GpuValues", out _)) return;

            UpgradeLegacyBackup(doc.RootElement, gpus);
        }
        catch { }
    }

    private static void UpgradeLegacyBackup(JsonElement root, List<HardwareSpooferGpu> gpus)
    {
        var entriesEl = root.TryGetProperty("Entries", out var entriesProperty) ? entriesProperty : root;
        var entries = JsonSerializer.Deserialize<List<BackupItem>>(entriesEl.GetRawText()) ?? [];

        var legacyValues = root.TryGetProperty("GpuBackup", out var legacyProperty)
            ? ParseLegacyGpuBackup(legacyProperty) ?? []
            : [];

        var values = CaptureGpuValues(gpus);

        // The legacy backup recorded the primary adapter's original name; put it back into the
        // Enum snapshot (the old writer renamed every adapter, so the current value is not usable).
        var gpuDesc = entries.FirstOrDefault(i => i.KeyPath == "__GPU__" && i.ValueName == "DriverDesc");
        var primary = gpus.FirstOrDefault(d => !IsIntegratedGpu(d.EnumKeyPath)) ?? gpus.FirstOrDefault();
        if (gpuDesc is not null && primary is not null)
        {
            OverrideCapturedGpuValue(values, primary.EnumKeyPath, "FriendlyName", gpuDesc.OriginalValue);
            var currentDesc = ReadValue(primary.EnumKeyPath, "DeviceDesc");
            OverrideCapturedGpuValue(values, primary.EnumKeyPath, "DeviceDesc",
                BuildSpoofedDeviceDesc(currentDesc, gpuDesc.OriginalValue));
        }

        // Legacy values are the exact pre-modification state, so they win over the current snapshot.
        foreach (var legacy in legacyValues)
        {
            var index = values.FindIndex(v => IsSameGpuValue(v, legacy));
            if (index >= 0) values[index] = legacy;
            else values.Add(legacy);
        }

        var json = JsonSerializer.Serialize(new
        {
            Entries = entries.Select(i => new { i.KeyPath, i.ValueName, i.Kind, i.OriginalValue }).ToList(),
            GpuValues = values
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(BackupPath, json);
    }

    private static bool IsSameGpuValue(GpuValueBackup a, GpuValueBackup b)
    {
        return a.KeyPath.Equals(b.KeyPath, StringComparison.OrdinalIgnoreCase) &&
               a.ValueName.Equals(b.ValueName, StringComparison.OrdinalIgnoreCase);
    }

    private static void OverrideCapturedGpuValue(
        List<GpuValueBackup> values, string keyPath, string valueName, string value)
    {
        var replacement = new GpuValueBackup
        {
            KeyPath = keyPath, ValueName = valueName, Kind = RegistryValueKind.String, Value = value
        };

        var index = values.FindIndex(v => IsSameGpuValue(v, replacement));
        if (index >= 0) values[index] = replacement;
        else values.Add(replacement);
    }

    public static int ApplyChanges(List<HardwareSpooferEntry> entries, List<HardwareSpooferGpu> gpus)
    {
        EnsureBackupForApply(entries, gpus);

        var count = 0;

        // GPU — only the adapters that were actually edited
        foreach (var gpu in gpus)
        {
            if (!gpu.IsModified) continue;
            if (WriteGpuName(gpu, gpu.NewName, gpu.NewProvider))
                count++;
        }

        // Other entries — standard single-key write
        foreach (var entry in entries)
        {
            if (entry.KeyPath == "__GPU__") continue; // legacy backup entries
            if (entry.CurrentValue is null || entry.CurrentValue == entry.OriginalValue) continue;
            if (WriteValue(entry.KeyPath, entry.ValueName, entry.CurrentValue, entry.Kind))
                count++;
        }
        return count;
    }

    public static int RestoreAll()
    {
        try
        {
            if (!File.Exists(BackupPath)) return 0;

            var json = File.ReadAllText(BackupPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var entriesEl = root.TryGetProperty("Entries", out var entriesProperty) ? entriesProperty : root;
            var items = JsonSerializer.Deserialize<List<BackupItem>>(entriesEl.GetRawText()) ?? [];
            var entries = items.Select(i => new HardwareSpooferEntry
            {
                KeyPath = i.KeyPath,
                ValueName = i.ValueName,
                Kind = i.Kind,
                OriginalValue = i.OriginalValue,
                CurrentValue = i.OriginalValue
            }).ToList();

            var count = 0;

            if (root.TryGetProperty("GpuValues", out var gpuValuesProperty))
            {
                // Current format: every key of every adapter was captured, restore them verbatim.
                var gpuValues = ParseGpuValues(gpuValuesProperty);
                if (gpuValues is not null)
                    count += RestoreGpuValues(gpuValues);
            }
            else
            {
                // 1.8.x and earlier: only the primary adapter's class/video values were saved.
                if (root.TryGetProperty("GpuBackup", out var legacyProperty))
                {
                    var legacyValues = ParseLegacyGpuBackup(legacyProperty);
                    if (legacyValues is not null)
                        count += RestoreLegacyPrimaryGpu(entries, legacyValues);
                }
                else
                {
                    count += RestoreLegacyPrimaryGpu(entries, []);
                }
            }

            // Other entries
            foreach (var entry in entries)
            {
                if (entry.KeyPath == "__GPU__") continue;
                if (WriteValue(entry.KeyPath, entry.ValueName, entry.OriginalValue, entry.Kind))
                    count++;
            }

            try { if (File.Exists(BackupPath)) File.Delete(BackupPath); } catch { }

            return count;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Legacy backups had no per-adapter Enum snapshot; restore the primary adapter's name first,
    /// then overwrite its exact class/video values that were backed up.
    /// </summary>
    private static int RestoreLegacyPrimaryGpu(
        List<HardwareSpooferEntry> entries, List<GpuValueBackup> legacyValues)
    {
        var count = 0;

        var gpuDesc = entries.FirstOrDefault(e => e.KeyPath == "__GPU__" && e.ValueName == "DriverDesc");
        if (gpuDesc is not null)
        {
            var gpuProvider = entries.FirstOrDefault(e => e.KeyPath == "__GPU__" && e.ValueName == "ProviderName");
            var devices = ReadGpuDevices();
            var primary = devices.FirstOrDefault(d => !IsIntegratedGpu(d.EnumKeyPath)) ?? devices.FirstOrDefault();
            if (primary is not null && WriteGpuName(primary, gpuDesc.OriginalValue, gpuProvider?.OriginalValue))
                count++;
        }

        if (legacyValues.Count > 0)
            count += RestoreGpuValues(legacyValues);

        return count;
    }

    internal static List<GpuValueBackup>? ParseGpuValues(JsonElement element)
    {
        try
        {
            return JsonSerializer.Deserialize<List<GpuValueBackup>>(element.GetRawText());
        }
        catch { return null; }
    }

    internal static List<GpuValueBackup>? ParseLegacyGpuBackup(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var result = new List<GpuValueBackup>();
        foreach (var keyProperty in element.EnumerateObject())
        {
            if (keyProperty.Value.ValueKind != JsonValueKind.Object) continue;

            foreach (var valueProperty in keyProperty.Value.EnumerateObject())
            {
                var name = valueProperty.Name;
                var raw = valueProperty.Value.ValueKind == JsonValueKind.String
                    ? valueProperty.Value.GetString() ?? ""
                    : valueProperty.Value.ToString();
                var isMulti = name.Equals("HardwareInformation.ChipType", StringComparison.OrdinalIgnoreCase);

                result.Add(new GpuValueBackup
                {
                    KeyPath = keyProperty.Name,
                    ValueName = name,
                    Kind = isMulti ? RegistryValueKind.MultiString : RegistryValueKind.String,
                    Value = raw,
                    Values = isMulti ? raw.Split('|') : null
                });
            }
        }
        return result;
    }

    private static int RestoreGpuValues(List<GpuValueBackup> values)
    {
        var count = 0;
        foreach (var item in values)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(item.KeyPath, writable: true);
                if (key is null) continue;

                if (!item.Exists)
                {
                    key.DeleteValue(item.ValueName, throwOnMissingValue: false);
                    count++;
                    continue;
                }

                switch (item.Kind)
                {
                    case RegistryValueKind.MultiString:
                        key.SetValue(item.ValueName,
                            item.Values ?? [item.Value ?? ""],
                            RegistryValueKind.MultiString);
                        break;
                    case RegistryValueKind.DWord:
                        if (!int.TryParse(item.Value, out var dword)) continue;
                        key.SetValue(item.ValueName, dword, RegistryValueKind.DWord);
                        break;
                    case RegistryValueKind.QWord:
                        if (!long.TryParse(item.Value, out var qword)) continue;
                        key.SetValue(item.ValueName, qword, RegistryValueKind.QWord);
                        break;
                    case RegistryValueKind.Binary:
                        continue;
                    default:
                        key.SetValue(item.ValueName, item.Value ?? "", item.Kind);
                        break;
                }

                count++;
            }
            catch { }
        }
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

    private sealed class BackupItem
    {
        public required string KeyPath { get; init; }
        public required string ValueName { get; init; }
        public required RegistryValueKind Kind { get; init; }
        public required string OriginalValue { get; init; }
    }

    internal sealed class GpuValueBackup
    {
        public required string KeyPath { get; init; }
        public required string ValueName { get; init; }
        public RegistryValueKind Kind { get; init; } = RegistryValueKind.String;
        public string? Value { get; init; }
        public string[]? Values { get; init; }
        public bool Exists { get; init; } = true;
    }
}
