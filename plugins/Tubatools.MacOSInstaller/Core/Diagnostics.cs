using System.Text;
using System.Text.RegularExpressions;

namespace Tubatools.MacOSInstaller.Core;

internal static class Diagnostics
{
    public static IReadOnlyList<string> FindLogs()
    {
        var files = new List<string>();
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Removable && drive.DriveType != DriveType.Fixed) continue;
                if (!drive.IsReady) continue;
                foreach (string pattern in new[] { "opencore-*.txt", "panic-*.txt" })
                {
                    files.AddRange(Directory.EnumerateFiles(drive.RootDirectory.FullName, pattern, SearchOption.TopDirectoryOnly));
                }
            }
            catch { }
        }
        return files.OrderByDescending(File.GetLastWriteTimeUtc).Take(8).ToList();
    }

    public static DiagnosticResult Analyze(IEnumerable<string> logFiles, string stoppedAt, string cpuHint)
    {
        var result = new DiagnosticResult { Cpu = cpuHint };
        var texts = new List<(string Path, string Text)>();
        foreach (string file in logFiles)
        {
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length > 8L * 1024 * 1024) continue;
                string text = File.ReadAllText(file, Encoding.UTF8);
                texts.Add((file, text));
                result.Notes.Add($"读取：{file}（{info.Length / 1024} KB）");
            }
            catch (Exception ex)
            {
                result.Notes.Add($"无法读取 {file}：{ex.Message}");
            }
        }

        string all = string.Join("\n", texts.Select(t => t.Text)).ToLowerInvariant();
        string said = stoppedAt.ToLowerInvariant();
        string source = texts.Count > 0 ? string.Join("、", texts.Select(t => Path.GetFileName(t.Path))) : "用户输入";
        result.LogPath = texts.FirstOrDefault().Path ?? "";

        void Add(string title, string cause, params string[] steps)
        {
            result.Findings.Add(new DiagnosticFinding
            {
                Title = title,
                Cause = cause,
                Steps = steps.ToList(),
                Source = source
            });
        }

        bool accessDenied = all.Contains("access denied") && all.Contains("driver");
        if (accessDenied)
            Add("Secure Boot 拦截了 OpenCore 驱动",
                "固件以 Access Denied 拒绝了 OpenCore 需要加载的 .efi 驱动，安装器因此无法读取。",
                "进入 BIOS 关闭 Secure Boot；有些主板需要把 OS Type 改成 Other OS，或清除 Secure Boot 密钥。",
                "保存后重新从 U 盘启动，选择 macOS 安装项。");

        if (Regex.IsMatch(all, @"oc:.*(failed to load configuration|failed to parse|invalid config|no such file)"))
            Add("OpenCore 无法读取 config.plist",
                "OpenCore 找不到或无法解析 OC/config.plist，通常是 EFI 目录不完整、config 被编辑器改坏，或 plist 格式错误。",
                "确认 U 盘根目录是 EFI/OC/config.plist，而不是 EFI/EFI/OC/config.plist。",
                "用 ProperTree 重新打开 config.plist，确认没有语法错误。",
                "重新构建 EFI 并完整复制整个 EFI 文件夹。");

        if (Regex.IsMatch(all, @"oc:.*(driver.*(not signed|load failed)|failed to load driver)"))
            Add("OpenCore 驱动加载失败",
                "OpenCore 的 .efi 驱动没有通过固件校验，或 config.plist 里的 Driver 路径与实际文件不一致。",
                "关闭 Secure Boot，并把 BIOS 的 OS Type 设为 Other OS（如果有）。",
                "检查 EFI/OC/Drivers 下是否有 OpenRuntime.efi 与 OpenHfsPlus.efi，文件名大小写要一致。",
                "如果只改了 config.plist，重新用 ocvalidate 校验后再复制到 U 盘。");

        if (Regex.IsMatch(all, @"unsupported cpu|unsupported platform|cpu not supported"))
            Add("macOS 不认识当前 CPU",
                "内核在启动早期拒绝了这颗 CPU；第 11 代以后的 Intel 或 AMD 通常需要 CPUID 伪装/内核补丁。",
                "确认构建时选择的平台与 CPU 代际匹配。",
                "第 11 代以后 Intel 需要在 Kernel → Emulate 里配置 CPUID 伪装，或改用受支持的平台。",
                "AMD 需要专门的内核补丁集，当前通用构建不包含。");

        if (Regex.IsMatch(all, @"appleintelcpupowermanagement|appleacpicpu|ioapic|apic"))
            Add("CPU 电源管理或中断控制器报错",
                "macOS 在初始化 CPU 电源管理/IOAPIC 时失败，常见原因是 CFG Lock、VT-d 或 ACPI 表冲突。",
                "BIOS 关闭 CFG Lock；找不到该选项时保留 AppleXcpmCfgLock。",
                "关闭 VT-d / IOMMU，并确认没有启用 VMD/RAID。",
                "只保留必要的 SSDT，移除最近新增的 ACPI 补丁再试。");

        if (Regex.IsMatch(all, @"waiting for dsmos|dsmos has arrived|ioconsoleusers"))
            Add("显卡驱动没有正常初始化",
                "系统已经进入图形阶段，但 Apple 的显卡驱动没有接管显示输出，通常会黑屏或卡在进度条。",
                "确认显示器接在受支持的核显或 AMD 独显上，不要接 NVIDIA。",
                "检查 config.plist 的 AAPL,ig-platform-id 与 device-id 是否与当前平台匹配。",
                "Navi 显卡确认 boot-args 里有 agdpmod=pikera，并关闭 Resizable BAR。");

        if (Regex.IsMatch(all, @"apfs_module_start|apfs.*(panic|failed)|nvme.*(timeout|panic)"))
            Add("APFS 或 NVMe 驱动在启动时出错",
                "macOS 无法通过当前存储控制器读取 APFS 容器，可能是 NVMe 兼容性、VMD/RAID 或 NVMeFix 配置问题。",
                "BIOS 关闭 Intel VMD/RAID，SATA 改为 AHCI。",
                "确认 EFI 里有 NVMeFix.kext，并且 Kernel → Add 中已启用。",
                "换一个 M.2 插槽或暂时移除第二块 NVMe 做交叉测试。");

        if (Regex.IsMatch(all, @"hfsplus|openhfsplus") && Regex.IsMatch(all, @"cannot be found|not found|missing"))
            Add("U 盘缺少读取安装器的文件系统驱动",
                "EFI 里没有 HfsPlus/OpenHfsPlus.efi，通常是复制 EFI 时漏了文件。",
                "重新构建 EFI，确认 OC/Drivers 下有 OpenHfsPlus.efi 或 HfsPlus.efi。",
                "重新复制整个 EFI 文件夹到 U 盘根目录。");

        if (all.Contains("exitbs:start"))
            Add("OpenCore 交接给 macOS 后停住（EXITBS:START）",
                "OpenCore 已经把控制权交给 macOS，但内核没有继续输出。多数情况是固件设置让某个设备在交接时仍被占用。",
                "关闭 VT-d（Intel）或 IOMMU（AMD），通常在 Advanced / AMD CBS 里。",
                "关闭 CSM，开启 Above 4G Decoding。",
                "如果是 12 代以后 Intel，再尝试关闭 Hyper-Threading / E-cores 做一次安装测试。");

        if (all.Contains("still waiting for root device"))
            Add("macOS 找不到 U 盘（Still waiting for root device）",
                "安装器启动后无法通过 USB 端口访问 U 盘。",
                "换到主板后置的 USB 2.0 接口，避免前置面板和 USB 3.0 Hub。",
                "BIOS 里开启 XHCI Hand-off，关闭 Fast Boot。",
                "重新插拔 U 盘后再启动。");

        if (Regex.IsMatch(all, @"eb\.mm\.akm|couldn't allocate runtime area|stop 0x16"))
            Add("内核没有足够的连续内存空间",
                "Apple 的引导器在启动早期找不到可用的内存区域。",
                "开启 Above 4G Decoding，关闭 CSM。",
                "关闭 Resizable BAR / Re-Size BAR 后再试。",
                "如果主板有 “Above 4G MMIO” 或 “Large BAR” 选项，也一并关闭。");

        if (Regex.IsMatch(all, @"ocb: .*type 32|perform boot windows"))
            Add("这次启动进了 Windows，而不是 macOS",
                "OpenCore 菜单里选择了 Windows 条目，或者默认项指向了 Windows。",
                "重新启动，在 OpenCore 菜单用方向键选择带 Install macOS 的条目。",
                "如果看不到 macOS 条目，按空格键显示全部条目。");

        if (all.Contains("kernel trap") || all.Contains("panic-"))
            Add("macOS 启动时发生内核崩溃",
                "内核在启动阶段报错，日志里通常会指出出问题的驱动或内存区域。",
                "把 panic-*.txt 发出来查看具体驱动名。",
                "先移除最近新增的 kext 或不必要的 ACPI 补丁再试。",
                "关闭 XMP/EXPO，内存频率回到默认，再启动一次。");

        if (Regex.IsMatch(all, @"boot failure|prohibited|startimage failed|systemimage abort"))
            Add("Apple 引导器拒绝启动 macOS",
                "出现禁止符号或 boot failure，说明 boot.efi 不接受当前的机型或安全设置。",
                "关闭 Secure Boot，并把 SMBIOS 机型改成与 macOS 版本匹配的型号。",
                "确认 config.plist 里的 SecureBootModel 为 Default 或 Disabled。",
                "重新构建 EFI 后再试。");

        if (Regex.IsMatch(said, @"sata|disk|drive|硬盘|磁盘|看不到"))
            Add("安装器里看不到硬盘",
                "macOS 只能看到固件以 AHCI/NVMe 方式暴露的磁盘；RAID、Intel RST、VMD 会把磁盘藏起来。",
                "BIOS 里把 SATA 模式改成 AHCI，关闭 Intel VMD Controller。",
                "如果 Windows 也在这块盘上，先按 Windows 的安全模式步骤切换到 AHCI，否则 Windows 会无法启动。",
                "在安装器的磁盘工具里选择「显示所有设备」，再抹掉目标硬盘。");

        if (Regex.IsMatch(said, @"pci configuration|iopciconfigurator"))
            Add("启动停在 PCI configuration",
                "macOS 在枚举 PCI 设备时卡住，通常是内存映射或 Resizable BAR 设置导致。",
                "开启 Above 4G Decoding，关闭 Resizable BAR。",
                "在 boot-args 里临时加入 npci=0x3000 后再试。",
                "关闭 CSM，保存 BIOS 后重新启动。");

        if (said.Contains("pkdownload") || said.Contains("下载"))
            Add("安装器下载 macOS 失败",
                "安装器需要从 Apple 服务器下载系统文件，网络不通或系统时间错误都会导致失败。",
                "优先使用有线网络，安装阶段 Wi-Fi 往往不可用。",
                "在安装器菜单打开「终端」，输入 date 检查日期；日期错误会直接导致下载失败。",
                "如果仍然失败，换一个网络或稍后重试。");

        if (Regex.IsMatch(said, @"black screen|黑屏|cursor|鼠标"))
            Add("安装完成后黑屏，只剩鼠标指针",
                "macOS 已经启动，但显示输出没有走到当前接口，或显卡帧缓冲没有正确设置。",
                "只保留一台显示器，接在独显的 DisplayPort 上再启动。",
                "等待两分钟，按一下回车，有些机器只是设置界面没有绘制出来。",
                "如果是 AMD Navi 显卡，确认 boot-args 里有 agdpmod=pikera。");

        if (result.Findings.Count == 0)
        {
            Add("没有匹配到已知故障",
                "日志里没有出现常见的错误特征，或者只读到了 OpenCore 交接前的部分。",
                "把屏幕上最后一行文字复制到上面的输入框里再分析一次。",
                "确认 BIOS 已关闭 Secure Boot、CSM、VT-d/IOMMU，并开启 Above 4G。",
                "如果仍然无法启动，换一个 USB 接口并重新制作 U 盘。");
        }

        return result;
    }
}
