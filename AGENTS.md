# TubaWinUi3 — Agent Notes

## What this is

A WinUI 3 (Windows App SDK / .NET 10) Chinese-language PC hardware toolbox ("图吧工具箱"). Catalogs and launches third-party diagnostic executables from a local `Tools/` folder, shows WMI/LibreHardwareMonitor hardware info, ships ~30 built-in utility tools, and does real-time hardware monitoring with an FPS overlay. UI strings are hardcoded Chinese — there is no resource/localization system.

## Solution layout (4 projects in `TubaWinUi3.sln`)

- `TubaWinUi3.WinUI3/` — **the app**. The only project you normally build/run; `dotnet` commands target `TubaWinUi3.WinUI3/TubaWinUi3.csproj`.
- `TubaWinUI3.BackEnd/` — a small **NativeAOT helper process** (`TubaWinUI3.BackEnd.exe`) used by the 主动拦截 (active intercept) feature; managed by `ActiveInterceptService`. The main csproj's `CopyBackendForBuild` target copies its framework-dependent output after every `dotnet build`, and `PublishBackend` builds/publishes the AOT version during `dotnet publish`.
- `TubaWinUi3.Compatible/` — a separate **.NET Framework 4.8 WinForms** edition (`图吧工具箱Winui3兼容版.exe`, ReaLTaiizor Crown theme + Costura.Fody single-file). NOT WinUI 3 and NOT .NET 10 — different toolchain, different conventions. Built by CI and bundled into portable zips. Do not mix its patterns into the main app. Supports tools.json cross-category (`category` + `categories`) copies and splits each arch variant (x64/ARM64/x86) into its own tool card; `builtin` placement entries are skipped (WinForms can't run WinUI built-ins).
- `TubaWinUi3.Tests/` — **xUnit** tests (xUnit 2.9 + coverlet), referencing the main project via `InternalsVisibleTo`.

## Build, run, test

```bash
dotnet build                                                      # Debug; RuntimeIdentifier auto-detects current arch
dotnet run                                                        # Unpackaged profile (only profile in launchSettings.json)
dotnet test                                                       # all tests
dotnet test --filter "FullyQualifiedName~ToolCatalogTests"        # one class / one test
.\run-msix.ps1                                                    # PACKAGED (MSIX) run: register build output as dev
                                                                  #   package 'tubawinui3.dev' (Developer Mode, no signing)
                                                                  #   → seed LocalState Tools → launch. CLI 版 F5。
.\run-msix.ps1 -NoBuild -NoLaunch                                 # 只重注册不启动（CI/无头）
```

- Platforms x86 / x64 / ARM64; `RuntimeIdentifier` defaults to the current process architecture.
- `WindowsPackageType=None` + `EnableMsixTooling=false` → runs unpackaged; no MSIX registration for dev.
- **MSIX 自测**：`run-msix.ps1` 把现有构建输出（`bin/<Config>/<tfm>/win-<arch>`，含 exe/pri/Tools/Metadata/Assets）直接用开发者模式注册为 dev 包（identity `tubawinui3.dev`，与 Store 的 `DA3D64F4.winui3` 互不干扰），再从 `shell:AppsFolder` 激活启动 → 真实包身份。LocalFolder 数据在 `%LOCALAPPDATA%\Packages\tubawinui3.dev_<hash>\LocalState\TubaWinUi3\`。注意：注册的是可写 bin 目录，Store 那种「安装目录 ACL 只读」不模拟；要上架级验证走 `build-msix-store.ps1`。
- **Requires admin**: `App.OnLaunched` auto-elevates via the `runas` verb and `Exit()`s if not admin (unpackaged mode only). Headless command-line modes skip the window entirely: `EnergyStarStartupService.SilentArg` (EcoQoS throttling), `--copy-path`, `--toast`, `--show-active-intercept`.
- `AllowUnsafeBlocks=true` (P/Invoke structs in `HardwareInfoService`).
- Publish is self-contained; `PublishTrimmed=false`, `PublishReadyToRun=true` — trimming is never used.
- **`.pri` gotcha**: after `dotnet publish`, copy `TubaWinUi3.pri` from `bin/<arch>/Release/.../<rid>/` into the publish output (CI does this; the app misbehaves without it).

## Stray root files — do not edit

`MainWindow.xaml`, `MainWindow.xaml.cs`, and `Pages/SettingsPage.xaml(.cs)` exist at the **repo root** but are NOT compiled by the main project. The live source is under `TubaWinUi3.WinUI3/`. Always edit there.

## Architecture essentials

- `App.xaml.cs` → `MainWindow` (custom TitleBar + `NavigationView` + `Frame`); nav categories come from `ToolCatalog.GetCategories()` and `BuiltinToolRegistry.GetCategories()`.
- **All services are static classes with no DI**, called directly from pages. The single exception is `LiteMonitorService`, a singleton (`Instance`).
- `ToolCatalog` scans `Tools/` for `.exe .bat .cmd .lnk .msc .ps1 .vbs`, merges x64/x86/ARM64 variants, and resolves the `Tools/` root by walking up from `AppContext.BaseDirectory` (`FindToolsRoot()`). No disk cache layer: in-memory cache + single-flight + parallel scan only.
- `ToolMetadataService` merges `Metadata/tools.json` + `FileVersionInfo` + `readme.txt`. The `"match"` field is a **case-insensitive substring** against tool filenames/paths. tools.json is the single source of truth for: metadata, card order (`"order"`), cross-category copies (`"category"` = physical primary category + `"categories"` = extra categories), and builtin tool placements (`"builtin"` = BuiltinToolRegistry id; virtual dir key `ToolsRoot/分类/目录名` keeps favorites/order-save compatible with the removed link.json dirs). Copy dir resolution scores candidates: exact dir name > flexible match equality > relative-path substring.
- Sorting: tools.json `"order"` primary → `AppSettings ToolOrder_{category}` fallback for uncatalogued custom tools → name. Drag-reorder (HomePage pure-category view + CustomToolManagerWindow) double-writes via `ToolMetadataService.SaveToolOrder(dirs)`; note `order` is entry-global, so copies share one order across categories.
- `ToolItem.InitArchOptions()` auto-selects the best arch for the OS (ARM64 > x64 > x86 preference).
- Built-in tools: see `BuiltinToolRegistry.RegisterDefaults()` (~31 tools). `CommunityToolBuiltinTool` registers only when `!RuntimeHelper.IsMsixPackaged`.

### AI 助手（AiAgentPage）— FieldCure ChatPanel 架构
- 「AI 助手」内置工具 = `AiAgentPage`，消息区/输入区/工具确认全部由 **`FieldCure.AssistStudio.Controls.WinUI`** 的 `ChatPanel` 组件库接管（WebView2 渲染 Markdown/思考块/内联工具调用、`ToolApprovalPanel` 危险操作确认）。
- 适配层在 `Services/Ai/`：`TubaChatProvider`（`IAiProvider`，OpenAI 兼容端点 → `StreamEvent` 流，含 `reasoning_content` JsonPatch 回传）、`AgentToolAdapter`（`AgentToolRegistry` 28 个工具 → `IAssistTool`，`RequiresConfirmation` 按 `AgentToolContext.IsFullAccess` 动态计算）。
- `AgentSession`/`AgentRuntime` 仍是**独立的旧引擎**（AiQuickAskFlyout 及测试使用），页面不再直接依赖；技能触发经 `AgentToolContext.SkillTriggerActive` 静态桥接（web_search 拦截）。
- 历史/记忆/技能沿用 `AiAssistantService` 的 messages.json / display.json / skills.json / memory.md；skills 默认全开、按会话存档。
- **包缺陷**：FieldCure 0.21.0 的 `.pri` 声明了缺失的 `AssistStudio.Controls/icon.png`，csproj 中 `FixFieldCureMissingPriPayload` 目标在构建期补位（升级包版本时若仍报 MSB3030 需检查该目标）。
- **顶栏 bot 吉祥物（唯一实例）**：`Assets/BotAvatar/`（bloub 引擎，github.com/jeremy-prt/bloub，MIT；esbuild IIFE 打包 + `botavatar.html`），顶栏右侧操作组最前（模型选择器旁）**44×44 单实例**（`BotAvatar`），原生 XAML 区域 → 透明必然生效。
  - **只放顶栏的原因**：曾尝试聊天区大号（72×72）悬浮在输入框上方（`UpdateBotAnchor`/`FindInputAnchor` 动态锚定），但该区域叠在 ChatPanel 消息区 WebView2 之上，透明 WebView2 无法穿透另一 WebView2（平台硬限制）→ 背景灰块无解，用户要求透明 → 改为顶栏唯一实例。
  - 初始化：`InitBotAvatarAsync` → `InitBotWebViewAsync(BotAvatar)` 单实例；导航成功才置 `_botAvatarReady` + 启动视线轮询 + `SendColorsTo` 补发主题色；`Unload` 时 Navigate about:blank。
  - **顶栏操作动画**：AI 设置 → `swirl`（进入设置视图）、提供商/模型切换 → `notify`（蓝点）、技能菜单 → `notify`、完全访问开关 → `wide`/`exclaim`、新对话 → `play`、历史 → `comet`。
  - **15 态全接入**（`BotPost` postMessage 联动）：
  - `thinking` 发消息 → `orbit` 工具执行（HTML 桥 3s 循环重放，环 3.6s 会淡出）→ 定稿庆祝三选一（`_roundTokens` 分级：0→`wink` 眨眼 / >0→`notify` 蓝点 / >4000→`burst` 爆炸）
  - `alert` 请求失败（`TubaChatProvider.RequestFailed`）、`play` 新对话、`sleep` 5 分钟无活动、`wide`/`exclaim` 完全访问开关开/关、`swirl` 打开 AI 设置、`comet` 历史会话加载完成、`egg`/`hexagon` AI 写入记忆时随机变形
  - 空闲时视线跟随鼠标（ChatPanel 的 WebView2 吞 XAML 指针事件，用 `GetCursorPos` 66ms 轮询，仅 baseFace 态生效）；body 墨色取系统强调色（`BotSendColors`，导航成功后再补发一次）
- 初始化失败不静默：Debug.WriteLine 留日志 + 首次导航失败自动重试一次。相关静态事件：`TubaChatProvider.RequestFailed`、`AgentToolAdapter.ToolExecutionStarted/Finished`。
- **防死循环护栏（会话级）**：`Services/Ai/AgentToolLoopGuard`（静态、线程安全，新/旧引擎共用；新对话 `AiAgentPage.ResetToNewChat` 调 `Reset()` 清空）。
  - **重复调用拦截**：同会话「工具+参数」相同（`NormalizeArgs` 递归按键排序签名）第二次直接拦截不执行，空参数（查询类）豁免；`MaxToolResultChars=6000` 结果截断 + 空结果标记「未返回内容」。
  - **无进展检测**：连续 6 轮纯工具调用（无用户文本）→ 新引擎 `TubaChatProvider` 在首条 system 注入终止指令，旧引擎 `AgentRuntime` 直接终止循环。
  - **web_search 技能拦截计数**：技能触发（配电脑查价）拦截第二次起返回硬终止「已连续两次被拦截」。
  - **思维链护栏**：`TubaChatProvider.MaxThinkingChars=6000` 流式按轮限流 + `TruncateThinking` 截断（保留开头+标记，防切断 surrogate pair）；旧引擎 `AgentRuntime` 流式累积同样限流。
  - **历史预算压缩**：`HistoryBudgetChars=40000`，超限从最旧丢（保留首条 system——DeepSeek 网关拒绝多条 system）；新引擎 `TubaChatProvider.TrimHistory` 与旧引擎 `AgentRuntime.TrimHistory` 同语义。
  - **错误终态分类**：`FormatToolError` 参数类异常（Argument/Json/Format）→「可调整重试」，其余系统性失败 →「请勿重试」（`AgentToolAdapter` 与 `AgentErrorPolicy` 文案对齐）。
  - 兜底：两引擎均有轮次上限（`MaxToolCallRounds=30` / `AgentRuntime.DefaultMaxRounds=30`）。

### 格式转换（FormatConverterPage）— 多引擎批量转换架构
- 「格式转换」内置工具 = `FormatConverterPage`（替换了旧的 VideoProcessorPage）。UI 为「拖入 → 选目标格式 → 文件队列逐个处理」，队列项有 等待/转换中/完成/失败/跳过 状态，**单个文件失败不中断批次**。
- 纯逻辑层（可单测，`FormatConvertTests`）：`FormatConvertCatalog`（扩展名 → `SourceCategory`，类别 → 目标 `FormatOption`，`EngineFor`/`ShouldUseLibreOffice`）、`FormatConvertPlanner`（FFmpeg/ImageMagick 参数、输出路径消歧、ZIP 打包 0-9 级）。
- 路由层 `DocumentConvertService`：Word/Excel/PPT/PDF/Markdown/Text/HTML/JSON 的目标分发。四条管线：
  - **OfficeCLI 渲染引擎** `OfficeCliService`（docx/xlsx/pptx 首选，真实渲染：github.com/iOfficeAI/OfficeCLI，Apache 2.0，单文件 33MB 自包含 .NET 程序，无任何外部依赖——渲染走系统 WebView2）：**纯 CLI 原生输出，不做任何中间转换链**。txt = `view text`；html = `view html -o` 直存；md = CLI 的 html → `HtmlConvert.ToMarkdown`（纯文本级变换）；**pdf/png/jpg = `view screenshot` 原生逐页截图**（无头浏览器渲染，像素零重采样——实测水印/复选框/表格分页全部干净；此前 html→WebView2 打印→pdf.js 的中转链会产生巨字水印叠加/勾选丢失等 bug，已废弃）。逐页循环的末页判定：CLI 对超出末页的页码会**钳位回第 1 页**（pptx 则报 "total slides: N"），按"页内容 MD5 已出现过"终止循环。pdf 目标 = 页面 PNG 经 doceng 新 job `startImagesToPdf`（pdf-lib）原样嵌入容器（页面尺寸=图片像素，文字不可选中——officecli 官方 pdf 导出需其 exporter 插件，尚未发布）；合并长图 = System.Drawing 纵向拼接；JPG = System.Drawing JPEG 编码（质量可调）。参数经 `DocConvertOptions`（ZipLevel/MergeImages/**ImageMaxEdge=--screenshot-width 清晰度/JpgQuality/PageRange=--page 页码范围/RenderMode=--render auto|native|html**，native 用本机 Word/PowerPoint 渲染）从转换对话框透传。仅 OOXML 源（.docx/.xlsx/.pptx）进入此管线，失败自动回退内置引擎（回退路径仍走 WebView2 打印 + pdf.js，共用同一组参数）；未就绪时页面弹窗确认下载（单文件 33MB，主源 = 作者自建 GitCode 镜像 `api.gitcode.com/api/v5/repos/luolangaga/tubatoolr/releases/12` 解析 browser_download_url——**不做 HEAD 验证**，该端点 HEAD 返回 401 假阴性而 GET 302 到 file-cdn 签名 CDN 正常下载；兜底 GitHub `releases/latest/download/` 直链 + gh-proxy.com/ghproxy.053000.xyz HEAD 探测，arm64 缺资产时回退 x64 靠 Windows on ARM 模拟）。老格式 .doc/.wps/.et/.dps/.ppt/.xls 仍走 Office/WPS COM，rtf/odt/odp/ods 走内置解析器。
  - **WebView2 文档引擎** `DocumentEngineService` + `Assets/DocEngine/doceng.html`（markdown-it / docx-preview / SheetJS / pdf.js / pdf-lib 全部本地打包，虚拟主机 `doceng`；ExecuteScriptAsync 不等 Promise → 「同步启动 + 轮询 getJob」模式）：渲染 PDF（PrintToPdfAsync）、PDF 文字层提取/表格行列聚类（→XLSX）、PDF 合并/拆分（pdf-lib）、工作簿读写（SheetJS）。OfficeCLI 未就绪时的回退管线。
  - **纯 C# 转换器**：`DocxWriter`（手写 OpenXML 包，HTML/文本→docx，含编号列表）、`DocxReader`（docx→Markdown/纯文本）、`RtfTextExtractor`（\'hh 按 GBK 双字节配对）、`OdfConverter`（odt/ods/odp→HTML）、`HtmlConvert`（HtmlAgilityPack，HTML→文本/Markdown；img 的 data:/blob: src 转占位符，防 Markdown 泄漏巨型 base64）、`TabularConvert`（CSV/JSON/MD/HTML 互转 + 智能编码读取 UTF-8/GB18030）。
  - **`OfficeInteropService`**：.doc/.ppt/.wps/.et/.dps 旧格式走本机 Office/WPS COM（动态探测 ProgID：Word/KWPS、Excel/KET、PowerPoint/KWPP；STA 线程 + 3 分钟超时 + 禁宏），未安装则给出明确错误。
- OCR = `OcrService`（Windows OCR API / `Windows.Media.Ocr`）。注意：Windows AI `Microsoft.Windows.Vision.TextRecognizer` 在 .NET 26100 投影与 NuGet 中均不可引用，故未采用（见文件头注释）。
- 图片→MP4/WebM 借用 FFmpeg（`BuildImageVideoArgs`：静态图 -loop 1 + 时长，GIF 直接转码）；OCR/ZIP/合并/拆分等非普通目标是 `ConvertSpecial`，由页面专门调度。
- Win32 拖放复用 `Win32DropHelper`（管理员 UIPI 绕过）；文件多选用 `Win32Dialogs.PickOpenMultiple`。

### 垃圾清理（JunkCleanerTool）— FluentCleaner.Core 引擎架构
- 「垃圾清理」内置工具已完全重构为 **Winapp2.ini 规则库驱动**，引擎移植自 builtbybel/FluentCleaner（MIT）的 `FluentCleaner.Core`，代码在 `Services/JunkCleaner/`，保留上游命名空间 `FluentCleaner.Models` / `FluentCleaner.Services` 以便对照上游更新。
- 管线：`Winapp2Parser`（FileKeyN/RegKeyN/ExcludeKeyN/Detect/DetectFile/SpecialDetect 多值键解析）→ `DetectionService`（注册表/文件/SpecialDetect 检测已安装应用，OR 逻辑）→ `CleaningService`（两阶段：Analyze 只读构建删除清单 + Clean 真删；CreateFileW 探测锁定文件、跳过 reparse point、REMOVESELF 剪除空目录、注册表排除分支保护）→ `PathExpander`（%EnvVar% 展开 + 通配符路径段递归解析）。
- 规则库管理 `JunkCleanerDatabase`：**两个规则库随应用内置**于 `Assets/JunkCleaner/`（Winapp2.ini 清理规则 + Winappx.ini 预装应用清单，csproj `Assets\**` Content 自动打包）。数据目录 `<DataDir>/JunkCleaner/` 存在副本时优先（`GetEffectivePath(kind)`），否则回退读内置文件——离线开箱即用。「更新规则库」手动触发，从原仓库拉取（raw.githubusercontent → jsdelivr CDN → gh-proxy 三级回退），先下临时文件校验再原子替换，覆盖 Winapp2 与 Winappx 两个文件。自定义规则放 `<DataDir>/JunkCleaner/Custom/*.ini`。
- 预装应用清理：Winappx 驱动，`AppxService`（移植上游，PowerShell `Get-AppxPackage` 检测 / `Remove-AppxPackage` 卸载）在工具页底部「预装应用清理」Expander 区，默认不勾选，卸载前确认。
- UI 防闪：扫描时进度文本单行截断 + `CreateUiProgress` 100ms 节流（避免千级路径刷新导致顶栏忽大忽小）；结果文本固定高度常驻（不 Collapsed/Visible 切换），布局不再重排。
- UI：条目按 `CategoryResolver`（LangSecRef → 分类，回退 Section → 其他应用程序）分组展示，逐条开关默认取 ini 的 `Default`；清理前有确认面板，含注册表项目黄色警示。旧硬编码分类 `JunkCleanerService` 与 AI 扫描 `AiJunkAnalyzerService` 已删除。
- 测试：`Winapp2ParserTests`（解析/Key 模型/分类映射）。

### Adding a built-in tool
1. New class in `TubaWinUi3.WinUI3/Services/BuiltinTools/` implementing `IBuiltinTool`.
2. Pick `BuiltinToolKind`: `Dialog` / `BackgroundTask` / `ProgressTask` / `InstantAction`.
3. Register in `BuiltinToolRegistry.RegisterDefaults()` — **duplicate IDs throw**.
4. Create dialogs via `context.CreateDialog(title)` (or manually set `RequestedTheme = ThemeService.CurrentElementTheme`) so ContentDialogs respect the app theme.

## Gotchas

- `Tools/` has Chinese category directory names (处理器工具, 显卡工具, …) — path handling must be Unicode-safe.
- `HardwareInfoService` runs WMI on `Task.Run`; results are consumed on the UI thread. `ApplyCpuzOverride()` deep-copies WMI sections and overwrites them with CPU-Z data (`IsVerified=true`).
- `LiteMonitorService` uses LibreHardwareMonitorLib (nvapi64 / ATI ADL / D3DKMT / WMI fallbacks) — installs **no kernel driver**; the FPS overlay reads the ETW `DxgKrnl` Present event via `FpsService` (needs admin, no driver). The game-monitor overlay no longer gates on the PawnIO driver.
- `FpsService` uses an ETW `DxgKrnl` trace session (`Microsoft.Diagnostics.Tracing.TraceEvent`) — needs admin for kernel tracing.
- `ConfigManager` supports two data locations — AppData (`%LocalAppData%/TubaWinUi3/`) or AppRoot (`<appdir>/Data/`) — selected by a `.config_location` marker file.
- `Package.appxmanifest` declares `runFullTrust` and `systemAIModels`.
- **Bundled icon cache**: `ToolIconService` prefers `<appdir>/IconCache/` (ships inside the package, read-only) over the writable `DataDir/IconCache`; missing/stale icons are copied from the bundled cache or extracted at runtime. `build-icon-cache.ps1` generates it (same SHA256 `{ToolsRoot}\<relative>` key scheme); a `GenerateBundledIconCache` MSBuild target runs it automatically before every `dotnet publish` (skipped when `ExcludeToolsFromPublish=true`). The `IconCache/` folder is gitignored — never commit it.

## File-transfer subsystem (separate from the .NET app)

The "文件传输" feature spans three pieces with their own toolchains — none are part of `dotnet build`:
- `TubaWinUi3.WinUI3/Services/BuiltinTools/LanFileShareTool.cs` + the `SIPSorcery` package (WebRTC) — the in-app side.
- `file-transfer-web/` — Vue 3 + Vuetify 4 web UI (`npm run dev`; build is `vue-tsc -b && vite build`).
- `cloudflare-worker/` — WebRTC signaling server (Cloudflare Durable Object `GroupRoom`); `wrangler dev` / `wrangler deploy`.

## Android downloader app (separate)

`android-tuba-installer/` is a **Kotlin + Jetpack Compose** Android app (图吧工具箱安装助手) that lets users pick a PC architecture (x64 default), download the official Inno setup exe (GitCode mirror first, GitHub fallback — same asset naming as `UpdateService`), and follow an MTP copy + double-click install guide. **No root / no ADB / no Bluetooth** by design (target PCs must stay offline). Not part of `dotnet build`:

- Build: `./gradlew -p android-tuba-installer assembleDebug` (Gradle wrapper inside the folder; needs Android SDK; `local.properties` is gitignored).
- APK CI: `.github/workflows/android-build.yml` (manual `workflow_dispatch`, uploads debug APK artifact).
- Android 10+ writes to public Downloads via MediaStore (no permissions); API 26–28 needs WRITE_EXTERNAL_STORAGE.
- Release API sources: `api.gitcode.com/api/v5/repos/{luolangaga|gcw_uDDNaqJw}/tubatool/releases/latest` → `api.github.com/repos/luolangaga/tubatool/releases/latest`; assets matched by `TubaWinUi3_Setup_*_{arch}.exe`.

## CI (`.github/workflows/` — all manual `workflow_dispatch`)

**There is no push/PR CI** — nothing builds or tests automatically on commit; run `dotnet build` + `dotnet test` locally before pushing.

- `build-release.yml` — bumps `<Version>` in **both** `.csproj`s and `#define MyAppVersion` in all `installer*.iss`, publishes x64/x86/ARM64 portable + Inno installer + x64-lite (`ExcludeToolsFromPublish=true` + `.lite_build` marker), builds the Compatible edition, restores `.pri`, generates the changelog via **DeepSeek** (`DEEPSEEK_API_KEY`), creates the GitHub release, and optionally mirrors to **GitCode/AtomGit** (`GITCODE_ACCESS_TOKEN`). Portable zips are staged as a `src/` folder plus the native `Launcher\bin\图吧工具箱WinUI3_<arch>.exe` (renamed `图吧工具箱WinUI3.exe`).
- `android-build.yml` — Gradle debug APK for `android-tuba-installer/`; the only workflow that runs tests.
- `sync-to-gitcode.yml` — re-uploads assets from an existing GitHub Release to GitCode via AtomGit API.

`Launcher/` is a native C launcher (`launcher.c` + `launcher.rc`, built via `Launcher/build.ps1`) that finds and starts the .NET app. `build-setup.ps1` / `build-store*.ps1` build the Inno installer / MSIX locally.

## Docs site (separate)

Root `package.json` + `src/docs/` are a **VitePress** site only (`npm run dev` / `npm run build`). `node_modules/` is not referenced by any `.csproj`.

## New official website (separate)

`website-winui3/` is the new official website, built on **WinUIonWeb** (Vue 3 + Web WinUI controls, `npm run dev` / `npm run build`). Docs markdown lives in `website-winui3/src/docs/` (migrated from `src/docs/`), tutorial images in `website-winui3/public/tutorials/images/`. Icon glyphs must exist in `src/assets/Fonts/SEGOEICONS.TTF` (check with `node check-font` against the cmap table).

## Conventions

- Namespaces: `TubaWinUi3` / `.Pages` / `.Services` / `.Models`. PascalCase; XAML + code-behind pairs.
- Commit messages are short Chinese summaries in practice (`优化OSD编辑器`, `修复打包`); `feat:`/`fix:` prefixes are welcome but not enforced.
- Never commit: `bin/`, `obj/`, `.pfx`, `.cer`.
