# 图标工坊（Tubatools.IconStudio）

给图吧工具箱换图标的小工具：内置工具卡片、侧栏菜单和消息弹窗的标题图标都可以逐项换成自己的图片；也可以整套切换到「远古彩色」或「Fluent 字形」预设。

## 功能

- **内置工具**：约 60 个工具卡片，按分类显示；逐项「换图片 / 恢复默认」。
- **侧栏菜单**：首页、硬件、内置、性能测试、插件、AI 助手、设置，以及硬件分类和插件工具菜单项。
- **消息弹窗**：下载更新、网络联机、手机连接、AI、存储、设置、文件、导出、导入、安全、普通消息，共 11 类标题图标。
- **全局预设**：
  - `默认彩色`：当前版本的彩色矢量图标。
  - `远古彩色`：从仓库早期提交提取的 24×24 简易彩色线稿，另为 11 个后加工具补了同风格图标（空间清理/插件页沿用旧别名资源）。
  - `Fluent 字形`：最早版本使用的 Segoe Fluent Icons 字体字形，不依赖图片。
- **搜索过滤**：按名称或分类筛选当前分组。

## 图片规则

- 支持 PNG、JPEG、BMP、GIF、ICO、SVG。
- 单个文件不超过 16 MB。
- ICO 与长边超过 1024 px 的图片会归一化成 PNG；小图保留原格式。
- 导入文件按 SHA-256 去重，放在：

  `%LOCALAPPDATA%\<工具箱数据目录>\IconStudio\Icons`

  打包版位于 `%LOCALAPPDATA%\Packages\<包标识>\LocalState\TubaWinUi3\IconStudio\Icons`。
- 切换预设或「全部恢复默认」会清空不再使用的导入文件。

## 生效方式

宿主核心服务 `IconThemeService` 负责保存全局预设（`IconStudio.Preset`）与单图标覆盖（`IconStudio.Overrides`）。
内置工具、导航和对话框在解析图标时会同步读取这些设置，因此不用打开插件页面也会生效；插件只是管理界面。

图标设置保存在工具箱设置里，**停用插件不会自动清空**（避免插件更新时丢配置）；需要还原时在插件页点「全部恢复默认」。卸载插件前建议先恢复。

## 构建与打包

```powershell
dotnet build Plugins\Tubatools.IconStudio\Tubatools.IconStudio.csproj -c Release
dotnet pack  Plugins\Tubatools.IconStudio\Tubatools.IconStudio.csproj -c Release -o artifacts\plugins
```

安装生成的 `Tubatools.IconStudio.1.0.0.nupkg`（插件页的本地安装入口）。插件声明 `fullTrust` 与 `icons`、`files` 能力，只应安装可信来源的包。

安装前请先重启工具箱，让包含 `IPluginIconService` 的新版宿主生效；如果插件页提示「当前宿主没有提供图标服务」，说明仍在旧进程里，重启后再打开即可。

## 目录

- `IconStudioPlugin.cs`：注册工具 `icon-studio` 与安装引导。
- `IconStudioPage.cs`：三组图标列表、预设卡片、图片选择与恢复操作。
- `assets/icon.svg`：插件卡片图标。
- 宿主资源：`TubaWinUi3.WinUI3/Assets/IconStyles/Classic/{Builtin,Navigation,Dialog}`。
