# NexBox 皮肤插件（Tubatools.NexBoxSkin）

把 NexBox 风格的主界面与游戏工具收进一个插件，不修改主程序：

- **纯皮肤插件**：不再向主程序内置工具索引注册游戏工具；所有页面都在 NexBox 侧栏里，避免污染经典界面的工具列表。
- **NexBox 皮肤**：把主窗口第 2 行替换为 WebView2 皮肤，**安装后默认开启**；可用侧栏“返回经典界面”、标题栏按钮或设置页关闭。
- **设置开关**：插件运行时在主程序「设置 → 外观和布局」注入「NexBox 皮肤」开关。
- **首次提示**：插件首次加载弹一次 InfoBar，说明皮肤已启用、可在设置或标题栏按钮关闭；提示本身可关闭，并带「关闭皮肤」按钮。
- **侧栏页面**：主页、硬件、工具、优化、**显示滤镜**、三角洲、设置。
- **显示滤镜**：逐显示器调整色温、亮度、对比度、饱和度、RGB 伽马与暗部增强，内置 10 个预设（标准/鲜艳/电影/高亮/柔和/游戏/阅读/去曝光/暗部增强/BenQ），通过 GDI Gamma Ramp 实现，不安装驱动、不注入游戏进程。退出应用或关闭皮肤时自动恢复原始 Gamma；设置会保存，下次启用时重新应用。

## 构建

```powershell
# 1. 先构建主程序（插件引用 TubaWinUi3.dll，只引用不修改）
dotnet build TubaWinUi3.WinUI3/TubaWinUi3.csproj -c Release

# 2. 构建前端（Node 18+；首次会安装依赖）
cd plugins/Tubatools.NexBoxSkin/web
npm install --include=dev
npm run build          # 输出到 plugins/Tubatools.NexBoxSkin/assets/web

# 3. 构建并打包插件
cd ../../..
dotnet build plugins/Tubatools.NexBoxSkin/Tubatools.NexBoxSkin.csproj -c Release
dotnet pack  plugins/Tubatools.NexBoxSkin/Tubatools.NexBoxSkin.csproj -c Release -o artifacts/plugins
```

产物：`artifacts/plugins/Tubatools.NexBoxSkin.1.0.0.nupkg`。

## 安装与使用

1. 主程序「插件」页安装 nupkg。
2. 插件加载后皮肤默认开启，主窗口内容区直接切换为 NexBox 外壳；「设置 → 外观和布局」会出现「NexBox 皮肤」开关。
3. 首次加载插件会提示一次：皮肤已启用，可在设置或标题栏按钮关闭。
4. 点击侧栏底部「返回经典界面」、标题栏的「经典界面」按钮或设置里的开关即可恢复经典界面。
5. 侧栏「滤镜」进入显示滤镜；切换显示器、拖动滑块或点击预设即时生效，右上开关控制启用/关闭，底部可恢复默认或打开 Windows 颜色管理。

## 数据与边界

- 改枪码：`https://df.nexbox.top` 公开 API；失败时由宿主 `HttpClient` 代理兜底。
- 主播设置：随插件打包的 `public/streamer-data` 静态数据。
- 随机装备：随插件打包的静态数据；图片来自 `playerhub.df.qq.com`。
- **不读取游戏内存、不注入游戏进程**；未实现的战绩分析、每日密码入口已移除。
- 显示滤镜使用 `GetDeviceGammaRamp` / `SetDeviceGammaRamp`；HDR 模式或部分显卡驱动可能拒绝写入，页面会提示。ICC 配置文件请使用 Windows 颜色管理。
- 皮肤通过 `IPluginHostAccess` 在运行时挂载，不修改主程序任何文件；卸载/停用插件时自动恢复经典界面并还原 Gamma。

## 许可证

界面与部分功能逻辑裁剪移植自 [NexBox](https://github.com/MuLiuSaMa/NexBox)（GPL-3.0）。
本插件与图吧工具箱同为 GPL-3.0。完整第三方许可见 `THIRD-PARTY-NOTICES.md`。
