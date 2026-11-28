namespace Tubatools.MacOSInstaller.Core;

using System.Text.RegularExpressions;

internal static class CompatibilityRules
{
    private static readonly HashSet<ushort> SupportedIntelIgpuIds =
    [
        0x3E91, 0x3E92, 0x3E98, 0x3E9B, 0x3E93, // Coffee Lake UHD 630
        0x9BC5, 0x9BC8, 0x9BCA        // Comet Lake UHD 630
    ];

    private static readonly HashSet<ushort> NaviIds =
    [
        0x731F, 0x73BF, 0x73EF, 0x73FF, 0x7420, 0x743F, 0x744C, 0x7470
    ];

    public static CompatibilityReport Evaluate(HardwareProfile hw, MacTarget target, BuildOptions options)
    {
        var r = new CompatibilityReport
        {
            IsLaptop = hw.IsLaptop,
            AudioLayout = Math.Clamp(options.AudioLayout, 0, 99)
        };

        string cpu = hw.CpuName;
        bool intel = hw.CpuVendor.Contains("Intel", StringComparison.OrdinalIgnoreCase)
            || cpu.Contains("Intel", StringComparison.OrdinalIgnoreCase);
        bool amdCpu = hw.CpuVendor.Contains("AMD", StringComparison.OrdinalIgnoreCase)
            || cpu.Contains("Ryzen", StringComparison.OrdinalIgnoreCase)
            || cpu.Contains("AMD", StringComparison.OrdinalIgnoreCase);

        if (amdCpu)
        {
            r.CanBuild = false;
            r.Errors.Add("AMD 处理器需要专门的内核补丁集，本版本尚未内置；请使用 Intel 平台。");
        }
        else if (!intel)
        {
            r.CanBuild = false;
            r.Errors.Add($"无法识别的处理器厂商：{hw.CpuVendor}。");
        }

        r.CpuClass = ClassifyIntel(cpu);
        if (r.CpuClass == "modern")
            r.Warnings.Add("第 11 代及更新的 Intel 处理器没有原生的 macOS 电源管理支持，可能需要额外的 CPUID 伪装与调校。");
        else if (r.CpuClass == "kaby" || r.CpuClass == "skylake")
            r.Warnings.Add("第 6/7 代 Intel 处理器对较新的 macOS 支持有限，建议选择 macOS 15 并做好兼容性验证。");

        GpuInfo? igpu = hw.Gpus.FirstOrDefault(g => g.VendorId == 0x8086 && SupportedIntelIgpuIds.Contains(g.DeviceId));
        GpuInfo? unsupportedIgpu = hw.Gpus.FirstOrDefault(g => g.VendorId == 0x8086 && !SupportedIntelIgpuIds.Contains(g.DeviceId));
        GpuInfo? amdGpu = hw.Gpus.FirstOrDefault(g => g.VendorId == 0x1002);
        GpuInfo? nvidiaGpu = hw.Gpus.FirstOrDefault(g => g.VendorId == 0x10DE);

        r.HasSupportedDgpu = amdGpu is not null;
        // 笔记本的内屏通常接在核显上，即使有可驱动的 AMD 独显也应保留核显输出路径。
        r.UseIgpu = igpu is not null && (r.IsLaptop || !r.HasSupportedDgpu);
        r.SupportedIntelIgpu = igpu is not null;
        r.DisabledNvidia = nvidiaGpu is not null;
        // 没有可驱动的核显/AMD 独显、但存在 Intel 显卡时，macOS 仍可能借固件基础显示启动。
        r.BasicDisplayOnly = igpu is null && amdGpu is null && unsupportedIgpu is not null;

        if (igpu is not null)
        {
            r.IgpuDeviceId = igpu.DeviceId;
            byte low = r.IsLaptop ? (byte)0x09 : (byte)0x07;
            r.IgpuPlatformIdBase64 = Convert.ToBase64String([low, 0x00, 0x9B, 0x3E]);
            ushort deviceId = igpu.DeviceId is 0x9BC5 or 0x9BC8 or 0x9BCA or 0x3E91 or 0x3E92 or 0x3E93 or 0x3E98
                ? (ushort)0x3E9B
                : igpu.DeviceId;
            r.IgpuDeviceIdBase64 = Convert.ToBase64String([
                (byte)(deviceId & 0xFF), (byte)((deviceId >> 8) & 0xFF), 0x00, 0x00
            ]);
        }

        if (r.IsLaptop)
        {
            if (igpu is null)
            {
                string name = unsupportedIgpu?.Name ?? "无核显";
                if (unsupportedIgpu is not null)
                {
                    r.Warnings.Add(amdGpu is null
                        ? $"这台笔记本的核显（{name}）没有 macOS 驱动；可以继续生成，但只能用固件提供的基础显示（可能低分辨率、无图形加速）。"
                        : $"笔记本的核显（{name}）没有 macOS 驱动；内屏可能不亮，可尝试用 AMD 独显接外接显示器。");
                }
                else
                {
                    r.CanBuild = false;
                    r.Errors.Add("这台笔记本没有可用的显示输出，无法生成能启动的 EFI。");
                }
            }
            if (nvidiaGpu is not null)
                r.Warnings.Add($"已通过 -wegnoegpu 禁用 NVIDIA 独显（{nvidiaGpu.Name}），macOS 没有对应驱动。");
        }
        else
        {
            if (!r.HasSupportedDgpu && igpu is null)
            {
                if (unsupportedIgpu is not null)
                {
                    r.Warnings.Add($"检测到 {unsupportedIgpu.Name}：没有 macOS 驱动；可以继续生成，但只能用固件提供的基础显示（可能低分辨率、无图形加速）。");
                }
                else
                {
                    r.CanBuild = false;
                    string name = string.Join("、", hw.Gpus.Select(g => g.Name).Where(n => !string.IsNullOrWhiteSpace(n)));
                    r.Errors.Add($"没有 macOS 可用的显卡：{name}。需要 Intel UHD 630 核显，或 AMD Polaris/Vega/Navi 独显。");
                }
            }
            if (nvidiaGpu is not null)
                r.Warnings.Add($"NVIDIA 显卡 {nvidiaGpu.Name} 在 macOS 15/26 下没有驱动，已通过 -wegnoegpu 保持禁用。");
        }

        if (hw.Gpus.Any(g => g.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase)) && !r.BasicDisplayOnly)
        {
            if (r.HasSupportedDgpu || r.UseIgpu)
            {
                string display = r.UseIgpu ? "Intel 核显" : "AMD 独显";
                r.Warnings.Add($"检测到 Intel Arc 显卡：macOS 没有 Arc 驱动，将由{display}负责显示。");
            }
            else if (r.CanBuild)
            {
                r.Errors.Add("检测到 Intel Arc 显卡：macOS 没有 Arc 驱动，不能用它启动或显示。");
            }
        }
        if (r.BasicDisplayOnly && hw.Gpus.Any(g => g.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase)))
        {
            r.Notes.Add("社区有面向虚拟机的 Intel Xe3 实验驱动项目，但缺少可分发许可证，本工具不内置，也不会替你下载。");
        }

        foreach (ControllerInfo c in hw.Controllers)
        {
            string text = (c.Name + " " + c.Device + " " + c.Pnp).ToLowerInvariant();
            if (text.Contains("vmd") || text.Contains("raid") || text.Contains("rst"))
                r.Warnings.Add($"检测到 {c.Name}：macOS 看不到 VMD/RAID 后面的硬盘，请在 BIOS 关闭 VMD/RAID，改用 AHCI。");
        }

        if (hw.Nics.Count > 0 && !hw.HasIntelEthernet && !hw.HasRealtekEthernet)
            r.Warnings.Add("没有识别到可驱动的有线网卡；安装阶段可能没有网络，建议先准备 USB 有线网卡或使用免网络安装方式。");

        r.Model = ChooseModel(hw, r, target);
        r.BoardId = BoardIdForModel(r.Model);
        r.Kexts = ChooseKexts(hw, r, target);
        r.AcpiFiles = ChooseAcpi(hw, r);
        r.BootArgs = BuildArgs(hw, r, target, options);

        if (r.IsLaptop)
        {
            r.Notes.Add("笔记本的 PS/2 键盘与触控板由 VoodooPS2Controller 驱动；I2C 精密触控板还需要按机型定制 VoodooI2C，未包含在通用构建中。");
        }
        if (hw.HasWirelessNic)
        {
            r.Warnings.Add("检测到无线网卡：Wi-Fi 没有通用驱动；安装阶段请使用有线网络。Intel 无线网卡需要另行配置 AirportItlwm/itlwm，且版本必须与 macOS 匹配。");
        }
        if (!hw.HasIntelEthernet && !hw.HasRealtekEthernet)
            r.Notes.Add("没有可用的有线网卡 kext；如果安装器需要联网，请准备一个 macOS 支持的 USB 网卡。");
        if (r.AudioLayout > 0)
            r.Notes.Add($"已写入音频 layout-id={r.AudioLayout}（AppleALC）。如果装好后没有声音，请在 1、3、7、11、13、21 等常见 layout 之间尝试。");

        if (options.ForceUnsupported)
        {
            if (r.Errors.Count > 0)
            {
                r.Warnings.InsertRange(0, r.Errors.Select(e => "已忽略：" + e));
                r.Errors.Clear();
            }
            r.CanBuild = true;
            r.Notes.Add("已启用「忽略兼容性检查」：生成的 EFI 不保证能在这台机器上启动。");
        }
        return r;
    }

    private static string ClassifyIntel(string cpu)
    {
        string t = cpu.ToLowerInvariant();
        if (t.Contains("ultra") || t.Contains("core ultra")) return "modern";
        if (Regex.IsMatch(t, @"i[3579]-1[0-4]\d{3}")) return "modern";
        if (Regex.IsMatch(t, @"i[3579]-10\d{3}")) return "comet";
        if (Regex.IsMatch(t, @"i[3579]-(8|9)\d{3}")) return "coffee";
        if (Regex.IsMatch(t, @"i[3579]-7\d{3}")) return "kaby";
        if (Regex.IsMatch(t, @"i[3579]-6\d{3}")) return "skylake";
        if (t.Contains("14th") || t.Contains("13th") || t.Contains("12th") || t.Contains("11th")) return "modern";
        if (t.Contains("10th")) return "comet";
        if (t.Contains("9th") || t.Contains("8th")) return "coffee";
        if (t.Contains("7th")) return "kaby";
        if (t.Contains("6th")) return "skylake";
        if (t.Contains("xeon")) return "skylake";
        return "unknown";
    }

    private static string ChooseModel(HardwareProfile hw, CompatibilityReport r, MacTarget target)
    {
        if (r.IsLaptop)
        {
            string cpu = hw.CpuName.ToLowerInvariant();
            bool highPower = cpu.Contains(" h") || cpu.Contains(" hq") || cpu.Contains(" hk") || cpu.Contains("hs");
            if (r.CpuClass == "comet" && highPower) return "MacBookPro16,1";
            if (r.CpuClass == "coffee") return highPower ? "MacBookPro15,1" : "MacBookPro15,2";
            return "MacBookPro16,2";
        }

        if (r.HasSupportedDgpu) return "MacPro7,1";
        if (r.CpuClass == "comet") return "iMac20,1";
        if (r.CpuClass == "coffee") return "iMac19,1";
        if (r.CpuClass == "kaby" || r.CpuClass == "skylake") return "iMac18,3";
        return target.Darwin == "25" ? "MacPro7,1" : "iMac20,1";
    }

    private static string BoardIdForModel(string model) => model switch
    {
        "iMac19,1" => "Mac-AA95B1DDAB278B95",
        "iMac18,3" => "Mac-BE088AF8C5EB4FA2",
        "iMac20,1" => "Mac-CFF7D910A743CAAF",
        "MacPro7,1" => "Mac-27AD2F918AE68F61",
        "MacBookPro15,1" => "Mac-937A206F2EE63C01",
        "MacBookPro15,2" => "Mac-827FB448E656EC26",
        "MacBookPro16,1" => "Mac-E1008331FDC96864",
        "MacBookPro16,2" => "Mac-5F9802EFE386AA28",
        _ => "Mac-27AD2F918AE68F61"
    };

    private static List<string> ChooseKexts(HardwareProfile hw, CompatibilityReport r, MacTarget target)
    {
        var list = new List<string> { "Lilu", "VirtualSMC", "SMCProcessor", "SMCSuperIO", "WhateverGreen" };
        if (r.IsLaptop)
        {
            list.Add("SMCBatteryManager");
            list.Add("SMCLightSensor");
            list.Add("VoodooPS2Controller");
        }
        if (r.AudioLayout > 0)
            list.Add("AppleALC");
        if (hw.HasNvme)
            list.Add("NVMeFix");
        if (hw.HasIntelEthernet)
            list.Add("IntelMausi");
        if (hw.HasRealtekEthernet)
            list.Add("RealtekRTL8111");
        if (r.Model == "MacPro7,1")
            list.Add("RestrictEvents");
        return list;
    }

    private static List<string> ChooseAcpi(HardwareProfile hw, CompatibilityReport r)
    {
        var files = new List<string> { r.CpuClass == "modern" ? "SSDT-PLUG-ALT.aml" : "SSDT-PLUG.aml", "SSDT-EC-USBX.aml" };
        if (r.IsLaptop)
        {
            files.Add("SSDT-PNLF.aml");
            files.Add("SSDT-SBUS-MCHC.aml");
        }
        string board = (hw.BoardProduct + " " + hw.BoardVersion).ToUpperInvariant();
        if (new[] { "Z370", "B360", "H370", "H310", "Z390", "B365" }.Any(board.Contains))
        {
            files.Add("SSDT-AWAC-DISABLE.aml");
            files.Add("SSDT-PMC.aml");
        }
        return files;
    }

    private static string BuildArgs(HardwareProfile hw, CompatibilityReport r, MacTarget target, BuildOptions options)
    {
        var args = new List<string>();
        if (options.VerboseBoot)
        {
            args.Add("-v");
            args.Add("keepsyms=1");
            args.Add("debug=0x100");
        }
        if (r.AudioLayout > 0)
            args.Add($"alcid={r.AudioLayout}");
        if (r.DisabledNvidia)
            args.Add("-wegnoegpu");
        if (hw.Gpus.Any(g => g.VendorId == 0x1002 && NaviIds.Contains(g.DeviceId)))
            args.Add("agdpmod=pikera");
        if (target.Darwin == "25")
            args.Add("-lilubetaall");
        return string.Join(' ', args);
    }
}
