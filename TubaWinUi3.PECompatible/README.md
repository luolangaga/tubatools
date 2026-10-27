# 图吧工具箱 PE 兼容版

独立的 MewUI 工具箱前端，使用 Win32 平台宿主、GDI 图形后端和 GDI 文本引擎；目录扫描、工具元数据、架构变体、图标缓存、收藏/设置存储和 WMI 硬件信息沿用现有兼容版服务。

## 构建

需要 .NET 10 SDK。项目面向 Windows 10 19041 或更高版本，发布为自包含单文件，因此不需要目标系统预装 .NET；自包含发布仍依赖 PE 镜像提供所需的 Windows API。

```powershell
dotnet build TubaWinUi3.PECompatible/TubaWinUi3.PECompatible.csproj
dotnet publish TubaWinUi3.PECompatible/TubaWinUi3.PECompatible.csproj -c Release -r win-x64
```

其他架构可将 RID 替换为 `win-x86` 或 `win-arm64`。项目启用 NativeAOT，发布为自包含单文件（x64 约 16 MB）。

注意 WMI：`System.Management` 依赖托管 COM interop，full AOT 下 ILC 会直接判定为不可用（IL3052，运行时必抛异常），
因此硬件信息改由 `Services/WmiProvider.cs` 直接调用 WMI 的 COM vtable 实现（`IWbemLocator` → `IWbemServices` →
`IEnumWbemClassObject` → `IWbemClassObject`，unmanaged 函数指针 + `CoSetProxyBlanket`）。
`TubaWinUi3.Compatible` 里的同名类是 `System.Management` 实现，两者对 `HardwareInfoService` 暴露相同的 `WmiRow` 契约。

Debug 构建完成后会自动把产物复制到主程序 Debug 输出目录下的 `图吧工具箱PE兼容版/` 子目录（如 `TubaWinUi3.WinUI3/bin/Debug/net10.0-windows10.0.26100.0/win-x64/图吧工具箱PE兼容版/`），便于直接以完整 `Tools/`、`Metadata/` 运行调试；主程序尚未构建出 Debug 产物时跳过复制。

## 便携目录

将 `图吧工具箱PE兼容版.exe` 与原便携包的 `src/` 目录放在同一目录。应用会自动查找 `src/Tools/` 和 `src/Metadata/`；也支持直接放置 `Tools/` 与 `Metadata/`。如果 PE 镜像未包含 WMI 组件，硬件页面可能只能显示可用的部分信息，工具目录和启动功能不依赖 WMI。

包含的兼容版功能：全部工具/分类浏览、名称/路径/标签搜索、工具启动、架构变体、打开工具目录、复制路径、收藏、深浅主题、硬件信息刷新与截图复制。

MewUI 官方支持 Windows 10+，但没有声明对 WinPE 的专项支持，本仓库也无法在实际 PE 镜像中验证。GDI 后端仍依赖 `gdiplus.dll` 和 Win32 桌面 API；需在目标 PE 镜像与目标架构上实际验证这些组件可用。部分 PE 镜像也未启用 WMI，硬件信息会相应缺失；工具扫描和启动不依赖 WMI。
