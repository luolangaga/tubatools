# 外部 Agent 调用图吧工具箱

在 **设置 → Agent Skills** 安装 Node.js 22.20+，选择 Codex / Claude Code / Cursor / OpenCode，点击“安装到所选 Agent”。安装通过 `npx skills add <本地技能路径> --global --agent <id> --yes --copy` 完成。也可复制 PowerShell 命令自行执行，或打开导出的技能文件夹手动安装到支持标准 SKILL.md 的 Agent。

安装完成后新建 Agent 会话，说：“使用图吧工具箱检查电脑卡顿原因，先读取温度、功耗和硬盘健康。”

## 无窗口接口

技能内的 `scripts/toolbox.mjs` 使用 Node 内置模块，读取相邻的 `toolbox.json` 获取工具箱路径，无额外 npm 依赖。可用环境变量 `TUBA_TOOLBOX_EXE` 覆盖路径。移动/更新安装目录后需重新安装技能。

```powershell
node "<技能目录>/scripts/toolbox.mjs" hardware
node "<技能目录>/scripts/toolbox.mjs" sensors
node "<技能目录>/scripts/toolbox.mjs" disks
node "<技能目录>/scripts/toolbox.mjs" cpuz
node "<技能目录>/scripts/toolbox.mjs" tools
node "<技能目录>/scripts/toolbox.mjs" run diskspd --allow-execution --timeout 10 -- '-?'
```

直接调用 exe 的协议为：

```text
TubaWinUi3.exe --agent-cli <命令> --output <本地绝对路径.json> [参数]
```

这是 Windows GUI exe，调用方必须等待进程退出后读取结果文件；推荐使用 Node 桥接脚本，由它处理等待、临时文件和退出码。

结果带 `schemaVersion: 1`、`ok`、`command` 和 `data`，失败返回 `error.code` / `error.message`。退出码：0 成功，1 命令或工具执行失败，2 无法写入结果。成功数据带 UTC 采集时间、管理员状态。温度 °C、功耗 W、频率 MHz、负载 %；传感器 -1 表示不可用。即使顶层成功，也应检查传感器/磁盘的各项可用状态。

Agent 无需以管理员身份运行。hardware/sensors/disks/cpuz 自动连接同一 Windows 用户、同一安装目录的管理员查询通道：优先复用主程序；未运行时通过标准 UAC 启动无窗口查询助手，助手空闲 5 分钟退出。取消授权会返回可重试的明确错误。查询通道只接受四个固定命令，拒绝路径、工具参数及 run；管理员端通过命名管道返回数据，不写调用方指定的文件。返回 `callerIsAdmin` 和 `executionMode`，区分调用方权限和实际查询权限。驱动/硬件不支持的项目仍会返回不可用。

`tools` 返回当前 CLI 白名单工具及路径（未安装为 null）。`run` 要求工具 ID、`--allow-execution`、1–300 秒超时及 `--` 后逐项工具参数；使用 ArgumentList 启动进程，不经过 shell。它执行本地工具的原生命令行，不推断参数语义。Agent 必须先取得用户对压测/写盘/系统修改等具体操作的授权。输出各最多保留 65536 字符；工具退出码、超时和成功状态也会返回。作业对象负责 CLI 退出后的子进程清理，超时则终止本次进程树。

## 扩展

- 在 `AgentCliService.Tools` 登记已验证支持 CLI 的第三方工具及候选 exe 名称；优先使用既有架构选择服务。
- 在 `AgentCliRequest`、`AgentCliService` 添加无窗口内置能力；复用服务层，不创建工具页面。
- 更新技能说明、帮助和本协议说明，添加对应解析/错误测试。
- 技能资源在 `Assets/AgentSkills/tuba-toolbox`，随应用自动复制/发布；导出时写入本机 exe 路径，并附上完整 CLI 工具文档。

验收建议：普通权限 Agent 授权后读取 CPU-Z、连续采样复用权限、取消 UAC 返回明确错误、管理员通道拒绝 run 和输出路径、不可用传感器不误判为零、中文参数不失真、未知工具/无界时长被拒绝。
