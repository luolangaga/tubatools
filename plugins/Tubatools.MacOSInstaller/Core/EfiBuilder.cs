using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tubatools.MacOSInstaller.Core;

internal sealed class EfiBuilder
{
    private static readonly Dictionary<string, string> Executables = new()
    {
        ["Lilu"] = "Lilu",
        ["VirtualSMC"] = "VirtualSMC",
        ["SMCProcessor"] = "SMCProcessor",
        ["SMCSuperIO"] = "SMCSuperIO",
        ["WhateverGreen"] = "WhateverGreen",
        ["AppleALC"] = "AppleALC",
        ["NVMeFix"] = "NVMeFix",
        ["IntelMausi"] = "IntelMausi",
        ["RealtekRTL8111"] = "RealtekRTL8111",
        ["RestrictEvents"] = "RestrictEvents",
        ["SMCBatteryManager"] = "SMCBatteryManager",
        ["SMCLightSensor"] = "SMCLightSensor",
        ["VoodooPS2Controller"] = "VoodooPS2Controller",
        ["VoodooPS2Keyboard"] = "VoodooPS2Keyboard",
        ["VoodooPS2Mouse"] = "VoodooPS2Mouse",
        ["VoodooPS2Trackpad"] = "VoodooPS2Trackpad",
        ["VoodooInput"] = "VoodooInput"
    };

    public static async Task<BuildOutcome> BuildAsync(
        PayloadManager payload,
        HardwareProfile hw,
        CompatibilityReport report,
        MacTarget target,
        BuildOptions options,
        ProcessRunner runner,
        string dataDirectory,
        Action<string> log,
        Action<string> progress,
        CancellationToken ct)
    {
        var outcome = new BuildOutcome
        {
            Model = report.Model,
            MacOSName = target.DisplayName,
            Kexts = report.Kexts,
            AcpiFiles = report.AcpiFiles,
            Warnings = report.Warnings.ToList()
        };

        await payload.EnsureAsync(log, ct);
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(payload.SamplePlist))
            throw new InvalidOperationException("OpenCore 组件包缺少 Docs/Sample.plist。");
        if (!File.Exists(payload.Ocvalidate))
            throw new InvalidOperationException("OpenCore 组件包缺少 ocvalidate。");

        string buildRoot = Path.Combine(dataDirectory, "build");
        string efiDir = Path.Combine(buildRoot, "EFI");
        if (Directory.Exists(efiDir))
        {
            progress("清理上一次构建…");
            await Task.Run(() => Directory.Delete(efiDir, true), ct);
        }
        Directory.CreateDirectory(buildRoot);

        progress("复制 OpenCore 引导文件…");
        await Task.Run(() => CopyDirectory(payload.X64Efi, efiDir), ct);

        string ocDir = Path.Combine(efiDir, "OC");
        string acpiDir = Path.Combine(ocDir, "ACPI");
        string kextDir = Path.Combine(ocDir, "Kexts");
        Directory.CreateDirectory(acpiDir);
        Directory.CreateDirectory(kextDir);

        foreach (string file in report.AcpiFiles)
        {
            string src = Path.Combine(payload.AcpiSamples, file);
            if (!File.Exists(src))
                throw new InvalidOperationException($"内置 ACPI 文件缺失：{file}");
            File.Copy(src, Path.Combine(acpiDir, file), overwrite: true);
            log($"  ACPI  {file}");
        }

        var kextEntries = new List<ConfigGenerator.KextEntry>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in report.Kexts)
        {
            if (name == "VoodooPS2Controller")
            {
                string? controller = payload.FindKext("VoodooPS2Controller");
                if (controller is null)
                {
                    outcome.Warnings.Add("未找到 VoodooPS2Controller.kext，已跳过。");
                    continue;
                }
                string parentBundle = "VoodooPS2Controller.kext";
                string parentExe = ResolveExecutable(controller, "VoodooPS2Controller",
                    Executables.GetValueOrDefault("VoodooPS2Controller"));
                AddKext(controller, parentBundle, parentExe, copy: true);
                string pluginsDir = Path.Combine(controller, "Contents", "PlugIns");
                if (Directory.Exists(pluginsDir))
                {
                    foreach (string plugin in Directory.EnumerateDirectories(pluginsDir, "*.kext").OrderBy(p => p))
                    {
                        string pluginBundle = Path.GetFileName(plugin);
                        string stem = Path.GetFileNameWithoutExtension(pluginBundle);
                        string pluginBundlePath = $"{parentBundle}/Contents/PlugIns/{pluginBundle}";
                        string pluginExecutable = ResolveExecutable(plugin, stem, Executables.GetValueOrDefault(stem, ""));
                        AddKext(plugin, pluginBundlePath, pluginExecutable, copy: false);
                    }
                }
                continue;
            }
            string? path = payload.FindKext(name);
            if (path is null)
            {
                outcome.Warnings.Add($"未找到 {name}.kext，已跳过。");
                continue;
            }
            string bundlePath = name + ".kext";
            string executable = ResolveExecutable(path, name, Executables.GetValueOrDefault(name, ""));
            AddKext(path, bundlePath, executable, copy: true);
        }

        void AddKext(string source, string bundlePath, string executableName, bool copy)
        {
            if (!added.Add(bundlePath)) return;
            if (copy)
            {
                string dest = Path.Combine(kextDir, bundlePath.Replace('/', Path.DirectorySeparatorChar));
                CopyDirectory(source, dest);
            }
            kextEntries.Add(new ConfigGenerator.KextEntry(bundlePath, executableName));
            log($"  KEXT  {bundlePath}");
        }

        progress("生成 SMBIOS 序列号…");
        var identity = await GenerateIdentityAsync(payload, report.Model, runner, log, ct);
        outcome.Serial = identity.Serial;
        outcome.Mlb = identity.Mlb;
        if (identity.Fallback)
            outcome.Warnings.Add("macserial 没有返回可用序列号，已使用本地生成的占位序列号；安装系统可用，但 iMessage/App Store 可能无法激活。");
        string uuid = Guid.NewGuid().ToString().ToUpperInvariant();
        string rom = CleanMac(hw.FirstMacAddress);

        var drivers = new List<string> { "OpenRuntime.efi" };
        string driversDir = Path.Combine(ocDir, "Drivers");
        string hfs = File.Exists(Path.Combine(driversDir, "OpenHfsPlus.efi"))
            ? "OpenHfsPlus.efi"
            : File.Exists(Path.Combine(driversDir, "HfsPlus.efi")) ? "HfsPlus.efi" : "";
        if (string.IsNullOrEmpty(hfs))
            throw new InvalidOperationException("OpenCore 组件包缺少 HfsPlus/OpenHfsPlus 驱动。");
        drivers.Add(hfs);

        progress("写入 config.plist…");
        string configPath = Path.Combine(ocDir, "config.plist");
        ConfigGenerator.Generate(
            payload.SamplePlist,
            configPath,
            hw,
            report,
            target,
            options,
            identity.Serial,
            identity.Mlb,
            uuid,
            rom,
            report.AcpiFiles,
            kextEntries,
            drivers);

        outcome.EfiDirectory = efiDir;
        outcome.ConfigPath = configPath;

        progress("用 ocvalidate 校验 config.plist…");
        var validation = await runner.RunAsync(
            payload.Ocvalidate,
            [configPath],
            payload.OpenCoreRoot,
            "ocvalidate",
            requireAdmin: false,
            killOnCancel: true,
            log,
            ct);
        outcome.RawValidation = (validation.Output + "\n" + validation.StdErr).Trim();
        outcome.LogPath = validation.LogPath;
        outcome.ValidationErrors = outcome.RawValidation
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("OCS:", StringComparison.OrdinalIgnoreCase)
                || l.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
                || l.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!validation.Success)
        {
            outcome.Success = false;
            if (outcome.ValidationErrors.Count == 0)
                outcome.ValidationErrors.Add(outcome.RawValidation.Length > 0 ? outcome.RawValidation : "ocvalidate 返回失败。");
            return outcome;
        }

        string infoPath = Path.Combine(buildRoot, "build-info.json");
        await File.WriteAllTextAsync(infoPath, JsonSerializer.Serialize(new
        {
            model = report.Model,
            serial = identity.Serial,
            mlb = identity.Mlb,
            macOS = target.DisplayName,
            darwin = target.Darwin,
            createdAt = DateTimeOffset.Now.ToString("O"),
            kexts = kextEntries.Select(k => k.BundlePath).ToArray(),
            acpi = report.AcpiFiles,
            bootArgs = report.BootArgs
        }, new JsonSerializerOptions { WriteIndented = true }), ct);

        outcome.Success = true;
        return outcome;
    }

    internal sealed record IdentityResult(string Serial, string Mlb, bool Fallback);

    internal static async Task<IdentityResult> GenerateIdentityAsync(
        PayloadManager payload,
        string model,
        ProcessRunner runner,
        Action<string> log,
        CancellationToken ct)
    {
        if (File.Exists(payload.Macserial))
        {
            var result = await runner.RunAsync(
                payload.Macserial,
                ["-m", model],
                payload.OpenCoreRoot,
                "macserial",
                requireAdmin: false,
                killOnCancel: true,
                log,
                ct);
            if (result.Success)
            {
                foreach (string line in result.Output.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (!trimmed.Contains('|')) continue;
                    string[] parts = trimmed.Split('|');
                    if (parts.Length < 2) continue;
                    string serial = parts[0].Trim();
                    string mlb = parts[1].Trim();
                    if (serial.Length == 12 && mlb.Length >= 15 && !serial.Contains(' '))
                        return new IdentityResult(serial, mlb, false);
                }
            }
        }

        log("  macserial 未返回可用序列号，改用本地生成的占位序列号。");
        return new IdentityResult(RandomCode(12), RandomCode(17), true);
    }

    private static string ResolveExecutable(string bundleDirectory, string stem, string? preferred)
    {
        string macos = Path.Combine(bundleDirectory, "Contents", "MacOS");
        if (!Directory.Exists(macos)) return "";
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            string candidate = Path.Combine(macos, preferred);
            if (File.Exists(candidate)) return preferred;
        }
        try
        {
            return Directory.EnumerateFiles(macos)
                .Select(Path.GetFileName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string RandomCode(int length)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++) sb.Append(chars[Random.Shared.Next(chars.Length)]);
        return sb.ToString();
    }

    private static string CleanMac(string mac) =>
        Regex.Replace(mac, "[^0-9A-Fa-f]", "").ToUpperInvariant();

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (string dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
