# TubaWinUi3.PluginSdk

图吧工具箱（TubaWinUi3）插件契约（API v2）。插件项目引用本包后实现 `ITubaPlugin` 并通过 `IPluginHost.RegisterTool` 注册工具；注册的工具会出现在工具箱的「内置工具」中。

```xml
<PackageReference Include="TubaWinUi3.PluginSdk" Version="2.1.0" PrivateAssets="all" ExcludeAssets="runtime" />
```

- 目标框架：`net10.0`，无 WinUI 依赖，普通 XAML 插件可直接编译。
- `PrivateAssets="all"` + `ExcludeAssets="runtime"`：SDK 只参与编译，不复制 SDK 程序集、不打进插件包；运行时由宿主提供。
- 开发文档：仓库根目录 `插件开发文档.md`，在线版 <https://tubawinui3.cn/dev/plugin>。
- 许可证：GPL-3.0。
