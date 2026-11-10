using TubaWinUi3.PluginSdk;

public sealed class HelloPlugin : ITubaPlugin
{
    public void Register(IPluginHost host) => host.RegisterTool(new PluginToolDefinition
    {
        Id = "hello", Name = "多功能插件示例", Category = "其他工具", Description = "一个入口包含配置、外观和工具调用", IconPath = "assets/icon.svg",
        Tags = ["示例", "多功能", "自定义界面"], ShowInSettings = true,
        Xaml = """
            <ScrollViewer xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <StackPanel Spacing="16" MaxWidth="900" HorizontalAlignment="Left">
                <Image x:Name="PreviewIcon" Width="48" Height="48" HorizontalAlignment="Left" />
                <TextBlock Text="一个工具，多个功能" Style="{ThemeResource SubtitleTextBlockStyle}" />
                <TextBlock Text="这是插件提供的原生 XAML，可以自由组织分组、选项卡、输入和结果。" TextWrapping="Wrap" />
                <TextBox x:Name="Note" Header="插件自己的持久化配置" PlaceholderText="输入一段备注" MinWidth="360" />
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <Button x:Name="Save" Content="保存备注" />
                  <Button x:Name="Copy" Content="复制备注" />
                  <Button x:Name="Pick" Content="选择文件" />
                </StackPanel>
                <ComboBox x:Name="Theme" Header="工具箱主题" SelectedIndex="0">
                  <ComboBoxItem Content="跟随系统" Tag="System" /><ComboBoxItem Content="浅色" Tag="Light" /><ComboBoxItem Content="深色" Tag="Dark" />
                </ComboBox>
                <ComboBox x:Name="Layout" Header="工具箱导航" SelectedIndex="0">
                  <ComboBoxItem Content="侧边栏" Tag="Sidebar" /><ComboBoxItem Content="顶部标签" Tag="Tabs" />
                </ComboBox>
                <ToggleSwitch x:Name="Compact" Header="紧凑模式" />
                <Button x:Name="Apply" Content="应用工具箱外观" />
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <Button x:Name="Wallpaper" Content="选择工具箱背景" /><Button x:Name="ClearWallpaper" Content="清除背景" />
                </StackPanel>
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <Button x:Name="Navigation" Content="示例：改名硬件导航" /><Button x:Name="ResetNavigation" Content="恢复默认导航" /><Button x:Name="HostInfo" Content="宿主信息" />
                </StackPanel>
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <Button x:Name="Tools" Content="查看可用工具数量" /><Button x:Name="Hardware" Content="打开硬件信息" />
                </StackPanel>
                <Button x:Name="Wait" Content="异步与取消示例" />
                <TextBlock x:Name="Result" TextWrapping="Wrap" IsTextSelectionEnabled="True" />
              </StackPanel>
            </ScrollViewer>
            """,
        InitializeAsync = async view =>
        {
            view.SetImage("PreviewIcon", "assets/icon.svg");
            view.OnClosed(() => { /* 在这里退订自己的事件、停止自己的计时器。 */ });
            view.SetValue("Note", await view.Services.GetSettingAsync("note", view.CancellationToken) ?? "");
            var appearance = view.Services.GetAppearance();
            view.SetValue("Theme", appearance.Theme.ToString()); view.SetValue("Layout", appearance.Navigation.ToString());
            view.SetValue("Compact", appearance.Compact.ToString());
            view.BindValueChanged("Compact", _ =>
            {
                view.SetValue("Result", "紧凑模式选择已更新；点击应用后才修改工具箱外观。");
                return Task.CompletedTask;
            });
            view.BindClick("Save", async context =>
            {
                await context.Services.SetSettingAsync("note", view.GetValue("Note"), context.CancellationToken);
                view.SetValue("Result", "备注已保存，升级插件后仍保留。");
            });
            view.BindClick("Copy", async context => await context.Services.CopyTextAsync(view.GetValue("Note"), context.CancellationToken));
            view.BindClick("Wallpaper", async context =>
            {
                var path = await context.Services.PickOpenFileAsync("选择工具箱背景图片", context.CancellationToken);
                if (path is null) return;
                var background = context.Services.GetService<IPluginBackgroundService>() ?? throw new NotSupportedException("请更新宿主。");
                await background.SetAsync(path, 0.2, context.CancellationToken); view.SetValue("Result", "工具箱背景已更新。");
            });
            view.BindClick("ClearWallpaper", async context =>
            {
                var background = context.Services.GetService<IPluginBackgroundService>() ?? throw new NotSupportedException("请更新宿主。");
                await background.SetAsync(null, ct: context.CancellationToken); view.SetValue("Result", "工具箱背景已清除。");
            });
            view.BindClick("Navigation", async context =>
            {
                var navigation = context.Services.GetService<IPluginNavigationService>() ?? throw new NotSupportedException("请更新宿主。");
                await navigation.UpdateAsync(new("hardware", Title: "硬件中心"), context.CancellationToken); view.SetValue("Result", "已修改硬件导航名称，原跳转不变。");
            });
            view.BindClick("ResetNavigation", async context =>
            {
                var navigation = context.Services.GetService<IPluginNavigationService>() ?? throw new NotSupportedException("请更新宿主。");
                await navigation.ResetAsync(ct: context.CancellationToken); view.SetValue("Result", "默认导航已恢复。");
            });
            view.BindClick("HostInfo", context =>
            {
                var info = context.Services.GetService<IPluginHostInfoService>()?.GetInfo();
                view.SetValue("Result", info is null ? "宿主不提供版本信息。" : $"版本 {info.Version}，API {info.ApiVersion}，{info.Architecture}，{info.Language}");
                return Task.CompletedTask;
            });
            view.BindClick("Pick", async context =>
            {
                var path = await context.Services.PickOpenFileAsync("选择文件", context.CancellationToken);
                if (path is not null) view.SetValue("Result", path);
            });
            view.BindClick("Apply", async context =>
            {
                if (!await context.ConfirmAsync("应用外观", "将立即修改工具箱主题、导航与紧凑模式，是否继续？")) return;
                await context.Services.ApplyAppearanceAsync(new(Enum.Parse<PluginTheme>(view.GetValue("Theme")),
                    Enum.Parse<PluginNavigationLayout>(view.GetValue("Layout")), bool.Parse(view.GetValue("Compact"))), context.CancellationToken);
                if (!context.CancellationToken.IsCancellationRequested) view.SetValue("Result", "工具箱外观已更新。");
            });
            view.BindClick("Tools", async context =>
            {
                context.ReportProgress("正在获取实际工具列表…");
                var tools = await context.Services.GetToolsAsync(context.CancellationToken);
                view.SetValue("Result", $"当前可用工具：{tools.Count} 个。可通过稳定 ID 调用，无需猜测路径。");
            });
            view.BindClick("Wait", async context =>
            {
                context.ReportProgress("可点击宿主的停止按钮取消本次异步操作。");
                await Task.Delay(TimeSpan.FromSeconds(10), context.CancellationToken);
                view.SetValue("Result", "异步操作已完成。");
            });
            view.BindClick("Hardware", async context => await context.Services.NavigateAsync(PluginDestination.Hardware, context.CancellationToken));
        }
    });
}
