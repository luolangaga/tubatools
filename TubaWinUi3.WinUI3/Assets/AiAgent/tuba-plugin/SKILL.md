---
name: tuba-plugin
description: 为图吧工具箱开发 API v2 插件：从需求、XAML 页面、打包、本机验证到发布到 NuGet 的陪跑向导；统一教网页上传，测试通过后主动鼓励发布并教别人如何安装。面向新手时用大白话解释；真正上传前确认用户同意公开。
---

# 图吧工具箱插件开发（API v2，陪跑版）

先读同目录 `plugin-api.md`（当前 API v2 契约）。当前工作区的 `sdk/TubaWinUi3.PluginSdk.dll` 是真实 SDK；不要修改 `sdk/`，不要从网上找猜测的旧 SDK 包。API v1 已移除，`RegisterTool` 的旧多参数回调不存在。

工作方式：用户可能完全不懂编程。每一步都先用一句人话说明“要做什么、为什么”，再执行；做完告诉用户“会看到什么”，等反馈，不要一口气丢十条命令。**没有实际构建/安装/验证前，不要声称插件已经可用。**

发布细节以 `plugin-api.md` 的「发布到 NuGet（小白版，统一网页上传）」为准；本技能第 4 节是给用户的陪跑流程。

## 0. 先问清楚，再动手

一次问完，不要挤牙膏：

1. 插件要在工具箱里**做什么**？最好一句话说清。
2. 插件**叫什么**？中文显示名 + 英文 `PackageId`（如 `Tubatools.MyPlugin`，发布后不要改）。
3. **作者署名**写什么？
4. 是否已经在本机测试通过？是否准备发布到 NuGet 给别人用？

同时记住两条产品规则：

- **一个插件默认只做一个有意义的工具入口**，把相关按钮、设置、结果整合在一个页面里。不要为“清缓存/刷 DNS/切电源”各注册一个工具；只有用户明确要求独立入口，或功能属于完全独立领域时才拆分。
- 先读 `projects.md`，确认是不是要新建项目。支持多个插件并存，但每个插件用独立目录、独立 `PackageId` 和独立工具 ID；修改已有插件时保持身份，递增版本，不要覆盖别的项目。

**发布到 NuGet 是公开行为**：包里的 DLL、XAML、图片、README 都能被任何人下载；插件会以工具箱的权限运行。测试通过后要主动鼓励用户发布，让更多人能用；真正点上传前确认用户同意公开，并检查包里没有密钥、密码和私人路径。先看第 4 节“发布到 NuGet”。

## 1. 最小插件骨架

入口公开、无参构造，实现 `ITubaPlugin`；注册只在同步 `Register` 中进行，不要在注册阶段联网、创建窗口或执行系统操作：

```csharp
using TubaWinUi3.PluginSdk;

public sealed class Entry : ITubaPlugin
{
    public void Register(IPluginHost host) => host.RegisterTool(new PluginToolDefinition
    {
        Id = "maintenance",
        Name = "电脑维护",
        Category = "系统优化",
        Description = "清理、健康检查和网络维护集中在一个页面",
        IconPath = "assets/icon.svg",
        Tags = ["系统", "维护"],
        Xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Spacing="12">
              <TextBox x:Name="Note" Header="备注" />
              <Button x:Name="Save" Content="保存" />
              <TextBlock x:Name="Result" TextWrapping="Wrap" />
            </StackPanel>
            """,
        InitializeAsync = async view =>
        {
            view.SetValue("Note", await view.Services.GetSettingAsync("note", view.CancellationToken) ?? "");
            view.BindClick("Save", async context =>
            {
                await context.Services.SetSettingAsync("note", view.GetValue("Note"), context.CancellationToken);
                view.SetValue("Result", "已保存");
            });
        }
    });
}
```

要点：

- XAML 必须有一个 `FrameworkElement` 根和 WinUI 默认 xmlns。运行时 XAML **不支持** `x:Class`、`x:Bind`、`Click="方法名"`；用 `InitializeAsync` + `BindClick`，或在 `CreateContent` 里用 C# 构建界面。
- 交互控件加 `x:Name` 和 `AutomationProperties.Name/AutomationId`。按钮用 `BindClick`；开关、输入、下拉、数值用 `BindValueChanged`。**ToggleSwitch 不是 ButtonBase**，不要强转。
- `GetValue/SetValue` 读写文本、开关、数字和下拉；布尔写 `true/false`，数字用 invariant culture，下拉优先把稳定值放 `ComboBoxItem.Tag`。
- 先读真实状态再 `SetValue` 初始化；初始化和 `SetValue` 不触发宿主回调。不要在初始化/刷新状态时写系统设置。
- 异步按钮用本次 `context.CancellationToken`；页面关闭要停计时器、退订事件，必要时用 `view.OnClosed(...)`。
- `context.ReportProgress("")` 会关闭进度条；`ReportProgress("文本")` 显示当前步骤。
- 修改系统、删除文件、重置网络、停服务、改启动项/电源前，用 `context.ConfirmAsync` 说明影响，不能因为用户说“优化”就默认授权。
- 多用 `view.Services.GetService<T>()` 的公开 SDK 服务；**不要通过反射去抓宿主内部静态类**。未知服务返回 null，要能降级。
- `CreateContent` 只给确实需要引用 WinUI 类型的高级插件；普通插件优先 SDK + XAML 字符串，避免把 WinUI/宿主 DLL 带进包。
- 包内图片/SVG 不要假设 `ms-appx:///` 指向插件包；用 `SetImage` 或 `Services.GetAssetPath` 拿真实路径。
- `fullTrust` 只在确实要操作宿主界面、主题、任意宿主设置时打开；它不是安全沙箱，只给可信插件，并且宿主升级后可能需要重新编译。启动型插件用 `IPluginStartup`，宿主会在主窗口加载后调用；停用/卸载时在 `ShutdownAsync` 还原。

## 2. 图标、清单和项目

每个工具要有真实名称、描述、Tags、包内图标（建议 128×128 SVG；也支持 PNG/JPEG/BMP），并填写清单：

```json
{
  "apiVersion": 2,
  "assembly": "plugin/MyPlugin.dll",
  "entryType": "MyPlugin.Entry",
  "name": "电脑维护套件",
  "description": "集中组织清理、健康检查和网络维护",
  "author": "实际作者",
  "website": "https://example.com/project",
  "icon": "assets/icon.svg"
}
```

普通插件的最小 csproj：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <PackageId>Tubatools.MyPlugin</PackageId>
    <Version>1.0.0</Version>
    <Authors>实际作者</Authors>
    <Description>一句话说明插件做什么</Description>
    <PackageTags>Tubatools</PackageTags>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="TubaWinUi3.PluginSdk">
      <HintPath>../sdk/TubaWinUi3.PluginSdk.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <None Include="tubatools.json" Pack="true" PackagePath="/" />
    <None Include="assets/**/*" Pack="true" PackagePath="%(Identity)" />
    <None Include="$(OutputPath)$(AssemblyName).dll" Pack="true" PackagePath="plugin/" />
    <None Include="$(OutputPath)$(AssemblyName).deps.json" Pack="true" PackagePath="plugin/" />
  </ItemGroup>
</Project>
```

如果用了 WinUI 类型（`CreateContent` 或 `.xaml` 编译），通常改用 `net10.0-windows10.0.26100.0` 并引用正确版本的 Windows App SDK，但**只编译引用**，运行时由宿主提供；不要把 `Microsoft.*`、`WinRT.Runtime.dll`、宿主 DLL 或 SDK DLL 打进包。第三方依赖放进 `plugin/`，宿主不会帮你还原 NuGet 依赖。

打包：

```powershell
# AI 工作区：输出到 artifacts/ 顶层，宿主自动加载只扫描这一层
dotnet pack <项目.csproj> -c Release -o artifacts/
```

仓库里的示例可以用 `artifacts/plugins/`，但 AI 工作区不要用子目录，否则自动加载看不到。上传时按实际生成的 `.nupkg` 路径操作。

检查退出码为 0，并确认 `.nupkg` 里有：

- 根目录 `tubatools.json`（`apiVersion` 必须是 2）
- `plugin/<入口>.dll` 和 `plugin/<入口>.deps.json`
- 你声明的 `assets/` 图标和 XAML
- 没有 SDK、宿主、WinUI、.NET 框架 DLL

每次改动都递增 `<Version>`；保持 `PackageId` 和工具 ID 稳定，这样用户的收藏、配置和更新关系不会断。

## 3. 本机验证（不能跳过）

1. 在工作区/项目目录构建并 `dotnet pack`；失败就修，不要用空包或占位代码交付。
2. 安装/加载到本机工具箱：内置工具 → 插件 → 安装或启用，或由 AI 工作区自动加载本轮新包。
3. 实际打开插件页，逐项确认：页面能开、控件能点、输入能读、设置能保存、耗时操作能取消、关闭再开不报错。
4. 重启工具箱再确认一次；启动型插件要确认启动后自动生效，停用/卸载后恢复默认。列表/页面可能异步重建，启动时机不对会表现为“启动不生效、停用再启用才行”，不要只测一次就下结论。
5. 看宿主日志和插件错误提示；失败按实际错误修，不要声称已加载。

## 4. 发布到 NuGet（统一用网页版，鼓励发布）

插件在本机测试通过后，**默认下一步就是发布到 NuGet**：这样别人不用拿到你的电脑，也不用命令行，在图吧工具箱的「内置工具 → 插件」里搜名字就能安装。发布不需要服务器、不需要 API Key，网页点几下就能完成。**不要做完就停在本地**，先带用户走完发布，再交付。

### 4.1 先鼓励，再确认公开

测试通过后，主动这样说：

> 插件已经在你电脑上测试通过了。建议直接发布到 NuGet，让更多用图吧工具箱的人能搜到、安装。整个过程在网页上完成，不用命令行；包里的代码、图片和说明会公开，我会先帮你检查没有密钥和私人文件。要现在发布吗？

- 用户同意后再上传；用户明确不想公开，就尊重选择，把本地 `.nupkg` 和源码路径交付，并告诉他以后想发布随时可以。
- 不要把“上传”说得很复杂；也不要跳过测试直接发布。
- **统一用网页版上传**：不要教命令行上传、不要创建 API Key、不要让用户把 Key 发给 AI。网页上传更简单，也不需要保管密钥。
- 上传前必须确认：插件已实测通过；`PackageId` 唯一且以后不改；`PackageTags` **包含 `Tubatools`**；版本是稳定版；包内没有密钥、密码、私人路径。
- 用户接受“公开可下载、没有人工审核、以工具箱权限运行”的事实；这是发布前要讲清楚的，不是劝退。

### 4.2 网页上传七步

1. **注册/登录 nuget.org**：打开 <https://www.nuget.org/>，用 Microsoft/GitHub 账号登录，完成邮箱验证。
2. **检查 PackageId**：打开 `https://www.nuget.org/packages/<你的PackageId>`；被占用就换一个唯一 ID（例如 `Tubatools.你的昵称.插件名`）。发布后不要改。
3. **填好包信息**：至少 `PackageId`、`Version`、`Authors`、`Description`、`PackageTags=Tubatools`。建议再加 `PackageReadmeFile`、`PackageLicenseExpression` 和 `PackageIcon`；README 用中文写“做什么、怎么用、需要什么权限、怎么卸载”，图标用 128×128 PNG。不想加 README/图标就删掉对应属性；指向的文件必须真实存在，否则打包会报错。
4. **打包**：`dotnet pack <项目.csproj> -c Release -o artifacts/`；确认生成的 `.nupkg` 内容和 4.1 的限制。AI 工作区用 `artifacts/` 顶层；仓库示例可以用 `artifacts/plugins/`。
5. **网页上传**：打开 <https://www.nuget.org/packages/manage/upload>，登录后选择 `.nupkg`；核对 PackageId/版本；按页面提示提交。工具箱的「上传到 NuGet」按钮也只是打开这个页面，不会替你上传。
6. **等待索引**：上传成功不等于立刻能搜到，通常等几分钟到十几分钟；打开 `https://www.nuget.org/packages/<PackageId>` 确认页面和版本。
7. **告诉别人怎么装**：把包页面链接或 PackageId 发给对方；对方在工具箱里打开「内置工具 → 插件」，搜索包名/ID，点安装并确认信任提示。

### 4.3 更新版本

- 改代码 → 递增 `<Version>`（同一版本不能传第二次）→ `dotnet pack` → 网页上传同一个 PackageId。
- 不要改 PackageId；它是插件身份，也是用户收藏、配置和更新关系的依据。
- NuGet 页面显示新版本后，用户在插件页点“更新到 x.y.z”；通常重启工具箱后生效。

### 4.4 常见错误（网页版）

| 现象 | 原因/处理 |
| --- | --- |
| 登录/上传页报错 | 账号未验证、登录过期或没有该 PackageId 的权限；重新登录并完成邮箱验证 |
| `409 Conflict` | 这个版本已经上传过；递增 `<Version>`，不要复用 |
| `400 Bad Request` | 元信息不完整（Authors/Description/README/Icon 等）或包格式不对；看 NuGet 页面提示 |
| 图吧工具箱搜不到 | 还没索引完；确认是稳定版、公开包、`PackageTags` 含 `Tubatools`；用 PackageId 精确搜 |
| 安装后插件不显示/不加载 | 检查 `tubatools.json` 是否在包根、`apiVersion` 是否为 2、`assembly`/`entryType` 是否正确、入口 DLL/deps.json 是否在 `plugin/` |
| 包太大/被拒绝 | 宿主限制压缩 100 MB、解压 300 MB、5000 个文件；不要打包 SDK/宿主/WinUI/框架副本 |
| 想下架 | NuGet 页面可以 Unlist（从搜索隐藏，已安装用户仍保留）；不要随意删除，PackageId 是插件身份 |

### 4.5 关于“只想给朋友用”

当前工具箱内置插件页只从 **nuget.org 公开源** 搜索带 `Tubatools` 标签的稳定包，没有私有源/白名单安装入口。只想小范围使用就别上传公开包；可以把源码和 `.nupkg` 直接给朋友，或让对方在开发环境中加载。不要为了“私有”把密钥或包塞进公开仓库。

## 5. 交付时告诉用户什么

- 源码在哪个文件夹；`.nupkg` 在实际输出目录的哪个文件（AI 工作区是 `artifacts/`，仓库示例可能是 `artifacts/plugins/`）。
- 本机测试结果，以及还有哪些没验证。
- 如果发布了：NuGet 包页面链接、PackageId、别人如何安装、如何更新。
- 如果没发布：明确说“没有上传”，不要用“已发布”混淆。
- 以后更新：改代码 → 递增 `<Version>` → `dotnet pack` → 重新上传同一个 PackageId；不要改包 ID。

## 说话方式

好：

> NuGet 就像微软的插件应用商店。你的插件在本机跑通后，我建议直接发布上去；别人在图吧工具箱的插件页搜名字就能装。网页上传几分钟就好，不用命令行。

好：

> 这一步是打包，不是上传。它只会在你电脑的 `artifacts/` 里生成一个文件，不会发到网上。

不好：

> 先传上去再说，测试不用做。

不好：

> 已经上传好了。（实际还没看到 NuGet 页面/成功返回）
