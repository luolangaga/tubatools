using System.Text;
using System.Xml.Linq;

namespace Tubatools.MacOSInstaller.Core;

internal sealed class ConfigGenerator
{
    public sealed record KextEntry(string BundlePath, string ExecutableName);

    public static void Generate(
        string templatePath,
        string outputPath,
        HardwareProfile hw,
        CompatibilityReport report,
        MacTarget target,
        BuildOptions options,
        string serial,
        string mlb,
        string uuid,
        string romHex,
        IEnumerable<string> acpiFiles,
        IEnumerable<KextEntry> kexts,
        IEnumerable<string> uefiDrivers)
    {
        XDocument doc = XDocument.Load(templatePath, LoadOptions.PreserveWhitespace);
        XElement top = doc.Root?.Element("dict") ?? throw new InvalidOperationException("Sample.plist 结构异常。");

        XElement acpi = Dict(top, "ACPI");
        XElement acpiAdd = Array(acpi, "Add");
        acpiAdd.RemoveNodes();
        foreach (string file in acpiFiles)
        {
            acpiAdd.Add(NewDict(
                ("Comment", Str(file)),
                ("Enabled", Bool(true)),
                ("Path", Str(file))));
        }

        XElement booter = Dict(top, "Booter");
        XElement booterQuirks = Dict(booter, "Quirks");
        SetBool(booterQuirks, "AvoidRuntimeDefrag", true);
        SetBool(booterQuirks, "DevirtualiseMmio", true);
        SetBool(booterQuirks, "EnableWriteUnprotector", false);
        SetBool(booterQuirks, "RebuildAppleMemoryMap", true);
        SetBool(booterQuirks, "SetupVirtualMap", true);
        SetBool(booterQuirks, "SyncRuntimePermissions", true);
        SetBool(booterQuirks, "ProtectMemoryRegions", false);
        SetBool(booterQuirks, "DisableVariableWrite", false);
        SetInteger(booterQuirks, "ResizeAppleGpuBars", -1);

        XElement kernel = Dict(top, "Kernel");
        XElement kernelAdd = Array(kernel, "Add");
        kernelAdd.RemoveNodes();
        foreach (KextEntry kext in kexts)
        {
            kernelAdd.Add(NewDict(
                ("Arch", Str("x86_64")),
                ("BundlePath", Str(kext.BundlePath)),
                ("Comment", Str("")),
                ("Enabled", Bool(true)),
                ("ExecutablePath", Str(string.IsNullOrEmpty(kext.ExecutableName) ? "" : "Contents/MacOS/" + kext.ExecutableName)),
                ("MaxKernel", Str("")),
                ("MinKernel", Str("")),
                ("PlistPath", Str("Contents/Info.plist"))));
        }

        XElement kernelQuirks = Dict(kernel, "Quirks");
        SetBool(kernelQuirks, "AppleCpuPmCfgLock", false);
        SetBool(kernelQuirks, "AppleXcpmCfgLock", true);
        SetBool(kernelQuirks, "DisableIoMapper", true);
        SetBool(kernelQuirks, "DisableLinkeditJettison", true);
        SetBool(kernelQuirks, "LapicKernelPanic", false);
        SetBool(kernelQuirks, "PanicNoKextDump", true);
        SetBool(kernelQuirks, "PowerTimeoutKernelPanic", true);
        SetBool(kernelQuirks, "XhciPortLimit", false);
        SetInteger(kernelQuirks, "SetApfsTrimTimeout", -1);

        XElement device = Dict(top, "DeviceProperties");
        XElement deviceAdd = Dict(device, "Add");
        deviceAdd.RemoveNodes();
        if (report.UseIgpu && !string.IsNullOrWhiteSpace(report.IgpuPlatformIdBase64))
        {
            deviceAdd.Add(new XElement("key", "PciRoot(0x0)/Pci(0x2,0x0)"),
                NewDict(
                    ("AAPL,ig-platform-id", Data(report.IgpuPlatformIdBase64)),
                    ("device-id", Data(report.IgpuDeviceIdBase64)),
                    ("framebuffer-patch-enable", Data("AQAAAA==")),
                    ("framebuffer-stolenmem", Data("AAAwAQ=="))));
        }
        if (report.AudioLayout > 0)
        {
            byte[] layout = BitConverter.GetBytes(report.AudioLayout);
            deviceAdd.Add(new XElement("key", "PciRoot(0x0)/Pci(0x1F,0x3)"),
                NewDict(("layout-id", Data(Convert.ToBase64String(layout)))));
        }

        XElement nvram = Dict(top, "NVRAM");
        XElement nvramAdd = Dict(nvram, "Add");
        XElement guid = Dict(nvramAdd, "7C436110-AB2A-4BBB-A880-FE41995C9F82");
        SetString(guid, "boot-args", report.BootArgs);
        SetData(guid, "csr-active-config", Convert.ToBase64String(new byte[4]));
        SetBool(nvram, "WriteFlash", true);

        XElement platform = Dict(top, "PlatformInfo");
        XElement generic = Dict(platform, "Generic");
        SetString(generic, "SystemProductName", report.Model);
        SetString(generic, "SystemSerialNumber", serial);
        SetString(generic, "MLB", mlb);
        SetString(generic, "SystemUUID", uuid);
        SetData(generic, "ROM", Convert.ToBase64String(ParseHex(romHex, 6)));
        SetString(platform, "UpdateSMBIOSMode", "Create");
        SetBool(platform, "UpdateSMBIOS", true);
        SetBool(platform, "UpdateDataHub", true);
        SetBool(platform, "UpdateNVRAM", true);

        XElement misc = Dict(top, "Misc");
        XElement boot = Dict(misc, "Boot");
        SetBool(boot, "HideAuxiliary", false);
        SetString(boot, "PickerMode", "Builtin");
        SetInteger(boot, "Timeout", 5);
        SetBool(boot, "ShowPicker", true);
        XElement security = Dict(misc, "Security");
        SetString(security, "SecureBootModel", "Default");
        SetString(security, "DmgLoading", "Signed");
        SetString(security, "Vault", "Optional");
        SetInteger(security, "ScanPolicy", 0);
        XElement debug = Dict(misc, "Debug");
        SetInteger(debug, "Target", 67);

        XElement uefi = Dict(top, "UEFI");
        XElement drivers = Array(uefi, "Drivers");
        drivers.RemoveNodes();
        foreach (string driver in uefiDrivers)
        {
            drivers.Add(NewDict(
                ("Arguments", Str("")),
                ("Comment", Str("")),
                ("Enabled", Bool(true)),
                ("HideVerbose", Bool(false)),
                ("LoadEarly", Bool(false)),
                ("Path", Str(driver))));
        }
        XElement uefiQuirks = Dict(uefi, "Quirks");
        SetBool(uefiQuirks, "RequestBootVarRouting", true);
        SetInteger(uefiQuirks, "ResizeGpuBars", -1);

        XElement? apfs = uefi.Element("APFS") is null ? null : Dict(uefi, "APFS");
        if (apfs is not null)
        {
            SetBool(apfs, "EnableJumpstart", true);
            SetBool(apfs, "GlobalConnect", false);
        }

        var settings = new System.Xml.XmlWriterSettings
        {
            Indent = true,
            IndentChars = "\t",
            Encoding = new UTF8Encoding(false)
        };
        using (var writer = System.Xml.XmlWriter.Create(outputPath, settings))
        {
            writer.WriteStartDocument();
            writer.WriteDocType("plist", "-//Apple//DTD PLIST 1.0//EN",
                "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null);
            doc.Root!.WriteTo(writer);
            writer.WriteEndDocument();
        }
    }

    private static XElement Dict(XElement parent, string key)
    {
        XElement? keyElement = parent.Elements("key").FirstOrDefault(e => e.Value == key);
        XElement? value = keyElement?.ElementsAfterSelf().FirstOrDefault(e => e.Name == "dict");
        if (value is null)
        {
            value = new XElement("dict");
            if (keyElement is null) parent.Add(new XElement("key", key), value);
            else keyElement.AddAfterSelf(value);
        }
        return value;
    }

    private static XElement Array(XElement parent, string key)
    {
        XElement? keyElement = parent.Elements("key").FirstOrDefault(e => e.Value == key);
        XElement? value = keyElement?.ElementsAfterSelf().FirstOrDefault(e => e.Name == "array");
        if (value is null)
        {
            value = new XElement("array");
            if (keyElement is null) parent.Add(new XElement("key", key), value);
            else keyElement.AddAfterSelf(value);
        }
        return value;
    }

    private static void SetString(XElement dict, string key, string value) => SetScalar(dict, key, new XElement("string", value));
    private static void SetBool(XElement dict, string key, bool value) => SetScalar(dict, key, value ? new XElement("true") : new XElement("false"));
    private static void SetInteger(XElement dict, string key, int value) => SetScalar(dict, key, new XElement("integer", value));
    private static void SetData(XElement dict, string key, string base64) => SetScalar(dict, key, Data(base64));

    private static void SetScalar(XElement dict, string key, XElement value)
    {
        XElement? keyElement = dict.Elements("key").FirstOrDefault(e => e.Value == key);
        if (keyElement is null)
        {
            dict.Add(new XElement("key", key), value);
            return;
        }
        XElement? old = keyElement.ElementsAfterSelf().FirstOrDefault();
        if (old is null) keyElement.AddAfterSelf(value);
        else old.ReplaceWith(value);
    }

    private static XElement Data(string base64) => new("data", base64);
    private static XElement Str(string value) => new("string", value);
    private static XElement Bool(bool value) => value ? new XElement("true") : new XElement("false");

    private static XElement NewDict(params (string Key, XElement Value)[] entries)
    {
        var dict = new XElement("dict");
        foreach ((string k, XElement v) in entries)
        {
            dict.Add(new XElement("key", k), v);
        }
        return dict;
    }

    private static byte[] ParseHex(string hex, int length)
    {
        string clean = new string(hex.Where(Uri.IsHexDigit).ToArray());
        if (clean.Length < length * 2)
        {
            var rnd = new byte[length];
            Random.Shared.NextBytes(rnd);
            return rnd;
        }
        byte[] bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
        return bytes;
    }
}
