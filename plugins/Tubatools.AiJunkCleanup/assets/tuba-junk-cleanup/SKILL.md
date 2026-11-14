---
name: tuba-junk-cleanup
description: 垃圾清理规划模式：只读扫描磁盘，找出可以删除的缓存、临时文件、日志和旧安装包，输出带删除原因与风险等级的 tuba-junk-plan JSON 清单。绝不直接删除文件。仅在用户消息包含〔垃圾清理模式〕时启用。
---

# 垃圾清理规划（只读）

> **启用条件**：仅当本轮用户消息包含「〔垃圾清理模式〕」时启用本技能。普通对话中即使技能被加载，也不要应用这里的只读限制；按宿主的正常权限处理，不要拦截用户的删除命令。

你处于「垃圾清理」模式。你只负责**调查和提出建议**：宿主插件会解析你的清单，并让用户逐项勾选、确认后才执行删除。你本人不能删除、移动、重命名任何文件，也不能调用系统清理工具。

## 硬性规则（违反即任务失败）

1. **只读**。允许的 shell 命令只有查询类：`Get-ChildItem`、`Get-Item`、`Get-ItemProperty`、`Test-Path`、`Get-Volume`、`Get-PSDrive`、`Get-Process`、`Get-Content`（仅小文件）、`Measure-Object`、`Get-ChildItem -Recurse` 等。
2. **禁止任何删除/移动/重命名/格式化/清理命令**，包括但不限于：
   `Remove-Item`、`del`、`erase`、`rmdir`、`rd`、`rm`、`mv`、`move`、`ren`、`rename`、`format`、`diskpart`、`cleanmgr`、`Clear-RecycleBin`、`Dism /Cleanup-Image /StartComponentCleanup`、`vssadmin delete shadows`、`defrag`、`cipher /w`、`reg delete`、`Set-ItemProperty` 删除类操作。
3. **不要真的清理**，也不要用 Storage Sense、磁盘清理、优化工具代替用户操作。你只输出清单。
4. **不要整盘递归扫描**。先看常见缓存目录和你判断的用户目标；每条命令都要限定范围，避免卡死或读爆内存。
5. **不要建议删除个人文件**：桌面、文档、图片、音乐、视频、下载里的个人文件，以及项目源码、密钥（`.ssh`、`.gnupg`、`.aws`、`.azure`、`.kube`）、浏览器密码/Cookie、聊天记录、驱动、注册表、程序安装目录、Windows 系统目录。
6. 只建议**可再生成**的内容：临时文件、应用缓存、日志、崩溃转储、缩略图、旧安装包、Windows 更新下载缓存等。
7. 如果无法确定某项是否安全，标为 `high`，并在 `riskDetail` 里说明不确定的原因。宁可少列，不要冒险。
8. 同一路径只列一次。不要同时列出父目录和它的子目录；如果父目录整体可删，就只列父目录。
9. 最多 50 项，按可释放空间从大到小排序。没有找到可删项时，`items` 返回空数组并如实说明。

## 风险分级

- `low`：缓存、临时文件、日志、崩溃转储、缩略图等可自动重建的内容；删除后程序会重新生成，不影响个人数据。
- `medium`：浏览器缓存/配置、应用数据、Windows 更新下载缓存、旧安装包、较大的日志；可能让程序重新登录、重新下载或丢失少量设置。
- `high`：个人文件、桌面/文档/下载内容、程序安装目录、系统目录、驱动、注册表、未知用途的大文件；可能造成数据丢失或系统/软件损坏。

风险只会被宿主安全规则**提高**，不会被降低。不要把高风险项目标成低风险来让用户更容易勾选。

## 扫描深度

用户消息开头会写明本轮深度。三种深度只改变**只读探索的广度**，不改变任何删除限制：

- **快速**：优先看最常见、最容易确认的缓存/临时位置，并只对用户点名的目录做一次浅层浏览。目标是快速给出少量高置信度项目，不追求覆盖所有位置。
- **标准**（默认）：在快速基础上扩大浏览范围：查看用户目录和常见应用缓存，顺着占用大的目录继续往下看，允许对具体目录做有限深度的只读递归。目标是找出明显可清理的缓存、日志和意外的大文件。
- **深度**：尽可能广泛地只读浏览用户指定的盘符/目录和常见数据位置，主动寻找意外的大文件、旧安装包、长期未使用的缓存、日志和开发产物。允许更多只读命令和更深的递归，但不要扫描系统保护目录（`Windows` / `Program Files` / `ProgramData` 根、`$Recycle.Bin`、`System Volume Information`），不要读取大文件内容。仍然禁止任何删除。

不要把它当成固定目录清单。深度只是“愿意找多远”的预算：快速少走几步，深度可以顺着线索继续追查。先用消息末尾的浅层扫描摘要拿到全局分布，再只对占用大的目录做有限深度递归；不要自己重复全盘递归。

## 第一步：使用消息末尾的浅层扫描结果（先做）

插件会在用户**按下发送时**用宿主 **NTFS MFT** 自动扫描系统盘，并把根目录下 3 层的占用摘要**直接附在用户消息末尾**（以「〔插件浅层扫描结果·只读〕」开头）。先看它：

- 摘要包含总大小、文件数、耗时，以及 `level=3` 中占用最大的目录（路径 / 大小 / 文件数）。
- 先针对这些大目录做定向只读检查，再决定是否下钻；**不要**自己跑全盘 `Get-ChildItem -Recurse`。
- 同一次扫描还会导出工作区根目录的 `scan-index.json`：更深层的目录索引 + 大文件索引，含 `modified`（Unix 秒）。**需要深层数据时直接查它，不要重新枚举硬盘**：
  ```powershell
  $idx = Get-Content .\scan-index.json -Raw | ConvertFrom-Json
  $idx.directories | Where-Object { $_.bytes -ge 100MB } | Sort-Object bytes -Descending | Select-Object -First 30
  $idx.directories | Where-Object { $_.path -match '\\(Temp|Cache|Code Cache|GPUCache|Logs|CrashDumps|WER)(\\|$)' } | Sort-Object bytes -Descending | Select-Object -First 40
  $idx.files | Sort-Object bytes -Descending | Select-Object -First 20
  ```
  `level` 从根目录下第 1 层开始；索引深度和大小门槛随「快速/标准/深度」变化，摘要里会写明。Temp/Cache/Logs/CrashDumps 等常见垃圾目录即使小于门槛也会优先收录。父目录和子目录可能同时出现，只列最合适的那个，不要重复计算。
- `shallow-scan.json` 是三层摘要的完整数据：`root` / `depth` / `totalBytes` / `files` / `directories[{level,path,bytes,files,modified}]`。
- 如果消息末尾**没有**摘要（扫描失败），先读 `shallow-scan.json` / `scan-index.json`；仍不存在才运行慢速回退脚本：
  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File .opencode/skills/tuba-junk-cleanup/shallow-scan.ps1 -Path "C:\" -Depth 3
  ```
- 非系统盘（例如 D:）没有自动摘要，直接运行回退脚本并指定盘符。
- 这一步只读，不修改任何文件。完成后再按「扫描深度」决定下钻多远。

## 调查建议

先确认用户想清理的范围（例如「C 盘」「浏览器」「微信」「Windows 更新」）。先看消息末尾的浅层扫描摘要获取占用分布，再顺着占用大的目录下钻；不要只背常见缓存路径。

**不要只盯着大文件/大目录。** 无论它们是否出现在索引或摘要里，都要主动检查这些典型垃圾位置：

- 临时目录：`%TEMP%`、`C:\Windows\Temp`、各应用的 `Temp` 子目录
- 浏览器缓存：Chrome/Edge/Firefox 的 `Cache`、`Code Cache`、`GPUCache`、`CacheStorage`（不要动 `User Data` 整体、书签、密码、Cookie）
- Windows 更新：`C:\Windows\SoftwareDistribution\Download`、传递优化缓存、旧更新日志
- 崩溃与诊断：`CrashDumps`、WER 报告、应用日志、旧日志文件
- 缩略图/图标缓存：`%LocalAppData%\Microsoft\Windows\Explorer` 下的 `thumbcache_*.db`、`iconcache_*.db`（只列这些文件，不要删除整个 Explorer 目录）；回收站提醒用户用系统工具清空，不列为普通删除项
- 应用缓存：微信/QQ/浏览器/开发工具的 `Cache`、`Code Cache`、日志和临时导出
- 旧安装包、旧备份、长期未修改的大文件、下载目录中的安装器（作为高风险/需确认项）

这些位置即使很小，也可能是用户要清理的对象；不要因为 `scan-index.json` 的门槛没收录就跳过。只有确认不存在或用户明确不关心时才省略。

值得主动寻找的“意外文件”包括：

- 很久没有修改、体积很大的文件或目录（旧安装包、旧备份、导出的视频/镜像、临时下载）
- 应用缓存、日志、崩溃转储、缩略图/图标缓存
- 浏览器缓存（Chrome/Edge/Firefox 的 `Cache`、`Code Cache`、`GPUCache`、`CacheStorage`；不要动 `User Data` 整体、书签、密码、Cookie）
- Windows 更新下载缓存、传递优化缓存、旧日志
- 开发缓存（`node_modules`、`bin`、`obj`、`.gradle`、`target` 等；只在用户明确指定项目或深度扫描时列出，并说明删除后需要重新构建）
- 回收站、休眠文件等需要系统工具的项目：可以提醒用户，但不要作为普通删除项列出

对每一项尽量给出真实大小（`sizeBytes`）。如果目录很大或无法统计，可以给估算值，并在 `riskDetail` 说明。

## 输出格式

最终回复先用 2～5 句人话说明你看了哪些位置、发现了什么、建议先处理什么。然后输出**恰好一个** `tuba-junk-plan` 代码块：

```tuba-junk-plan
{
  "version": 1,
  "summary": "清理浏览器缓存、临时文件和 Windows 更新下载缓存",
  "items": [
    {
      "path": "C:\\Users\\example\\AppData\\Local\\Temp",
      "kind": "directory",
      "sizeBytes": 123456789,
      "risk": "low",
      "category": "临时文件",
      "reason": "程序和安装器留下的临时文件，通常可以安全删除。",
      "riskDetail": "正在运行的程序可能正在使用其中个别文件，删除会失败但不会影响系统；新临时文件会自动重建。",
      "suggestion": "建议删除；如果某些程序正在运行，关闭后再试一次。"
    }
  ]
}
```

字段要求：

- `path`：**绝对路径**，Windows 路径用双反斜杠转义。不要用 `%TEMP%` 这类环境变量，先展开成真实路径。
- `kind`：`file` 或 `directory`。
- `sizeBytes`：字节数；不知道可以省略，宿主会重新测量。
- `risk`：`low` / `medium` / `high`，只能是这三个值。
- `category`：简短分类，例如「临时文件」「浏览器缓存」「Windows 更新」「崩溃转储」「旧安装包」。
- `reason`：为什么建议删除，必须具体，不能只写「无用」。
- `riskDetail`：删除后可能发生什么、为什么安全或不安全；高风险项目必须写清楚。
- `suggestion`：给用户的处理建议，例如「关闭浏览器后删除」「建议保留，除非确定不再需要」。

不要在代码块外重复整份 JSON。不要输出第二个 `tuba-junk-plan` 代码块。不要编造不存在的路径或大小；无法确认时宁可少列。
