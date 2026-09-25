<div align="center">

English | [中文](README.md)

<img src="images/banner-en.png" width="880" alt="Tuba Toolbox CE — PC hardware toolbox">

<sub>Note: this English README is translated by AI and may contain inaccuracies.</sub>

</div>

<br>

<div align="center">

[![Release](https://img.shields.io/github/v/release/luolangaga/tubatools?style=for-the-badge&labelColor=1f2937&color=3b82f6&logo=windows11&logoColor=white)](https://github.com/luolangaga/tubatools/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/luolangaga/tubatools/total?style=for-the-badge&labelColor=1f2937&color=22c55e)](https://github.com/luolangaga/tubatools/releases)
[![Platform](https://img.shields.io/badge/Windows-10%201809%2B-0078d6?style=for-the-badge&labelColor=1f2937&logo=windows&logoColor=white)](#system-requirements)
[![.NET](https://img.shields.io/badge/.NET-10-512bd4?style=for-the-badge&labelColor=1f2937&logo=dotnet&logoColor=white)](#build-from-source)
[![Bundled Tools](https://img.shields.io/badge/Bundled%20Tools-89%20apps-8b5cf6?style=for-the-badge&labelColor=1f2937)](#bundled-tools-89)
[![License](https://img.shields.io/badge/License-GPL--3.0-0078d6?style=for-the-badge&labelColor=1f2937)](LICENSE)

**One toolbox for a new PC — from first-boot checks and benchmarks to drivers, cleanup and repair.**

89 classic hardware tools ship in the box, 47 native Fluent tools work out of the box, and an AI assistant lives inside that can actually operate your PC.

[Official Docs](https://tubawinui3.cn) | [Download](https://github.com/luolangaga/tubatools/releases) | [Issues](https://github.com/luolangaga/tubatools/issues) | [Discussions](https://github.com/luolangaga/tubatools/discussions)

<a href="https://hellogithub.com/repository/luolangaga/tubatools" target="_blank"><img src="https://api.hellogithub.com/v1/widgets/recommend.svg?rid=f86864b693ec4f5ca75749cc152de2e8&claim_uid=9j2VeIwBLtJZzfr" alt="Featured｜HelloGitHub" style="width: 250px; height: 54px;" width="250" height="54" /></a>
<a href="https://atomgit.com/luolangaga/tubatool"><img alt="AtomGit G-Star" src="https://atomgit.com/luolangaga/tubatool/star/new_badge.svg" height="55"/></a>
<a href="https://trendshift.io/repositories/51042?utm_source=repository-badge&utm_medium=badge&utm_campaign=badge-repository-51042" target="_blank" rel="noopener noreferrer"><img src="https://trendshift.io/api/badge/repositories/51042" alt="luolangaga%2Ftubatools | Trendshift" width="250" height="55"/></a>
<a href="https://trendshift.io/repositories/51042?utm_source=trendshift-badge&utm_medium=badge&utm_campaign=badge-trendshift-51042" target="_blank" rel="noopener noreferrer"><img src="https://trendshift.io/api/badge/trendshift/repositories/51042/daily?language=C%23" alt="luolangaga%2Ftubatools | Trendshift" width="250" height="55"/></a>
<a href="https://trendshift.io/repositories/51042?utm_source=trendshift-badge&utm_medium=badge&utm_campaign=badge-trendshift-51042" target="_blank" rel="noopener noreferrer"><img src="https://trendshift.io/api/badge/trendshift/repositories/51042/monthly?language=C%23" alt="luolangaga%2Ftubatools | Trendshift" width="250" height="55"/></a>

</div>

> [!WARNING]
> There have been many unofficial download channels lately — please verify before downloading! Only use the official channels listed under [Installation](#installation).

> **Not a computer expert?** Open the built-in [AI Assistant](#ai-assistant) and describe what you need in plain language —
> it can read hardware info, analyze disk usage, clean up junk, read and write files, run commands and search the web,
> and it asks you first before doing anything risky.

---

## What It Can Do

<p align="center">
  <img src="images/screenshots/favorites.png" width="420" alt="Favorites and recommendations">
  <img src="images/screenshots/settings.png" width="420" alt="Settings">
</p>

<p align="center">
  <sub>Left: a favorites page ranked by how often you use each tool, with drag-to-reorder ｜ Right: Mica material, light/dark themes, UI language and data backup</sub>
</p>

The left navigation browses every tool by category, the search box at the top finds any of them, and a star puts the ones you like on the favorites page.
**The 89 bundled classic tools and the 47 built-in native tools share one card layout, one favorites list, one sort order and one desktop-shortcut path** — so it never feels like two separate products.

### Feature Highlights

* **AI Assistant** — a built-in system agent: reads hardware info, analyzes disk usage, cleans junk, reads/writes files, runs commands and searches the web, with 28 agent tools and confirmation for risky actions
* **Hardware Info & Live Monitoring** — live temperature, frequency and power curves for CPU / GPU / memory / disk, with CPU-Z data verification
* **Benchmarks & Leaderboards** — full CPU / GPU / memory / disk / browser tests, PDF report export and community cloud leaderboards
* **Format Converter** — images / audio & video / Word / Excel / PPT / PDF, plus OCR, PDF merge & split and batch queues
* **Gaming Toolbox** — a customizable FPS / temperature / load overlay, one-click virtual LAN co-op, and runtime repair
* **System Optimization & Security** — junk cleaner / startup manager / context menu manager / malware blocker / sandbox analysis / time sync
* **Network Tools** — speed test / traffic monitor / port viewer / hosts editor / LAN file sharing
* **Native Fluent Experience** — Mica material, rounded windows, light & dark themes, English and Chinese UI, global search, drag-to-reorder favorites
* **An Extensible Tool List** — drop your own tools into the `Tools/` folder and they are picked up; metadata lives in `Metadata/tools.json`
* **Clean** — the portable build runs straight after extraction and the installer is per-user; there are no background services, so closing it really closes it

---

## AI Assistant

The built-in system agent is what "let AI drive your PC" looks like inside a toolbox: it does not just answer questions, it calls system tools — reading hardware info, analyzing disk usage, cleaning junk, reading and writing files, running commands and searching the web.

<p align="center">
  <img src="images/screenshots/ai-assistant.png" width="820" alt="AI Assistant">
</p>

<p align="center">
  <sub>Streaming reasoning, inline tool calls and confirmation for risky operations; works with any OpenAI-compatible provider and model</sub>
</p>

* **System-level tool calling** — 28 built-in agent tools (hardware info, disk analysis, process management, file I/O, command execution, web search and more), with a confirmation dialog before anything risky
* **Full-access mode** — an optional "full access" switch: turn it on and expert workflows run without a prompt each time
* **Visible reasoning** — the reasoning chain streams in, and long answers collapse automatically
* **Memory & skills** — cross-session memory (`memory.md`) and per-session skills; the longer you use it, the better it knows your machine
* **Model freedom** — a built-in model works out of the box, or point it at any OpenAI-compatible endpoint (DeepSeek / OpenAI / a local model…)
* **Loop guardrails** — repeated tool calls are blocked, reasoning and tool results are length-capped, and a session that keeps making no progress is stopped on purpose

---

## Hardware Detection & Performance

### Benchmarks & Leaderboards

Five suites — CPU / GPU / memory / disk / browser — are scored into "gaming performance" and "office performance" ratings and exported as a professional PDF report.

<p align="center">
  <img src="images/screenshots/stress-test.png" width="420" alt="Stress test">
  <img src="images/screenshots/gpu-stress-test.png" width="420" alt="Fractal GPU stress test">
</p>

<p align="center">
  <img src="images/screenshots/benchmark-cloud.png" width="420" alt="Benchmark leaderboard">
  <img src="images/screenshots/quick-device-check.png" width="420" alt="New PC check wizard">
</p>

<p align="center">
  <sub>Stress test ｜ Fractal GPU stress test ｜ Community leaderboard ｜ New-PC check wizard</sub>
</p>

* **Stress test** — pick any combination of CPU / GPU / NIC burn-in and watch temperature, frequency, power and NIC throughput live
* **Fractal GPU test** — a GPU fractal stress test with easy / medium / extreme pressure levels, super-resolution rendering and live FPS
* **Benchmark leaderboards** — upload reports to the community, browse global rankings and compare against users with the same hardware
* **New-PC check wizard** — one pass over appearance, hardware info, disk power-on hours, dead pixels, peripherals, camera and audio

### More Hardware Tools

<p align="center">
  <img src="images/screenshots/hardware.png" width="420" alt="Hardware info">
  <img src="images/screenshots/cpu-ranking.png" width="420" alt="CPU ranking">
</p>

<p align="center">
  <sub>Model / OS / CPU / memory / GPU / disk / NIC at a glance, plus desktop and laptop performance rankings (data from NanoReview)</sub>
</p>

* **Live monitoring** — temperature, frequency and power curves for CPU / GPU / memory / disk, shown in the page header or as an in-game overlay
* **Disk health** — SMART health checks (the CrystalDiskInfo approach): temperature / power-on time / lifespan / read-write totals, plus SSD TRIM and HDD defrag
* **Battery analyzer** — battery drain trends and per-app power rankings, a stronger report than what Windows Settings gives you
* **Keyboard & screen tests** — highlighted key detection (left/right Shift, Ctrl and Alt are distinguished) and full-screen solid colors / patterns to spot dead pixels and backlight bleed
* **Core-to-core latency** — browse community-uploaded latency heatmaps and compare core-to-core communication across CPUs

---

## Gaming Toolbox

### Game Monitor (overlay designer)

Design your monitoring overlay by dragging components: several layout presets ship in the box, and you can add FPS, CPU / GPU temperature and load, memory usage and more. It shows live in-game, and selected metrics can be recorded (up to 2 hours) and exported afterwards as a JSON / Markdown / CSV report.

<p align="center">
  <img src="images/screenshots/game-monitor.png" width="420" alt="Game monitor">
  <img src="images/screenshots/game-tunnel.png" width="420" alt="LAN co-op helper">
</p>

<p align="center">
  <sub>Drag-and-drop overlay layout, with automatic game-window detection ｜ Join by invite code — no public IP, no router changes</sub>
</p>

* **LAN co-op helper** — a Tailscale-based layer-3 virtual LAN, so TCP / UDP games like Minecraft, Terraria and Palworld connect directly; your friend pastes an invite code and the client installs and joins automatically, with one-click firewall rules and network checks
* **Recording viewer** — replays exported logs as charts: one chart per metric with its own Y axis, plus group switching, range trimming and P1 / P99 statistics
* **Runtime repair** — one-click install of missing Visual C++ 2008-2026, .NET Framework and legacy DirectX game components (official Microsoft sources with signature checks)
* **3D anti-motion-sickness** — a center crosshair plus edge markers to ease 3D motion sickness

---

## System Optimization & Security

<p align="center">
  <img src="images/screenshots/junk-cleaner.png" width="270" alt="Junk cleaner">
  <img src="images/screenshots/startup-manager.png" width="270" alt="Startup manager">
  <img src="images/screenshots/rogue-cleaner.png" width="270" alt="Rogue software cleaner">
</p>

<p align="center">
  <sub>Junk cleaner (4000+ Winapp2 rules) ｜ Startup manager ｜ Rogue software cleaner, with a restore center</sub>
</p>

* **Malware blocker** — adds software vendor certificates to the system's untrusted list, stopping rogue software from installing or running in the first place
* **Malware sandbox** — a Sandboxie-Plus environment to run and analyze suspicious programs safely; delete the sandbox and the system is back as it was
* **Context menu manager** — enable / disable / edit / add / delete context menu entries, covering New, Send to, Open with and the WinX menus
* **Time sync** — switch the system NTP server in one click (Aliyun / Tencent Cloud / NICT…), with server speed tests, offset detection, instant resync and one-click repair
* **And more** — hardware spoofer / Windows hidden features / .NET environment repair / background throttle saver / OptimizerDuck / Windows image download

---

## Network Tools

<p align="center">
  <img src="images/screenshots/speed-test.png" width="270" alt="Speed test">
  <img src="images/screenshots/traffic-monitor.png" width="270" alt="Traffic monitor">
  <img src="images/screenshots/port-viewer.png" width="270" alt="Port viewer">
</p>

<p align="center">
  <sub>Speed test (Zhejiang University / Ookla / Cloudflare nodes) ｜ Traffic monitor ｜ Port viewer</sub>
</p>

* **Hosts editor** — a visual editor for the system hosts file, with per-rule toggles and DNS flush
* **WiFi passwords** — view the names and passwords of the WiFi networks this machine has joined
* **Network optimizer** — TCP parameter tuning, DNS latency testing and configuration, public IP lookup, network reset and DHCP repair
* **Network scheduler** — aggregates multiple adapters, distributes traffic intelligently and accelerates automatically when Wi-Fi and Ethernet are both up
* **LAN file sharing** — an HTTP file-sharing service on your LAN that other devices open in a browser, with drag-and-drop upload

---

## Format Converter

Drop files in, pick a target format, and let the batch queue work through them one by one: images / audio & video / Word / Excel / PPT / PDF / text / JSON, **where one failed file never interrupts the batch**.

<p align="center">
  <img src="images/screenshots/format-converter.png" width="820" alt="Format converter">
</p>

<p align="center">
  <sub>Drag-and-drop and multi-select, batch queue processing, optional ZIP delivery; every target format carries its own tunable parameters (bitrate / bit depth / sample rate / CRF / resolution / frame rate…)</sub>
</p>

* **Document conversion** — Word / Excel / PPT / PDF / Markdown / HTML / JSON in any direction; for OOXML documents a built-in rendering engine exports PDFs and long images page by page with zero re-sampling
* **Images / audio & video** — FFmpeg and ImageMagick under the hood, with encoder, rate control, preset, resolution and audio-track options for video
* **OCR** — Windows' native OCR turns images into text in one step
* **PDF tools** — merge, split, and extract tables from the text layer into XLSX
* **Legacy formats** — old .doc / .ppt / .wps files are handed to a locally installed Office or WPS, and you get a clear message if neither is present

---

## Utilities

<p align="center">
  <img src="images/screenshots/time-sync.png" width="270" alt="Time sync">
  <img src="images/screenshots/windows-image.png" width="270" alt="Windows image download">
  <img src="images/screenshots/community.png" width="270" alt="Community tools">
</p>

<p align="center">
  <sub>Time sync (NTP server speed test and one-click resync) ｜ Windows image download (UUP Dump API) ｜ Community tools</sub>
</p>

* **PC tutorials** — new-PC unboxing guide, basics, burn-in checks, and common-sense myth busting
* **Official websites & service centers** — one-click access to Steam, Epic, UU Accelerator and more; plus official service-center addresses for major brands
* **Genuine software store / UniGetUI** — browse and install software through WinGet, or manage packages from winget / scoop / chocolatey / pip / npm in one GUI
* **Digital literacy test** — 25 multiple-choice questions on PC basics, to see where you stand
* **Community tools** — tool plugins contributed by the community, downloadable and ready to use (portable build only)

---

## Built-in Tools (47)

> A native tool set built with Fluent Design — no third-party software required. It covers system optimization, hardware detection, network diagnostics, gaming and more.

| Category | Count | Representative tools |
|:--------:|:-----:|:---------------------|
| **System** | 13 | Junk Cleaner / Rogue Cleaner / Malware Blocker / Startup Manager / Time Sync / Windows Image |
| **Hardware** | 12 | Benchmark / Stress Test / Fractal GPU Test / New-PC Wizard / Disk Health / Keyboard Test / Battery Analyzer |
| **Network** | 8 | Speed Test / Traffic Monitor / Port Viewer / Hosts Editor / LAN File Share / Network Optimizer |
| **Gaming** | 5 | Game Monitor / Recording Viewer / LAN Co-op Helper / Runtime Repair / 3D Anti-Motion-Sickness |
| **Utilities** | 9 | AI Assistant / Format Converter / PC Tutorials / Official Websites / Genuine Software Store / UniGetUI |

<details>
<summary>Click to expand the full tool list</summary>

### System Tools
- **Malware Blocker** — Blocks rogue software installs and runs by adding vendor certificates to the system untrusted list
- **Rogue Cleaner** — Scans and cleans rogue context menus, auto-starts, scheduled tasks, services, browser extensions and file-association leftovers, with a restore center
- **Malware Sandbox** — Sandboxie-Plus environment to safely run and analyze suspicious programs; delete the sandbox to restore the system
- **Junk Cleaner** — Winapp2 rule-based scanning and cleanup of app caches, temp files and registry leftovers (engine ported from FluentCleaner)
- **Startup Manager** — Scans auto-start entries (registry Run, startup folders, scheduled tasks, services), hides Microsoft entries and spots abnormal ones fast
- **Context Menu Manager** — Manage Windows context menu entries: enable / disable / edit / add / delete, covering New, Send to, Open with and the WinX / modern / IE menus
- **Hardware Spoofer** — Change the CPU / GPU / system info shown in the registry, with one-click restore of the original values
- **Background Throttle Saver** — Throttles background processes through Windows 11 efficiency mode (EcoQoS) to save power and run cooler while foreground apps stay smooth (based on the Energy Star X engine)
- **.NET Environment Repair** — Detects and installs missing .NET Runtime / SDK / Framework components from the official site
- **Windows Hidden Features** — Query, enable, disable and reset Windows experimental feature switches (a ViVe engine port with millisecond response)
- **Windows Image** — Download original Windows images (ISO / ESD), with ESD-to-ISO conversion
- **Time Sync** — One-click switching of the system NTP server, with server speed tests, offset detection, instant resync, service repair and restore-to-default
- **OptimizerDuck** — An open-source Windows optimizer for system cleanup, performance tuning and privacy protection

### Hardware Tools
- **Benchmark** — Full CPU / GPU / memory / disk / browser testing, scored into gaming and office performance ratings, exported as a professional PDF report
- **Benchmark Ranking** — Upload reports to the community, browse global leaderboards and compare with same-hardware users
- **Stress Test** — CPU / GPU / NIC burn-in with freely combinable items; NIC testing supports custom data volume and rate reference, all with live temperature, frequency, power and throughput
- **Fractal GPU Test** — GPU fractal stress testing in easy / medium / extreme levels, with super-resolution rendering beyond the screen and live FPS monitoring
- **New-PC Check Wizard** — One-stop inspection: appearance, hardware info, disk power-on hours, dead pixels, peripherals, camera, audio and burn-in tests
- **Disk Health** — SMART health monitoring (the CrystalDiskInfo approach): temperature / power-on / lifespan / read-write totals, plus SSD TRIM and HDD defrag
- **Battery Analyzer** — Battery drain trends and per-app power rankings, a stronger battery report than Windows Settings
- **Keyboard Test** — Verifies every key with highlighted feedback, full-size / compact layouts, left-right Shift / Ctrl / Alt distinction and Copilot key support
- **Screen Test** — Full-screen solid colors and test patterns to spot dead pixels, backlight bleed and banding
- **CPU / GPU Ranking** — Desktop and laptop performance leaderboards with brand filters and sorting (data from NanoReview)
- **Core-to-Core Latency** — Browse community-uploaded core-to-core latency heatmaps to compare CPUs

### Network Tools
- **Speed Test** — Native latency, download and upload testing with switchable Zhejiang University / Ookla / Cloudflare nodes
- **Traffic Monitor** — Pick an adapter to watch per-connection traffic, speed and latency, with whole-adapter throughput charts, snapshot recording and slider replay
- **Port Viewer** — View all TCP / UDP port usage and find the owning process
- **Hosts Editor** — Visually edit the system hosts file with per-rule toggles and DNS flush
- **WiFi Passwords** — View the names and passwords of previously joined WiFi networks
- **Network Optimizer** — TCP parameter tuning (congestion control / Chimney / Nagle / adapter power saving), DNS latency testing and configuration, public IP lookup, network reset and DHCP repair
- **Network Scheduler** — Aggregates multiple adapters, distributes traffic intelligently and accelerates Wi-Fi plus Ethernet setups
- **LAN File Share** — Creates an HTTP file-sharing service on your LAN that other devices can browse and download from, with drag-and-drop upload

### Gaming Tools
- **Game Monitor** — Design a monitoring overlay by dragging components, with built-in layout presets, live FPS / temperature / load readouts and JSON / Markdown / CSV report export
- **Recording Viewer** — Parses exported JSON / CSV logs and replays FPS, temperature and load history: one chart per metric with its own Y axis, plus group switching, range trimming and P1 / P99 statistics
- **LAN Co-op Helper** — Virtual LAN co-op: put two PCs on the same virtual network and your friend joins by pasting an invite code — no public IP and no router changes
- **Runtime Repair** — Detects and repairs missing Visual C++ 2008-2026, .NET Framework 4.8.1 and legacy DirectX game components, downloaded from official Microsoft sources with signature checks
- **3D Anti-Motion-Sickness** — A center crosshair plus edge markers to ease 3D motion sickness

### Utilities
- **AI Assistant** — An intelligent system agent that can diagnose problems, tune settings, read and write files, run commands, search the web and carry out operations
- **Format Converter** — Images / audio & video / Word / Excel / PPT / PDF / text conversion, OCR, PDF merge and split, ZIP packing, batch queues and drag-and-drop
- **PC Tutorials** — A new-PC unboxing guide, the basics, burn-in testing, and common sense versus myths — a hand-held walkthrough
- **Official Websites** — One-click access to the official sites of Steam, Epic, UU Accelerator and other common software
- **Service Center Locator** — Look up official service centers for major laptop and desktop brands
- **Genuine Software Store** — Browse and install genuine software based on the WinGet source
- **UniGetUI Package Manager** — An open-source GUI for Windows package managers, supporting winget / scoop / chocolatey / pip / npm and more
- **Digital Literacy Test** — Test your PC knowledge: 25 multiple-choice questions, 100 points total
- **Community Tools** — Tool plugins contributed by the community, ready to use after download, with support for submitting and removing tools (portable build only)

</details>

---

## Bundled Tools (89)

> **89** classic third-party tools ship with the app, covering the whole hardware-detection landscape. They launch in one click and need no installation — or drop your own tools into `Tools/` and they are picked up automatically.

| Category | Count | Representative tools |
|:--------:|:-----:|:---------------------|
| CPU | 9 | CPU-Z / Core Temp / ThrottleStop / LinX / Prime95 / Superpi / wPrime / C2CLatency |
| GPU | 9 | GPU-Z / DDU / GpuTest / dxvachecker / nvidiaProfileInspector / AMD·NVIDIA driver downloads |
| Storage | 22 | CrystalDiskMark / CrystalDiskInfo / DiskGenius / HDTune / WizTree / finaldata / URWTEST |
| Memory | 7 | MemTest / MemTest64 / TM5 / Thaiphoon Burner / ZenTimings |
| Comprehensive | 5 | AIDA64 / HWiNFO / HWMonitor / Speccy / RWEverything |
| Peripherals | 7 | Keyboard Test / Mouse Rate / MouseTester / KeyTweak |
| Display | 3 | Color gamut check / UFO test / Windows HDR Calibration |
| Burn-in | 1 | FurMark burn-in edition |
| Others | 26 | Everything / Dism++ / Rufus / Ventoy / Autoruns / ProcessMonitor / BlueScreenView / DirectX repair |

See the [official docs](https://tubawinui3.cn) for the complete tool list.

---

## Getting Started

### System Requirements

* Windows 10 1809 (build 17763) or newer; Windows 11 is fully supported
* x64 / x86 / ARM64, with a **self-contained** package: the .NET 10 runtime and Windows App SDK are bundled, so there is nothing else to install
* The installer is per-user; features that need administrator rights request elevation once for the app (see the [FAQ](#faq))

| Windows version | Support |
|:---------------:|:-------:|
| Windows 11 | ✅ Fully supported |
| Windows 10 21H2+ | ✅ Fully supported |
| Windows 10 1809+ | ✅ Minimum supported |

| Platform | Support |
|:--------:|:-------:|
| x64 (Intel / AMD 64-bit) | ✅ Fully supported |
| x86 (Intel / AMD 32-bit) | ✅ Fully supported |
| ARM64 (Qualcomm Snapdragon, etc.) | ✅ Native support |

### Installation

#### GitHub Releases (recommended)

Download the latest version from [Releases](https://github.com/luolangaga/tubatools/releases/latest). Two forms are provided:

* **Portable (ZIP)** — extract and run, no installation; small enough to carry on a USB drive
* **Installer (Inno Setup)** — a traditional setup program with selectable install location and shortcuts

Signing policy for release artifacts: see [Code signing policy](#code-signing-policy).

#### GitCode Releases (China mirror)

In China you can download from the [GitCode mirror](https://gitcode.com/luolangaga/tubatool) for better speeds.

#### Winget

```powershell
winget install luolangaga.tubatools
```

#### Scoop

```powershell
# 1. Add the Tuba Toolbox CE bucket
scoop bucket add tubatools https://github.com/luolangaga/scoop-tubatools

# 2. Install (picks the x64 / arm64 portable build automatically)
scoop install tubatools/tubatool
```

Update to the latest version with `scoop update tubatool`. Bucket repository: [luolangaga/scoop-tubatools](https://github.com/luolangaga/scoop-tubatools).

#### Microsoft Store

<a href="https://apps.microsoft.com/detail/9P15095X7MGB?referrer=appbadge&mode=full" target="_blank" rel="noopener noreferrer">
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>

### Build from Source

```powershell
git clone https://github.com/luolangaga/tubatools.git
cd tubatools
dotnet build        # Build (RuntimeIdentifier matches the current architecture)
dotnet run          # Run unpackaged (it will request administrator rights)
dotnet test         # Run the xUnit tests
```

The app itself is `TubaWinUi3.WinUI3/TubaWinUi3.csproj`. To test the packaged form under a real package identity, run `.\run-msix.ps1` to register a dev package and launch it.

<details>
<summary>Prerequisites</summary>

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Visual Studio 2022 17.14+](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/) with C# Dev Kit
- Windows 10 1809 or newer, on x86 / x64 / ARM64

</details>

---

## Architecture

```
TubaWinUi3.WinUI3/            The app (WinUI 3 / .NET 10 / Windows App SDK)
  App.xaml.cs                 Startup, elevation, global exception fallback
  MainWindow.xaml             Custom title bar + NavigationView + Frame, with the AI mascot in the header
  Pages/                      Tool pages (built-in tools, settings, search, time sync, image download…)
  Services/
    BuiltinTools/             The 47 built-in tools (IBuiltinTool + BuiltinToolRegistry)
    Ai/                       AI Assistant layer (TubaChatProvider / AgentToolAdapter / loop guard)
    GameTunnel/               LAN co-op (Tailscale wrapper, invite codes, firewall, port probing)
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
TubaWinUi3.Compatible/        .NET Framework 4.8 WinForms edition for older systems
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

---

## FAQ

**Q: Why does it trigger UAC on launch?**
Burn-in tests, the FPS overlay, junk cleaning and time sync all need to read sensors or write system settings, so they require elevation. The unpackaged build requests administrator rights once at startup; the Microsoft Store build runs under package identity and does not force elevation.

**Q: Are the bundled third-party tools official?**
The 89 third-party tools are copyrighted by their respective authors and are bundled for learning and evaluation only — please support the originals. The 47 built-in tools are native to this project, and their source code is right here in this repository.

**Q: Can I carry it on a USB drive?**
Yes. The portable build runs straight after extraction, and settings default to `%LocalAppData%\TubaWinUi3\`. To keep your data next to the app instead (for example on an external drive), place a `.config_location` marker file in the program folder and settings move to `Data\` beneath it.

**Q: Antivirus flags it — what should I do?**
Some tools legitimately operate at system level (drivers, registry, cleanup), so heuristic engines may flag them. Official release artifacts are signed through SignPath; please download only from the [official channels](#installation).

**Q: Will it slow my PC down?**
There are no background services: hardware monitoring runs only while its page or an in-game overlay is open, and the overlay and co-op helper end when you close them — nothing lingers, nothing runs behind your back.

**Q: A tool won't start, or complains about missing DLLs?**
Use "Runtime Repair" to install the missing Visual C++ 2008-2026, .NET Framework and DirectX components in one click. Legacy document conversion needs a locally installed Office or WPS — if neither is present, you get a clear message instead of a silent failure.

**Q: Which models can the AI Assistant use?**
A built-in model works out of the box, and you can point it at any OpenAI-compatible endpoint (DeepSeek / OpenAI / a local model…). API keys are stored on your machine only.

**Q: Where are the logs?**
By default under `%LocalAppData%\TubaWinUi3\` (`Data\` next to the app in portable mode): `agent-debug.log` is the AI Assistant log, and startup crashes are written to `%TEMP%\app_crash.log`.

---

## Community

Join the QQ group for discussion: **485079194**

---

## Contributors

Thanks to everyone who has contributed to this project!

<a href="https://github.com/luolangaga/tubatools/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=luolangaga/tubatools&max=30&columns=10" alt="Contributors" />
</a>

---

## Recognition

* **AtomGit G-Star graduated project** — passed the official [AtomGit](https://atomgit.com) review and earned the G-Star Graduation Certification (No.0614, 2026.07.24); follow us at [atomgit.com/luolangaga/tubatool](https://atomgit.com/luolangaga/tubatool)
* **Featured by HelloGitHub** — listed and recommended in the [HelloGitHub](https://hellogithub.com/repository/luolangaga/tubatools) repository library
* **Trendshift** — charted on the daily and monthly C# lists at [Trendshift](https://trendshift.io/repositories/51042)

<div align="center">

<img src="images/atomgit-gstar-certification.jpg" alt="AtomGit G-Star graduation certificate" width="560"/>

</div>

---

## Links

* Website and docs: [tubawinui3.cn](https://tubawinui3.cn)
* Download: [GitHub Releases](https://github.com/luolangaga/tubatools/releases) · [GitCode mirror](https://gitcode.com/luolangaga/tubatool)
* Package managers: [Winget](https://winget.run/pkg/luolangaga/tubatools) · [Scoop bucket](https://github.com/luolangaga/scoop-tubatools)
* Feedback: [Issues](https://github.com/luolangaga/tubatools/issues) · [Discussions](https://github.com/luolangaga/tubatools/discussions)
* Android installer helper: [android-tuba-installer](android-tuba-installer)
* QQ group: **485079194**

---

## Code signing policy

> Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Signing happens inside the automated build pipeline, so a signature means the binary is an automated build of this repository's source: the build must pass trusted build system (GitHub Actions) origin verification and be manually approved by an Approver before it is signed.

- **Committers / Reviewers**: [@luolangaga](https://github.com/luolangaga)
- **Approvers**: [@luolangaga](https://github.com/luolangaga)
- **Privacy policy**: [PrivacyPolicy.txt](PrivacyPolicy.txt)

---

## License

This project is licensed under **GPL-3.0**.

- Source code may be freely used, modified and distributed
- Derivative works must be released under the same license
- See [LICENSE](LICENSE) for details; the accompanying [License.txt](License.txt) is a software usage notice covering network and privacy disclosures, and does not add restrictions on top of GPL-3.0

---

## Donate

If this toolbox has been useful to you, you're welcome to buy me a milk tea ☕ — thank you to everyone who supports it!

<div align="center">

<img src="images/捐赠WeChat.png" alt="WeChat donation QR code" width="280"/>
<img src="images/捐赠支付宝.png" alt="Alipay donation QR code" width="280" style="margin-left: 24px;"/>

</div>

---

<div align="center">

![Repobeats](https://repobeats.axiom.co/api/embed/4b0d8326594907dda0ab84b9485aa4eda1e2a336.svg "Repobeats analytics image")

<a href="https://star-history.com/#luolangaga/tubatools&type=date&legend=bottom-right">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=luolangaga/tubatools&type=date&theme=dark&legend=bottom-right" />
    <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/svg?repos=luolangaga/tubatools&type=date&legend=bottom-right" />
    <img alt="Star History Chart" src="https://api.star-history.com/svg?repos=luolangaga/tubatools&type=date&legend=bottom-right" />
  </picture>
</a>

**If you find this useful, please give it a star!**

<sub>WinUI 3 · .NET 10 · Windows App SDK · GPL-3.0</sub>

</div>
