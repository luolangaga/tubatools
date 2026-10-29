[← Back to README](../../README_EN.md)

# Getting Started

## System Requirements

* Windows 10 1809 (build 17763) or newer; Windows 11 is fully supported
* x64 / x86 / ARM64, with a **self-contained** package: the .NET 10 runtime and Windows App SDK are bundled, so there is nothing else to install
* The installer is per-user; features that need administrator rights request elevation once for the app (see the [FAQ](faq.md))

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

## Installation

### GitHub Releases (recommended)

Download the latest version from [Releases](https://github.com/luolangaga/tubatools/releases/latest). Two forms are provided:

* **Portable (ZIP)** — extract and run, no installation; small enough to carry on a USB drive
* **Installer (Inno Setup)** — a traditional setup program with selectable install location and shortcuts

Signing policy for release artifacts: see [Code signing policy](../../README_EN.md#code-signing-policy).

### GitCode Releases (China mirror)

In China you can download from the [GitCode mirror](https://gitcode.com/luolangaga/tubatool) for better speeds.

### Winget

```powershell
winget install luolangaga.tubatools
```

### Scoop

```powershell
# 1. Add the Tuba Toolbox CE bucket
scoop bucket add tubatools https://github.com/luolangaga/scoop-tubatools

# 2. Install (picks the x64 / arm64 portable build automatically)
scoop install tubatools/tubatool
```

Update to the latest version with `scoop update tubatool`. Bucket repository: [luolangaga/scoop-tubatools](https://github.com/luolangaga/scoop-tubatools).

### Microsoft Store

<a href="https://apps.microsoft.com/detail/9P15095X7MGB?referrer=appbadge&mode=full" target="_blank" rel="noopener noreferrer">
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>

## Build from Source

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
