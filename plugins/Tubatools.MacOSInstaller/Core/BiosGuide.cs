using System.Net;
using System.Text;

namespace Tubatools.MacOSInstaller.Core;

internal static class BiosGuide
{
    public static string BuildHtml(HardwareProfile hw, CompatibilityReport report, BuildOutcome outcome, MacTarget target)
    {
        string vendor = hw.BoardManufacturer.Length > 0 ? hw.BoardManufacturer : hw.ComputerManufacturer;
        string bootKey = BootKey(vendor);
        string menuKey = BootMenuKey(vendor);
        string board = WebUtility.HtmlEncode($"{hw.BoardManufacturer} {hw.BoardProduct}".Trim());
        string gpu = WebUtility.HtmlEncode(string.Join(" / ", hw.Gpus.Select(g => g.Name)));
        string cpu = WebUtility.HtmlEncode(hw.CpuName);

        var settings = new List<string>
        {
            "Secure Boot：关闭；如果只有 “Other OS” 选项，就选 Other OS。",
            "CSM / Compatibility Support Module：关闭（纯 UEFI）。",
            "Above 4G Decoding：开启。",
            "VT-d（Intel）/ IOMMU（AMD）：关闭。",
            "SATA 模式：AHCI，不要用 RAID / Intel RST。",
            "Intel VMD Controller：关闭。",
            "Fast Boot：关闭。",
            "Resizable BAR / Re-Size BAR：关闭（尤其是 AMD Navi 显卡）。",
            "CFG Lock：如果 BIOS 里有，关闭；没有就保持默认。",
            "XHCI Hand-off：开启。",
            "TPM / PTT / FTPM：可以先保持开启；如果启动被拦截再关闭。"
        };
        if (report.HasSupportedDgpu)
            settings.Add("独显输出：优先把显示器接在 AMD 显卡的 DisplayPort 上，安装阶段不要接多块屏。");
        if (report.IsLaptop)
            settings.Add("笔记本：如果 BIOS 没有 Above 4G 选项，先保持默认；DVMT 显存尽量设为 64 MB 或更高。");

        var warnings = new StringBuilder();
        foreach (string w in report.Errors.Concat(report.Warnings))
            warnings.Append("<li>").Append(WebUtility.HtmlEncode(w)).AppendLine("</li>");
        foreach (string w in outcome.Warnings)
            warnings.Append("<li>").Append(WebUtility.HtmlEncode(w)).AppendLine("</li>");

        var settingsHtml = new StringBuilder();
        foreach (string s in settings)
            settingsHtml.Append("<li>").Append(WebUtility.HtmlEncode(s)).AppendLine("</li>");

        string html = $$"""
            <!doctype html>
            <html lang="zh-CN">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>BIOS 与安装步骤 · {{WebUtility.HtmlEncode(target.DisplayName)}}</title>
            <style>
              :root { color-scheme: dark; --bg:#141416; --card:#1d1d21; --line:#303038; --text:#f2f2f4; --muted:#a6a6b0; --accent:#0a84ff; --warn:#ffb02e; }
              * { box-sizing: border-box; }
              body { margin:0; background:var(--bg); color:var(--text); font:15px/1.7 "Segoe UI", "Microsoft YaHei", sans-serif; }
              main { max-width: 980px; margin: 0 auto; padding: 48px 28px 80px; }
              h1 { font-size: 30px; margin: 0 0 8px; letter-spacing: .2px; }
              h2 { font-size: 20px; margin: 36px 0 12px; }
              .sub { color: var(--muted); margin-bottom: 28px; }
              .grid { display:grid; grid-template-columns: repeat(auto-fit,minmax(220px,1fr)); gap:12px; }
              .card { background:var(--card); border:1px solid var(--line); border-radius:14px; padding:18px 20px; }
              .label { color:var(--muted); font-size:12px; text-transform:uppercase; letter-spacing:.08em; }
              .value { font-size:16px; font-weight:600; margin-top:4px; word-break:break-word; }
              ol, ul { padding-left: 22px; }
              li { margin: 7px 0; }
              .note { border-left:3px solid var(--accent); padding:12px 16px; background:#0f2237; border-radius:8px; }
              .warn { border-left:3px solid var(--warn); padding:12px 16px; background:#2a2110; border-radius:8px; }
              code { background:#0e0e11; border:1px solid var(--line); padding:1px 6px; border-radius:6px; }
              .step { background:var(--card); border:1px solid var(--line); border-radius:12px; padding:16px 18px; margin:10px 0; }
              .step b { color:#fff; }
              a { color: var(--accent); }
              footer { margin-top:44px; color:var(--muted); font-size:13px; }
            </style>
            </head>
            <body><main>
              <h1>BIOS 设置与安装步骤</h1>
              <div class="sub">为这台电脑生成的专属步骤 · {{WebUtility.HtmlEncode(target.DisplayName)}} · {{board}}</div>

              <div class="grid">
                <div class="card"><div class="label">主板</div><div class="value">{{board}}</div></div>
                <div class="card"><div class="label">处理器</div><div class="value">{{cpu}}</div></div>
                <div class="card"><div class="label">显卡</div><div class="value">{{gpu}}</div></div>
                <div class="card"><div class="label">SMBIOS 机型</div><div class="value">{{WebUtility.HtmlEncode(outcome.Model)}}</div></div>
                <div class="card"><div class="label">进入 BIOS</div><div class="value">{{bootKey}}</div></div>
                <div class="card"><div class="label">启动菜单</div><div class="value">{{menuKey}}</div></div>
              </div>

              {{(warnings.Length > 0 ? $"<h2>先看这些警告</h2><div class=\"warn\"><ul>{warnings}</ul></div>" : "")}}

              <h2>一、BIOS 设置</h2>
              <ul>{{settingsHtml}}</ul>

              <h2>二、从 U 盘启动</h2>
              <div class="step"><b>1.</b> 把制作好的 U 盘插到主板后置 USB 接口（优先 USB 2.0 口）。</div>
              <div class="step"><b>2.</b> 开机连按 <code>{{menuKey}}</code> 打开启动菜单，选择 <code>UEFI: U 盘名称</code>。</div>
              <div class="step"><b>3.</b> 看到 OpenCore 菜单后，选择带 <code>Install macOS</code> 字样的条目；如果没有，按空格键显示全部条目。</div>
              <div class="step"><b>4.</b> 第一次启动会比较慢，屏幕会滚动大量文字，这是正常的详细模式输出。</div>

              <h2>三、安装 macOS</h2>
              <div class="step"><b>1.</b> 进入安装器后，先打开「磁盘工具」。</div>
              <div class="step"><b>2.</b> 菜单栏选择「显示所有设备」，选中准备装 macOS 的那块 SSD，抹掉为 <code>APFS</code> / <code>GUID 分区图</code>。内置硬盘和 Windows 分区不要动。</div>
              <div class="step"><b>3.</b> 关闭磁盘工具，选择「安装 macOS」，按提示继续。安装过程中会自动重启数次。</div>
              <div class="step"><b>4.</b> 每次重启仍然从 U 盘启动，在 OpenCore 菜单选择 <code>macOS Installer</code>，直到进入「欢迎」设置界面。</div>

              <h2>四、装完之后</h2>
              <div class="step"><b>1.</b> 进入 macOS 后，把 U 盘里的 <code>EFI</code> 文件夹复制到装 macOS 那块 SSD 的 EFI 分区，不要复制到 Windows 盘，然后才能脱离 U 盘启动。</div>
              <div class="step"><b>2.</b> 用 OpenCore Configurator 或 ProperTree 打开 <code>config.plist</code>，确认 <code>PlatformInfo</code> 里是你生成的序列号。</div>
              <div class="step"><b>3.</b> 如果声音、网卡、触控板不正常，回到「工具」页做启动日志诊断，或按提示调整 layout-id / kext。</div>

              <div class="note">
                本页面由 macOS 安装助手根据当前硬件生成。OpenCore 的完整文档与排错指南见
                <a href="https://dortania.github.io/OpenCore-Install-Guide/" target="_blank" rel="noreferrer">Dortania OpenCore Install Guide</a>。
              </div>

              <footer>生成时间：{{DateTime.Now:yyyy-MM-dd HH:mm}} · 序列号：{{WebUtility.HtmlEncode(outcome.Serial)}}</footer>
            </main></body></html>
            """;
        return html;
    }

    private static string BootKey(string vendor)
    {
        string v = vendor.ToLowerInvariant();
        if (v.Contains("asus") || v.Contains("asrock")) return "Del 或 F2";
        if (v.Contains("msi")) return "Del";
        if (v.Contains("gigabyte")) return "Del";
        if (v.Contains("lenovo")) return "F2 或 Fn+F2";
        if (v.Contains("hp") || v.Contains("惠普")) return "F10 或 Esc";
        if (v.Contains("dell") || v.Contains("戴尔")) return "F2";
        if (v.Contains("acer") || v.Contains("宏碁")) return "Del 或 F2";
        if (v.Contains("honor") || v.Contains("huawei") || v.Contains("荣耀") || v.Contains("华为")) return "F2";
        return "Del 或 F2";
    }

    private static string BootMenuKey(string vendor)
    {
        string v = vendor.ToLowerInvariant();
        if (v.Contains("asus")) return "F8";
        if (v.Contains("asrock")) return "F11";
        if (v.Contains("msi")) return "F11";
        if (v.Contains("gigabyte")) return "F12";
        if (v.Contains("lenovo")) return "F12";
        if (v.Contains("hp") || v.Contains("惠普")) return "F9";
        if (v.Contains("dell") || v.Contains("戴尔")) return "F12";
        if (v.Contains("acer") || v.Contains("宏碁")) return "F12";
        if (v.Contains("honor") || v.Contains("huawei") || v.Contains("荣耀") || v.Contains("华为")) return "F12";
        return "F12";
    }
}
