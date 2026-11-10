[← Back to README](../../README_EN.md)

# Architecture

```
TubaWinUi3.WinUI3/            The app (WinUI 3 / .NET 10 / Windows App SDK)
  App.xaml.cs                 Startup, elevation, global exception fallback
  MainWindow.xaml             Custom title bar + NavigationView + Frame, with the AI mascot in the header
  Pages/                      Tool pages (built-in tools, settings, search, time sync, image download…)
  Services/
    BuiltinTools/             The 47 built-in tools (IBuiltinTool + BuiltinToolRegistry)
    Ai/                       AI Assistant layer (TubaChatProvider / AgentToolAdapter / loop guard)
    GameTunnel/               LAN co-op (EasyTier virtual network, room codes, port forwarding, firewall, port probing)
    JunkCleaner/              Junk cleaning engine (FluentCleaner.Core port + Winapp2 rule sets)
    TimeSync/                 Time sync (w32tm wrapper, SNTP probing, preset servers)
    ToolCatalog.cs            Scans Tools/ + tools.json, merges architecture variants, builds tool cards
    HardwareInfoService.cs    WMI / CPU-Z hardware info (P/Invoke)
    LiteMonitorService.cs     LibreHardwareMonitor live monitoring (singleton)
  Assets/
    BuiltinIcons/             Color vector icons for built-in tools, drawn to Fluent System Icons rules
    DocEngine/                WebView2 document engine (markdown-it / pdf.js / SheetJS / pdf-lib)
  Strings/<lang>/             UI language resources (zh-CN / en-US)
  Metadata/tools.json         Single source of truth for tool metadata (description / order / categories / built-in mounts)
TubaWinUI3.BackEnd/           NativeAOT helper process (active interception)
TubaWinUi3.Tui/               .NET 8 Native AOT terminal compatibility edition
TubaWinUi3.Tests/             xUnit tests (catalog / time sync / co-op / junk cleaner / localization…)
Tools/                        Bundled third-party tools, arranged in category folders
installer*.iss · Launcher/    Inno Setup scripts and the native launcher
website-winui3/ · android-tuba-installer/ · file-transfer-web/ · cloudflare-worker/
                              Website / Android installer helper / LAN transfer / co-op signaling (separate toolchains)
```

A few implementation details worth calling out:

* **The tool list is data-driven** — `Metadata/tools.json` is the single source of truth: descriptions, card order, cross-category copies and built-in tool mounts all live there. A folder is listed only when the folder itself or one of its executables matches a `match` entry, so unlisted folders never show up.
* **One scan, one card per tool** — x64 / x86 / ARM64 variants are merged into a single card, with the right architecture selected automatically for the current system.
* **Three-layer icons** — built-in tools prefer their color vector SVG and fall back to a font glyph; the card template carries all three layers and shows what is available, so a missing icon degrades to the old look instead of a blank tile.
* **CPU temperature / frequency / power go through MSR** — which needs the PawnIO driver: if it is installed but not loaded, the app starts it; if it is still unavailable you get a link to the official installer. The FPS overlay instead uses the ETW `DxgKrnl` present event and needs no driver.
* **The AI Assistant carries session-level guardrails** — a repeated tool call in the same session is blocked the second time, reasoning and tool results are length-capped, and six rounds of tool calls with no progress trigger a stop instruction (`AgentToolLoopGuard`), shared by both engines.
* **Document conversion falls back through four engines** — OfficeCLI native rendering with per-page screenshots → the WebView2 document engine (pdf.js / SheetJS) → pure C# converters → local Office / WPS COM.
* **Junk cleaning is rule-driven** — two rule sets ship in the box (Winapp2.ini and Winappx.ini) and run in two phases (read-only analysis, then real deletion), so it works offline out of the box and can pull updated rules on demand.
* **Shortcuts are never created through PowerShell** — the Start-menu search entry and "send to desktop" share an in-process WScript.Shell COM call, avoiding Chinese paths turning into mojibake when they pass through a child process command line.
* **Localization applies instantly** — resources live in `Strings/`, tool metadata is overlaid from `Metadata/Tools_<language>.json`, and switching language needs no restart.
