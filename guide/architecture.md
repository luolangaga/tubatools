[← 返回 README](../README.md)

# 架构

```
TubaWinUi3.WinUI3/            主程序（WinUI 3 / .NET 10 / Windows App SDK）
  App.xaml.cs                 启动、提权、全局异常兜底
  MainWindow.xaml             自定义标题栏 + NavigationView + Frame，AI 吉祥物常驻顶栏
  Pages/                      各工具页（内置工具、设置、搜索、时间同步、镜像下载……）
  Services/
    BuiltinTools/             47 款内置工具实现（IBuiltinTool + BuiltinToolRegistry）
    Ai/                       AI 助手适配层（TubaChatProvider / AgentToolAdapter / 防死循环护栏）
    GameTunnel/               游戏联机（EasyTier 虚拟网络、房间码、端口转发、防火墙、端口探测）
    JunkCleaner/              垃圾清理引擎（FluentCleaner.Core 移植 + Winapp2 规则库）
    TimeSync/                 时间同步（w32tm 封装、SNTP 探测、预设服务器）
    ToolCatalog.cs            扫描 Tools/ + tools.json，合并架构变体，生成工具卡片
    HardwareInfoService.cs    WMI / CPU-Z 硬件信息（P/Invoke）
    LiteMonitorService.cs     LibreHardwareMonitor 实时监控（单例）
  Assets/
    BuiltinIcons/             内置工具的彩色矢量图标（按 Fluent System Icons 规范手绘）
    DocEngine/                WebView2 文档引擎（markdown-it / pdf.js / SheetJS / pdf-lib）
  Strings/<lang>/             界面语言资源（zh-CN / en-US）
  Metadata/tools.json         工具元数据的单一事实源（描述 / 排序 / 跨分类 / 内置工具挂载）
TubaWinUI3.BackEnd/           NativeAOT 辅助进程（主动拦截）
TubaWinUi3.Tui/               .NET 8 Native AOT 控制台 TUI 兼容版
TubaWinUi3.Tests/             xUnit 测试（工具目录 / 时间同步 / 联机 / 垃圾清理 / 本地化……）
Tools/                        随包收录的第三方工具（按中文分类目录存放）
installer*.iss · Launcher/    Inno Setup 安装脚本与原生启动器
website-winui3/ · android-tuba-installer/ · file-transfer-web/ · cloudflare-worker/
                              官网 / 安卓安装助手 / 局域网传输 / 联机信令（各自独立工具链）
```

几个值得一提的实现细节：

* **工具清单是数据驱动的** —— `Metadata/tools.json` 是唯一事实源：描述、卡片顺序、跨分类副本、内置工具挂载全在里面；目录本身或其中任一可执行文件被 `match` 命中才会收录，未收录的目录不会展示。
* **只扫描一次，多架构合并成一张卡** —— x64 / x86 / ARM64 变体合并展示，并按当前系统自动选中合适的架构。
* **图标三选一图层** —— 内置工具优先用彩色矢量 SVG，缺图就回退到字体字形，卡片模板三层并存、按需显隐，所以漏配图标只会退回旧样式、不会开天窗。
* **CPU 温度 / 频率 / 功耗走 MSR** —— 依赖 PawnIO 驱动：检测到「已安装但没加载」会自动补启动，仍不可用时给出官方安装引导；FPS 覆盖层则走 ETW 的 `DxgKrnl` Present 事件，不需要驱动。
* **AI 助手有会话级护栏** —— 同会话重复的工具调用第二次直接拦截，思维链与工具结果有长度上限，连续 6 轮纯工具调用且无进展会注入终止指令（`AgentToolLoopGuard`），两套引擎共用。
* **文档转换四引擎逐级回退** —— OfficeCLI 原生渲染逐页截图 → WebView2 文档引擎（pdf.js / SheetJS）→ 纯 C# 转换器 → 本机 Office / WPS COM。
* **垃圾清理是规则库驱动** —— 内置 Winapp2.ini / Winappx.ini 两个规则库，两阶段执行（只读分析 → 真删），离线开箱即用，也能一键更新规则库。
* **快捷方式不用 PowerShell 生成** —— 开始菜单搜索与「发送到桌面」共用进程内的 WScript.Shell COM，避免中文路径经子进程命令行变成乱码。
* **界面多语言即时生效** —— 资源在 `Strings/`，工具元数据用 `Metadata/Tools_<语言>.json` 覆盖层，切换语言不用重启。
