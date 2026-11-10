# Steam 工具箱插件（Tubatools.SteamToolbox）

图吧工具箱官方内置工具「Steam 工具箱」的插件化版本。保留原内置页的全部布局与功能：

- 我的收藏：Steam 安装目录检测/更改、本机账号卡片与个人主页、多存储库游戏分组、
  搜索与排序、封面、本机时长、启动游戏、挂时长、打开游戏文件夹、Steam 商店页。
- 挂卡与时长：SteamKit 登录（二维码 / 账号密码 / Steam Guard）、当前任务卡片与计时、
  暂停/恢复/停止/退出、账号游戏库（含未安装游戏）、手动 AppID、好友列表。

## 包信息

| 项 | 值 |
| --- | --- |
| PackageId | `Tubatools.SteamToolbox` |
| 插件 API | v2 |
| 入口 | `Tubatools.SteamToolbox.SteamToolboxPlugin` |
| 工具 ID | `steam-toolbox`（全局 `plugin:tubatools.steamtoolbox:steam-toolbox`） |
| 分类 | 游戏工具 |

插件自带 `SteamKit2`、`QRCoder` 及其传递依赖（`protobuf-net`、`System.IO.Hashing`、
`ZstdSharp`、`System.Drawing.Common`），宿主不再需要引用 `SteamKit2`。

## 构建与打包

```powershell
dotnet build plugins/Tubatools.SteamToolbox/Tubatools.SteamToolbox.csproj -c Release
dotnet pack  plugins/Tubatools.SteamToolbox/Tubatools.SteamToolbox.csproj -c Release -o artifacts/plugins
```

产物：`artifacts/plugins/Tubatools.SteamToolbox.<Version>.nupkg`

包内布局：

```
tubatools.json
assets/icon.svg
plugin/Tubatools.SteamToolbox.dll
plugin/Tubatools.SteamToolbox.deps.json
plugin/SteamKit2.dll
plugin/QRCoder.dll
plugin/protobuf-net.dll
plugin/protobuf-net.Core.dll
plugin/System.IO.Hashing.dll
plugin/ZstdSharp.dll
plugin/System.Drawing.Common.dll
```

修改插件版本只需递增 csproj 的 `<Version>`。保持 PackageId、工具 ID 和分类不变，
已安装用户才能覆盖升级并保留收藏/排序。

## 发布到 NuGet

```powershell
dotnet nuget push artifacts/plugins/Tubatools.SteamToolbox.1.0.1.nupkg `
  --api-key <YOUR_NUGET_API_KEY> --source https://api.nuget.org/v3/index.json
```

工具箱的「插件」页按 `Tubatools` 标签检索 nuget.org；发布完成后用户可直接搜索安装。
也可以在本地把 nupkg 放进 AI 开发工作区的 `artifacts/`，由宿主在打包成功后自动校验加载。

## 说明

- 宿主页面统一提供页头（返回按钮 + 彩色插件图标 + 标题/说明），与内置工具一致。
- 文件夹选择、文件选择、剪贴板、打开链接、配置读写均走 `IPluginServices`；
  原生文件夹选择使用宿主新增的 `PickFolderAsync`，与内置工具共用同一选择器实现。
- 退出工具箱时插件通过 `AppDomain.ProcessExit` 释放 Steam 连接；页面关闭后挂卡会话
  继续在后台运行，与迁移前行为一致。
- MSIX/商店版按宿主策略不加载插件；该版本需保留内置工具或等待宿主开放插件加载。
- 第三方许可证：SteamKit2（LGPL-2.1）、QRCoder（MIT）、protobuf-net（Apache-2.0）、
  ZstdSharp（MIT）、System.IO.Hashing（MIT）、System.Drawing.Common（MIT）。
