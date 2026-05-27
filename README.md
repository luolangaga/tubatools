# 图吧工具箱 TubaWinUi3

官网：[https://tubawinui3.cn](https://tubawinui3.cn)

一个用 WinUI 3 做的 PC 硬件工具集合，把硬件检测、压力测试、系统优化之类的工具收在一起，打开就能用。

## 功能

- **一键启动工具** -- 自动扫描 `Tools/` 文件夹里的可执行文件，按分类展示，点一下就能跑
- **实时搜索** -- 按名字或路径搜索工具
- **硬件信息** -- 通过 WMI 读取 CPU、内存、显卡、硬盘、显示器等信息
- **收藏夹** -- 常用工具加收藏，下次直接找
- **管理员运行** -- 一键以管理员身份启动
- **发送到桌面** -- 一键创建桌面快捷方式
- **自动更新** -- 启动时静默检查，有新版本会提醒
- **主题切换** -- 亮色/暗色/跟随系统

## 收录工具

总共 **94 款**，按类别整理如下：

---

### 处理器工具（9 款）

| 工具 | 说明 |
|------|------|
| CPU-Z | 处理器、主板、内存和显卡基础信息查看 |
| Core Temp | CPU 每个核心的实时温度监控，支持系统托盘显示 |
| C2CLatency | CPU 缓存到缓存延迟测试，测量 L1/L2/L3 缓存间通信延迟 |
| LinX | 基于 Intel LINPACK 的 CPU 稳定性测试，烤机用 |
| Prime95 | GIMPS 项目分布式计算客户端，广泛用于 CPU 稳定性测试 |
| Super PI | 圆周率计算性能测试，衡量 CPU 单核性能 |
| ThrottleStop | CPU 降频监控和功耗控制，可解除笔记本 CPU 功耗限制 |
| wPrime | 多线程 CPU 性能测试，通过计算质数衡量多核计算能力 |
| 国际象棋 | Fritz 国际象棋基准测试，评估 CPU 多核运算性能 |

### 显卡工具（11 款）

| 工具 | 说明 |
|------|------|
| GPU-Z | 显卡型号、核心参数、显存、传感器和 BIOS 信息查看 |
| FurMark | GPU 压力测试（OpenGL），俗称"显卡杀手"，检测显卡稳定性 |
| DDU | 显卡驱动彻底卸载工具，安全清除 AMD/NVIDIA/Intel 驱动残留 |
| GpuTest | 跨平台 GPU 基准测试，支持 OpenGL 压力测试和性能评分 |
| nvidiaInspector | NVIDIA 显卡信息查看和超频工具，可调电压、频率和风扇曲线 |
| nvidiaProfileInspector | NVIDIA 显卡驱动配置文件编辑器，可修改隐藏驱动设置和 SLI 配置 |
| NVFlash | NVIDIA 显卡 BIOS 刷写工具 |
| MorePowerTool | GPU 功耗限制和电压编辑工具 |
| FPT64 | Intel Flash Programming Tool，用于 GPU BIOS 刷写 |
| AMD 显卡驱动下载 | AMD 显卡驱动官方下载入口 |
| NVIDIA 显卡驱动下载 | NVIDIA 显卡驱动官方下载入口 |

### 显示器工具（3 款）

| 工具 | 说明 |
|------|------|
| 色域检测 | 显示器色域覆盖率和面板信息查看 |
| 在线屏幕测试 | 通过浏览器进行坏点、色彩和灰阶检测 |
| UFO 测试 | 在线显示器刷新率和运动模糊测试 |

### 内存工具（7 款）

| 工具 | 说明 |
|------|------|
| MemTest | 内存稳定性测试，反复读写检测内存错误 |
| MemTest64 | 64 位内存稳定性测试，支持大容量内存错误检测 |
| MemTest Pro | 专业版内存测试，支持多线程和更全面的错误检测 |
| TestMem5 (TM5) | 第五代内存稳定性测试，支持多种测试配置和极限压力测试 |
| Thaiphoon | 内存 SPD 信息读取，查看制造商、时序、频率等详细参数 |
| ZenTimings | AMD Zen 架构内存时序和频率查看 |
| 魔方内存盘 | 虚拟内存盘（RAM Disk）创建工具，将部分内存虚拟为高速磁盘 |

### 硬盘工具（20 款）

| 工具 | 说明 |
|------|------|
| CrystalDiskMark | 硬盘读写速度基准测试 |
| CrystalDiskInfo | 硬盘 SMART、健康状态、温度和通电时间查看 |
| AS SSD Benchmark | SSD 专用基准测试，测量顺序/随机读写速度和访问时间 |
| ATTO 磁盘基准测试 | 测量不同块大小下的磁盘读写速度 |
| HDTune | 硬盘性能测试和健康检测 |
| TxBENCH | SSD 存储设备基准测试，支持多种读写模式性能评估 |
| SSD-Z | SSD 固态硬盘信息查看和健康检测 |
| DiskGenius | 磁盘分区、数据恢复、坏道检测和分区表维护 |
| Defraggler | 磁盘碎片整理，支持按文件/文件夹级别整理 |
| SpaceSniffer | 磁盘空间可视化分析，树状图展示文件夹占用 |
| WizTree | 超快磁盘空间分析，用 MFT 快速扫描 NTFS 分区 |
| WinDirStat | 磁盘空间使用统计，彩色方块图展示文件类型和空间占用 |
| FINALDATA | 数据恢复，恢复误删除、格式化或病毒破坏的文件 |
| 魔方数据恢复 | 文件数据恢复，支持误删除、格式化和分区丢失场景 |
| H2testw | U 盘/存储卡容量真实性检测，鉴别扩容盘 |
| MyDiskTest | U 盘/存储卡扩容检测和速度测试 |
| URWTest | U 盘读写速度测试 |
| FlashMaster | U 盘量产工具，芯片检测和量产修复 |
| LLFTOOL | 低级格式化工具，对磁盘进行彻底的底层擦除 |
| SSD Utils | SSD 固态硬盘在线工具入口 |

### 烤鸡工具（2 款）

| 工具 | 说明 |
|------|------|
| FurMark | GPU 压力测试，检测显卡稳定性（OpenGL） |
| FurMark 64 位 | FurMark 的 64 位版本 |

### 综合检测（5 款）

| 工具 | 说明 |
|------|------|
| AIDA64 | 系统硬件信息、传感器监控和稳定性测试 |
| HWiNFO | 专业硬件信息读取、传感器监控和日志记录 |
| HWMonitor | 硬件温度、电压和风扇转速实时监控 |
| Speccy | 系统硬件信息快速查看，简洁的硬件配置摘要 |
| RWEverything | 硬件寄存器读写，可访问 PCI、SMBus、Super I/O 等底层信息 |

### 声卡工具（1 款）

| 工具 | 说明 |
|------|------|
| LatencyMon | 系统实时音频延迟检测，分析 DPC/ISR 延迟，排查音频卡顿和爆音 |

### 外设工具（7 款）

| 工具 | 说明 |
|------|------|
| Keyboard Test Utility | 键盘按键测试，逐键检测每个按键是否正常 |
| KeyTweak | 键盘按键重映射，自定义修改按键映射关系 |
| Areson Mouse Test | 鼠标按键和滚轮测试 |
| MouseTester | 鼠标性能测试，检测移动轨迹、抖动和按键延迟 |
| Mouse Rate | 鼠标回报率检测，实时测量 USB 报告速率 |
| 鼠标单击变双击测试器 | 鼠标微动故障检测，识别单击变双击的老化问题 |
| 在线外设测试中心 | 通过浏览器测试鼠标、键盘和显示器 |

### 游戏工具（11 款）

| 工具 | 说明 |
|------|------|
| Steam | Steam 游戏平台下载入口 |
| Epic Games | Epic Games 游戏平台下载入口 |
| Battle.net | 暴雪战网平台下载入口 |
| EA App | EA 游戏平台下载入口 |
| 游戏加加 | 硬件监控和游戏优化工具下载入口 |
| 风灵月影 | 单机游戏修改器下载入口 |
| 斧牛加速器 | 游戏网络加速器下载入口 |
| 雷神加速器 | 游戏网络加速器下载入口，支持按时计费 |
| 玩家动力 | 游戏加速器下载入口 |
| 迅雷加速器 | 迅雷游戏网络加速器下载入口 |
| 迅游加速器 | 老牌游戏加速器下载入口 |

### 其他工具（18 款）

| 工具 | 说明 |
|------|------|
| Everything | 超快文件搜索，基于 NTFS USN 日志瞬间找到文件 |
| Dism++ | Windows 系统精简和优化，图形化的 DISM 管理工具 |
| Geek Uninstaller | 轻量级软件卸载，支持强制卸载和清理残留注册表 |
| Process Explorer | 高级进程管理，树状结构显示进程关系和资源占用 |
| BlueScreenView | 蓝屏崩溃转储分析，查看 BSOD 错误代码和导致崩溃的驱动 |
| WinDbg | 微软官方内核级调试器，驱动和系统级问题深度调试 |
| DirectX Repair | DirectX 修复工具，自动检测和修复组件缺失或损坏 |
| Rufus | U 盘启动盘制作，快速创建可引导 USB 安装盘 |
| Ventoy | 多系统 U 盘启动制作，直接拷贝 ISO 文件即可启动 |
| UltraISO | 光盘映像文件制作和编辑，支持 ISO 创建、编辑和刻录 |
| BatteryInfoView | 笔记本电池容量、循环次数、损耗和实时状态查看 |
| DesktopOK | 桌面图标位置保存和恢复，防止分辨率变化后图标排列混乱 |
| GifCam | GIF 动画录制，窗口框选区域直接录制成 GIF |
| MSI Afterburner | 显卡超频监控工具下载入口，支持所有品牌显卡 |
| MSDN I Tell You | 微软原版系统镜像下载入口 |
| CPU/显卡天梯图 | CPU 和显卡性能天梯图，直观对比各型号性能排名 |
| 皮肤编辑器 | 图吧工具箱皮肤在线编辑器入口 |
| 游戏加加 | 硬件监控和游戏优化工具下载入口 |

---

## 构建 & 运行

需要 .NET 10 SDK：

```bash
dotnet build        # 编译
dotnet run          # 运行（Unpackaged 模式）
```

支持 x86、x64、ARM64，默认使用当前系统架构。

## 技术栈

- .NET 10 + WinUI 3（Windows App SDK 1.8）
- System.Management（WMI 硬件信息查询）
- System.Drawing.Common（图标提取）
- 最低支持 Windows 10 1809

## 新手开发指南

本节面向刚接触本项目的新手开发者，介绍如何为图吧工具箱添加新的内置工具，以及 Git 协作流程。

### 环境准备

1. 安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
2. 安装 [Visual Studio 2022 17.14+](https://visualstudio.microsoft.com/) 或 [VS Code](https://code.visualstudio.com/)（配合 C# Dev Kit 扩展）
3. 安装 [Git](https://git-scm.com/downloads)

```bash
git clone <仓库地址>
cd tubawinui3
dotnet build        # 确认编译通过
dotnet run          # 运行（Unpackaged 模式）
```

---

### 项目结构速览

```
App.xaml.cs                       → 应用入口，创建 MainWindow
MainWindow.xaml.cs                → 导航框架，侧边栏 + Frame
Pages/
  HomePage.xaml.cs                → 外部工具网格（扫描 Tools/ 文件夹）
  BuiltinToolsPage.xaml.cs        → 内置工具页面（展示 IBuiltinTool 列表）
  HardwarePage.xaml.cs            → WMI 硬件信息
  SettingsPage.xaml.cs            → 设置页
Services/
  IBuiltinTool.cs                 → 内置工具接口（核心）
  BuiltinToolRegistry.cs          → 内置工具注册表
  BuiltinTools/                   → 所有内置工具的实现
    KeyboardTestTool.cs
    DiskSpaceAnalyzerTool.cs
    ...
  ToolCatalog.cs                  → 外部工具扫描（Tools/ 文件夹）
  ToolMetadataService.cs          → 工具元数据（tools.json）
  *Service.cs                     → 各工具对应的后端服务
Models/
  ToolItem.cs                     → 外部工具数据模型
Metadata/
  tools.json                      → 外部工具的描述/发布者/下载链接
Tools/                            → 第三方可执行文件（按中文分类文件夹组织）
```

---

### 添加内置工具（4 步）

内置工具是直接嵌入在应用中的功能，无需外部 exe。所有内置工具都实现 `IBuiltinTool` 接口。

#### 第 1 步：创建工具类

在 `Services/BuiltinTools/` 下新建一个 `.cs` 文件，实现 `IBuiltinTool` 接口：

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class MyNewTool : IBuiltinTool
{
    public string Id => "my-new-tool";           // 唯一标识，用 kebab-case
    public string Name => "我的工具";              // 显示名称
    public string Description => "这是一个示例内置工具。"; // 工具描述
    public string Glyph => "\uE8E5";             // Segoe MDL2 Assets 图标
    public string Category => "系统工具";          // 分类（已有的：系统工具/网络工具/外设工具/硬件信息...）
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog; // 工具类型

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var dialog = new ContentDialog
        {
            Title = Name,
            Content = "Hello from MyNewTool!",
            CloseButtonText = "关闭",
            XamlRoot = context.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
```

#### 第 2 步：选择工具类型（BuiltinToolKind）

| 类型 | 说明 | 适用场景 |
|------|------|----------|
| `Dialog` | 弹窗式，工具 UI 在 ContentDialog 中展示 | 需要交互界面的工具（键盘测试、端口查看） |
| `BackgroundTask` | 后台执行，完成后通知 | 快速查询类（WiFi 密码查看） |
| `ProgressTask` | 带进度的长时间任务 | 需要进度条的任务（网速测试、电池报告） |
| `InstantAction` | 即时操作，无 UI | 一键执行的动作（刷新 DNS） |

#### 第 3 步：注册工具

打开 `Services/BuiltinToolRegistry.cs`，在 `RegisterDefaults()` 方法中添加一行：

```csharp
public static void RegisterDefaults()
{
    // ... 已有工具 ...
    Register(new MyNewTool());   // ← 添加这一行
}
```

#### 第 4 步：运行验证

```bash
dotnet build
dotnet run
```

启动后在左侧导航点击"内置工具"，即可看到新工具出现在列表中。

---

### 内置工具开发模式参考

项目中已有 12 个内置工具，可以作为参考：

| 工具文件 | 类型 | 特点 |
|----------|------|------|
| `KeyboardTestTool.cs` | Dialog | 纯弹窗交互，KeyDown/KeyUp 事件处理 |
| `DiskSpaceAnalyzerTool.cs` | Dialog | 打开独立 Window，Canvas 自绘，复杂 UI |
| `PortViewerTool.cs` | Dialog | 列表 + 搜索 + 筛选，后台数据加载 |
| `HostsEditorTool.cs` | Dialog | CRUD 操作，保存/备份，未保存提醒 |
| `WifiPasswordTool.cs` | BackgroundTask | 后台获取数据，加载状态切换 |
| `SpeedTestTool.cs` | ProgressTask | 进度条 + 取消，长时间任务 |
| `BatteryReportTool.cs` | ProgressTask | 异步加载 + 空状态处理 |
| `BsodAnalysisTool.cs` | Dialog | 文件解析，结果展示 |
| `JunkCleanerTool.cs` | ProgressTask | 扫描 + 清理两阶段 |
| `CertBlockTool.cs` | Dialog | 证书管理 |
| `PowerMonitorTool.cs` | BackgroundTask | 实时监控数据 |
| `WingetInstallerTool.cs` | Dialog | 软件包管理 |

**常见模式**：

- **状态管理**：工具 UI 中的控件引用通常通过 `ScrollViewer.Tag` 存储一个内部 State 类（如 `SpeedTestState`），避免字段初始化问题。
- **异步加载**：先显示加载动画（ProgressRing），`await Task.Run(...)` 执行后台操作，完成后切换到内容面板。
- **颜色常量**：使用 `ThemeColors` 静态类保持风格统一，强调色用 `AccentBlue/Green/Orange/Red/Purple`。
- **卡片布局**：`MakeStatCard()` 是常用的统计卡片构建方法，在各工具中反复出现，可参考复制。

---

### 添加外部工具（2 步）

外部工具是放在 `Tools/` 文件夹中的第三方可执行文件，应用会自动扫描并展示。

#### 第 1 步：放入工具文件

将工具放在对应的中文分类文件夹下（如 `Tools/硬盘工具/CrystalDiskMark/`）。支持的文件类型：

`.exe` `.bat` `.cmd` `.lnk` `.msc` `.ps1` `.vbs`

如果文件夹内有多个可执行文件，应用会按以下优先级选择主入口：
1. 文件名与文件夹名完全匹配
2. 去掉架构后缀（x64/x86/ARM64）后匹配
3. 根目录下的第一个可执行文件

#### 第 2 步：添加元数据（可选）

编辑 `Metadata/tools.json`，添加描述和下载链接：

```json
{
  "match": "CrystalDiskMark",
  "description": "硬盘读写速度基准测试工具。",
  "publisher": "Crystal Dew World",
  "tags": [ "硬盘", "速度测试" ]
}
```

- `match`：大小写不敏感的子串匹配，会同时匹配文件名和相对路径
- `downloadUrl`：如果工具不内置而提供下载，填入下载地址。`gh:用户名/仓库名` 格式表示从 GitHub Release 下载
- `downloadFilter`：下载文件名通配符筛选，如 `"*UserSetup*x64*.exe"`

---

### 常用 Segoe MDL2 图标

内置工具的 `Glyph` 使用 Segoe MDL2 Assets 字体：

| 图标 | Glyph | 用途 |
|------|-------|------|
| 🖥 | `\uE975` | 设备/电脑 |
| 🔧 | `\uE90F` | 设置/工具 |
| 📊 | `\uE774` | 图表/监控 |
| 🔍 | `\uE721` | 搜索 |
| ⚡ | `\uE8A0` | 电力/性能 |
| 🌐 | `\uE774` | 网络 |
| 🗑 | `\uE74D` | 删除 |
| 📋 | `\uE8C8` | 复制/列表 |
| ▶ | `\uE768` | 播放/运行 |
| 🔄 | `\uE72C` | 刷新 |

完整图标列表：[Segoe MDL2 Assets](https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-ui-symbol-icon)

---

### Git 协作流程

```bash
git clone <仓库地址> && cd tubawinui3   # 克隆
git checkout -b feature/xxx              # 新建分支开发
git add <文件> && git commit -m "feat: 添加xxx"  # 提交
git push origin feature/xxx              # 推送，然后创建 PR
```

提交信息格式：`feat:` 新功能 / `fix:` 修复 / `docs:` 文档 / `refactor:` 重构

> 提交前确保 `dotnet build` 通过，不要提交 `bin/` `obj/` `.pfx` `.cer` 等文件。

---

## 致谢

收录的第三方工具版权归原作者所有，本项目仅做整理归集。感谢：

- [Windows App SDK (WinUI 3)](https://github.com/microsoft/WindowsAppSDK)
- [Win2D](https://github.com/microsoft/Win2D)
- 所有收录工具的开发者

## 开源协议

MIT


[![Stargazers over time](https://starchart.cc/luolangaga/tubatool.svg?variant=adaptive)](https://starchart.cc/luolangaga/tubatool)
