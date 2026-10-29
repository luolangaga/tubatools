[← 返回 README](../README.md)

# 快速开始

## 系统要求

* Windows 10 1809（build 17763）或更高版本，Windows 11 完全支持
* x64 / x86 / ARM64 三种架构，安装包**自包含**：.NET 10 运行时与 Windows App SDK 都在包里，不用另外装
* 安装版为每用户安装，装完即用；程序运行时需要管理员权限的功能会统一请求提权（详见 [常见问题](faq.md)）

| Windows 版本 | 支持状态 |
|:------------:|:--------:|
| Windows 11 | ✅ 完全支持 |
| Windows 10 21H2+ | ✅ 完全支持 |
| Windows 10 1809+ | ✅ 最低支持 |

| 平台 | 支持状态 |
|:----:|:--------:|
| x64（Intel / AMD 64 位） | ✅ 完全支持 |
| x86（Intel / AMD 32 位） | ✅ 完全支持 |
| ARM64（高通骁龙等） | ✅ 原生支持 |

## 安装

### GitHub Releases（推荐）

到 [Releases](https://github.com/luolangaga/tubatools/releases/latest) 下载最新版本，提供两种形式：

* **便携版（ZIP）** —— 解压即用，无需安装，也可以放进 U 盘随身带
* **安装版（Inno Setup）** —— 传统安装程序，可选安装位置与快捷方式

发布产物的签名策略见 [Code signing policy](../README.md#code-signing-policy)。

### GitCode Releases（国内镜像）

国内网络可从 [GitCode 镜像](https://gitcode.com/luolangaga/tubatool) 下载，速度更快。

### Winget

```powershell
winget install luolangaga.tubatools
```

### Scoop

```powershell
# 1. 添加图吧工具箱CE bucket
scoop bucket add tubatools https://github.com/luolangaga/scoop-tubatools

# 2. 安装（自动根据系统架构选择 x64 / arm64 便携版）
scoop install tubatools/tubatool
```

更新到最新版：`scoop update tubatool`。Scoop 源仓库：[luolangaga/scoop-tubatools](https://github.com/luolangaga/scoop-tubatools)。

### Microsoft Store

<a href="https://apps.microsoft.com/detail/9P15095X7MGB?referrer=appbadge&mode=full" target="_blank" rel="noopener noreferrer">
	<img src="https://get.microsoft.com/images/zh-cn%20dark.svg" width="200"/>
</a>

## 从源码构建

```powershell
git clone https://github.com/luolangaga/tubatools.git
cd tubatools
dotnet build        # 编译（RuntimeIdentifier 自动匹配当前架构）
dotnet run          # 以 Unpackaged 模式运行（会请求管理员权限）
dotnet test         # 跑 xUnit 测试
```

主程序是 `TubaWinUi3.WinUI3/TubaWinUi3.csproj`；想在真实包身份下自测打包形态，用 `.\run-msix.ps1` 注册一个开发包再启动。

<details>
<summary>环境要求</summary>

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Visual Studio 2022 17.14+](https://visualstudio.microsoft.com/) 或 [VS Code](https://code.visualstudio.com/)（配合 C# Dev Kit）
- 最低支持 Windows 10 1809，支持 x86 / x64 / ARM64

</details>
