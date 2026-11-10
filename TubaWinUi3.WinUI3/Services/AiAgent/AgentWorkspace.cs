using System.Text.Json;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TubaWinUi3.Services.AiAgent;

public static class AgentWorkspace
{
    public static string DirectoryPath => Path.Combine(AiComponentService.Root, "Workspace");
    public static string PackagesDirectory => Path.Combine(DirectoryPath, "artifacts");
    public static void Prepare()
    {
        Directory.CreateDirectory(DirectoryPath);
        Directory.CreateDirectory(PackagesDirectory);
        // 正常开发走 NuGet 包 TubaWinUi3.PluginSdk；保留宿主同版本 DLL 作为离线或未发布版本的兜底。
        var sdk = Path.Combine(DirectoryPath, "sdk");
        Directory.CreateDirectory(sdk);
        File.Copy(typeof(PluginSdk.ITubaPlugin).Assembly.Location, Path.Combine(sdk, "TubaWinUi3.PluginSdk.dll"), true);
        var skill = Path.Combine(DirectoryPath, ".opencode", "skills", "tuba-plugin");
        Directory.CreateDirectory(skill);
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "AiAgent");
        File.Copy(Path.Combine(assets, "tuba-plugin", "SKILL.md"), Path.Combine(skill, "SKILL.md"), true);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Metadata", "插件开发文档.md"), Path.Combine(skill, "plugin-api.md"), true);
        var toolbox = Path.Combine(DirectoryPath, ".opencode", "skills", "tuba-toolbox");
        CopySkill(Path.Combine(AppContext.BaseDirectory, "Assets", "AgentSkills", "tuba-toolbox"), toolbox);
        foreach (var obsolete in new[] { "scripts/toolbox.mjs", "references/cli-tools.md" })
            File.Delete(Path.Combine(toolbox, obsolete));
        var computer = Path.Combine(DirectoryPath, ".opencode", "skills", "tuba-computer-use");
        CopySkill(Path.Combine(AppContext.BaseDirectory, "Assets", "AgentSkills", "tuba-computer-use"), computer);
        var canvasSkill = Path.Combine(DirectoryPath, ".opencode", "skills", "tuba-canvas");
        CopySkill(Path.Combine(assets, "tuba-canvas"), canvasSkill);
        File.WriteAllText(Path.Combine(toolbox, "toolbox.json"), JsonSerializer.Serialize(new
        { executable = Path.Combine(AppContext.BaseDirectory, "TubaWinUi3.exe") }));
        File.WriteAllText(Path.Combine(DirectoryPath, "AGENTS.md"), """
            你是图吧 AI 助手，通过图吧工具箱的原生界面为用户解决问题。按当前任务自动加载匹配的技能，不要要求用户先选择模式。
            使用用户的语言，解释实际进展，不在普通产品流程中介绍实现引擎、内部命令或框架。
            任务涉及创建、修复、发布或更新图吧工具箱插件（含上传 NuGet）时，自动加载 tuba-plugin 技能，并阅读其 plugin-api.md。当前只支持 API v2，不兼容旧接口。
            内置工具入口资源珍贵，不推荐创建多个简单工具。默认一个工具用自定义 XAML 整合多个相关功能，不把每个小按钮分别注册为内置工具。
            当前目录是专用插件开发工作区。插件 SDK 优先从 NuGet 包 TubaWinUi3.PluginSdk 还原（PackageReference，PrivateAssets=all + ExcludeAssets=runtime）；仅当 restore 因离线或版本未发布失败时，才回退引用宿主维护的 sdk/TubaWinUi3.PluginSdk.dll，不要修改 sdk/ 或使用来源不明的 DLL；开发仅修改此目录内的项目。
            支持同时开发多个插件，先阅读 projects.md 了解已有项目。每个插件使用独立项目目录、PackageId 和工具 ID。
            新增另一个插件时创建新项目，保留已有插件；修改已有插件时选择对应项目，保持其 PackageId 和工具 ID 并递增版本。
            需求无法确定属于哪个项目时用 question 卡片询问，不擅自覆盖其他项目。不要使用临时反射项目作为插件入口。
            收到询问卡片回答后，先简短说明下一步，再调用实际需要的工具推进任务，不要只反复规划。
            仅在开发插件时：写代码后运行 dotnet pack，完成编译验证，输出 .nupkg 到 artifacts/。
            用户没装 .NET 10 SDK 时先说明原因，再用 winget install --id Microsoft.DotNet.SDK.10 -e --source winget --accept-package-agreements --accept-source-agreements 安装，最后用 dotnet --list-sdks 验证；不要从第三方站点下载安装包。
            未验证构建成功时不要声称插件已完成。编译失败请修复，不要用空包或占位代码交付。
            不要自行复制 DLL 到宿主或修改 installed.json。成功执行 dotnet pack 并结束回复后，宿主会自动校验和动态加载本轮生成的插件包；不需要用户再点击按钮。
            插件测试通过后主动建议发布到 NuGet；真正上传前确认用户同意公开，不代管上传凭据、不上传源码或执行与任务无关的系统变更。
            电脑诊断任务自动加载 tuba-toolbox 和 tuba-computer-use 技能。只有硬件信息保留直接查询；工具启动使用 shell（.opencode/skills/tuba-toolbox/scripts/launch-tool.ps1 或直接执行 Tools 下的程序），打开后的监控、性能平台与修复界面再用 computer-use（优先 AX 语义树，像素/拖动再用 GUI）。禁止用 GUI 双击工具箱卡片启动工具，也禁止调用旧业务 API 或猜测隐藏接口。
            电脑操作前必须出现置顶控制提示，用户停止后不得自行继续控制。查看温度/功耗优先用 HWMonitor，先用 AX observe 读取，失败再 GUI 截图；HWiNFO 适配较差，必要时用 GUI 截图。不编造硬件、监控或跑分结果。
            教学模式：用户消息以〔教学模式 · 画布 canvas/<会话>/index.html〕开头时，说明本轮是教学任务。只有确实需要可视化讲解（概念演示、可操作示例、结构图）时才把内容写成该 HTML 文件——它会作为一条「画布消息」出现在对话里；普通问答、进度汇报、纯文字能说清的结论不要碰画布文件。写的时候：只改这一个文件，图片放同目录 assets/ 并用相对路径引用；完全离线，不允许 CDN/外链；文档必须以 </html> 结尾；不要让页面用 100vh 或垂直居中撑满整屏。已有画布优先用 edit 局部更新。页面通过 window.tuba 把按钮点击发回对话。讲完只用一两句话说明变化，不要在回复里重复 HTML 源码。
            """);
        File.WriteAllText(Path.Combine(DirectoryPath, "host-tools.md"), """
            # 硬件信息与电脑操作

            宿主程序路径（JSON 字符串）：
            """ + JsonSerializer.Serialize(Path.Combine(AppContext.BaseDirectory, "TubaWinUi3.exe")) + """

            唯一保留的业务查询：宿主.exe --agent-cli hardware --output <工作区内绝对路径.json>
            可直接运行 .opencode/skills/tuba-toolbox/scripts/hardware.ps1；它自行读取相邻 toolbox.json，不要求 Node.js。
            JSON 文件内 ok=false 或退出码非零表示失败；报告真实错误，不编造测量结果。
            旧 sensors/disks/cpuz/tools/run 已移除。工具启动用 shell（.opencode/skills/tuba-toolbox/scripts/launch-tool.ps1 或直接执行 Tools 下的程序），打开后的界面用已连接的 computer_use MCP（AX / GUI）操作：先用 AX 查看窗口与控件树，需要像素或拖动时再用 GUI。
            工具清单：.opencode/skills/tuba-toolbox/references/gui-tools.md。控制提示未出现或用户点击停止时，不得控制电脑。
            不需要复制宿主，不改变单实例规则，不使用 --takeover，不退出用户正在运行的工具箱。
            """);
        // 用户要求默认全部权限；会话可在输入区切换为逐次确认。
        File.WriteAllText(Path.Combine(DirectoryPath, "opencode.json"), JsonSerializer.Serialize(new
        {
            autoupdate = false,
            mcp = new { servers = ComputerServers() },
            providers = AgentProviderStore.Configuration(AgentProviderStore.Read()),
            agents = new Dictionary<string, object>
            {
                ["tuba"] = new
                {
                    description = "图吧 AI 助手：系统诊断与工具箱插件开发",
                    mode = "primary",
                    system = AgentPersona.SystemPrompt
                },
                // 旧会话仍引用 tuba-maintenance；保留同名别名并指向统一提示词，避免历史会话无法继续。
                ["tuba-maintenance"] = new { description = "旧会话兼容入口", mode = "primary", system = AgentPersona.SystemPrompt }
            },
            permissions = AgentPermissionPolicy.Rules(AgentPermissionMode.FullAccess)
        }));
        RefreshProjectIndex();
    }

    internal static Dictionary<string, object> ComputerServers() => new()
    {
        ["computer_use"] = new
        {
            type = "local", codemode = false,
            command = new[] { HostExecutable, ComputerUseMcpProxy.Argument },
            environment = new Dictionary<string, string>
            {
                [ComputerUseMcpProxy.ControlRootVariable] = ComputerControlService.Root,
                [ComputerUseMcpProxy.SessionVariable] = ComputerUseMcpProxy.DefaultSession,
                [ComputerUseMcpProxy.ExecutableVariable] = HybridComputerUseService.Executable
            }
        }
    };

    private static string HostExecutable => Path.Combine(AppContext.BaseDirectory, "TubaWinUi3.exe");

    public static void RefreshProjectIndex()
    {
        Directory.CreateDirectory(DirectoryPath);
        var text = new StringBuilder("# 已有插件项目\n\n宿主按实际 csproj 与 tubatools.json 更新此索引。支持多个项目并存；新插件使用新目录和独立 PackageId，修改已有插件保持身份并递增版本。不要覆盖其他项目。\n\n");
        foreach (var project in FindProjects(DirectoryPath))
            text.AppendLine($"- 项目：`{project.Path}`；包 ID：`{project.PackageId}`；版本：`{project.Version}`");
        File.WriteAllText(Path.Combine(DirectoryPath, "projects.md"), text.ToString());
    }

    internal sealed record PluginProject(string Path, string PackageId, string Version);
    internal static IReadOnlyList<PluginProject> FindProjects(string root)
    {
        var projects = new List<PluginProject>();
        if (!Directory.Exists(root)) return projects;
        var ignored = new HashSet<string>(["bin", "obj", "sdk", "artifacts", ".opencode", ".git"], StringComparer.OrdinalIgnoreCase);
        void Scan(string directory, int depth)
        {
            if (depth > 5 || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            if (File.Exists(Path.Combine(directory, "tubatools.json")))
                foreach (var file in Directory.EnumerateFiles(directory, "*.csproj"))
                {
                    try
                    {
                        using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                        var xml = XDocument.Load(reader);
                        string Value(string name) => xml.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Replace('`', ' ').Replace('\n', ' ').Replace('\r', ' ') ?? "";
                        projects.Add(new(Path.GetRelativePath(root, file).Replace('\\', '/'), Value("PackageId"), Value("Version")));
                    }
                    catch (Exception ex) when (ex is IOException or XmlException) { /* An in-progress edit must not block the agent. */ }
                }
            foreach (var child in Directory.EnumerateDirectories(directory))
                if (!ignored.Contains(Path.GetFileName(child))) Scan(child, depth + 1);
        }
        Scan(root, 0);
        return projects.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void CopySkill(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var folder in Directory.GetDirectories(source)) CopySkill(folder, Path.Combine(destination, Path.GetFileName(folder)));
    }

    public static IReadOnlyList<string> GetPackages() => Directory.Exists(PackagesDirectory)
        ? Directory.GetFiles(PackagesDirectory, "*.nupkg").OrderByDescending(File.GetLastWriteTimeUtc).ToList() : [];
}
