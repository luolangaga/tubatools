# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TubaWinUi3 ("图吧工具箱") is a Windows desktop app that catalogs and launches third-party diagnostic/stress-test executables from a bundled `Tools/` folder, shows WMI/LibreHardwareMonitor hardware info, and ships 52 built-in utility tools. The UI is Chinese-first, with a partial English localization layer (see "Localization").

Verified toolchain facts (re-check them whenever the SDK or csproj changes):

- **.NET 10**; app project targets `net10.0-windows10.0.26100.0` (min OS `10.0.17763`), `<Version>1.6.4</Version>`
- **Windows App SDK 2.2.0**, `WindowsAppSDKSelfContained=true`
- **Unpackaged by default**: `WindowsPackageType=None` + `EnableMsixTooling=false`; MSIX is produced only by the build scripts
- Platforms: x86, x64, ARM64 (`RuntimeIdentifier` auto-detects host arch)

## Repository Layout

`TubaWinUi3.sln` contains 5 projects:

| Project | Kind | Role |
| --- | --- | --- |
| `TubaWinUi3.WinUI3/` | WinUI 3 `WinExe` | The app: all XAML pages, `Services/`, `Models/`, `Metadata/`, `Tools/` |
| `TubaWinUI3.BackEnd/` | NativeAOT `Exe` | "主动拦截" backend: state store + context-menu/COM monitor + named-pipe server (`NamedPipeBackendServer.cs`, `PipeContracts.cs`); single instance via global mutex; CLI `--config <path>` / `--once` / `--stop`; built by `build-backend-aot.ps1` |
| `TubaWinUI3.ShellExtension/` | NativeAOT `Library` | Win11 `IExplorerCommand` ("文件占用查看"), loaded by `dllhost` through the app manifest's `com:SurrogateServer`; shares `FileLockShellMenuContract.cs` by linked compile; built by `build-shell-ext-aot.ps1` (MSIX-only) |
| `TubaWinUi3.Compatible/` | `net48` WinForms | Legacy compatibility build (ReaLTaiizor + Costura.Fody single file) |
| `TubaWinUi3.Tests/` | xUnit | ~1280 tests; references the app and BackEnd (`Aliases="backend"`) |

Also present but **not** in the solution: `TubaWinUi3.PECompatible/` (`WinExe`, `net10.0-windows10.0.19041.0`, assembly name 图吧工具箱PE兼容版) — CI publishes it directly. Other top-level folders (`website-winui3/`, `cloudflare-worker/`, `rating-worker/`, `benchmark-worker/`, `telemetry-dashboard/`, `file-transfer-web/`, `android-tuba-installer/`, `android-tuba-remote/`, `Launcher/`, `DxTraceDiag/`, `remotedefender/`) are side projects and are not part of the desktop app build.

Order of magnitude, for orientation: `Services/` ≈ 334 files / ≈ 96k lines, `Pages/` ≈ 76 files / ≈ 58k lines. Known oversized files: `RogueCleanerPage.xaml.cs` (~3.9k lines), `HardwareInfoService.cs` (~3.3k), `SettingsPage.xaml.cs` (~3.0k), `GameOverlayPage.xaml.cs` (~2.7k). Prefer adding new code to focused files instead of growing these.

## Build & Run

```bash
dotnet build TubaWinUi3.WinUI3/TubaWinUi3.csproj             # Debug, host arch
dotnet run   --project TubaWinUi3.WinUI3/TubaWinUi3.csproj   # Unpackaged, no MSIX registration needed
dotnet publish -c Release -r win-x64 TubaWinUi3.WinUI3/TubaWinUi3.csproj
```

- All app commands go through `TubaWinUi3.WinUI3/TubaWinUi3.csproj`; building the whole `.sln` also builds the net48 WinForms project and the tests.
- `PublishTrimmed=false`, `PublishReadyToRun=true`, `WindowsAppSDKSelfContained=true`.
- Full rebuilds are slow (minutes) — prefer incremental `dotnet build` and `--no-build` for follow-up test runs.
- Known `.pri` quirk: after `dotnet publish`, resources sometimes have to be copied from `TubaWinUi3.WinUI3/bin/<config>/<tfm>/` into the publish output (the csproj already patches the `AssistStudio.Controls.pri` packaging bug), otherwise the UI can lose localized resources.
- Elevation: the app restarts itself elevated with the `runas` verb (`App.IsRunningAsAdmin()`); packaged (MSIX) builds use a different path because `ShellExecute runas` is not allowed there.

## Verification

```bash
dotnet test TubaWinUi3.Tests/TubaWinUi3.Tests.csproj                 # full suite (~1280 tests)
dotnet test TubaWinUi3.Tests/TubaWinUi3.Tests.csproj --no-build --filter FullyQualifiedName~LocalizationTests
```

- Stack: xUnit 2.9.3, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit.runner.visualstudio` 3.1.4, `coverlet.collector` 6.0.4. **A test project exists — "no test framework" is wrong.**
- `TubaWinUi3.Tests/xunit.runner.json` intentionally disables collection parallelization because test classes share process-wide statics (`ToolCatalog.ToolsRoot`, `ConfigManager`, `AgentToolRegistry`). Keep that in mind when adding tests, and don't re-enable parallelism without removing the shared state.
- **No CI job runs the tests**: `.github/workflows/build-test.yml` only publishes Lite/PE packages, and nothing collects coverlet coverage. Run the suite locally and quote the real summary before claiming a change is verified.
- Static analysis is the only other gate, and it is weak: there is no `Directory.Build.props`, no `global.json`, no ruleset and no `TreatWarningsAsErrors`. Keep new/edited code warning-free manually and avoid adding `#pragma warning disable` (9 exist today).

## Architecture

```text
App.xaml.cs          → creates MainWindow, calls LocalizationService.Initialize(), registers BuiltinToolRegistry, auto-elevates
MainWindow.xaml.cs   → TitleBar + NavigationView (x:Name="NavView") with Frame; nav is built from ToolCatalog categories
Pages/               → UI pages (mostly XAML + code-behind pairs; a few code-only pages)
Services/            → all business logic (static classes, no DI)
  BuiltinTools/      → 52 IBuiltinTool implementations
  ToolCatalog        → scans Tools/ for launchable files (.exe .bat .cmd .lnk .msc .ps1 .vbs) and caches categories
  ToolMetadataService→ merges tools.json metadata + FileVersionInfo + readme.txt
  ToolIconService    → extracts .exe/.lnk icons into %LocalAppData%/TubaWinUi3/IconCache/
  HardwareInfoService→ WMI queries on Task.Run; results consumed on the UI thread
  LiteMonitorService → LibreHardwareMonitorLib (vendor APIs/D3DKMT/WMI), no kernel driver; admin needed for FPS ETW (FpsService)
  BuiltinToolRegistry→ static registry of IBuiltinTool implementations
  UnifiedSearchService→ search across external tools, built-in tools, settings, quick actions
  LocalizationService→ resw lookup with fallback to the original literal (incremental migration)
  AppSettings        → JSON at %LocalAppData%/TubaWinUi3/settings.json
  ConfigManager      → data directory selection (AppData vs AppRoot)
Models/              → ToolItem, HardwareInfoItem, SearchResult, …
Metadata/tools.json  → descriptions/publishers/tags matched by the "match" field (case-insensitive substring);
                       tools.default.json is generated from it at build time, Tools_en-US.json holds English metadata
Tools/               → bundled third-party executables in Chinese-named category folders (处理器工具, 显卡工具, 硬盘工具, 烤鸡工具, 综合检测, …)
```

## Localization

- Resources live in `TubaWinUI3.WinUI3/Strings/{zh-CN,en-US}/Resources.resw` and are loaded by `Services/LocalizationService.cs` (called from `App.OnLaunched`); missing keys fall back to the literal passed by the caller (progressive migration).
- XAML still hardcodes Chinese text (0 uses of `x:Uid`), so new XAML strings are Chinese by default; move strings into the resw + `LocalizationService` when an English counterpart is needed.

## Adding a Built-in Tool

1. Add a class in `Services/BuiltinTools/` implementing `IBuiltinTool` (`string Id { get; }` plus the `BuiltinToolKind` behaviour).
2. Pick the kind: `Dialog` (popup UI), `BackgroundTask` (silent), `ProgressTask` (progress bar), `InstantAction` (immediate).
3. Register it in `BuiltinToolRegistry.RegisterDefaults()`; duplicate IDs throw `InvalidOperationException`. Registrations may be conditional (e.g. `CommunityToolBuiltinTool` is skipped in packaged builds).
4. Use `context.CreateDialog(title, closeButtonText = "关闭")` for themed dialogs.

## Key Conventions

- **Namespaces**: `TubaWinUi3`, `TubaWinUi3.Pages`, `TubaWinUi3.Services`, `TubaWinUi3.Models`.
- **Services are static classes without DI** — called directly from pages.
- **Tools/ content is bundled** via `<Content Include="Tools\**\*" CopyToOutputDirectory="PreserveNewest">`; Lite/Store builds exclude it (`-p:ExcludeToolsFromPublish=true`, `-p:IncludeLiteTools=true`) and download the Tools.zip/Tools_Lite.zip kernel package at runtime instead.
- **Metadata `"match"` field** is a case-insensitive substring match on tool filename/path.
- **File naming**: PascalCase for C#; XAML + code-behind pairs.
- **Commit format**: `feat:` / `fix:` / `docs:` / `refactor:`.
- **Never commit**: `bin/`, `obj/`, `.pfx`, `.cer`, local build output (`publish_*/`, `SetupOutput/`, `StoreOutput/`) or agent scratch folders (`.mimosa/`, `.vbin/`, `.wv2test/`).
- **Existing debt to not amplify**: ~650 empty `catch { }` blocks, ~270 `async void` handlers (mostly event handlers), 87 `.Result` uses and 5 `GetAwaiter().GetResult()` on UI paths. Log or comment exceptions instead of swallowing them, and keep async work `await`ed.

## Gotchas

- `Tools/` folders have Chinese names; all path handling must be Unicode-safe.
- `ToolCatalog.FindToolsRoot()` walks up from `AppContext.BaseDirectory` to locate `Tools/` — works packaged and unpackaged.
- `LiteMonitorService` uses LibreHardwareMonitorLib (nvapi64 / ATI ADL / D3DKMT / WMI) and installs no kernel driver; the FPS overlay uses an ETW DxgKrnl trace session (`FpsService`) and needs admin.
- `Package.appxmanifest` declares `runFullTrust` and the `webcam` device capability, plus the `com:SurrogateServer` entry for the shell extension. It does **not** declare `systemAIModels`.
- MSIX/Store builds strip the backend (`build-store.ps1` deletes `TubaWinUI3.BackEnd.*`, unsupported in the MSIX sandbox).
- Node/JS assets are not part of the .NET build: the root `package.json` is a VitePress site pointing at `src/docs/` (that folder no longer exists), the current site is `website-winui3/` (Vite + pnpm); `*-worker/` and `file-transfer-web/` are separate deployments.
- Build scripts at the repo root: `build-setup.ps1` (Inno Setup, x64/ARM64), `build-store.ps1` / `build-msix-store.ps1` (MSIX for Store), `build-backend-aot.ps1`, `build-shell-ext-aot.ps1`, `build-icon-cache.ps1`.
- Workflows in `.github/workflows/`: `build-test.yml` (Lite/PE publish smoke, no tests), `build-release.yml` (manual dispatch; portable zips + Inno installer), `sign-test.yml`, `android-build.yml`, `sync-to-gitcode.yml`.
- This file has drifted before (SDK version, tool count, test framework, CI description). When you change build, test or tool-registration facts, update the matching section here in the same change.
