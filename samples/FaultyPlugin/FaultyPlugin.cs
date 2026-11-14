using TubaWinUi3.PluginSdk;

/// <summary>
/// 故障注入测试插件：故意在插件的各个阶段制造错误，用来验证宿主的
/// 安全模式 / 故障隔离是否真的按预期工作。**不要发布到 NuGet。**
///
/// 加载期与启动钩子的故障由环境变量 TUBA_FAULTY_MODE 控制（需要在启动工具箱前设置）：
///   load    → Register 抛异常（插件加载失败，连续 3 次会被自动停用）
///   startup → IPluginStartup.StartupAsync 抛异常（启动钩子失败，同样计入加载失败）
/// 其余故障在插件页面里点按钮触发。
/// </summary>
public sealed class FaultyPlugin : ITubaPlugin, IPluginStartup
{
    private const string ModeVariable = "TUBA_FAULTY_MODE";
    private static string Mode => Environment.GetEnvironmentVariable(ModeVariable)?.Trim().ToLowerInvariant() ?? "";

    public void Register(IPluginHost host)
    {
        if (Mode == "load")
            throw new InvalidOperationException(
                $"[故障注入] TUBA_FAULTY_MODE=load：Register 故意抛异常，用于验证加载失败计数与自动停用。");

        host.RegisterTool(new PluginToolDefinition
        {
            Id = "fault-lab",
            Name = "故障注入测试（请勿发布）",
            Category = "其他工具",
            Description = "故意制造 WinUI 层错误：业务异常、XAML 加载失败、UI 线程卡死、后台线程崩溃",
            IconPath = "assets/icon.svg",
            Glyph = "\uE7BA",
            Tags = ["测试", "故障注入", "安全模式"],
            Xaml = PageXaml,
            InitializeAsync = InitializeAsync
        });

        // 第二个工具：XAML 能通过宿主的静态校验（合法 XML、正确的根命名空间），
        // 但 XamlReader.Load 时才会撞上不存在的属性 —— 真正的 WinUI 层错误。
        host.RegisterTool(new PluginToolDefinition
        {
            Id = "broken-xaml",
            Name = "XAML 加载失败演示",
            Category = "其他工具",
            Description = "点击后由 WinUI 的 XamlReader 抛错，验证工具页的错误呈现",
            Glyph = "\uE783",
            Xaml = """
                <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <TextBlock Text="这个页面永远不会显示：下面这一行在 XAML 解析时就会失败。" />
                  <Button Content="占位" NoSuchWinUiProperty="1" />
                </StackPanel>
                """
        });
    }

    public ValueTask StartupAsync(IPluginStartupContext context, CancellationToken ct = default)
    {
        if (Mode == "startup")
            throw new InvalidOperationException(
                "[故障注入] TUBA_FAULTY_MODE=startup：StartupAsync 故意抛异常，用于验证启动钩子失败的处理。");
        return ValueTask.CompletedTask;
    }

    public ValueTask ShutdownAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    private static Task InitializeAsync(PluginViewContext view)
    {
        view.SetValue("Mode", string.IsNullOrEmpty(Mode) ? "（未设置环境变量）" : Mode);

        // 1) 业务异常：被宿主捕获 → 工具页显示错误条 + 计入「运行报错」，不自动停用。
        view.BindClick("ThrowBusiness", _ =>
            throw new InvalidOperationException("业务异常演示：输入不合法，插件自己抛出的普通异常。"));

        // 2) 异步任务里抛异常（await 之后）：同样被宿主捕获。
        view.BindClick("ThrowAsync", async context =>
        {
            context.ReportProgress("准备在 1 秒后抛异常…");
            await Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken);
            throw new InvalidOperationException("异步异常演示：在 await 之后抛出。");
        });

        // 3) 阻塞 UI 线程 60 秒：用于验证「插件把界面拖死」后，
        //    用户强杀进程 → 下次启动是否弹出安全模式询问。
        view.BindClick("Freeze", _ =>
        {
            view.SetValue("Result", "界面已卡死 60 秒，请尝试拖动窗口或点击其他菜单。");
            Thread.Sleep(TimeSpan.FromSeconds(60));
            view.SetValue("Result", "界面恢复。这 60 秒里整个工具箱都无法响应。");
            return Task.CompletedTask;
        });

        // 4) 后台线程未处理异常：.NET 会直接终止进程（AppDomain.UnhandledException），
        //    这是最接近「真崩溃」的场景，用来验证崩溃后下次启动的安全模式询问。
        view.BindClick("Crash", _ =>
        {
            view.SetValue("Result", "即将在后台线程抛出未处理异常，整个工具箱会崩溃退出。");
            var thread = new Thread(() => throw new InvalidOperationException(
                "[故障注入] 后台线程未处理异常：用于验证崩溃后下次启动的安全模式询问。"))
            { IsBackground = false, Name = "FaultyPlugin.Crash" };
            thread.Start();
            return Task.CompletedTask;
        });

        // 5) 能力检查：清单里 permissions 是空数组，所以剪贴板/文件/工具列表都应被拒绝。
        view.BindClick("CheckCapability", async context =>
        {
            var report = new List<string>();
            void Try(string name, Func<Task> action)
            {
                try { action().GetAwaiter().GetResult(); report.Add($"{name}：允许"); }
                catch (InvalidOperationException ex) { report.Add($"{name}：已拒绝（{ex.Message}）"); }
                catch (Exception ex) { report.Add($"{name}：其他异常（{ex.GetType().Name}: {ex.Message}）"); }
            }

            Try("剪贴板", () => context.Services.CopyTextAsync("x", context.CancellationToken));
            Try("工具列表", () => context.Services.GetToolsAsync(context.CancellationToken));
            Try("背景服务", () => Task.FromResult(context.Services.GetService<IPluginBackgroundService>()));
            view.SetValue("Result", string.Join("\n", report));
        });

        view.OnClosed(() => { });
        return Task.CompletedTask;
    }

    private const string PageXaml = """
        <ScrollViewer xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <StackPanel Spacing="14" MaxWidth="900" HorizontalAlignment="Left">
            <TextBlock Text="故障注入测试插件" Style="{ThemeResource SubtitleTextBlockStyle}" />
            <TextBlock TextWrapping="Wrap"
                       Text="这些按钮会故意触发不同层级的错误，用来验证安全模式与故障隔离。发布版请勿安装。" />
            <TextBlock x:Name="Mode" TextWrapping="Wrap" Opacity="0.8" />

            <TextBlock Text="1. 运行期异常（被宿主捕获）" Style="{ThemeResource BodyStrongTextBlockStyle}" />
            <StackPanel Orientation="Horizontal" Spacing="8">
              <Button x:Name="ThrowBusiness" Content="同步抛异常" />
              <Button x:Name="ThrowAsync" Content="await 之后抛异常" />
            </StackPanel>

            <TextBlock Text="2. 界面卡死（需手动结束进程）" Style="{ThemeResource BodyStrongTextBlockStyle}" />
            <Button x:Name="Freeze" Content="阻塞 UI 线程 60 秒" />

            <TextBlock Text="3. 进程崩溃" Style="{ThemeResource BodyStrongTextBlockStyle}" />
            <Button x:Name="Crash" Content="后台线程未处理异常（整个程序退出）" />

            <TextBlock Text="4. 能力清单（本插件 permissions 为空）" Style="{ThemeResource BodyStrongTextBlockStyle}" />
            <Button x:Name="CheckCapability" Content="检查各项能力是否被放行" />

            <TextBlock x:Name="Result" TextWrapping="Wrap" IsTextSelectionEnabled="True" />
          </StackPanel>
        </ScrollViewer>
        """;
}
