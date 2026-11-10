# 贡献指南（Contributing）

感谢你愿意为 **图吧工具箱CE TubaWinUi3** 贡献！无论是提交 Issue、改进代码还是补充文档，我们都欢迎。

- **新增工具请以插件形式提供**：本项目不接受直接向主程序提交的新工具或第三方工具安装包；需要新工具时，请开发插件（安装后出现在「内置工具」中，可发布到 NuGet 供他人安装），开发与打包方法见[插件开发文档](插件开发文档.md)（[在线版](https://tubawinui3.cn/dev/plugin)）
- 遇到问题请先阅读 [Issue 模板](.github/ISSUE_TEMPLATE/bug_report.yml) 与 [SECURITY.md](SECURITY.md)
- 想直接贡献代码？继续阅读本文档

---

## 仓库结构

| 目录 | 说明 |
|---|---|
| `TubaWinUi3.WinUI3/` | 主程序（WinUI 3 / .NET 10），绝大多数开发都在这里 |
| `TubaWinUi3.Tui/` | .NET 8 控制台 TUI 兼容版（Native AOT） |
| `TubaWinUi3.Tests/` | xUnit 单元测试 |
| `src/docs/` | VitePress 文档站点源码 |
| `website-winui3/` | 新官网（Vue 3 + WinUIonWeb） |
| `Tools/` | 第三方诊断工具目录（打包进安装包） |

## 开发环境

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Visual Studio 2022 17.14+](https://visualstudio.microsoft.com/)（含 WinUI 3 工作负载）或 VS Code + C# Dev Kit
- 最低支持 Windows 10 2004（Build 19041，与 `TargetPlatformMinVersion` 及安装器门禁一致）；支持 x86 / x64 / ARM64
- **注意**：应用启动时会自动请求管理员权限（`runas`），部分功能（内核监控、ETW 追踪、WMI）需要管理员

## 构建与测试

```bash
dotnet build        # Debug 编译（自动检测当前架构）
dotnet run          # 运行（Unpackaged 模式）
dotnet test         # 运行全部测试
dotnet test --filter "FullyQualifiedName~ToolCatalogTests"   # 只跑某个测试类
```

发布时 `dotnet publish` 会自动生成图标缓存（`build-icon-cache.ps1`），`TubaWinUi3.pri` 需从发布目录复制到输出（CI 已处理，本地打包注意）。

## 代码规范

请遵循既有代码风格（多数规则也记录在 [AGENTS.md](AGENTS.md)）：

- 命名空间：`TubaWinUi3` / `.Pages` / `.Services` / `.Models`；类型、成员使用 PascalCase；XAML 与 code-behind 成对命名
- 所有服务为静态类、无 DI，从页面直接调用（唯一例外：`LiteMonitorService` 为单例 `Instance`）
- 内置工具开发（仅限维护者规划的改动）：在 `TubaWinUi3.WinUI3/Services/BuiltinTools/` 下新建类实现 `IBuiltinTool`，在 `BuiltinToolRegistry.RegisterDefaults()` 注册（**重复 ID 会抛异常**），详情见[内置工具开发文档](https://tubawinui3.cn/dev/builtin-tools.html)
- 对话框请通过 `context.CreateDialog(...)` 创建，使其跟随应用主题
- 界面文案走本地化系统（WinUI3Localizer）：资源在 `TubaWinUi3.WinUI3/Strings/zh-CN|en-US/Resources.resw`，XAML 用 `l:Uids.Uid="Key"`，代码用 `LocalizationService.L("Key", "中文回退")`；新增或修改文案时中英文需一起加，键集一致性由 `LocalizationTests` 校验
- 页面若用代码赋值文本并需要随语言切换刷新，需声明实现 `ILocalizablePage`；尚未迁移的页面可直接写中文（缺键会回退到 fallback），但新代码请走资源键；工具名称/描述的中英文覆盖层是 `Metadata/Tools_<lang>.json`

## 提交规范

提交信息使用 Conventional Commits 前缀，正文可用中文：

```
feat: 新增xxx功能
fix: 修复xxx问题
docs: 更新文档
refactor: 重构xxx
test: 补充测试
chore: 构建/工具链改动
```

**禁止提交**：`bin/`、`obj/`、`.pfx`、`.cer`（已在 .gitignore 中）。

## 提交流程（Pull Request）

1. **Fork** 本仓库（`luolangaga/tubatool`）到你的账号
2. 创建功能分支：`git checkout -b feat/your-feature`
3. 修改代码，本地通过 `dotnet build` 与 `dotnet test`
4. 提交并推送到你的 Fork，然后向主仓库发起 Pull Request
5. 在 PR 描述中说明改动目的、影响范围与测试情况（见 [pull_request_template.md](.github/pull_request_template.md)）

## Issue 规范

- 提交 Bug 前请先搜索是否已存在相同问题
- Bug 报告请尽量包含：系统版本 / CPU / GPU / 驱动版本 / 图吧工具箱CE版本 / 复现步骤 / 截图或日志（参考 [bug_report 模板](.github/ISSUE_TEMPLATE/bug_report.yml)）
- 安全漏洞**请勿在 Issue 公开**，按 [SECURITY.md](SECURITY.md) 私密上报

## 许可证

本项目采用 **GPL-3.0** 开源协议（详见 [LICENSE](LICENSE)）。你的贡献将基于 GPL-3.0 授权，提交即表示你同意你的代码以此协议发布。
