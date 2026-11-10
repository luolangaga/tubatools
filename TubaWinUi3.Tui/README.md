# 图吧工具箱 TUI 兼容版

左侧是导航栏，右侧显示当前页面的工具卡片或硬件信息。默认由键盘控制左栏。上下键切换页面，按 Enter 进入右栏，再用方向键选卡片、按 Enter 启动。按 Esc 回到左栏。鼠标点击左栏切换页面，单击右栏选中条目，双击工具即可启动。宽窗口并排显示两张卡片，窄窗口自动切换为单列。滚轮可以浏览鼠标所在的栏。

支持 ANSI 彩色和 Unicode 的终端可显示 EXE 内嵌图标的像素预览。窗口高度不足或文件没有可用图标时，卡片显示文字。

- `/` 或 `F3` 搜索工具名称、路径或标签，输入时更新右栏，按 Enter 确认。
- 右栏选中工具后，`空格` 查看详情、`F` 切换收藏、`O` 打开目录、`C` 复制路径。
- 硬件信息页中，`Enter` 或 `空格` 查看完整字段，`C` 复制该字段所在分组。
- `F5` 刷新工具库或硬件信息，`Q` 退出。双栏布局需要至少 48 列 × 14 行。

## 运行

便携包中直接打开 `图吧工具箱TUI兼容版.exe`。程序从旁边的 `src/Tools` 和 `src/Metadata` 读取工具库，也支持把 `Tools` 和 `Metadata` 直接放在 EXE 旁边。Lite 包缺少部分工具时，可在图形版中安装，再回到 TUI 刷新工具库。

TUI 沿用现有数据目录和收藏文件。硬件信息需要系统提供 WMI，缺失的字段会显示「未知」或「不可用」。

## 构建和发布

项目面向 .NET 8，界面使用 Spectre.Console。Native AOT 发布需要 Visual Studio C++ 工具和 Windows SDK。产物是单个原生 EXE，目标机器无需安装 .NET。

```powershell
dotnet run --project TubaWinUi3.Tui/TubaWinUi3.Tui.csproj
dotnet publish TubaWinUi3.Tui/TubaWinUi3.Tui.csproj -c Release -r win-x64
dotnet publish TubaWinUi3.Tui/TubaWinUi3.Tui.csproj -c Release -r win-arm64
```

发布目录为 `bin/Release/net8.0-windows/<RID>/publish/`。Windows Native AOT 的 .NET 8 目标架构为 x64 和 ARM64。WinPE 镜像的运行情况取决于镜像提供的 Windows 组件。
