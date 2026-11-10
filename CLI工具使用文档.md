# 图吧工具箱CE CLI 工具使用文档

> 本文档收录图吧工具箱CE `Tools/` 目录下**所有支持命令行参数调用**的工具，并给出详细用法、参数表和可直接复制的示例。
> 适用范围：图吧工具箱CE v2026.01（`Tools/Version`）。文档中所有路径均以 `Tools\` 目录为基准。

---

## 目录

1. [通用说明](#通用说明)
2. [工具索引总表](#工具索引总表)
3. [处理器工具](#处理器工具)
4. [显卡工具](#显卡工具)
5. [硬盘工具](#硬盘工具)
6. [综合检测](#综合检测)
7. [其他工具](#其他工具)
8. [实用脚本组合示例](#实用脚本组合示例)
9. [附：无命令行参数的主要工具](#附无命令行参数的主要工具)

---

## 通用说明

### Tools 根目录如何定位

- **开发环境**：`TubaWinUi3.WinUI3\Tools\`（即本文档所在仓库目录）
- **打包后的绿色版**：`图吧工具箱WinUI3\Tools\`
- 下文所有相对路径均指 `Tools\` 下的路径，例如 `处理器工具\Prime95\prime95.exe`。

### 使用前必读

1. **中文/空格路径必须加双引号**：例如 `"Tools\显卡工具\GpuTest_Windows x64\GpuTest.exe"`、`"Core Temp x64.exe"`。
2. **架构选择**：同一工具常有 x86 / x64 / ARM64 多个版本（如 CPU-Z、HWiNFO、Autoruns）。64 位系统请优先用 x64 版，ARM64 设备用 ARM64 版。
3. **管理员权限**：凡是读写硬件、内核驱动、系统服务、磁盘分区的操作（FPT64、Ventoy、CrystalDiskInfo 部分功能、WinDbg 内核调试等）都需要**以管理员身份运行** CMD/PowerShell。
4. **退出码（%ERRORLEVEL%）**：命令行工具通常用退出码表示执行结果（0 = 成功），可在批处理脚本中做流程判断，文档中已标注支持的工具。
5. **交互式工具**：部分工具（如 Prime95、urwtest）执行后是**阻塞式**的（一直运行直到结束），在脚本中使用时请注意，或配合 `start` 命令后台启动。
6. 参数整理自各工具官方文档、工具内置帮助（help.txt / readme.txt）及官方说明页；标注 "※" 的工具参数来自社区通用文档，如有出入以工具内帮助为准。

---

## 工具索引总表

| 工具 | 分类 | 可执行文件 | CLI 能力 |
|------|------|-----------|---------|
| Prime95 | 处理器 | `处理器工具\Prime95\prime95.exe` | 静默单烤 CPU（`-t`） |
| FurMark | 显卡 | `烤鸡工具\FurMark_win64\furmark.exe` | 完整命令行：预设/自定义分辨率/时长/日志 |
| GpuTest | 显卡 | `显卡工具\GpuTest_Windows x64\GpuTest.exe` | 仅 GUI（实测命令行参数无效） |
| FPT64 | 显卡 | `显卡工具\FPT64\fptw64.exe` | BIOS 备份/刷写（Intel FPT 全套参数） |
| nvidiaInspector | 显卡 | `显卡工具\nvidiaInspector\nvidiaInspector.exe` | 命令行超频/风扇/电压控制 |
| nvidiaProfileInspector | 显卡 | `显卡工具\nvidiaProfileInspector\nvidiaProfileInspector.exe` | 驱动配置档导入导出/应用 |
| CrystalDiskInfo | 硬盘 | `硬盘工具\CrystalDiskInfo\DiskInfo64S.exe` | SMART 信息导出（/CopyExit） |
| Defraggler | 硬盘 | `硬盘工具\Defraggler\df.exe` | 命令行碎片整理/分析 |
| DiskGenius | 硬盘 | `硬盘工具\DiskGenius\DiskGenius.exe` | /cmd 脚本化分区管理 |
| urwtest | 硬盘 | `硬盘工具\URWTEST\urwtest_v18.exe` | 纯命令行坏块/可靠性测试 |
| WizTree | 硬盘 | `硬盘工具\WizTree\WizTree.exe` | CSV 导出/树图导出/MFT 导出 |
| AIDA64 | 综合检测 | `综合检测\AIDA64\aida64.exe` | 静默报告生成（/R /S /HTML 等） |
| HWiNFO | 综合检测 | `综合检测\hwinfo\HWiNFO64.exe` | 静默/仅传感器/日志 ※ |
| USBDeview | 其他 | `其他工具\USBDeview\USBDeview.exe` | 完整命令行：导出/禁用/卸载 USB 设备 |
| BlueScreenView | 其他 | `其他工具\bluescreenview\BlueScreenViewx64.exe` | 蓝屏 dump 导出/筛选 |
| BatteryInfoView | 其他 | `其他工具\BatteryInfoView\BatteryInfoView.exe` | 电池信息导出/监控 |
| Autoruns | 其他 | `其他工具\Autoruns\autorunsc64.exe` | 启动项全量扫描导出（CSV/XML） |
| Everything | 其他 | `其他工具\Everything\Everything.exe` | 命令行搜索/文件列表生成 |
| Ventoy | 其他 | `其他工具\ventoy\Ventoy2Disk.exe` | VTOYCLI 无交互安装/升级 |
| WinDbg | 其他 | `其他工具\WinDbg\windbg.exe` | 完整调试器命令行 |
| UltraISO | 其他 | `其他工具\ULTRAISO\ULTRAISO.exe` | 仅 GUI（实测命令行挂起） |

---

## 处理器工具

### Prime95 —— CPU 烤机

- **路径**：`处理器工具\Prime95\prime95.exe`
- **说明**：Mersenne 素数搜索程序，圈内最常用的 CPU 稳定性/散热测试工具。图吧工具箱CE的一键烤机脚本 `start.bat` 正是调用 `prime95.exe -t`。

**常用参数**

| 参数 | 说明 |
|------|------|
| `-t` | 启动 Torture Test（压力测试）模式，直接开始烤机，跳过交互界面 |

**示例**

```bat
:: 单烤 CPU（推荐配合 Core Temp / HWiNFO 监控温度功耗）
"Tools\处理器工具\Prime95\prime95.exe" -t

:: 烤机完成后强制结束（工具箱 start.bat 的做法）
taskkill /f /im prime95.exe
```

**注意事项**

- `-t` 烤机负载比 AIDA64 单烤 FPU 更高，请确保散热与电源余量充足。
- 烤机是阻塞式运行，脚本中如需自动化请先 `start "" "prime95.exe" -t` 再轮询进程。

---

## 显卡工具

### FurMark —— 显卡烤机/基准测试

- **路径**：`烤鸡工具\FurMark_win64\furmark.exe`（也可用同目录 `FurMark_GUI.exe` 打开图形界面）
- **说明**：Geeks3D 经典 GPU 烤机/基准工具，命令行参数非常完整（以下参数表来自其自带 `help.txt`，为官方权威内容）。

**语法**

```
furmark [--option [value]] ...
```

**核心参数表**

| 参数 | 说明 | 示例 |
|------|------|------|
| `--help` | 打印全部参数 | `furmark --help` |
| `--version` | 显示版本 | |
| `--demolist` | 列出可用 demo 名称 | `furmark --demolist` |
| `--demo <name>` | 运行指定 demo（配合 --demolist 查看全部名称） | `--demo furmark-gl` |
| `--benchmark` | 基准测试模式，结束时显示分数框 | `--benchmark` |
| `--p1080` / `--p1440` / `--p2160` | 基准预设：1080p / 1440p / 4K | `--p1080` |
| `--width <px>` / `--height <px>` | 自定义窗口分辨率 | `--width 1920 --height 1080` |
| `--fullscreen` | 全屏运行 | `--fullscreen` |
| `--max-time <秒>` | 最长运行时长（默认 0 = 不限，用于压力测试） | `--max-time 600` |
| `--duration-ms <毫秒>` | 基准时长（仅自定义设置时有效，默认 60000） | `--duration-ms 30000` |
| `--max-frames <帧数>` | 按帧数限制运行时长 | `--max-frames 1000` |
| `--no-score-box` | 结束时隐藏分数框（分数仍写入日志和 _scores.csv） | |
| `--gpu-index <n>` | 指定 GPU（0 起） | `--gpu-index 0` |
| `--msaa <1\|2\|4\|8>` | 多重采样抗锯齿级别 | `--msaa 8` |
| `--vsync <n>` | 垂直同步状态 | `--vsync 0` |
| `--hpgfx <0\|1>` | 混合显卡笔记本强制高性能 GPU | `--hpgfx 1` |
| `--no-osi` | 关闭屏幕信息显示（OSI） | |
| `--no-gpumon` | 关闭 GPU 监控 | |
| `--log-gpu-data` | 把 GPU 数据记录到 CSV 文件 | |
| `--log-gpu-data-filename <路径>` | 指定 GPU 数据日志路径 | |
| `--disable-logfile` | 不生成日志文件 | |
| `--logfile-suffix <后缀>` | 给日志文件名加后缀 | |
| `--glinfo` / `--glinfo-all` | 打印 OpenGL 报告 | |
| `--vkinfo` / `--vkinfo-all` | 打印 Vulkan 报告 | |
| `--gpuinfo` | 打印 GPU 信息 | |
| `--furmark-vram-test-gb <0\|2\|4\|6\|8\|12\|16\|20\|24>` | FurMark 显存压力测试数据量（GB） | `--furmark-vram-test-gb 8` |
| `--artifact-scanner` | 运行画面伪影扫描 | |
| `--title-bar <0\|1>` | 是否显示窗口标题栏 | |

**示例**

```bat
:: 1080p 预设基准测试（不带分数框，结果在 _scores.csv）
"Tools\烤鸡工具\FurMark_win64\furmark.exe" --demo furmark-gl --p1080 --no-score-box

:: 自定义 1920x1080 烤机 10 分钟（压力测试）
"Tools\烤鸡工具\FurMark_win64\furmark.exe" --demo furmark-gl --benchmark --width 1920 --height 1080 --max-time 600

:: 全屏 Vulkan demo 指定第二块显卡
"Tools\烤鸡工具\FurMark_win64\furmark.exe" --demo furmark-vk --width 1920 --height 1080 --fullscreen --gpu-index 1

:: 只看显卡信息
"Tools\烤鸡工具\FurMark_win64\furmark.exe" --gpuinfo
```

**注意事项**

- 烤机对显卡压力极大，注意温度；建议先用 `--max-time` 限制时长。
- 目录下已附带的 `start_benchmark.bat` / `start_fullscreen.bat` / `start_vram_test.bat` 均为现成命令行示例，可直接参考。
- `FurMark_win64\gpushark\gpushark_x64.exe`（GPU Shark）与 `cpuburner\cpuburner.exe`（CPU 烤机）为同包附带工具。

### FPT64 —— Intel 主板 BIOS 备份/刷写

- **路径**：`显卡工具\FPT64\fptw64.exe`
- **说明**：Intel Flash Programming Tool（FPT）64 位版，可备份/刷写 Intel 平台主板 BIOS。图吧工具箱CE自带的 `backup.cmd` / `flash.cmd` 即基于此工具。

**核心参数（官方用法）**

| 参数 | 说明 | 示例 |
|------|------|------|
| `-d <文件>` | 把当前 BIOS 内容导出（dump）到文件 | `fptw64.exe -d bios_bak.bin -bios` |
| `-f <文件>` | 把指定 BIOS 镜像刷写进主板 | `fptw64.exe -f bios_f.bin -bios` |
| `-bios` | 指定操作对象为 BIOS 区域（配合 -d / -f 使用） | |
| `-y` | 跳过确认提示，全自动执行 | |
| `-p` | 不重置平台（-preserve?） | |
| `-help` | 打印完整参数列表 | `fptw64.exe -help` |

**示例**

```bat
:: 备份当前 BIOS（工具箱 backup.cmd 原版）
"Tools\显卡工具\FPT64\fptw64.exe" -d bios_bak.bin -bios

:: 刷写 BIOS 镜像（工具箱 flash.cmd 原版）
"Tools\显卡工具\FPT64\fptw64.exe" -f bios_f.bin -bios
```

**注意事项**

- ⚠️ **高危操作**：BIOS 刷写失败可能导致主板变砖！务必先备份、确保镜像与主板型号匹配、电池电量充足/接电源。
- 需要管理员权限。
- `fparts.txt` 为 FPT 的 flash 描述文件，请勿删除或修改。
- 完整参数可用 `fptw64.exe -help` 查看（Intel FPT 官方文档）。

### nvidiaInspector —— NVIDIA 显卡超频/监控 ※

- **路径**：`显卡工具\nvidiaInspector\nvidiaInspector.exe`
- **说明**：NVIDIA Inspector 命令行超频接口，无需开 GUI 即可调整 N 卡核心/显存频率偏移、功耗墙、温度墙、风扇转速等。参数来自 Guru3D 社区通用文档。

**语法**

```
nvidiaInspector.exe -<选项> <gpu索引>,<值> [,<gpu索引>,<值> ...]
```

（`<gpu索引>` 从 0 开始，多卡可用逗号分隔多组值）

**核心参数表**

| 参数 | 说明 | 示例 |
|------|------|------|
| `-setBaseClockOffset` | 设置核心频率偏移（MHz） | `-setBaseClockOffset 0,100` |
| `-setShaderClockOffset` | 设置着色器频率偏移（MHz） | `-setShaderClockOffset 0,200` |
| `-setMemoryClockOffset` | 设置显存频率偏移（MHz） | `-setMemoryClockOffset 0,300` |
| `-setPowerTarget` | 设置功耗墙（百分比） | `-setPowerTarget 0,105` |
| `-setTempTarget` | 设置温度墙（℃） | `-setTempTarget 0,80` |
| `-setFanSpeed` | 锁定风扇转速（百分比） | `-setFanSpeed 0,70` |
| `-lockVoltage` | 锁定电压（mV，慎用） | `-lockVoltage 0,1000` |
| `-restore` | 恢复默认频率/电压设置 | `-restore 0` |
| `-dump` | 输出当前显卡详细状态 | `-dump 0` |
| `-multigpu` | 对多卡同时应用 | |

**示例**

```bat
:: 核心 +100MHz，显存 +300MHz，功耗墙拉到 105%
"Tools\显卡工具\nvidiaInspector\nvidiaInspector.exe" -setBaseClockOffset 0,100 -setMemoryClockOffset 0,300 -setPowerTarget 0,105

:: 锁定风扇 70% 转速
"Tools\显卡工具\nvidiaInspector\nvidiaInspector.exe" -setFanSpeed 0,70

:: 恢复默认
"Tools\显卡工具\nvidiaInspector\nvidiaInspector.exe" -restore 0
```

**注意事项**

- ⚠️ 超频有损坏硬件/蓝屏风险，请逐步小幅调整并用 FurMark/3DMark 验证稳定性。
- 命令行超频同样会写入驱动注册表，重启后仍生效，请用 `-restore` 恢复。

### nvidiaProfileInspector —— NVIDIA 驱动配置档管理 ※

- **路径**：`显卡工具\nvidiaProfileInspector\nvidiaProfileInspector.exe`
- **说明**：管理 NVIDIA 驱动配置档（Profile），可导入/导出/应用配置档，适合批量部署游戏优化配置。

**核心参数表**

| 参数 | 说明 | 示例 |
|------|------|------|
| `-SetProfile <名称>` | 切换到指定配置档 | `-SetProfile "RTX 4090 OC"` |
| `-GetProfile <名称>` | 读取指定配置档并显示 | |
| `-ExportProfile <文件>` | 导出当前配置档到文件 | `-ExportProfile profile.nip` |
| `-ImportProfile <文件>` | 从文件导入配置档 | `-ImportProfile profile.nip` |
| `-Apply` | 应用当前设置 | |
| `-DeleteProfile <名称>` | 删除配置档 | |

**示例**

```bat
:: 导出当前配置档
"Tools\显卡工具\nvidiaProfileInspector\nvidiaProfileInspector.exe" -ExportProfile myprofile.nip

:: 导入并应用
"Tools\显卡工具\nvidiaProfileInspector\nvidiaProfileInspector.exe" -ImportProfile myprofile.nip -Apply
```

---

## 硬盘工具

### CrystalDiskInfo —— SMART 健康信息

- **路径**：`硬盘工具\CrystalDiskInfo\DiskInfo64S.exe`（另有 `DiskInfo32S.exe`）
- **说明**：查看硬盘 SMART 健康状态。命令行参数来自官方手册 Advanced Features 章节。

**核心参数表**

| 参数 | 说明 |
|------|------|
| `/Exit` | 刷新 S.M.A.R.T. 信息与 AAM/APM 状态后自动退出 |
| `/Copy` | 把 "编辑 > 复制" 的结果输出到 `DiskInfo.txt` |
| `/CopyExit` | 输出 `DiskInfo.txt` 后自动退出（最常用，适合脚本采集） |

**示例**

```bat
:: 生成 SMART 报告到 DiskInfo.txt 并退出（适合任务计划定时采集）
"Tools\硬盘工具\CrystalDiskInfo\DiskInfo64S.exe" /CopyExit
```

**注意事项**

- `DiskInfo.txt` 生成在与 exe 相同的目录（即 `硬盘工具\CrystalDiskInfo\`）。
- 部分 SMART 属性（如温度历史）需要管理员权限读取。

### Defraggler —— 碎片整理（命令行版 df.exe）

- **路径**：`硬盘工具\Defraggler\df.exe`（GUI 版为 `Defraggler.exe`）
- **说明**：Piriform 出品的磁盘碎片整理工具，`df.exe` 为独立命令行版。※

**核心参数表**

| 参数 | 说明 | 示例 |
|------|------|------|
| `/A` | 只分析不整理 | `df.exe /A C:` |
| `/D` | 整理指定驱动器 | `df.exe /D C:` |
| `/E` | 整理可用空间（快速整理） | `df.exe /E C:` |
| `/H` | 整理休眠文件 | |
| `/L` | 低优先级运行（后台整理不影响使用） | `df.exe /D C: /L` |
| `/Q` | 静默模式 | |
| `/T <秒>` | 限制整理时长 | `df.exe /D C: /T 60` |
| `/X` | 排除指定文件夹 | `df.exe /D C: /X "C:\Windows"` |
| `/V` | 详细输出 | |

**示例**

```bat
:: 只分析 C 盘
"Tools\硬盘工具\Defraggler\df.exe" /A C:

:: 低优先级后台整理 D 盘，限时 10 分钟
"Tools\硬盘工具\Defraggler\df.exe" /D D: /L /T 600

:: SSD 用户注意：SSD 无需碎片整理，仅机械硬盘（HDD）适用
```

### DiskGenius —— 分区管理/数据恢复（命令行模式）※

- **路径**：`硬盘工具\DiskGenius\DiskGenius.exe`
- **说明**：专业分区管理工具，支持 `/cmd` 命令行脚本模式，适合无人值守执行分区操作。命令行文档为社区整理，具体以官方命令行版文档为准。

**语法**

```
DiskGenius.exe /cmd <脚本文件> [/LANG <语言>] [/AUTOEXIT] [/LOG]
```

**核心参数表**

| 参数 | 说明 | 示例 |
|------|------|------|
| `/cmd <文件>` | 从脚本文件读取命令序列执行 | `/cmd script.txt` |
| `/LANG <语言>` | 指定界面语言（如 zh-CN） | `/LANG zh-CN` |
| `/AUTOEXIT` | 命令执行完自动退出 | |
| `/LOG` | 记录操作日志 | |

**脚本内常用命令**（写入 .txt 脚本，每行一条）

| 命令 | 说明 | 示例 |
|------|------|------|
| `COPY` | 复制分区/文件 | `COPY /SRC=1:2 /DST=1:3` |
| `CLONE` | 克隆分区到指定位置 | `CLONE /SRC=1:2 /DST=1:3` |
| `FORMAT` | 格式化分区 | `FORMAT /PART=1:2 /FS=NTFS` |
| `DELETE` | 删除分区 | `DELETE /PART=1:2` |
| `ERASE` | 擦除分区数据 | `ERASE /PART=1:2` |
| `REBUILD` | 重建分区表（找回丢失分区） | `REBUILD /DISK=1` |
| `IMAGE` | 备份分区为镜像 | `IMAGE /SRC=1:2 /DST="d:\backup.img"` |
| `RESTOREIMAGE` | 从镜像还原分区 | `RESTOREIMAGE /SRC="d:\backup.img" /DST=1:2` |
| `LIST` | 列出磁盘/分区信息 | `LIST DISK` |

**示例**

```bat
:: 执行脚本并自动退出（脚本内容见上表）
"Tools\硬盘工具\DiskGenius\DiskGenius.exe" /cmd backup_script.txt /LANG zh-CN /AUTOEXIT /LOG
```

**注意事项**

- ⚠️ 分区操作危险，脚本务必先备份数据并仔细核对盘符/分区号。
- 完整命令列表请以 DiskGenius 官方命令行文档为准（官网"命令行版"专题页）。

### urwtest —— U 盘/SSD 读写可靠性测试

- **路径**：`硬盘工具\URWTEST\urwtest_v18.exe`
- **说明**：国产纯命令行工具（mYdigit），向目标盘写入数据并校验，用于检测扩容盘（虚标容量）、坏块和读写可靠性。**官方用法已在本机实测验证**。

**语法**

```
urwtest_v18.exe X: [Y/N]
```

**参数说明**

| 参数 | 说明 |
|------|------|
| `X:` | 待测盘符（冒号必带），测试数据写入该盘根目录 |
| `[Y/N]` | 是否在写满后立即校验；**省略 = 立即校验** |

**示例**

```bat
:: 测试 E 盘并立即校验（推荐，最全面）
"Tools\硬盘工具\URWTEST\urwtest_v18.exe" E:

:: 只写入不校验（快速）
"Tools\硬盘工具\URWTEST\urwtest_v18.exe" E: N
```

**注意事项**

- 测试会向盘内写入与容量等量的数据文件，测试完成后**会自动删除**测试文件，但测试前仍建议备份重要数据。
- 纯控制台程序，输出中文进度；写入/校验全程阻塞运行，可用 `start /wait` 等待完成。
- 扩容盘会在校验阶段报错，是检测假盘的首选工具。
- 不带参数直接运行会进入交互模式并列出当前所有盘符。

### WizTree —— 磁盘空间分析（命令行导出）

- **路径**：`硬盘工具\WizTree\WizTree.exe`（64 位系统为 `WizTree.exe`，官方文档中的 64 位版名为 `wiztree64.exe`）
- **说明**：基于 MFT 的极速磁盘空间分析工具，命令行支持 CSV 导出、树图导出、MFT 导出。参数来自官方 guide（diskanalyzer.com/guide），权威。

**语法**

```
WizTree.exe "盘符或路径" /export="文件名" [/filter="过滤"] [/filterexclude="排除"] [/admin=0|1] ...
```

**核心参数表**

| 参数 | 说明 | 示例 |
|------|------|------|
| `"C:"` 或 `"C:\Users"` | 第一个位置参数：要扫描的盘或目录 | `WizTree.exe "C:"` |
| `/export="<文件>"` | 导出 CSV；文件名中的 `%d`/`%t` 自动替换为日期/时间 | `/export="c:\temp\export%d_%t.csv"` |
| `/filter="<规格>"` | 只导出匹配文件，多规格用 `\|` 分隔 | `/filter="*.mp3\|*.wav"` |
| `/filterexclude="<规格>"` | 排除匹配文件 | `/filterexclude="d:\temp\"` |
| `/admin=0\|1` | 1 = 管理员模式（MFT 快速扫描，需要管理员权限） | `/admin=1` |
| `/exportfolders=0\|1` | 是否导出文件夹（0 = 只导出文件） | `/exportfolders=0` |
| `/exportfiles=0\|1` | 是否导出文件 | |
| `/sortby=<0-3>` | 排序：0 文件名 / 1 文件大小 / 2 分配大小 / 3 修改日期 | `/sortby=1` |
| `/exportfiletypes="<文件>"` | 按文件类型统计导出 | |
| `/treemapimagefile="<png>"` | 导出树图 PNG 图片 | `/treemapimagefile="c:\temp\map%d.png"` |
| `/treemapimagewidth` / `height` | 树图尺寸（默认 1920x1080） | `/treemapimagewidth=1024` |
| `/dumpmftfile="<文件>"` | 导出 NTFS 卷的 MFT（需管理员） | `/dumpmftfile="c:\mft\ddrive%d%t.MFT"` |
| `/exportUTCTime=0\|1` | 时间是否用 UTC | |
| `/exportmaxdepth=<n>` | 最大导出深度（0 = 不限） | |

**示例**

```bat
:: 全盘扫描并导出 CSV（管理员模式，快速）
start /wait "Tools\硬盘工具\WizTree\WizTree.exe" "C:" /export="c:\temp\cdrive%d_%t.csv" /admin=1

:: 只导出 C 盘所有音视频文件列表
"Tools\硬盘工具\WizTree\WizTree.exe" "C:" /export="c:\temp\media.csv" /filter="*.mp3|*.wav|*.flac|*.mp4" /admin=0 /exportfolders=0

:: 扫描 + 导出 CSV + 生成树图 PNG
"Tools\硬盘工具\WizTree\WizTree.exe" "C:" /export="c:\temp\cdrive%d_%t.csv" /treemapimagefile="c:\temp\cdrive%d_%t.png" /admin=1
```

**注意事项**

- 在批处理中请用 `start /wait` 等待扫描完成再处理生成的 CSV。
- 批处理中 `%d`/`%t` 要写成 `%%d`/`%%t`。

---

## 综合检测

### AIDA64 —— 硬件检测/系统报告（命令行报告生成）

- **路径**：`综合检测\AIDA64\aida64.exe`
- **说明**：权威硬件检测工具，命令行主要用于**无人值守生成硬件报告**（网络审计、装机配置导出等）。参数来自官方用户手册 Command Line Options 章节。

**语法**

```
aida64.exe /R <报告文件> /<格式> /<内容范围> [/SILENT] [/SAFE] ...
```

**核心参数表**

| 参数 | 说明 | 示例 |
|------|------|------|
| `/R [文件]` | 生成报告到文件（不写文件则用"偏好设置"中配置的路径） | `/R C:\Reports\pc.html` |
| `/E [邮箱]` | 生成报告并通过邮件发送 | `/E admin@example.com` |
| `/SUBJ <主题>` | 配合 /E 指定邮件主题 | `/SUBJ "Report of $HOSTNAME"` |
| `/FTPUPLOAD [文件]` | 生成报告并上传 FTP | `/FTPUPLOAD $HOSTNAME` |
| `/HTML` / `/MHTML` / `/TEXT` / `/XML` / `/CSV` | 报告格式（互斥，任选其一） | `/HTML` |
| `/ALL` / `/SUM` / `/HW` / `/SW` / `/BENCH` / `/AUDIT` / `/CUSTOM` | 报告内容范围（互斥）：全部 / 摘要 / 仅硬件 / 仅软件 / 基准 / 审计 / 自定义 | `/SUM` |
| `/SILENT` | 静默模式（隐藏托盘图标） | |
| `/SAFE` | 安全模式（不加载可能冲突的驱动，出现在官方示例中） | |
| `/SHOWP` | 显示报告生成进度（不可中断） | |
| `/SHOWPCANCEL` | 显示进度且允许用户取消 | |
| `/SHOWS` | 显示启动进度 | |
| `/SHOWED` | 发送邮件前弹出对话框（帮助台场景） | |
| `/STAY` | 报告生成后保持后台运行 | |
| `/DELAY <秒>` | 延迟指定秒数后再开始 | `/DELAY 30` |
| `/IDLE` | 以最低优先级运行（不干扰用户） | |
| `/INIFILE <文件>` | 使用自定义偏好配置文件 | `/INIFILE \\server\share\aida64.ini` |
| `/NOICONS` | 不加载图标（省资源） | |
| `/NOLICENSE` | 隐藏软件许可信息页 | |

**示例**

```bat
:: 生成纯文本摘要报告
"Tools\综合检测\AIDA64\aida64.exe" /R C:\Reports\summary.txt /TEXT /SUM /SILENT

:: 生成完整硬件 HTML 报告并静默退出
"Tools\综合检测\AIDA64\aida64.exe" /R C:\Reports\pc.html /HTML /HW /SILENT

:: 文件名校验位：$HOSTNAME 会被替换为计算机名
"Tools\综合检测\AIDA64\aida64.exe" /R "C:\Reports\$HOSTNAME-hw.mht" /MHTML /HW /SILENT /DELAY 30 /IDLE

:: 完整审计报告（企业部署常用）
"Tools\综合检测\AIDA64\aida64.exe" /R \\server\share\$HOSTNAME /CSV /AUDIT /SILENT /SAFE
```

**注意事项**

- 报告文件名支持 `$HOSTNAME`、`$USERNAME`、`$DATE`、`$TIME`、`$IPADDR`、`$DMISYSPROD` 等变量，批量装机时非常有用。
- 该便携版为 Business/Engineer/Extreme 一体版，`/FTPUPLOAD` 等功能完整可用。

### HWiNFO —— 硬件信息/传感器监控 ※

- **路径**：`综合检测\hwinfo\HWiNFO64.exe`（另有 HWiNFO32 / HWiNFO_ARM64）
- **说明**：专业硬件信息与实时传感器监控工具，支持静默/仅传感器等命令行启动方式。参数为社区通用文档整理，详见 HWiNFO 官方论坛。

**核心参数表**

| 参数 | 说明 | 示例 |
|------|------|------|
| `/S` | 静默模式启动（无主窗口） | `HWiNFO64.exe /S` |
| `/SM` | 仅传感器模式（跳过信息页直接进传感器） | `HWiNFO64.exe /SM` |
| `/LOG` | 将传感器数据写入日志文件 | `HWiNFO64.exe /SM /LOG` |
| `/CONFIG <文件>` | 使用指定配置文件 | `/CONFIG my.ini` |
| `/SAFE` | 安全模式（禁用部分驱动，排查兼容性问题） | |
| `/TRAY` | 最小化到系统托盘 | |
| `/QT` | 快速启动 | |

**示例**

```bat
:: 直接进入传感器监控界面
"Tools\综合检测\hwinfo\HWiNFO64.exe" /SM

:: 静默启动 + 传感器日志（用于长时间记录温度）
"Tools\综合检测\hwinfo\HWiNFO64.exe" /SM /LOG /S
```

**注意事项**

- 日志文件生成在与 exe 相同目录。
- 首次启动需安装传感器内核驱动，请允许。

---

## 其他工具

### USBDeview —— USB 设备管理（命令行功能最全的工具之一）

- **路径**：`其他工具\USBDeview\USBDeview.exe`
- **说明**：NirSoft 出品，列出/导出/禁用/卸载所有 USB 设备。以下参数表来自其自带 `readme.txt`（官方权威）。支持用**退出码**做流程判断（如 `/is_connected` 返回匹配设备数）。

**核心参数表**

| 类别 | 参数 | 说明 | 示例 |
|------|------|------|------|
| **导出** | `/stext <文件>` | 导出为纯文本 | `/stext usb.txt` |
| | `/stab <文件>` | 导出为制表符分隔文本 | `/stab usb.txt` |
| | `/scomma <文件>` | 导出为 CSV（逗号分隔） | `/scomma usb.csv` |
| | `/stabular <文件>` | 表格形式文本 | |
| | `/shtml <文件>` | 导出为 HTML | `/shtml usb.html` |
| | `/sverhtml <文件>` | 导出为纵向 HTML | |
| | `/sxml <文件>` | 导出为 XML | `/sxml usb.xml` |
| | `/sort <列>` | 按列排序导出（列名或索引，`~` 前缀 = 倒序，可多个） | `/sort "Device Type" /sort ~1` |
| | `/nosort` | 不排序 | |
| | `/AddExportHeaderLine <0\|1>` | CSV 是否加表头 | |
| | `""` 作为文件名 | 输出到 stdout（可管道） | `/scomma "" \| more` |
| **断开** | `/stop <设备名>` | 断开设备（支持名称/描述片段） | `/stop "DataTraveler"` |
| | `/stop_by_serial <序列号>` | 按序列号断开 | |
| | `/stop_by_drive <盘符>` | 按盘符断开 | `/stop_by_drive g:` |
| | `/stop_by_pid <VID;PID>` | 按 VendorID/ProductID 断开 | `/stop_by_pid 13fe;1a00` |
| | `/stop_by_class <类;子类;协议>` | 按 USB 类别断开（如 08;06;50 = 大容量存储） | |
| | `/stop_all` | 断开所有设备 | |
| **禁用/启用** | `/disable <设备名>` 等 | 禁用设备（同名变体：_by_serial/_by_drive/_by_pid/_by_class/_all） | `/disable "USB\Vid_1058&Pid_1023\..."` |
| | `/enable <设备名>` 等 | 启用设备 | |
| | `/disable_enable <设备名>` | 禁用后立即启用（模拟拔插） | |
| **卸载** | `/remove <设备名>` 等 | 卸载设备（变体同上；`/remove_all_connected` 卸载所有已连接设备） | |
| **状态查询** | `/is_connected <设备名>` 等 | 检查是否连接（返回匹配设备数，0 = 未连接，用于 %ERRORLEVEL%） | `/is_connected_by_serial "753895734..."` |
| | `/is_disabled <设备名>` 等 | 检查是否被禁用 | |
| **其他** | `/RunAsAdmin` | 与需提权的操作联用，自动请求管理员权限 | `/RunAsAdmin /disable ...` |
| | `/remote \\计算机名` | 连接远程电脑 | `/remote \\MyComp` |
| | `/remotefile <列表文件>` | 批量连接多台远程电脑 | |
| | `/regfile <SYSTEM文件>` | 从外部 SYSTEM 注册表文件读取 | |
| | `/cfg <配置文件>` | 使用指定配置 | `/cfg "%AppData%\USBDeview.cfg"` |
| | `/savelangfile` | 导出语言文件 | |

**示例**

```bat
:: 导出全部 USB 设备到 CSV（带表头）
"Tools\其他工具\USBDeview\USBDeview.exe" /scomma "%USERPROFILE%\Desktop\usb.csv" /AddExportHeaderLine 1

:: 按设备类型排序导出 HTML 报告
"Tools\其他工具\USBDeview\USBDeview.exe" /shtml usb-list.html /sort "Device Type"

:: 检查某序列号 U 盘是否连接（echo 1 = 已连接，0 = 未连接）
"Tools\其他工具\USBDeview\USBDeview.exe" /is_connected_by_serial "7538957348957398"
echo %ERRORLEVEL%

:: 安全弹出 G 盘（断开 USB 存储）
"Tools\其他工具\USBDeview\USBDeview.exe" /stop_by_drive g:

:: 管理员权限卸载指定设备
"Tools\其他工具\USBDeview\USBDeview.exe" /RunAsAdmin /remove "USB\Vid_1058&Pid_1023\8539583490834690"
```

**注意事项**

- 禁用/启用/卸载需要管理员权限；x64 系统禁用/启用设备必须用 64 位版本（本目录为通用版）。
- `/stop`、`/disable` 等按名称匹配时支持**部分匹配**（如 `"kingston"` 可匹配 `"Kingston DataTraveler 2.0"`）。
- 目录下另有 `USBDeview.chm` 帮助文件可查全部细节。

### BlueScreenView —— 蓝屏 dump 分析 ※

- **路径**：`其他工具\bluescreenview\BlueScreenViewx64.exe`（另 BlueScreenViewx86.exe）
- **说明**：NirSoft 出品，解析系统蓝屏（BSOD）dump 文件，显示崩溃原因与驱动。命令行接口与 USBDeview 同体系（NirSoft 标准导出参数）。

**核心参数表**

| 参数 | 说明 | 示例 |
|------|------|------|
| `/stext` `/stab` `/scomma` `/shtml` `/sverhtml` `/sxml` `<文件>` | 按格式导出全部蓝屏记录 | `/shtml bluescreen.html` |
| `/sort <列>` `/nosort` | 排序导出 | `/sort "Crash Time"` |
| `/Dump <dump文件>` | 加载指定的 dump 文件 | `/Dump "C:\Windows\Minidump\Mini0812-01.dmp"` |
| `/cfg <文件>` | 指定配置文件 | |

**示例**

```bat
:: 导出全部蓝屏记录为 CSV
"Tools\其他工具\bluescreenview\BlueScreenViewx64.exe" /scomma bsod.csv /AddExportHeaderLine 1

:: 分析指定 dump 文件
"Tools\其他工具\bluescreenview\BlueScreenViewx64.exe" /Dump "C:\Windows\Minidump\Mini0812-01.dmp"
```

### BatteryInfoView —— 电池信息 ※

- **路径**：`其他工具\BatteryInfoView\BatteryInfoView.exe`
- **说明**：NirSoft 出品，显示笔记本电池设计容量/当前容量/磨损度/充放电状态等。命令行接口同 NirSoft 标准体系。

**核心参数表**

| 参数 | 说明 | 示例 |
|------|------|------|
| `/stext` `/stab` `/scomma` `/shtml` `/sverhtml` `/sxml` `<文件>` | 导出电池信息 | `/scomma battery.csv` |
| `/sort <列>` | 排序 | |
| `/monitor <秒>` | 监控模式：每 N 秒刷新（配合导出参数可做持续记录） | `/monitor 60` |
| `/cfg <文件>` | 指定配置 | |

**示例**

```bat
:: 导出当前电池状态
"Tools\其他工具\BatteryInfoView\BatteryInfoView.exe" /scomma battery.csv

:: 每 60 秒刷新并持续记录到日志（脚本循环中配合使用）
"Tools\其他工具\BatteryInfoView\BatteryInfoView.exe" /monitor 60
```

### Autoruns / autorunsc —— 自启动项管理

- **路径**：`其他工具\Autoruns\autorunsc64.exe`（命令行版；GUI 版为 `Autoruns.exe`/`Autoruns64.exe`/`Autoruns64a.exe`）
- **说明**：Sysinternals 权威自启动项扫描工具，**autorunsc 是命令行版**，可全量导出自启动项，适合审计与对比。参数来自微软官方文档。

**语法**

```
autorunsc [-a <类型>] [-c|-ct|-x] [-h] [-m] [-s] [-t] [-vt] [[-z <离线系统>] | [用户名]]
```

**核心参数表**

| 参数 | 说明 |
|------|------|
| `-a <类型>` | 扫描类型。`*` = 全部；`b` 引导执行 / `d` Appinit DLL / `e` 资源管理器加载项 / `h` 映像劫持 / `i` IE 加载项 / `k` 已知 DLL / `l` 登录启动（默认）/ `m` WMI / `n` Winsock 提供程序 / `o` 编解码器 / `p` 打印监视器 / `r` LSA 安全提供程序 / `s` 服务与驱动 / `t` 计划任务 / `w` Winlogon |
| `-c` | 输出为 CSV |
| `-ct` | 输出为制表符分隔 |
| `-x` | 输出为 XML |
| `-h` | 显示文件哈希 |
| `-m` | 隐藏微软条目（与 -v 联用则隐藏已签名条目） |
| `-s` | 验证数字签名 |
| `-t` | 时间戳用 UTC 格式 |
| `-u` | 显示 VirusTotal 未知或非零检测的文件（否则只显示未签名文件） |
| `-v[r][s]` | 查询 VirusTotal（r = 打开非零检测报告；s = 上传未扫描文件） |
| `-vt` | 接受 VirusTotal 服务条款 |
| `-z <离线系统>` | 扫描离线 Windows 系统（如 PE 盘中的系统） |
| `用户名` | 指定用户，`*` = 所有用户 |

**示例**

```bat
:: 导出全部自启动项为 CSV（全类型）
"Tools\其他工具\Autoruns\autorunsc64.exe" -a * -c -accepteula > autoruns.csv

:: 只看登录启动项 + 服务驱动，XML 格式
"Tools\其他工具\Autoruns\autorunsc64.exe" -a l -a s -x -accepteula > autoruns.xml

:: 隐藏微软项，验证签名（找第三方可疑启动项）
"Tools\其他工具\Autoruns\autorunsc64.exe" -a * -c -m -s -accepteula > thirdparty.csv
```

**注意事项**

- Sysinternals 工具首次运行需接受 EULA：加上 `-accepteula` 参数跳过交互（如示例）。
- `-z` 可扫描离线系统（如从 PE 启动时指定系统盘路径）。

### Everything —— 文件搜索

- **路径**：`其他工具\Everything\Everything.exe`
- **说明**：极速文件名搜索工具，支持命令行直接传入搜索词、生成文件列表等。参数来自官方文档（voidtools.com），权威。

**语法**

```
Everything.exe [文件列表文件] [选项]
```

**核心参数表**

| 类别 | 参数 | 说明 | 示例 |
|------|------|------|------|
| **搜索** | `-s <文本>` / `-search` | 打开窗口并搜索 | `Everything.exe -s "ABC\|123"` |
| | `-filename <名称>` | 按文件名搜索 | |
| | `-p <路径>` / `-path` | 按路径搜索 | `Everything.exe -p "C:\Windows"` |
| | `-parent <路径>` | 只搜该目录（不含子目录） | |
| | `-regex` / `-noregex` | 正则搜索开关 | |
| | `-case` / `-nocase` | 区分大小写开关 | |
| | `-filter <名称>` | 使用指定搜索筛选器 | `-filter "音频"` |
| | `-bookmark <名称>` | 打开收藏的搜索 | |
| **文件列表** | `-create-file-list <文件> <路径>` | 把路径下的文件列表保存为 .efu 文件（可加 -create-file-list-include-only-files 等过滤） | `Everything.exe -create-file-list "music.efu" "D:\Music" -create-file-list-include-only-files "*.mp3;*.flac"` |
| | `-f <文件>` / `-filelist` | 打开文件列表 | |
| | `-edit <文件>` | 编辑文件列表 | |
| **结果** | `-sort <名称>` | 排序（如 size、"Date Modified"） | `-sort size` |
| | `-select <文件>` | 聚焦选中指定结果 | |
| | `-focus-results` | 聚焦结果列表 | |
| **窗口/运行** | `-instance <名称>` | 使用指定实例（多开） | `-instance mysearch` |
| | `-startup` | 后台启动（无窗口） | |
| | `-newwindow` / `-nonewwindow` | 强制新窗口 / 复用窗口 | |
| | `-exit` / `-quit` | 退出已运行的 Everything | |
| | `-reindex` | 强制重建索引 | |
| | `-admin` | 以管理员运行 | |
| **ETP** | `-connect <主机>` | 连接 ETP 服务器 | `-connect "ComputerName" -drive-links` |

**示例**

```bat
:: 打开 Everything 并搜索 mp4 或 avi
"Tools\其他工具\Everything\Everything.exe" -s "*.mp4|*.avi"

:: 生成 D 盘音乐文件列表（支持脚本后续处理）
"Tools\其他工具\Everything\Everything.exe" -create-file-list "D:\music.efu" "D:\Music" -create-file-list-include-only-files "*.mp3;*.flac"

:: 更新索引后退出
"Tools\其他工具\Everything\Everything.exe" -reindex -exit
```

### Ventoy —— 启动盘制作（无交互命令行模式）

- **路径**：`其他工具\ventoy\Ventoy2Disk.exe`
- **说明**：Ventoy 启动盘制作工具，支持 `VTOYCLI` 命令行模式**无交互安装/升级**，适合脚本批量制作。参数来自官方文档（ventoy.net），权威。

**语法**

```
Ventoy2Disk.exe VTOYCLI CMD DISK [选项]
```

（参数不区分大小写）

**核心参数表**

| 参数 | 说明 | 示例 |
|------|------|------|
| `VTOYCLI` | 固定第 1 参数，进入命令行模式 | |
| `/I` | 安装 Ventoy 到磁盘 | `/I` |
| `/U` | 升级已有 Ventoy（保留数据） | `/U` |
| `/Drive:<盘符>` | 按盘符指定目标盘 | `/Drive:F:` |
| `/PhyDrive:<编号>` | 按物理盘号指定（0 起） | `/PhyDrive:1` |
| `/GPT` | 使用 GPT 分区表（默认 MBR） | `/GPT` |
| `/NOSB` | 不启用安全启动支持 | `/NOSB` |
| `/NOUSBCheck` | 不检查是否为 USB 盘 | `/NOUSBCheck` |
| `/R:<MB>` | 预留尾部空间（MB） | `/R:2048` |
| `/FS:<格式>` | 指定文件系统（NTFS/EXFAT/FAT32...） | `/FS:NTFS` |
| `/NonDest` | 非破坏性安装（Ventoy 新特性） | `/NonDest` |

**执行结果文件**（在同目录生成）

| 文件 | 含义 |
|------|------|
| `cli_done.txt` | 执行完成标记，内容 `0` = 成功，`1` = 失败 |
| `cli_percent.txt` | 进度百分比 |
| `cli_log.txt` | 详细日志 |

**示例**

```bat
:: 安装 Ventoy 到 D 盘（GPT + 跳过 USB 检查 + 不启用安全启动 + 预留 4GB）
"Tools\其他工具\ventoy\Ventoy2Disk.exe" VTOYCLI /I /Drive:D: /GPT /NOUSBCheck /NOSB /R:4096

:: 升级已安装 Ventoy 的物理盘 1
"Tools\其他工具\ventoy\Ventoy2Disk.exe" VTOYCLI /U /PhyDrive:1
```

**注意事项**

- ⚠️ 安装会**格式化目标盘**（/I 时），确认盘符无误再执行！
- 脚本可轮询 `cli_done.txt` 判断完成状态。
- 同目录 `VentoyPlugson.exe` 为插件配置工具，`VentoyVlnk.exe` 为 Vlnk 管理工具（GUI）。

### WinDbg —— 系统/崩溃调试器

- **路径**：`其他工具\WinDbg\windbg.exe`
- **说明**：微软官方调试器，命令行功能完整，常用于分析 dump、调试进程。参数来自微软官方文档。

**语法**

```
windbg [选项] [-p PID | -z Dump文件 | 可执行文件]
```

**核心参数表**

| 参数 | 说明 | 示例 |
|------|------|------|
| `-z <dump文件>` | 打开崩溃转储文件（可多个） | `windbg -z C:\memory.dmp` |
| `-y <符号路径>` | 指定符号服务器/路径 | `-y srv*C:\symbols*https://msdl.microsoft.com/download/symbols` |
| `-c "<命令>"` | 启动后自动执行调试命令（分号分隔多条） | `-c "!analyze -v;q"` |
| `-p <PID>` | 附加到运行中的进程 | `-p 1234` |
| `-pn <进程名>` | 按名称附加 | `-pn notepad.exe` |
| `-o` | 同时调试目标进程的子进程 | |
| `-g` | 附加后不中断，直接继续运行 | |
| `-G` | 目标退出后立即结束会话 | |
| `-logo <文件>` / `-loga <文件>` | 日志输出（覆盖/追加） | `-logo debug.txt` |
| `-k` / `-kl` | 内核调试（-kl = 本机） | |
| `-Q` | 关闭工作区保存提示 | |
| `-I` | 注册为事后（postmortem）调试器 | |
| `-IA` | 关联 .dmp 文件扩展名 | |
| `-?` | 打开帮助 | |

**示例**

```bat
:: 打开蓝屏转储并自动运行 !analyze -v 分析
"Tools\其他工具\WinDbg\windbg.exe" -z "C:\Windows\MEMORY.DMP" -y "srv*C:\symbols*https://msdl.microsoft.com/download/symbols" -c "!analyze -v"

:: 附加调试指定进程（需调试权限）
"Tools\其他工具\WinDbg\windbg.exe" -pn notepad.exe -g
```

**注意事项**

- 调试符号较大，首次运行会下载，`-y` 指定本地缓存目录可复用。
- 蓝屏分析常用命令：`!analyze -v`（自动分析）、`!process 0 0`、`.reload`。

---

## 实用脚本组合示例

### 1. 一键导出 USB 设备清单（CSV + HTML 双份）

```bat
@echo off
set TOOLS=%~dp0TubaWinUi3.WinUI3\Tools
"%TOOLS%\其他工具\USBDeview\USBDeview.exe" /scomma "%USERPROFILE%\Desktop\usb_devices.csv" /AddExportHeaderLine 1
"%TOOLS%\其他工具\USBDeview\USBDeview.exe" /shtml "%USERPROFILE%\Desktop\usb_devices.html" /sort "Device Type"
echo 已导出到桌面。
```

### 2. 检测 U 盘是否为扩容盘（urwtest）

```bat
@echo off
echo 开始检测 E 盘（写满后校验）...
start /wait "" "%TOOLS%\硬盘工具\URWTEST\urwtest_v18.exe" E:
echo 检测完成，如中途报校验错误则该盘为扩容盘/坏盘。
```

### 3. 静默烤机 + 自动停止（FurMark + Prime95 组合）

```bat
@echo off
:: 显卡烤机 10 分钟
start "" "%TOOLS%\烤鸡工具\FurMark_win64\furmark.exe" --demo furmark-gl --width 1920 --height 1080 --max-time 600 --fullscreen
:: CPU 烤机同步进行（Prime95 单烤）
start "" "%TOOLS%\处理器工具\Prime95\prime95.exe" -t
timeout /t 600 /nobreak
taskkill /f /im furmark.exe 2>nul
taskkill /f /im prime95.exe 2>nul
echo 双烤结束，请用 HWiNFO 回看温度记录。
```

### 4. 装机后用 AIDA64 生成硬件配置报告

```bat
"%TOOLS%\综合检测\AIDA64\aida64.exe" /R "C:\Reports\$HOSTNAME.html" /HTML /HW /SILENT /SAFE
:: 生成文件：C:\Reports\<计算机名>.html
```

### 5. 磁盘空间大户报表（WizTree）

```bat
start /wait "" "%TOOLS%\硬盘工具\WizTree\WizTree.exe" "C:" /export="C:\temp\c盘占用%d_%t.csv" /admin=1 /sortby=1
echo 报表已生成，可用 Excel 打开按大小排序查看。
```

### 6. 检查自启动项中可疑程序（autorunsc）

```bat
"%TOOLS%\其他工具\Autoruns\autorunsc64.exe" -a * -c -m -s -accepteula > "%USERPROFILE%\Desktop\startup_items.csv"
echo 已导出非微软启动项，见桌面 startup_items.csv
```

---

## 附：无命令行参数的主要工具

以下工具为纯 GUI 程序（或命令行支持极为有限），**未收录**于本文档，仅供核对：

| 分类 | 工具 | 说明 |
|------|------|------|
| 处理器 | CPU-Z、CoreTemp、C2CLatency、XIANGQI、ThrottleStop、LinX、wPrime、SuperPI | 均为 GUI 工具（ThrottleStop/LinX 可通过同目录 ini 配置参数，但无命令行开关） |
| 显卡 | GPU-Z、DDU、DXVAChecker、MSI Afterburner、**GpuTest** | GUI 工具（GpuTest 实测命令行参数无效，仅 GUI） |
| 硬盘 | CrystalDiskMark、AS SSD、ATTO、HDTune、HD Tune、BOOTICE、SpaceSniffer、WinDirStat、TxBENCH、SSDZ、H2testw、LLFTOOL、DiskGenius GUI 模式 | GUI 工具（BOOTICE 无命令行；H2testw 仅交互式） |
| 内存 | MemTest64、memtest、memtestpro、TestMem5（TM5）、ZenTimings、Thaiphoon、魔方内存盘、RAMMap | GUI 工具（TM5 可通过 TM5.ini 配置测试档） |
| 综合检测 | HWMonitor、Speccy、RWEverything、LatencyMon | GUI 工具 |
| 其他 | procexp（Process Explorer）、Geek Uninstaller、HiBit Uninstaller、GifCam、DesktopOK、SpaceSniffer、DirectX Repair、BatteryInfoView 之外的 NirSoft GUI 工具、**UltraISO** | GUI 工具（UltraISO 实测命令行挂起，仅 GUI） |

> 说明：这些工具仍然可以从命令行**启动**（`"路径\xxx.exe"`），只是没有可供脚本化的参数开关。

---

*文档生成于 2026-08-12。参数以各工具官方文档为准；标注 ※ 的工具参数整理自社区通用文档，如有出入请以工具内帮助（`--help` / `/ ?` / readme）为准。*
