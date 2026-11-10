# 项目多维度审查（2026-11-05）

> 修复已实施：下方发现描述的是修复前状态，逐项实现及最终验证见文末「修复记录」。

审查对象：当前工作区，包括已有未提交修改；不是仅审查 Git diff。没有修改业务代码。P1 为优先处理的安全问题，P2 为影响正确性、数据可靠性或交付的问题，P3 为应改善的工程问题。等级是项目内优先级，不是 CVSS。

主项目可见 C# 源码共 581 个文件、173,029 行，其中 33 个文件超过 1,000 行。这是范围统计，不代表逐行人工审完。检查覆盖主程序配置、进程管理、单实例、下载/更新、局域网服务、WinUI 界面，兼顾 TUI、Android 身份校验与遥测看板。

## 发现一览

| 编号 | 优先级 | 维度 | 问题 |
|---|---|---|---|
| 1 | P1 | 凭据/隐私 | GitHub 令牌明文存盘，并随配置备份导出 |
| 2 | P1 | 文件安全 | 配置导入仅校验字符串路径，未防目录联接越界 |
| 3 | P2 | MSIX 兼容 | 切换配置位置必须写安装目录，商店只读安装目录下失败 |
| 4 | P2 | 数据完整性 | 配置导入逐项删除覆盖，失败后留下半份配置 |
| 5 | P2 | 并发/激活 | 单实例启动监听有丢请求窗口，已隔离复现 |
| 6 | P2 | 进程/稳定性 | DISM/UltraISO 重定向输出后先等待退出，可能管道死锁 |
| 7 | P2 | 任务生命周期 | 镜像转换取消未传到已运行的子进程 |
| 8 | P2 | 数值持久化 | 区域格式改变后浮点配置被读成不同数值，已隔离复现 |
| 9 | P2 | 线程安全 | AppSettings 返回共享可变 Dictionary，读取没有同锁保护 |
| 10 | P2 | 数据可靠性 | 设置/收藏直接覆盖原文件，无原子替换及恢复副本 |
| 11 | P2 | 用户反馈 | 文件共享 HTTP 错误响应仍提示上传成功 |
| 12 | P2 | 多客户端一致性 | TUI 与 WinUI 共用收藏文件，互斥体协议和缓存未统一 |
| 13 | P2 | CI/测试 | 发布流水线未执行 .NET 测试，也没有 PR 自动测试门禁 |
| 14 | P3 | 构建可重复性 | 未固定 SDK，本机自动选择 .NET 11 预览版 |
| 15 | P3 | UI 性能 | 首页编辑器在 ScrollViewer 中嵌套 ListView，失去受限视口 |
| 16 | P2 | 剪贴板可靠性 | 窗口管理页绕过统一剪贴板重试入口，已有回归测试失败 |

## 1. GitHub 登录令牌进入普通配置备份

证据：`TubaWinUi3.WinUI3/Services/GitHubAuthService.cs:29–35` 直接通过 AppSettings 读写 GitHubToken；`Services/AppSettings.cs:57` 明文序列化并写盘；`Services/ConfigManager.cs:338–347` 导出 settings.json，没有剔除此字段。

触发：已登录 GitHub 后导出配置，将备份交给他人或上传排障。备份中可能包含仍有效的账号访问令牌。Device Flow 请求的 scope 为 public_repo（GitHubAuthService.cs:180），因此影响不只是泄漏用户名。没有读取用户实际令牌，也没有发送认证请求。

建议：令牌使用 Windows 凭据存储/DPAPI；普通备份明确排除 GitHubToken、代理密码等认证信息；导入备份不应恢复第三方登录会话。加密本地存储和导出剔除应同时实施。

## 2. 配置导入没有防止目录联接越界

证据：`Services/ConfigManager.cs:387–396` 仅 GetFullPath + UserToolLibrary.IsWithin，随后按路径创建目录、删除旧文件并解压。`Services/UserToolLibrary.cs:65–69` 的 IsWithin 是字符串前缀检查，不解析重解析点。已有 SafeSharePath 的重解析点防护未在这里使用。

触发前提：数据目录内已有指向外部目录的 junction/symlink，且用户导入包含该子目录路径的备份。例如 Data/linked 指向隔离外部目录，备份条目 linked/marker.txt 在字符串上通过检查，但实际文件访问落在外部目录。非打包主程序通常运行在管理员权限下，会放大影响。此项是明确代码路径分析，未对真实用户目录执行越界写入，也未声称远程零交互利用。

建议：导入前拒绝目标及祖先重解析点，并考虑检查与使用之间的替换竞态；先在私有暂存目录验证全部内容，再提交受控文件集合。

## 3. 配置位置指示文件写在安装目录

证据：`Services/ConfigManager.cs:25–26,107–125` 无论切换到 AppData 还是自定义路径，都先创建 AppDirectory/Data，并在那里维护 .config_location。`Pages/ConfigManagerDialog.xaml.cs:200–204` 调用此流程。

影响：真正 Store/MSIX 安装目录只读，即使目标自定义目录可写，切换仍会失败。注册可写 bin 的开发包不足以验证这个场景。建议把位置指示写在每用户可写且固定的位置，独立于当前数据目录和包安装目录。

## 4. 配置导入没有事务或回滚

证据：`Services/ConfigManager.cs:384–400` 读到一项就删原文件、解压一项；只在所有条目成功后才清缓存。外层只 catch 后返回 false。

触发：备份前几项合法、后面条目损坏/路径非法，或者中途磁盘写满。先前项已经覆盖，失败项可能在删除后写不完整，内存仍持有旧缓存。UI 的“导入失败”不表示原配置完整保留。

建议：预检路径、展开大小和文件格式；解压到暂存目录，验证后原子提交，保留回滚副本。缓存切换与磁盘提交统一处理。

## 5. 单实例激活监听启动期间漏请求（已复现）

证据：`Services/MainInstance/SingleInstanceService.cs:166–177` 先枚举待处理文件，再创建并启用 watcher，最后才订阅 Created。

在隔离宿主中链接实际源码，只把 ConfigManager 替换为临时目录桩：预置 first.json；处理它的回调中写入 second.json。结果为 `received=1, stranded=True`。第二份请求落在枚举快照之后、监听建立之前，既不在旧快照里，也收不到创建事件。

用户表现：启动过程中再次双击/从快捷方式打开指定工具，第二个进程退出，但主窗口不响应；该意图可能到下一次启动才被处理。

建议：先订阅并启动监听，再补扫积压，处理端按文件认领/去重；写端先写临时文件再原子改名，读端为文件未写完增加有限重试。另有小问题：IsRunning 的 TryOpenExisting 输出句柄被丢弃（:50），应及时 Dispose。

## 6. 镜像处理可能在输出管道上永久等待

证据：`Services/WindowsImageService.cs:363–370,391–400` 重定向 stdout/stderr 后，先 WaitForExit，成功分支完全不读取 stdout。`RunDismCaptureAsync` (:413–419) 则顺序读 stdout 而不并发排空 stderr。

触发条件：任一被重定向管道输出超过缓冲容量。子进程等父进程读取，父进程等子进程退出，任务永远不完成。DISM 长任务带进度输出，因此这不是所有调用都可安全忽略的模式。

建议：启动后立即并发读取两个输出流，再 await WaitForExitAsync；统一子进程执行器，设置有界日志及超时。审查未执行真实 DISM/ISO 转换。

## 7. CancellationToken 不会取消已开始的镜像子进程

证据：`Services/WindowsImageService.cs:354–371,383–421` 只是向 Task.Run 传 ct，委托内部同步 WaitForExit，没有注册取消或 Kill。任务开始后取消 token 不会终止运行中的 DISM/UltraISO。页面的 CancelConvertBtn_Click（`Pages/WindowsImagePage.xaml.cs:411`）也是空实现。

影响：用户取消下载后处理时，进程仍可能继续占用磁盘、CPU 并写输出；取消完成的语义不可靠。

建议：WaitForExitAsync(ct)，取消时终止并等待整个子进程树，最后清理临时产物；确认 UI 按钮与队列取消都进入同一执行路径。

## 8. 浮点设置依赖当前区域格式（已复现）

证据：`Services/AppSettings.cs:105,137–140` 写入使用 ToString("F2")，读取使用无 CultureInfo 的 double.TryParse。

隔离实测：en-US 写 1.5 得到字符串 1.50，再用 de-DE 读取，实际返回 150。系统区域格式变化、跨地区迁移备份都可触发；背景透明度、材质参数等会读成错误值。建议新值使用 InvariantCulture 和可往返格式，并为旧数据设计兼容迁移，读取后校验参数范围。

## 9. AppSettings 的并发读写锁不完整

证据：`Services/AppSettings.cs:20–43` 的 Load 在锁内取得缓存，但把原始 Dictionary 返回；`Get` (:119–123) 在退出锁后 TryGetValue，Set/Remove 在另一线程持锁修改同一个 Dictionary。

影响：这是共享 Dictionary 的并发读写，读者没有参与写者的同步协议。局域网请求、后台服务与 UI 都会访问设置；不能把 Load 内部锁当作整个读取的保护。

建议：Get 在相同 _gate 下完成查找；Load 对外返回只读快照，或把可变缓存完全封装。此项未用压力测试声称已复现崩溃。

## 10. 核心配置和收藏缺少原子持久化

证据：`Services/AppSettings.cs:57` 与 `Services/FavoritesService.cs:104` 都直接 File.WriteAllText 覆盖唯一原文件；加载失败回退为空字典/列表。

影响：写盘中进程中断、断电或 IO 失败可留下空/截断文件，下次启动按全新配置处理；后续写入可能覆盖恢复线索。收藏写失败还被 catch 静默吞掉，UI 先更新缓存，重启后更改消失。

建议：同目录临时文件 + flush + 原子替换，保留上次有效备份；加载失败隔离损坏文件并恢复；将持久化失败反馈给调用方。

## 11. 上传失败仍提示成功

证据：`Services/FluentPageHtml.cs:911–916` 的 xhr.onload 不检查 HTTP 状态或 JSON 结果，直接显示“已上传 N 个文件”。onerror 仅能处理网络层错误。

触发：服务返回 401、413、429 或 500 时，浏览器仍触发 onload。当前服务已经实现体积限制和并发限制，这些响应是实际可达分支。用户会误以为文件已安全传输。

建议：仅在 2xx 且响应业务状态成功时显示成功；失败展示原因，保留选择/重试入口。批量部分成功需要返回逐项结果或成功数量。

## 12. TUI/WinUI 同时修改收藏会丢更新

证据：`TubaWinUi3.Tui/Services/FavoritesService.cs:20–54` 每次读取磁盘、获取 Tui.Favorites 互斥体再原子写；WinUI 的 `Services/FavoritesService.cs:11–35,42–65,96–106` 长期缓存且不使用这个互斥体。

触发：两个界面均已打开，TUI 添加 B；WinUI 仍持有不含 B 的旧缓存，随后添加 A，写回旧列表覆盖掉 B。TUI 单方面加互斥体不能同步另一个未参与者。

建议：两端共享一套存储协议和互斥体；锁内重读最新磁盘数据再合并修改；用版本号或文件变更事件刷新 UI 缓存，排序也参与同一协议。

## 13. CI 发布不以测试通过为前提

证据：`.github/workflows/` 下可见工作流均为手动触发；检索没有 dotnet test。`build-test.yml` 实际是产物构建工作流，不能等同于运行 xUnit。发布流可在测试失败时照常生成发布包。

建议：新增轻量 PR/push 构建测试工作流，并让手动发布复用同一测试任务，测试失败阻止发布。硬件集成测试与纯逻辑测试分组，前者在专用机器或手动验证运行，不让硬件环境成为基础测试的阻塞条件。

## 14. 构建未固定 .NET SDK

仓库未见 global.json。本机默认 dotnet 实际选择 11.0.100-rc.1.26425.128，尽管主程序面向 .NET 10；CI 安装 10.0.x。首次 Release 验证出现 DashboardContentEditor 的 WMC0909/WMC1111 类型解析错误，不能仅凭这次失败断言该 XAML 源码无法编译。

建议：固定受支持的 .NET 10 SDK feature band 和 rollForward 策略，开发、CI、发布使用一致 SDK。将 SDK 11 下的失败单独记录为兼容性线索。

## 15. 首页编辑器有嵌套滚动性能风险

证据：`Controls/DashboardContentEditor.xaml:61–114` 外层 ScrollViewer 内包含 SelectedList，列表没有固定/最大高度，沿滚动方向可能以无界高度测量，无法依靠正常受限视口控制容器创建。

影响：已选卡片较多时创建更多 UI 元素，拖动、选择和滚动成本增高；两个滚动容器也使键盘与触摸滚动更难预测。此项基于布局静态检查，没有宣称测得具体帧率或内存增长。

建议：为列表分配受限星号行，让其自身滚动；窄窗口下把详情编辑区作为独立区域或响应式布局。用大量卡片进行实际 UI 性能验证。

## 16. 窗口管理页绕过统一剪贴板服务（测试已失败）

证据：`Pages/WindowManagerPage.xaml.cs:200` 直接调用 Clipboard.SetContent；`ClipboardSingleEntryPointTests.OnlyClipboardServiceWritesToTheClipboard` 本次运行明确失败并指出该位置。

影响：剪贴板被其他程序短时占用时，当前处理器立即报错，没有 ClipboardService 提供的重试与统一日志。这里已有 try/catch，因此不能据此称当前处理器一定会闪退。

建议：调用 ClipboardService.TrySetText 并按结果反馈成功/失败，恢复现有回归测试。无需更改测试规则来豁免这一个调用点。

## 工程与文档补充观察

- AGENTS.md 开头写“没有本地化系统”，但实际已有 Strings/en-US、zh-CN 与 LocalizationService；AI 助手一节提及的 AgentSession/AiAgentPage 文件在当前工作区也不存在。应按当前架构更新说明，避免后续维护者按旧路径修改。这不是要求恢复已删除功能。
- 当前主项目存在 3,957 行的 RogueCleanerPage、3,497 行的 HardwareInfoService、2,715 行的 GameOverlayPage 等大文件。建议先围绕进程执行、配置事务、共享文件存储提取可测试边界；不建议仅为形式统一把全部静态服务改成 DI/MVVM。
- 构建输出中出现空引用、过时 Skia API、xUnit 阻塞调用等警告，应区分真实风险与测试钩子噪声逐项处理。未把每条 warning 都认定为产品缺陷。
- 本次没有执行真实 UI 无障碍测试、长时间监控内存基准、ARM64/x86 实机测试、Store 只读安装包验证，也没有对 Tools 内第三方 exe/驱动做二进制或 CVE 审计；这些仍是后续专项验证范围。

## 验证记录

- 遥测看板：`npm test`，20 项通过，0 项失败。
- 隔离源码宿主：单实例漏请求和跨区域浮点误读均复现；仅在 TEMP 下创建测试数据，没有操作用户真实设置。
- 主项目 Debug 测试构建：被正在运行的 TubaWinUi3.exe 锁定，未终止用户进程。
- 主项目 Release + 默认 SDK 11：XAML 编译失败。
- .NET 10.0.400 + Release + 串行构建 + 关闭共享编译：主程序与测试项目编译成功。完整测试 2,680 项，通过 2,677、失败 2、跳过 1，测试执行耗时 2 分 8 秒。使用 ExcludeToolsFromPublish=true 避免复制完整第三方工具目录。
- 失败 1：ClipboardSingleEntryPointTests.OnlyClipboardServiceWritesToTheClipboard。源码直连剪贴板，见发现 16。
- 失败 2：WindowManagerTests.ExternalWindowCanBeChangedHiddenAndRestored，在第 40 行“两次图标 PNG 完全相同”的断言失败：PNG 宽度字节显示从 16 变成 32；尚未执行该方法后续的窗口恢复断言，因此不能据此判定窗口恢复功能坏了。单独无构建重跑该测试通过（1/1）。说明有时序/环境敏感性，需要让外部测试窗体进入图标稳定状态后再比较，或在目标窗体中明确设置固定图标；不能只靠重跑消除这个信号。
- 跳过项：PhoneLinkTests.Service_HttpRemainsAvailableWhenDiscoveryPortIsBusy_AndRestartKeepsCode，AdminFact 需要管理员测试宿主。该测试涉及旧 HTTP.sys 的描述也应随当前 Kestrel HTTPS 实现检查是否仍适用。
- TRX：TubaWinUi3.Tests/TestResults/project-audit.trx。第二次单测重跑使用默认结果输出，没有覆盖此完整测试 TRX。

可重复的完整验证命令（本次在 TEMP 下的 global.json 固定 SDK 10.0.400，并以主项目所在仓库的绝对项目路径执行）：

```powershell
dotnet test TubaWinUi3.Tests/TubaWinUi3.Tests.csproj -c Release --no-restore -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false -p:ExcludeToolsFromPublish=true --logger 'trx;LogFileName=project-audit.trx'
```

建议处理顺序：凭据导出与配置导入边界 → 配置事务/原子落盘 → 激活与子进程生命周期 → 上传反馈及收藏协议 → CI 与 UI 性能。


## 修复记录（2026-11-05）

报告中的 16 项均已落实代码或工程配置修复；原始审查正文保留用于追踪。

| 编号 | 已实施的修复 |
|---|---|
| 1 | AppSettings 对 GitHub、代理、搜索等凭据使用当前 Windows 用户的 DPAPI；首次加载迁移旧明文并保护备份。配置导出和导入递归剔除凭据/配对授权，排除恢复副本和激活目录。 |
| 2 | 导入统一使用 SafeSharePath，拒绝越界、特殊文件、重复路径及重解析点；提交期间以目录句柄禁止祖先目录被重命名替换。 |
| 3 | 位置标记移至每用户可写目录，保留旧便携版标记的只读回退，明确保存 AppData 选择；TUI 同步读取新协议。 |
| 4 | 导入先暂存、验证全部条目/展开大小/JSON，再提交；保留原文件，异常时逆序回滚，回滚失败保留恢复副本。 |
| 5 | ActivationInbox 先监听后扫描；处理 Created/Changed/Renamed，并定时补偿扫描。写端原子改名，读端等待旧写入者完成，认领后派发，隔离坏请求；IsRunning 及时释放互斥体句柄。 |
| 6 | CapturedProcess 并发排空两个管道，捕获内容有上限；DISM 和 UltraISO 共用执行器，检查退出码。 |
| 7 | 执行器取消/超时后终止并等待进程树。镜像转换使用下载队列传入的同一 token，删除未被界面使用的空取消处理器。DISM 索引查询固定英文输出并校验结果。 |
| 8 | 浮点写入 InvariantCulture + 往返格式；旧逗号/小数点格式兼容读取，拒绝千位分隔歧义和 NaN/Infinity。 |
| 9 | AppSettings.Load 返回独立快照；查找和修改在同一锁内完成；旧测试改为通过数据目录和缓存失效隔离，不再直接修改内部字典。 |
| 10 | 同目录临时写入、flush 后原子替换，保留 .bak 并支持自动恢复；坏文件只隔离一次；设置和收藏持久化失败有中英双语 UI 提示。 |
| 11 | 上传处理检查 HTTP 状态，仅成功后提示完成/清空选择，失败保留选择并展示服务端错误。 |
| 12 | WinUI 和 TUI 链接同一 FavoriteFile/AtomicFile，共用按路径派生的互斥体；修改锁内重读，排序保留其他前端新加的收藏。 |
| 13 | 新增可复用 quality 工作流，PR/主分支自动测试，GitHub 发布构建依赖通过门禁；包含 .NET、TUI AOT 和 JS 测试；GitCode 发布任务也增加测试步骤。 |
| 14 | global.json 固定 .NET 10.0.400 feature band，允许同 band patch 更新、拒绝预览；流水线安装匹配 SDK。 |
| 15 | 列表直接放在受限高度 Grid 的星号行中，只让详情区独立滚动，窄窗口约束同步调整。 |
| 16 | 窗口管理页改用 ClipboardService，按复制结果显示反馈。 |

补充：窗口管理集成测试使用固定图标并等待 Shown，再比较两次读取；改为 await，消除相关阻塞警告。AGENTS.md/CLAUDE.md 的本地化与已移除 AI 助手说明已更新。配置迁移保留原目录作为恢复副本，避免递归删除用户自定义目录里的其他文件。

更正原第 15 项的风险表述：复核发现旧 code-behind 已设置 SelectedPane.Height，因此原文“无界高度/虚拟化失效”不能视为已证实；本次仍移除了嵌套列表滚动，采用更直接的受限布局。没有编造帧率或内存改善数据。

验证覆盖：新增 AuditFixTests，以及 lan-share-upload.test.mjs。目录联接测试只使用临时目录；进程测试只启动临时 PowerShell 子进程，不运行 DISM、安装器或压力测试。

实现边界：导入回滚覆盖捕获到的执行失败，不是多文件断电事务；进程崩溃中断导入时需使用保留的 .config-import-*/rollback 恢复副本。未执行真实 Store 只读安装、ARM64/x86 实机和 UI 截图验收；CI 文件已本地校验，未向远端触发流水线或发布。


### 修复后最终验证

- .NET SDK 10.0.400，Release 全量测试：**2,700 项，通过 2,699，失败 0，跳过 1**；执行耗时 1 分 28 秒。跳过的是原有 AdminFact 手机服务集成测试，当前宿主没有管理员权限。
- 新增 AuditFixTests：20 个测试用例，均通过；包括旧明文迁移、真实导出脱敏、导入预检/回滚/目录联接拦截、可写位置标记、浮点跨区域、并发快照、收藏跨前端合并与损坏恢复、激活竞态/坏请求、管道大输出和子进程取消。
- 文件共享上传 JS 回归：5/5 通过，覆盖 HTTP 401/413/429/500 与成功响应。
- 遥测看板：20/20 通过。
- TUI win-x64 Native AOT 发布成功，没有新增 IL2026/IL3050 警告；发布 exe 的 --version 冒烟成功。
- GitHub Actions/GitCode YAML 解析、发布 needs 依赖、XAML/RESW XML 解析及 git diff --check 均通过；WinUI Analyzer 已随正常项目构建运行，未关闭现有检查。
- 最终全量结果：TubaWinUi3.Tests/TestResults/audit-fixed-final.trx。

没有启动远端 CI、提交 Git 或发布产物；工作区原有修改继续保留。
