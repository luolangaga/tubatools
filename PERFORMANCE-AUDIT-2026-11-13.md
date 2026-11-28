# 卡顿 / 未响应 代码审计报告

日期：2026-11-13
范围：`TubaWinUi3.WinUI3`（主程序）全部服务层与页面层，`Shared`、`TubaWinUI3.BackEnd` 仅在看门狗与进程协作处引用
性质：**静态全量排查 + 关键路径实测**（本次未改动任何代码，未做动态复现）

---

## 一、结论先说

用户反馈的「卡顿 / 未响应」不是单点 bug，而是**两类问题叠加**：

1. **UI 线程上的同步阻塞**（磁盘全量扫描、命名互斥体 + 文件读、无超时 `Join`、子进程双管道读取）——一旦命中就是几百毫秒到数秒的整窗冻结，Windows 会把标题栏写成「未响应」。
2. **高频、无节流的 UI 重建**（AI 流式输出逐帧重建整棵 Markdown 视觉树、250 ms 常驻 UI 线程轮询）——不是整窗冻结，而是持续的掉帧、输入迟滞，用户描述为「卡」。

按严重度排序，最可能的三个根因：

| 级别 | 根因 | 用户怎么遇到 | 典型表现 |
| --- | --- | --- | --- |
| **P0-1** | `ToolCatalog` 全量目录扫描被同步调用在 UI 线程（且扫描内部有 O(N²) 同级目录枚举 + 每项一次「命名互斥体 + 收藏文件读」） | 搜索结果点进去、打开「内置工具」页、教程里点「启动工具」 | 点击后 1–15s 无反应，窗口「未响应」 |
| **P0-2** | AI 助手流式输出每来一段就 `Children.Clear()` + 全文重解析 + 重建所有 block；同时用「拼接全部消息文本」做变更签名 | 用 AI 助手问答、长回答、长会话 | 输出期间输入框卡、滚动卡、偶发整窗未响应 |
| **P0-3** | `ComputerControlService` 的 250 ms `DispatcherTimer` 从启动起永不停止，每 tick 在 UI 线程做两次目录枚举 + 逐文件读 | 全程（尤其装了杀软/同步盘的机器） | 常态轻微掉帧、滚动不顺、功耗偏高 |
| **P1-1** | 设置页单页 1173 个 XAML 元素（135 KB）一次性构建 | 首次点「设置」 | 0.3–1.5s 白屏/卡顿（有 `NavigationCacheMode=Enabled`，只付一次） |
| **P1-2** | 硬件信息页每次进入都重建 WebView2 场景并销毁 | 反复进出「硬件信息」 | 每次进出 0.2–1s 停顿 + 浏览器进程反复启停 |
| **P1-3** | 教学画布每条画布消息一个 WebView2 | 教学模式多轮 | 内存/CPU 上升，切页卡 |
| **P2** | 无超时的 `Thread.Join()`、`reg.exe` 双管道 `ReadToEnd` 顺序读（潜在永久死锁）、`MarkdownView` 无增量路径 | 偶发 | 极小概率「一直未响应，只能杀进程」 |

---

## 二、排查方法与量化口径

- 全仓检索 UI 线程阻塞模式：`.Result` / `.Wait()` / `GetAwaiter().GetResult()` / `WaitForExit` / `WaitOne` / `Thread.Sleep` / `Join()` / `DispatcherTimer` / `CompositionTarget.Rendering` / `async void` 事件；逐条区分「已完成 Task 上的 `.Result`（无害）」与「未完成等待（有害）」。
- 对每个「高频 UI 刷新点」核对：是否有节流、是否 diff 更新、是否虚拟化、是否把解析/IO 挪到后台。
- 实测（本机、缓存热、开发者 Tools 目录：9 个分类、82 个子目录、137 个可启动文件）：

| 实测项 | 结果 |
| --- | --- |
| `FileVersionInfo.GetVersionInfo`（每工具一次） | 137 个共 77 ms，**0.56 ms/个** |
| `Directory.GetDirectories`（同级目录枚举） | 9 次调用出 82 个子目录，11 ms（含 PowerShell 每调用开销）；**C# 实测 0.105–0.145 ms/次**（见第十一节校准） |
| 「命名互斥体 + 读收藏文件 + 双重 JSON 解析」一次 | **≈1.1 ms/次**（含 PowerShell 解释开销，纯 C# 更低，但量级一致） |
| 扫描内部同级目录枚举次数（`Σ 每分类工具数 × 该分类子目录数`） | **≈2105 次**（C# 实测 222–304 ms 单线程；8 核 `Parallel` 摊薄后 **≈30–50 ms**，见第十一节） |

> 说明：以上是**本机小目录**的数字。整目录冷扫描实测 **≈110–220 ms**（SSD、缓存热）；机械盘/网络盘/杀软逐文件扫描 + 更大的工具库会放大 3–20 倍，即 **0.3–4 s**——这才是「用户机器上会卡」的量级，不要按本机数字直接外推成 30 s。
> 未做动态复现（需真实管理员会话 + 用户 Tools 目录），所以本报告结论是「机制上确定 + 成本量级实测」，不是火焰图结论。

---

## 三、P0-1：目录扫描在 UI 线程（最像「未响应」的一条）

### 证据

扫描本体完全同步、无异步壳：

- `Services/ToolCatalog.cs:436` `GetAllToolsCached()` → `PeekAllToolsCache()` 未命中 → `lock (_scanLock)` → `ScanAllTools()`
- `Services/ToolCatalog.cs:455` `ScanAllTools()` → `Parallel.For(categories)` → 每分类 `GetTools(category)`
- `Services/ToolCatalog.cs:256` 每个工具目录都走 `CreateToolItemWithVariants(...)`
- `Services/ToolCatalog.cs:594-597`：**只要 `FindJsonMetadataByDir(toolDir)?.Directory is null` 就遍历该分类下所有同级目录**

而 `Metadata/tools.json` 的条目结构是 `{ match, builtin, categories }` —— **142 条全部没有 `directory` 字段**，`JsonToolMetadata.Directory` 恒为 `null`（见 `Services/ToolMetadataService.cs:320-337` 的匹配逻辑）。所以这段 O(N²) 同级目录枚举**对每一个工具条目都会执行，永远不会短路**。

扫描中途每个条目还要付：

- `Services/ToolMetadataService.cs:69` `FileVersionInfo.GetVersionInfo(toolPath)`（打开 PE 资源，冷盘/杀软下最贵）
- `Services/ToolCatalog.cs:694`（及 `:1241` 内置条目）`FavoritesService.IsFavorite(path)`
- `Services/FavoritesService.cs:17` `IsFavorite` → `:13` `GetFavorites()` → `Shared/FavoriteFile.cs:10` `AtomicFile.Lock(path)` → `Shared/AtomicFile.cs:9-20`：**每次调用新建一个命名 `Mutex` + `WaitOne(10s)` + 读整个文件 + `ReadLocked` 里把 JSON 解析两遍**（`Validate` 里一次、返回后再一次）
- `Parallel.For` 各分类并发扫描时，所有线程都撞同一把收藏文件互斥体 → 并行被串成「锁车队」，实际退化为串行

### UI 线程入口（确认）

| 调用点 | 场景 | 备注 |
| --- | --- | --- |
| `Pages/HomePage.xaml.cs:192` | 全局搜索命中后跳转首页并高亮该工具 | `OnNavigatedTo` 里同步 `GetAllToolsCached()` 做路径匹配 |
| `Pages/BuiltinToolsPage.xaml.cs:26`（`LoadTools()`，`:213-221`） | 打开「内置工具」页 | **Page 构造函数**里为 45 个内置工具各调一次 `GetBuiltinFavoriteKey` |
| `Services/ToolCatalog.cs:1202-1215` | 上面那 45 次调用的落点 | 每次 `GetAllToolsCached()` + 线性查找 |
| `MainWindow.xaml.cs:1297` | 搜索结果里直接点「启动」 | |
| `Pages/PcTutorialPage.cs:1312`、`:1342` | 教程里「按名称/按 exe 启动工具」 | |

正确的对照：`Pages/DashboardPage.xaml.cs:155/187/224`、`Pages/FavoritesPage.xaml.cs:247`、`HomePage.LoadToolsAsync` 都用 `Task.Run` / `GetAllToolsAsync`——说明「不能同步扫盘」这件事团队是知道的，只是上面 5 个入口漏掉了。

### 触发条件（为什么不是每次）

`GetAllToolsCached()` 命中内存缓存时是微秒级。以下动作会 `ToolCatalog.OnToolsChanged()` 清空缓存（`Services/ToolCatalog.cs:534-538`）：

- 工具安装/下载/更新/删除完成
- 拖拽排序、`SaveToolOrder`
- 语言切换后的导航重建
- 启动 90 s 后的「空分类清理」瘦身到 `PruneCategoryIfEmpty` 后 `RefreshToolCategories()`
- `run-msix.ps1` 首次铺 `Tools`

清空之后，**第一个碰到上面 5 个入口的用户操作**就把全量扫描搬到 UI 线程——这正是「装完一个工具后点搜索结果，整个窗口卡死十秒」的完整成因链。

### 修复建议（按性价比排序）

1. **把 5 个入口改成异步**：`HomePage.OnNavigatedTo` 的 highlight 解析不要扫目录，先按字符串路径匹配（`_highlightToolPath` 本身就是绝对路径，直接与 `_tools` 里已有项比对即可）；其余入口改 `await ToolCatalog.GetAllToolsAsync()`，页面先渲染、后补高亮。
2. **`BuiltinToolsPage.LoadTools()` 改 `async`**，先 `await` 拿到列表再算收藏键；顺手把 45 次 `GetBuiltinFavoriteKey` 换成一次「一次遍历建字典」。
3. **消掉 O(N²)**：在 `GetTools` 里按分类做一次 `Directory.GetDirectories(categoryRoot)`，建「去架构后缀 → 目录」索引，传给 `CreateToolItemWithVariants`（当前每项都自己枚举一遍）。
4. **收藏读取上内存缓存**：`FavoritesService` 加 `_snapshot` + 文件 `LastWriteTimeUtc`/长度校验；扫描期用批量接口 `GetFavoritesSnapshot()` 一次读、一次解析，`IsFavorite` 只查内存；同时把 `FavoriteFile.ReadLocked` 的双重 `Parse` 去掉。
5. 可选的第二层保险：给 `GetAllToolsCached()` 加断言式防守——`Debug.WriteLine` + `LogService.Warn` 在检测到「UI 线程且耗时 > 200 ms」时打日志（见第七节埋点建议），避免以后又漏。

---

## 四、P0-2：AI 助手流式输出逐帧重建整棵视觉树

### 证据

- `Pages/AiAssistantPage.Run.cs:68`
  `var signature = state.Key + CanvasSignature(canvases) + string.Join("\u001f", rows.Select(r => r.Id + r.Text + r.Operation));`
  → **每一次刷新都把所有消息的完整正文拼成一个大字符串**再比较。长会话（几十条 × 每条几 KB）就是每次几百 KB 的字符串分配。
- `Pages/AiAssistantPage.Run.cs:106`
  `else if (node.LastText != row.Text && node.Markdown is { } body) { body.Markdown = row.Text; ... }`
- `Controls/MarkdownView.cs:15-46`：setter 直接
  `_body.Children.Clear();` → `MarkdownBlocks.Parse(全文)` → 每个 block **新建** `RichTextBlock` + `MarkdownTextService.RenderToRichTextBlock`（逐行正则 + 行内正则 + 逐段新建 `Paragraph`/`Run`）
- `Transcript` 是 `StackPanel`（无虚拟化）；每次正文变长都改变该气泡高度 → 整条时间线重新测量
- 流式回调 `Run.cs:315-323 Update(...)` → `RenderTimeline()` → 上面整条链路；`Progress()`（`:327`）也会调 `RefreshHistoryView()`
- 末尾还有 `JumpToLatest()`（`:110`）每次都滚动对齐

### 机制

设回答长 n 字符、流式增量 m 次，则每轮成本 ≈ O(n) 解析 + O(blocks) 视觉树重建 + 全时间线重新测量，累计 **O(n·m)**（二次方）。一个 6 KB、分 60 次到达的回答 ≈ 360 KB 的解析量 + 数千次元素创建；若期间还有工具调用进度、操作卡更新，UI 队列被持续占满，用户输入/滚动就排队——表现是「AI 回答时整个软件发木」。

### 修复建议

1. **节流重绘**：对同一 message 的文本更新做 60–100 ms 合并（只保留最后一次文本），或按「新增字数 ≥ 24 / 距上次 ≥ 80 ms」触发。
2. **增量快路径**：`MarkdownView` 增加 `UpdateText(string)` —— 若新文本是旧文本的前缀（流式常态），只重绘最后一个 block；否则回退全量。
3. **签名改为逐行比较**：为每个 row 记 `(Id, Text.Length, Text.GetHashCode())` 之类轻量指纹，不要拼全量正文。
4. 中期：`Transcript` 换 `ItemsRepeater`/虚拟化容器；`JumpToLatest` 只在用户原本就在底部时执行（当前已有 `follow` 判断，可保留但避免每 tick 调用）。

---

## 五、P0-3：常驻 250 ms 的 UI 线程轮询

### 证据

- `Services/AiAgent/ComputerControlService.cs:19` `Timer.Interval = 250 ms`
- `:43-47 Attach(Window)`：`Timer.Tick += Tick; Timer.Start();` —— **启动即开，之后没有任何「无会话时停机」的逻辑**（只有 `:368` 退出时 `Timer.Stop()`）
- `:89-121 Tick`：`ProcessCaptureSignals()`（`Directory.EnumerateFiles(Root, "capture-*.json")`）+ `Directory.EnumerateFiles(Root, "lease-*.json")` + 每个 lease 文件 `new FileInfo().Length` / `File.ReadAllText` / `JsonSerializer.Deserialize` / `File.Exists` / `Process.GetProcessById`
- `:140/146` 有 lease 时还会启动 16 ms 的 `EdgeTimer`（60 fps 的 UI 线程动画）

### 机制与影响

- 空闲时：每秒 4 次 UI 线程目录枚举（2 次 × 2）。本身不致命，但它**永不停止**，在杀软实时扫描、OneDrive/坚果云同步、机械盘上会被放大成每次几毫秒到几十毫秒的 UI 停顿——表现为「一直有点卡、滚动不跟手」。
- 有 AI 电脑控制会话时：每 tick 读 N 个 lease 文件 + 打开进程句柄，再叠加 16 ms `EdgeTimer`（60 fps 重绘边缘流光、进度卡片命中测试）——这是「AI 在操控电脑时整机变卡」的直接原因。

### 修复建议

1. 空闲停机：`Tick` 里 `Leases.Count == 0 && _banner is null && _progressPopup is null && _edgeLine is null` 时 `Timer.Stop()`，改由 `BeginSession`/`UpdateProgress` 重新 `Start()`；或降频到 2000 ms 作为「保活检查」。
2. 文件轮询移出 UI 线程：`Task.Run` 做枚举 + 解析，只把「结果差异」`TryEnqueue` 回 UI；用 `FileSystemWatcher` 替代轮询更彻底（仓库已有同构写法：`SingleInstanceService`、`PhoneLinkActivation`）。
3. `EdgeTimer` 只在确有可见 overlay 时运行，且改为 `CompositionTarget.Rendering` + 逐帧 delta，避免 16 ms 与渲染帧错位空转。

---

## 六、P1 / P2 明细

### P1-1 设置页单页 1173 个 XAML 元素

`Pages/SettingsPage.xaml`：135,034 字符 / 1,297 行 / **1,173 个元素节点**（92 Border、27 ComboBox、30 Button、12 ToggleSwitch、5 Expander）。WinUI 不做 XAML 延迟实例化，`Frame.Navigate` 时整棵树解析 + 测量 + 布局，全部在 UI 线程。`NavigationCacheMode="Enabled"`（该文件 Page 头部）意味着只在首次进入付费，但也意味着这 1173 个元素的常驻内存。

叠加 `OnNavigatedTo`（`:236-259`）里的同步动作：`InitHardwareStyle()`、`RefreshLogDirCard()`、`InitAiBrowserSettings()`。

建议：把「关于/赞助/危险操作/插件与账户」等长尾区块改为**代码按需构建**（或用 `Expander.Expanding` 首次展开时再挂内容），首屏只保留「常规/主题/工具」；`RefreshLogDirCard` 之类的文件系统探测挪到 `Task.Run`。

### P1-2 硬件信息页每次进入重建 WebView2

`Pages/HardwarePage.xaml.cs:106-146`：`Loaded`/`OnNavigatedTo` 起场景，`OnNavigatedFrom`/`Unloaded` → `SuspendHardwareScene()`/`ReleaseHardwareScene()`；`Services/HardwareSceneRuntime.cs:37-39` 每次 `WebView2EnvironmentService.GetAsync()` + `EnsureCoreWebView2Async`。页面未设 `NavigationCacheMode`，所以每次进出都要付 WebView2 环境 + `msedgewebview2.exe` 启动（0.2–1 s 量级）。

建议：`HardwarePage` 设 `NavigationCacheMode="Enabled"`（或把 `HardwareSceneRuntime` 提到窗口级单例复用），离开时只 `Suspend`（`Visibility=Collapsed` / 停止发帧），不 `Release`。

### P1-3 每个画布消息一个 WebView2

`Controls/CanvasBubble.cs:24` `private readonly WebView2 _view = new() {...}`、`:360 EnsureCoreWebView2Async(...)`。一个会话里有多张画布 = 多个 WebView2 + 多个 `msedgewebview2.exe`（每个 100 MB 级内存 + 独立渲染线程）。教学模式下多轮迭代会持续累积。

建议：画布气泡离开视口时挂起（`Suspend`/`Visibility` + 停止导航），超过 N 张时释放非可见实例；或改单 WebView2「池」复用。

### P2-1 无超时的 STA `Join()`（潜在整窗死等）

`Services/WindowsSearchIndexService.cs:190-209` `RunOnSta`：后台线程分支 `staThread.Join()`（**无超时**）。COM `WScript.Shell` 若被安全软件/异常注册阻断，UI 线程（「发送到桌面」按钮走的就是这条）会无限等待 → 永久「未响应」。建议 `Join(TimeSpan.FromSeconds(2))`，超时抛可读错误并降级提示。

### P2-2 `reg.exe` 双管道顺序 `ReadToEnd`（经典死锁形态）

`Services/RogueCleaner/ContextMenuAdvancedModules.cs:687`：
`output = process.StandardOutput.ReadToEnd(); error = process.StandardError.ReadToEnd(); process.WaitForExit();`
若子进程在写 stderr 时把 4 KB 管道缓冲写满，而父进程正阻塞在 stdout 读上，双方互等 → **永久挂起**。概率低（reg.exe 正常输出很少），但一旦命中就是「只能杀进程」。建议改为 `ReadToEndAsync` 两路并行 await + `WaitForExitAsync` 加超时。

### P2-3 其余「UI 线程直接做 IO/子进程」的点（可接受但值得标注）

- `Services/DiskHealthService.cs:520` `RunDefragSync` 里 `process.WaitForExit()` 无超时——目前唯一调用方 `OptimizeAsync`（`:469`）包在 `Task.Run` 里，安全；但注释写着「快速操作」，TRIM 在脏盘上可以是分钟级，建议加 60 s 上限。
- `Pages/BatteryAnalyzerPage.cs:731-733`、`Pages/WindowsFeaturePage.xaml.cs:124`、`Pages/QuickDeviceCheckPage.xaml.cs:80-81`：都是 `await Task.WhenAll(...)` 之后的 `.Result`，Task 已完成，**无害**（已确认，不要当 bug 改）。

---

## 七、建议补的观测埋点（为下一次反馈留证据）

目前全仓没有 UI 卡顿量化数据，`StartupTrace` 已无调用点（`startup.log` 实际不会被写）。建议最小落地：

1. **UI 停滞看门狗**（`Services/Diagnostics/UiStallWatchdog`，约 60 行）
   - 在 `MainWindow` 构造末尾启动一个 100 ms 的 `DispatcherQueueTimer`；
   - 每次 tick 计算距上次 tick 的实测间隔，`> 250 ms` 就 `LogService.Warn(LogService.App, $"[UIStall] {gap}ms 触发点={_lastStage}")`；
   - 配合在各页面 `OnNavigatedTo`/关键同步调用前后手动打点（`_lastStage = "HomePage.highlight"`），能直接把「哪一段把 UI 按住」定位到函数级。
2. **慢调用计时**：对 `ToolCatalog.GetAllToolsCached`、`FavoritesService.GetFavorites`、`Frame.Navigate` 包一层 `Stopwatch`，超过 50 ms 记 `LogService.Warn`（含 `Environment.StackTrace` 前 3 帧）。
3. **用户反馈模板**：请用户回传 `<DataDir>/Logs/app.log` 与 `startup.log`；日志目录在设置页可见（`LogService` 统一写入、类别一文件一文件）。

这三个埋点都不改变现有行为，能直接把「卡顿」变成「可定位的时间戳 + 调用栈」。

---

## 八、确认「已经做对」的地方（改代码时别误伤）

这些地方看起来可疑，但实测/设计是对的，**不要为了「优化」而改回去**：

| 位置 | 为什么是对的 |
| --- | --- |
| `Services/ToolIconService.cs:168-231` | 缓存命中检查整体挪进 `Task.Run`（注释记录了历史上 142 个 exe ≈1200 次 UI 线程 syscall 的教训），发布回 UI 用 `DispatcherQueuePriority.Low`，且用「顺序生产者」避免为每个离屏工具建 Task |
| `App.xaml.cs:975-1041` | 图标缓存清理(45s)/硬件 WMI 预热(20s)/日志清理(60s)/空分类清理(90s) 全部 `StartupMaintenance` 延迟到后台，不抢首帧 |
| `Pages/FavoritesPage.xaml.cs:247`、`DashboardPage` | 目录枚举包在 `Task.Run` / `GetAllToolsAsync` |
| `Pages/TrafficMonitorPage.xaml.cs:249-288` | 行更新是 diff（`_rowCache` + `Insert/Move/Remove`），不是整表重建 |
| `Controls/StressTestControl.xaml.cs:565-579` | 监控 tick 有 `_monitorInFlight` 防重入，且 `Read()` 在 `Task.Run` 里（注释明确写了 async void 叠加把 UI 压死的历史） |
| `Pages/QuickDeviceCheckPage.xaml.cs:542-549`、`Controls/SpaceTreemap.cs:86-91` | `CompositionTarget.Rendering` 都按「动画开始订阅 / 结束退订」成对管理 |
| `Services/ToolCatalog.cs:432-451` | single-flight 合并并发扫描，避免冷启动 3–4 路重复全量扫 |
| `Pages/GameTunnel/*`、`App.TryRelaunchElevated` | EasyTier CLI / UAC 载体都包在 `Task.Run` 里，没有 UI 线程同步等待 |

---

## 九、建议的修复顺序（若下一步要动手）

1. **P0-1 的 5 个 UI 线程入口**（改动小、收益最大，直接消掉「未响应」类反馈）：`HomePage:192`、`BuiltinToolsPage` 构造、`MainWindow:1297`、`PcTutorialPage` 两处。
2. **`FavoritesService` 内存快照 + 去掉双重解析**（不改任何 UI，纯服务层，直接砍掉扫描里每项 ≈1 ms 的固定开销）。
3. **`CreateToolItemWithVariants` 的 O(N²) 同级枚举**（分类级一次枚举 + 索引）。
4. **AI 页重绘节流 + `MarkdownView` 前缀增量**（体验最明显的一块）。
5. **`ComputerControlService.Timer` 空闲停机 + 轮询挪后台**。
6. P1/P2 视反馈再排。

每一步都可以用现有测试兜底：`ToolCatalogTests`、`DesktopShortcutTests`、`AiCanvasTests`、`AgentClient` 相关用例；扫描改动建议补一个「count 一次 `Directory.GetDirectories`」的行为测试。

---

## 十、为什么「每次都要再扫描一遍」

一句话：**缓存只活在内存里，而且失效是全局一刀切；清干净之后，下一次任何一次访问都会把整棵树从头走一遍。** 代码里所有「按需重扫」的注释，实际效果都是「全量重扫」。

### 10.1 结构上的三个前提

1. **没有磁盘快照**。`Services/ToolCatalog.cs:502-509` 注释明写：「无磁盘缓存层：tools.json 是唯一元数据事实来源」。所以**每次进程启动必然冷扫一次**，上一次进程的扫描结果一点都没留下。
2. **失效粒度是「全部」**。`Services/ToolCatalog.cs:534-538`：

   ```csharp
   public static void OnToolsChanged()
   {
       lock (_cacheLock) { _toolsCache.Clear(); _cachedAllTools = null; _cachedAllToolsRoot = null; }
       Interlocked.Increment(ref _cacheVersion);
   }
   ```

   没有 `InvalidateCategory(category)` 这类接口。改一个工具的排序、勾掉一个工具的显示、安装一个工具，都会把**所有分类**的缓存一起作废。
3. **「清空」和「重扫」被拆成两件事**，于是清空之后谁第一个访问就由谁来付全量扫描的账——而第一个访问者经常是 UI 线程（见第三节的 5 个入口）。

### 10.2 会触发「下次全量重扫」的地方（12 处）

| 触发动作 | 入口 | 后果 |
| --- | --- | --- |
| 拖拽排序落一次 | `Pages/HomePage.xaml.cs:531`（`SaveToolOrder` 之后） | 全部分类缓存作废；下次进首页全量扫 |
| 自定义工具页勾选「显示/隐藏」 | `Services/ToolVisibilityService.cs:50` | 同上，一次勾选一次全量 |
| 工具安装 / 更新完成 | `Models/DownloadQueueModels.cs:955`（`RefreshToolsRoot`）+ `:960`（`RefreshToolCategories`） | **同一件事清两次**，并各触发一轮刷新 |
| 社区工具安装完成 | `Services/CommunityToolInstallService.cs:182-184` | 同上 |
| 把工具拖进 Tools 目录 | `Pages/CustomToolDropHandler.cs:150` 与 `:165` | 一次拖入连清两次 |
| 插件加载/卸载 | `Services/Plugins/PluginManager.cs:43`、`:350` | 每次插件变更全量 |
| 工具包导入 | `Services/CustomToolPackageService.cs:95`、`:190` | 每个包全量 |
| 工具修复 | `Services/ToolRecoveryService.cs:49` | 全量 |
| 配置导入/重置 | `Services/ConfigManager.cs:504` | 全量 |
| 工具更新检查后落盘 | `Services/ToolUpdateService.cs:180` | 全量 |
| 语言切换 | `MainWindow.xaml.cs:218-220` | `ToolMetadataService.InvalidateCache()` + 全量 |
| **启动后约 90 秒的空分类清理** | `App.xaml.cs:1017`（先全量扫一遍找空分类）→ `:1033`（删空目录）→ `:1039` `InvalidateTagsCache()` → `:1041` `RefreshToolCategories()` | **每次启动都会在 90 秒时把刚扫完的缓存作废再扫一遍**（只要存在空分类被删掉） |

另外 `Pages/HomePage.xaml.cs:75-82` 的 `ToolsChanged` 事件处理器收到通知后还会**再** `InvalidateTagsCache()` + `LoadToolsAsync()`——也就是说 `RefreshToolsRoot()`（`ToolCatalog.cs:546-552`，内部已经调过 `InvalidateTagsCache()` 并抛事件）会额外引发第三轮扫描。

把这张表合起来看，用户体感就是：**装一个工具、拖一次顺序、勾一次显示，软件都要重头扫一遍全库**。

### 10.3 为什么「重扫一遍」不是毫秒级而是秒级

同样是第二节那三个放大器，每次重扫都要全付一遍：

- 每项一次 `FileVersionInfo.GetVersionInfo`（实测 0.56 ms × 137 ≈ 77 ms）
- 每项一次 `FavoritesService.IsFavorite` → 命名 Mutex + `WaitOne` + 读整个文件 + 双重 `Parse`（≈1.1 ms × 137 ≈ 150 ms，且并行时抢同一把锁）
- 每项一次 O(N²) 同级目录枚举（`ToolCatalog.cs:594-597`，`tools.json` 无 `directory` 字段 → 永不短路）≈2105 次 × 1.2 ms ≈ **2.5 s 串行**
- 每个目录一次递归 `EnumerateLaunchables`（`ToolCatalog.cs:245`）

合起来单线程约 0.5 s，8 核 `Parallel` 摊到 **≈110–220 ms**（实测，见第十一节）；机械盘/杀软实时扫描下放大到 0.3–4 s。这就是「同一个骨架的两次扫描，平时能忍、撞在 UI 线程上就成了未响应」的原因。

### 10.4 正确做法（三件事，按性价比）

1. **失效改成分类级**：新增 `InvalidateCategory(category)`，只 `_toolsCache.Remove(category)` 并作废 `_cachedAllTools`（all-tools 一览可以重建，但至少别把每个分类都丢掉）。排序 / 可见性 / 单工具安装都只需要失效它所在的那一个分类。
2. **事件合并去重**：`OnToolsChanged()` 加 150–300 ms 去抖，把「安装完成 → RefreshToolsRoot → HomePage 事件 → RefreshToolCategories」这一串合并成一次失效 + 一次刷新；同时让 `RefreshToolsRoot()` 不再重复调用 `InvalidateTagsCache()`。
3. **磁盘快照（根治「每次启动都要扫」）**：扫描结果按 `(ToolsRoot, tools.json mtime+size, 顶层分类目录 mtime)` 写一份紧凑快照（只存 `ToolItem` 的必要字段），启动时命中就直接用、后台再校验；或改用 `FileSystemWatcher` 维护增量。仓库里已有同构写法可抄：`SingleInstanceService`、`PhoneLinkActivation` 的 watcher + 去抖。

做完 1+2 后，排序/可见性这类操作从「全量重扫」变成「一个分类的几十毫秒」；做完 3 之后，冷启动也不再需要全树扫。

---

## 十一、已实施：扫描侧优化与实测对比（2026-11-14）

### 11.1 改了什么

| 文件 | 改动 | 目的 |
| --- | --- | --- |
| `TubaWinUi3.WinUI3/Services/ToolCatalog.cs` | `MergeArchDirectories` → `MergeArchGroups(record ArchGroup(Primary, Siblings))`：合并架构目录时**保留**被合并掉的成员目录，随分组一起传出 | 不再「先丢弃、再逐项重扫分类目录把它找回来」 |
| 同上 | `GetTools` 把 `group.Siblings` 传给 `CreateToolItemWithVariants`；**删除** `594-622` 的整段 `Directory.GetDirectories` 循环 | 2105 次分类目录枚举 → **1 次**（分组时那次本来就不枚举磁盘） |
| 同上 | 新增兄弟变体去重（按路径跳过已有 `alternates`） | 避免同名变体重复入列 |
| `TubaWinUi3.WinUI3/Services/FavoritesService.cs` | 收藏读取加内存快照，按 `(路径, LastWriteTimeUtc, Length)` 校验；`IsFavorite` 只查 `HashSet`；写入成功后 `InvalidateCache()` | 扫描期 137 次「开命名互斥体 + 读文件 + 解析两遍」→ 1 次读 + 每次一次 stat |
| `Shared/FavoriteFile.cs` | `ReadLocked` 只解析一次（校验时把结果留下，不再二次 `Parse`） | 去掉双重 JSON 解析 |

### 11.2 实测（真实 `Tools/` 目录：9 分类 / 82 目录 / 137 个启动项 / 77 个条目）

同一测试进程内连续 5 轮，每轮前清空缓存：

| 版本 | 5 轮耗时（ms） | 首轮冷启动 |
| --- | --- | --- |
| 改动前 | 158.0 / 186.6 / 190.9 / 217.1 / —（4 轮有效） | 598 |
| 改动后 | 109.8 / 118.9 / 126.2 / 142.7 / 150.5 | 309 |

去掉的枚举量：**2105 次 → 0 次**；C# 层校准 2105 次 `Directory.GetDirectories` = 222–304 ms 单线程（0.105–0.145 ms/次）。

### 11.3 口径更正（重要）

第二节原先写的「2105 次 × 1.2 ms ≈ 2.5 s」是**错的**：1.2 ms 里绝大部分是 PowerShell 每次调用的解释开销。C# 实测 0.105–0.145 ms/次，2105 次 = **222–304 ms 单线程**，`Parallel` 摊薄后 **≈30–50 ms**。

因此：
- 一次全量冷扫描的正确量级是 **110–220 ms**（本机 SSD、缓存热），不是 2.9 s；
- 用户机器上的 0.3–4 s 来自机械盘/网络盘/杀软逐文件扫描与更大的工具库，不能按本机数字线性外推。

### 11.4 剩下的 110 ms 花在哪（同一进程实测，77 条目串行）

| 环节 | 耗时 |
| --- | --- |
| `ToolMetadataService.GetMetadata`（含 `FileVersionInfo` + readme） | 61.0 ms |
| `FindJsonMetadata`（每次线性扫 142 条 + `GetFullPath`） | 40.8 ms |
| `FileVersionInfo`（单独计） | 18.5 ms |
| 82 个目录递归枚举 | 12.2 ms |
| `FavoritesService.IsFavorite`（优化后） | **3.2 ms** |

结论：扫描侧下一步该动的是**元数据匹配**（`CurrentCultureIgnoreCase` 子串匹配 + 每次都从头线性扫），其收益远大于继续抠目录 IO。可做的两件事：① 匹配改 `OrdinalIgnoreCase` 并建一次「关键词 → 条目」索引；② `FindJsonMetadata(path)` / `FindJsonMetadataByDir(dir)` 按路径缓存，随 `InvalidateCache()` 一起失效。

### 11.5 验证

- `dotnet build TubaWinUi3.WinUI3/TubaWinUi3.csproj`：**0 错误**（54 个警告均为既有）。
- `dotnet test`（`ToolCatalogTests` / `FavoritesServiceTests` / `AuditFixTests` / `ToolStartupScanTests` / `UserToolUpdateTests`）：**102 项全过，0 失败**。
- 行为一致性：改动前后真实目录扫描输出均为 **77 个条目 / 12 个带架构变体**（`memtest` → `memtest64` 的 x64 变体仍正确挂上）。

---

## 十二、第二轮：元数据匹配索引化 + UI 线程入口异步化（2026-11-14）

### 12.1 元数据匹配（`ToolMetadataService.cs`）

**问题**：`FindJsonMetadata` / `FindJsonMetadataByDir` 每次调用都遍历全部 142 条，并对每条带 `directory` 的条目调用 `UserToolLibrary.IsWithin`——它内部做两次 `Path.GetFullPath`。一次扫描 77 个条目 × 142 条 × 2 次路径规范化，这就是 61 ms + 41 ms 的来源。

**改法**：

1. `BuildIndex`：`tools.json` 加载时一次建索引——match 表按 match 长度降序（等价原 `OrderByDescending`，同长度保持原顺序）、用户库注册目录预先解析成绝对路径前缀表与精确目录字典。
2. 匹配比较从 `CurrentCultureIgnoreCase` 换成 `OrdinalIgnoreCase`（文件名/路径场景），查询串与 match 预先小写化，避免每次调用反复分配。
3. `FindJsonMetadata` / `FindJsonMetadataByDir` 结果按路径缓存；`InvalidateCache()` 与 metadata 列表实例变化都会整体失效。

> **测试暴露的一个真问题**：`Copy_DuplicateDeclarations_OnlyOneCardPerLaunchPath` 失败——`SetMetadataRootForTests` 只把 `_metadata` 置空，索引与查找缓存仍指向上一份 `tools.json`。改成以 `_metadata` 列表实例做身份校验（`_indexSource` / `_cacheMetadata`）后修复。这条不是为测试妥协：语言切换、用户库换目录同样会换实例，生产线也要靠它。

### 12.2 实测（同一进程 5 轮，真实目录 77 条目）

| 环节 | 第一轮后 | 第二轮后 |
| --- | --- | --- |
| `GetMetadata` × 77 | 61.0 ms | **16.2 ms** |
| `FindJsonMetadata` × 77 | 40.8 ms | **0.1 ms**（全部缓存命中） |
| `FindJsonMetadataByDir` × 77 | 未单独测 | 0.6 ms |
| `FavoritesService.IsFavorite` × 77 | 3.2 ms | 1.2 ms |
| 整扫描（warm，5 轮） | 110–150 ms | **15.6 / 17.7 / 19.8 / 20.2 ms** |
| 整扫描（首轮） | 309 ms | 187 ms |
| 对照最初版本（158–217 / 冷 598） | — | **快约 10 倍 / 冷启动 3.2 倍** |

### 12.3 UI 线程入口（原先 5 处同步扫描）

| 位置 | 原来 | 现在 |
| --- | --- | --- |
| `Pages/HomePage.xaml.cs` `OnNavigatedTo`（`:192`） | UI 线程直接 `GetAllToolsCached()` 找高亮工具 | `async void OnNavigatedTo` + `await ToolCatalog.GetAllToolsAsync()` |
| `Pages/BuiltinToolsPage.xaml.cs:26` 构造函数 | 构造期 `LoadTools()` → 45 次 `GetBuiltinFavoriteKey` 触发首扫 | 构造函数不再扫描；`OnNavigatedTo` 先 `await GetAllToolsAsync()` 预热，再 `RebuildPivot()` |
| `MainWindow.xaml.cs:1293` `LaunchSearchTool` | 同步扫描后启动 | `async void` + `await` |
| `Pages/PcTutorialPage.cs:1310` `LaunchToolByName` | 同步扫描 | `async Task` + `await` |
| `Pages/PcTutorialPage.cs:1331` `LaunchToolByExeName` | 同步扫描 | `async Task` + `await`；`OnActionLinkClick` 改 `async void` 并在 `try/catch` 内 await |

**仍然留在 UI 线程的调用（如实列出）**：`UnifiedSearchService.DeduplicateResults/FindToolByPath`、`ToolIconService:304`、`WinPerformanceService:114` 仍同步调 `GetAllToolsCached()`。它们共享同一份内存缓存，热态只是一次列表读取；冷态从这些入口首次进入仍会等一次扫描（本机约 0.19 s）。若要求「任何入口都不在 UI 线程首扫」，这几处也应改成 `await GetAllToolsAsync()`。

### 12.4 验证

- `dotnet build TubaWinUi3.WinUI3/TubaWinUi3.csproj`：**0 错误**（269 个警告，均为既有）。
- **全量测试 2981 项：2977 通过 / 4 跳过 / 0 失败**（含 `ToolCatalogTests`、`ToolCategoryCopyTests`、`ToolStartupScanTests` 等 102 项扫描相关）。
- 行为一致性：真实目录仍为 **77 个条目**，嵌套架构变体用例 `CategoryScan_KeepsNestedArchVariants_AndRefreshesAfterUpdate` 通过。

### 12.5 还能再快吗

剩余 15–20 ms 里主要是 `FileVersionInfo`（77 个文件约 15 ms）与 82 个目录递归枚举（12.2 ms，与前者部分重叠并行）。这两项已接近磁盘元数据读取的下限；真要再降只能做**磁盘快照**（启动直接读一份紧凑缓存），属于第十节 10.4 第 3 条，代价是新增一套失效逻辑。


