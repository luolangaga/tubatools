# 经典样式（Classic Theme）

给图吧工具箱主界面套上经典蓝色渐变：深蓝侧栏、浅蓝内容区、半透明白色卡片与白色文字。

## 特点

- 标题栏、侧栏、内容区统一蓝色渐变
- 卡片、控件、菜单、对话框同步半透明白/蓝色调
- 白字与白色图标，保持原有布局不变
- 安装后自动生效；插件页可随时开关
- 停用/卸载插件会恢复默认外观

## 构建与打包

```powershell
dotnet build plugins/Tubatools.ClassicTheme/Tubatools.ClassicTheme.csproj -c Release
dotnet pack plugins/Tubatools.ClassicTheme/Tubatools.ClassicTheme.csproj -c Release -o artifacts/plugins
```

## 安装

安装生成的 `Tubatools.ClassicTheme.1.0.2.nupkg`（插件页的 NuGet 搜索或本地安装入口）。插件声明了 `fullTrust`，只应安装可信来源的包。

发布到 NuGet：

```powershell
dotnet nuget push artifacts/plugins/Tubatools.ClassicTheme.1.0.2.nupkg `
  --api-key <YOUR_KEY> --source https://api.nuget.org/v3/index.json
```
