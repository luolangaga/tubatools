---
name: tuba-toolbox
description: 在 Windows 上通过图吧工具箱读取硬件配置、CPU/GPU 温度和功耗、内存占用、磁盘 SMART 健康和 CPU-Z 报告，并调用已安装的 FurMark、DiskSpd 等 CLI 工具。用户要求排查电脑卡顿、过热、掉帧、蓝屏、硬盘故障或进行硬件诊断、稳定性测试时使用。
compatibility: Windows 10/11、已安装图吧工具箱 CE、Node.js 22.20 或更新版本；Agent 可使用普通权限，硬件查询由工具箱管理员查询通道完成。
---

# 图吧工具箱自动化诊断

将下文 `<skill>` 替换为本 SKILL.md 所在文件夹的绝对路径。使用系统 shell 正确引用路径，始终通过 Node 脚本调用，不依赖当前目录或工具箱 GUI exe 的标准输出。

```powershell
node "<skill>/scripts/toolbox.mjs" help
node "<skill>/scripts/toolbox.mjs" hardware
node "<skill>/scripts/toolbox.mjs" sensors
node "<skill>/scripts/toolbox.mjs" disks
node "<skill>/scripts/toolbox.mjs" cpuz
node "<skill>/scripts/toolbox.mjs" tools
```

## 排查流程

1. 先了解症状，再获取 hardware、sensors、disks。它们复用工具箱内置硬件盘点、LibreHardwareMonitor 和 SMART 服务；无需打开主窗口，不影响已运行的工具箱。
2. 检查 `ok` 和 `error`。传感器数值 `-1` 表示不可用，不能解释为零温度/零功耗。温度 °C、功耗 W、频率 MHz、负载 %、容量 GB；读取时间见 `capturedAt`。`sensorError`、磁盘的 `error` 与 `hasSmart` 需分别检查，即使顶层成功也可能缺部分数据。
3. CPU-Z 报告使用 cpuz。普通权限 Agent 会自动连接已提权的工具箱查询通道；工具箱未运行时自动弹出 Windows 管理员授权窗口，允许后由无窗口查询助手完成读取，Agent 自身保持普通权限。助手空闲 5 分钟自动退出，连续采样期间复用授权。无需要求用户重启 Agent 或手动运行管理员终端。用户取消 UAC 时说明已取消，可重新调用并允许授权；不要把它说成“不支持读取”。不要自动安装驱动或绕过 UAC。检查 `callerIsAdmin` 与 `executionMode` 区分 Agent 自身和实际查询权限。工具/驱动本身不支持的传感器仍可能不可用。
4. 使用 tools 获取实际已安装的 CLI 工具 ID 与路径。`path: null` 表示未安装，不要自行猜测路径。命令参考 [CLI 工具文档](references/cli-tools.md)。不同版本参数以本机工具帮助为准。
5. 根据观测给出证据、可能原因和下一步。一次快照无法证明硬件损坏。需要趋势时重复采样，保留时间戳。

## 执行外部工具

读取信息优先用上面的内置命令。执行第三方工具需要 `--allow-execution` 和 1–300 秒时限。工具进程及子进程超时会被终止。参数逐项传递，不经过 shell，不支持管道、重定向或任意路径执行。

```powershell
# 显卡短基准：仅在用户已授权压测后执行
node "<skill>/scripts/toolbox.mjs" run furmark --allow-execution --timeout 45 -- --demo furmark-vk --width 1280 --height 720 --benchmark --duration-ms 15000 --no-score-box
```

工具输出位于 `data.stdout` / `data.stderr`，各保留最多 65536 字符，退出码和超时状态位于 `data.exitCode` / `data.timedOut`。顶层 `ok` 与工具 `data.success` 均应检查。GUI/报告工具可能无 stdout，这不等于生成了报告；需检查命令指定的文件是否存在、是否为本轮新生成。

压测、写盘、磁盘整理/分区、启动项/USB/驱动/超频修改需要用户明确授权具体操作，不能从“检查一下电脑”推断授权。磁盘测试只允许使用用户同意的独立临时文件；禁止对裸盘、分区或现有文件写入。DiskSpd 创建测试文件也会写盘，即便使用 `-w0`，也要说明文件大小及路径。不要执行刷固件/格式化/擦除操作。压力测试时并行采样温度，出现异常、用户要求停止或超过约定时长即终止本次测试进程。

CLI 支持工具的已知名单见 tools；完整工具箱里许多工具仅支持 GUI，不能当作 CLI。不要自动运行本文档中改变硬件或系统设置的示例。

## 安装与路径

从工具箱“设置 → Agent Skills”选择 Agent 后安装。技能携带当前工具箱 exe 的本地路径。移动/升级安装目录后重新安装技能。也可设置环境变量 `TUBA_TOOLBOX_EXE` 覆盖为工具箱 exe 的绝对路径。该技能仅调用本机工具，无需配置 API Key 或启动网络服务。
