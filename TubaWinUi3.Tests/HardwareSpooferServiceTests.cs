using System.Text.Json;
using Microsoft.Win32;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class HardwareSpooferServiceTests
{
    [Fact]
    public void FindGpuEnumKeyPaths_ReturnsAtLeastOneGpu()
    {
        var keys = HardwareSpooferService.FindGpuEnumKeyPaths();

        // Machine without a real GPU (VM / CI runner) — skip instead of failing.
        if (keys.Count == 0) return;

        foreach (var key in keys)
            Assert.StartsWith(@"SYSTEM\CurrentControlSet\Enum\PCI\", key);
    }

    [Fact]
    public void FindPrimaryGpuEnumKey_IsAmongEnumKeys()
    {
        var primary = HardwareSpooferService.FindPrimaryGpuEnumKey();
        if (primary is null) return;

        var all = HardwareSpooferService.FindGpuEnumKeyPaths();
        Assert.Contains(primary, all);
    }

    [Fact]
    public void ReadCurrentGpuDesc_ReturnsNonEmptyNameWhenGpuPresent()
    {
        var desc = HardwareSpooferService.ReadCurrentGpuDesc();
        if (desc.Length == 0) return; // no GPU on this machine

        // The returned name must be the display name, without INF/ID prefixes.
        Assert.DoesNotContain(';', desc);
        Assert.DoesNotContain("oem", desc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadGpuDevices_GroupsEachAdapterSeparately()
    {
        var gpus = HardwareSpooferService.ReadGpuDevices();
        if (gpus.Count == 0) return; // no GPU on this machine

        // One entry per Enum\PCI instance, never a merged "all GPUs" entry.
        Assert.Equal(
            gpus.Count,
            gpus.Select(g => g.EnumKeyPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var gpu in gpus)
        {
            Assert.StartsWith(@"SYSTEM\CurrentControlSet\Enum\PCI\", gpu.EnumKeyPath);
            Assert.False(string.IsNullOrWhiteSpace(gpu.DisplayName));
            Assert.Equal(gpu.DisplayName, gpu.NewName);
            Assert.Equal(gpu.ProviderName, gpu.NewProvider);
            Assert.False(gpu.IsModified);

            if (gpu.ClassKeyPath is not null)
            {
                Assert.StartsWith(
                    @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\",
                    gpu.ClassKeyPath);
            }

            foreach (var videoPath in gpu.VideoKeyPaths)
                Assert.StartsWith(@"SYSTEM\CurrentControlSet\Control\Video\", videoPath);
        }

        // Two adapters must never share the same driver instance key.
        var classKeys = gpus.Where(g => g.ClassKeyPath is not null).Select(g => g.ClassKeyPath!).ToList();
        Assert.Equal(classKeys.Count, classKeys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // When the registry exposes the linking values, the key groups must resolve.
        foreach (var gpu in gpus)
        {
            if (HardwareSpooferService.ReadValue(gpu.EnumKeyPath, "Driver").Length > 0)
                Assert.NotNull(gpu.ClassKeyPath);

            var videoId = HardwareSpooferService.ReadValue(
                $@"{gpu.EnumKeyPath}\Device Parameters", "VideoID");
            if (videoId.Length > 0)
                Assert.NotEmpty(gpu.VideoKeyPaths);
        }
    }

    [Fact]
    public void ReadGpuDevices_EditAffectsOnlyOneAdapter()
    {
        var gpus = HardwareSpooferService.ReadGpuDevices();
        if (gpus.Count < 2) return; // needs multi-GPU machine

        gpus[0].NewName = "NVIDIA GeForce RTX 4090";

        Assert.True(gpus[0].IsModified);
        foreach (var other in gpus.Skip(1))
            Assert.False(other.IsModified);
    }

    [Fact]
    public void StripDisplayPrefix_RemovesInfTokenPrefix()
    {
        Assert.Equal(
            "Intel(R) Arc(TM) B390 GPU",
            HardwareSpooferService.StripDisplayPrefix("@oem14.inf,%intel_dev_b080%;Intel(R) Arc(TM) B390 GPU"));
        Assert.Equal("Plain Name", HardwareSpooferService.StripDisplayPrefix("Plain Name"));
    }

    [Fact]
    public void ReadAllCurrent_DoesNotContainSyntheticGpuEntries()
    {
        var entries = HardwareSpooferService.ReadAllCurrent();

        Assert.DoesNotContain(entries, e => e.KeyPath == "__GPU__");
        Assert.DoesNotContain(entries, e => e.ValueName == "DriverDesc");
    }

    [Fact]
    public void GpuValueBackup_RoundTripsThroughJson()
    {
        var gpus = HardwareSpooferService.ReadGpuDevices();
        if (gpus.Count == 0) return;

        var captured = HardwareSpooferService.CaptureGpuValues(gpus);
        Assert.NotEmpty(captured);

        foreach (var gpu in gpus)
        {
            Assert.Contains(captured, v => v.KeyPath == gpu.EnumKeyPath && v.ValueName == "FriendlyName");
            Assert.Contains(captured, v => v.KeyPath == gpu.EnumKeyPath && v.ValueName == "DeviceDesc");
        }

        var json = JsonSerializer.Serialize(captured);
        using var doc = JsonDocument.Parse(json);
        var parsed = HardwareSpooferService.ParseGpuValues(doc.RootElement);

        Assert.NotNull(parsed);
        Assert.Equal(captured.Count, parsed!.Count);
        for (var i = 0; i < captured.Count; i++)
        {
            Assert.Equal(captured[i].KeyPath, parsed[i].KeyPath);
            Assert.Equal(captured[i].ValueName, parsed[i].ValueName);
            Assert.Equal(captured[i].Kind, parsed[i].Kind);
            Assert.Equal(captured[i].Exists, parsed[i].Exists);
            Assert.Equal(captured[i].Value, parsed[i].Value);
            Assert.Equal(captured[i].Values, parsed[i].Values);
        }
    }

    [Fact]
    public void ParseLegacyGpuBackup_ConvertsPrimaryAdapterValues()
    {
        const string json = """
            {
              "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}\\0001": {
                "DriverDesc": "NVIDIA GeForce RTX 4060",
                "ProviderName": "NVIDIA",
                "HardwareInformation.ChipType": "NVIDIA GeForce RTX 4060|Family"
              }
            }
            """;

        using var doc = JsonDocument.Parse(json);
        var parsed = HardwareSpooferService.ParseLegacyGpuBackup(doc.RootElement);

        Assert.NotNull(parsed);
        Assert.Equal(3, parsed!.Count);

        var chipType = Assert.Single(parsed, v => v.ValueName == "HardwareInformation.ChipType");
        Assert.Equal(RegistryValueKind.MultiString, chipType.Kind);
        Assert.Equal(new[] { "NVIDIA GeForce RTX 4060", "Family" }, chipType.Values);

        var desc = Assert.Single(parsed, v => v.ValueName == "DriverDesc");
        Assert.Equal(RegistryValueKind.String, desc.Kind);
        Assert.Equal("NVIDIA GeForce RTX 4060", desc.Value);
    }
}
