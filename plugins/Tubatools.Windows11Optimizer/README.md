# Windows 11 优化插件（Tubatools.Windows11Optimizer）

图吧工具箱插件（API v2）。一个入口里集中提供 Windows 10/11 的常用外观、隐私、性能、更新、电源、通知与预装应用管理，功能参考 Winhance / UnattendedWinstall 公开的注册表项独立实现。

## 功能分区

| 分区 | 内容 |
| --- | --- |
| 概览 | 常用快捷开关、推荐优化、状态概览、注册表备份入口 |
| 资源管理器 | 去快捷方式箭头、去 UAC 盾牌、文件扩展名、隐藏文件、任务栏对齐/秒/小组件/搜索样式、开始菜单推荐、深色模式与透明 |
| 隐私与安全 | 遥测、广告 ID、活动历史、定位/摄像头/麦克风、语音激活、输入个性化、剪贴板同步、搜索亮点、网络搜索、传递优化、自动播放、UAC 级别 |
| 性能与游戏 | 游戏模式、Game DVR、HAGS、鼠标加速、视觉效果、窗口/任务栏动画、菜单延迟、后台应用、Power Throttling、优先级、Prefetch、SysMain |
| Windows 更新 | 驱动更新、其他 Microsoft 产品、重启提醒、登录时自动重启 |
| 电源与声音 | 电源计划、快速启动、休眠、USB 选择性暂停、启动声音、通话音量闪避 |
| 通知与建议 | 全局通知、通知声音、锁屏通知、系统建议与广告、静默安装推荐应用、欢迎体验、锁屏聚焦 |
| 应用管理 | 预装商店应用卸载（含系统预置副本）、winget 常用软件安装 |
| 高级工具 | 重启资源管理器、清理图标缓存、重置文件夹视图、暂停/恢复更新、打开系统页面、注册表备份/恢复 |

## 安全与回滚

- 每个开关都有明确的 Windows 默认值，关闭开关即可恢复。
- 「应用推荐优化」「恢复全部默认」和恢复备份前会自动写入 `<数据目录>/backups/optimizer-*.json`。
- 注册表读写固定使用 64 位视图（避免 x86 工具箱被重定向到 Wow6432Node）。
- 高危项目（彻底删除 IsShortcut、关闭超级隐藏文件、禁用 SysMain、修改 UAC、休眠等）会先弹出确认。
- 预装应用卸载只允许清单内的包名，脚本通过 `-EncodedCommand` 执行，不拼接任意用户输入。

## 构建与打包

```powershell
dotnet build plugins/Tubatools.Windows11Optimizer/Tubatools.Windows11Optimizer.csproj -c Release
dotnet pack  plugins/Tubatools.Windows11Optimizer/Tubatools.Windows11Optimizer.csproj -c Release -o artifacts/plugins
```

产物：`artifacts/plugins/Tubatools.Windows11Optimizer.<Version>.nupkg`

包内布局：

```
tubatools.json
assets/icon.svg
plugin/Tubatools.Windows11Optimizer.dll
plugin/Tubatools.Windows11Optimizer.deps.json
```

## 说明

- 插件与工具箱同进程、同权限运行。系统级（HKLM）设置需要管理员；请在便携版或提权会话中使用。
- 去箭头/去盾牌、任务栏等外壳设置需要重启资源管理器（概览或高级工具里有一键重启）。
- UAC、HAGS、电源计划等部分设置需要重启或重新登录后完全生效。
- winget 安装需要系统已安装「应用安装程序」并联网。
- Winhance 采用 PolyForm Shield 许可，本插件未复制其源代码，只实现相同的公开注册表调整。
