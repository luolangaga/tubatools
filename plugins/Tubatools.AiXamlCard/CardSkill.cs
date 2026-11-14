using System.Text;

namespace Tubatools.AiXamlCard;

/// <summary>把卡片技能写进 AI 工作区，让助手知道怎么更主动、更省事地展示卡片。</summary>
internal static class CardSkill
{
    public const string SkillName = "tuba-xaml-card";
    public const string ScriptName = "show-card.ps1";

    public static void Install(string pluginDataDirectory, string? scriptSource = null)
    {
        foreach (var workspace in Workspaces(pluginDataDirectory))
        {
            var directory = Path.Combine(workspace, ".opencode", "skills", SkillName);
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "SKILL.md"), Content, new UTF8Encoding(false));
                if (scriptSource is { Length: > 0 } && File.Exists(scriptSource))
                {
                    var scripts = Path.Combine(directory, "scripts");
                    Directory.CreateDirectory(scripts);
                    File.Copy(scriptSource, Path.Combine(scripts, ScriptName), true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static IEnumerable<string> Workspaces(string pluginData)
    {
        var directory = new DirectoryInfo(pluginData);
        for (var i = 0; i < 4 && directory is not null; i++, directory = directory.Parent)
        {
            var aiAssistant = Path.Combine(directory.FullName, "AiAssistant");
            if (Directory.Exists(aiAssistant)) yield return Path.Combine(aiAssistant, "Workspace");
        }
    }

    public const string Content = """
        ---
        name: tuba-xaml-card
        description: 原生界面卡片。凡是数值换算、参数调节、多项对比、结构化清单、结果汇总这类「看一眼比读一段文字更清楚」的内容，都用卡片展示，而不是堆文字或表格文字。把界面写成 WinUI XAML，需要交互时补一段 C# 代码后置（宿主用 Roslyn 动态编译）。硬件参数、性能数据、配置建议、价格对比、方案权衡都适用。
        ---

        # 界面卡片（tuba-xaml-card）

        ## 优先用卡片：这是默认表达方式

        图吧工具箱的用户是来「看东西、调参数」的。只要回答里有任何一项符合下面任意一条，
        **就主动做一张卡片**，不要等用户要求：

        - 有 3 个以上可以并排比较的项目（CPU/显卡/内存/型号/方案/价格）
        - 有可以用滑块或输入框调、调完结果会变的量（功耗、频率、容量、预算、时长）
        - 有一个需要用户填参数才能算的结论（电源瓦数、带宽、等效频率、续航）
        - 有一段「结论 + 依据 + 建议」的结构化内容（诊断结果、优化清单、体检报告）
        - 用户会反复回来看的数据（硬件清单、当前配置、待办项）

        反过来，只在「一句话就能说清」时才用纯文字回答，例如确认、报错原因、单个数值。

        **一次回答可以、也经常应该给多张卡片**：一次提交多个标签页，比来回追问效率高得多。
        例如「帮我看看这套配置」→ 功耗估算 + 内存分析 + 升级建议，三张卡一次给全。

        ## 怎么展示（唯一正确做法）

        写完 XAML 后立刻提交，卡片会开窗渲染出来。三种写法按场景挑最省事的：

        ```powershell
        # 单张，直接内联（最简单，适合短卡片）
        pwsh -File .opencode/skills/tuba-xaml-card/scripts/show-card.ps1 -Title "功耗估算" -Xaml @'
        <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">...</Border>
        '@

        # 单张，从文件读（卡片内容长、或需要保留副本）
        pwsh -File .opencode/skills/tuba-xaml-card/scripts/show-card.ps1 `
          -XamlFile cards/<slug>/card.xaml -CodeFile cards/<slug>/card.cs -Title "内存换算"

        # 多张，整批提交（推荐：一次给全套面板）
        pwsh -File .opencode/skills/tuba-xaml-card/scripts/show-card.ps1 -JsonFile cards/batch.json
        ```

        整批 JSON 格式：

        ```json
        {"title":"整机体检","cards":[
          {"title":"功耗估算","xaml":"...","code":"..."},
          {"title":"内存分析","xaml":"...","code":"..."}
        ]}
        ```

        - 看到「已提交卡片」才算成功；脚本报错就修好再提交，不要假装已经展示。
        - 每批最多 8 张；同一批用一个 JSON，别开好几个进程。
        - 卡片内容变了要重新提交，文件改动本身不会刷新窗口。

        ## card.xaml 规则

        1. 根元素只能有一个，必须带命名空间：
           `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"`
           `xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"`
        2. **事件不写在 XAML 里**（没有 `Click=`）。用 `x:Name` 命名控件，交互在 `card.cs` 里订阅。
        3. 颜色用令牌字面量，宿主按主题替换：
           `TubaText` `TubaMuted` `TubaCard` `TubaSubtle` `TubaStroke`
           `TubaAccent` `TubaGood` `TubaWarn` `TubaBad`
        4. 图片写 `Source="assets/图片名.png"`（png/jpg/jpeg/bmp/gif）。
        5. 允许的元素：`Grid` `StackPanel` `Border` `ScrollViewer` `RelativePanel` `Canvas` `Viewbox`
           `Expander` `TextBlock` `Button` `ToggleButton` `CheckBox` `RadioButton` `ToggleSwitch`
           `TextBox` `NumberBox` `Slider` `ComboBox` `ComboBoxItem` `ProgressBar` `ProgressRing`
           `InfoBar` `FontIcon` `Image` `Rectangle` `Ellipse` `Line` `Path` `Polygon` `Polyline`。
           不要用 `WebView2`、`MediaPlayerElement`、`Frame`、`Page`、自定义控件、`x:Bind`、`Binding`。
        6. 列宽写 `Auto` / `*` / `120` 都行；根元素建议 `MaxWidth="640"`，不要写死 `Height`。

        ## card.cs 规则

        ```csharp
        public sealed class CardController : CardControllerBase
        {
            public override void OnReady()
            {
                if (Find<Slider>("Level") is { } slider) slider.ValueChanged += (_, _) => Update();
                if (Find<Button>("Ask") is { } button) button.Click += (_, _) => Send("请分析这个结果");
                Update();   // 只算初始显示，不做副作用
            }

            private void Update()
            {
                if (Find<Slider>("Level") is not { } slider || Find<TextBlock>("Out") is not { } text) return;
                text.Text = $"{slider.Value:0} 分";
            }
        }
        ```

        ### 副作用只允许发生在用户操作里（硬性规则）

        卡片会自动渲染，`OnReady()` 里每件事都会在用户没操作时执行一次：

        - `OnReady()` **只做**：取控件、订阅事件、按初始值算一次显示、设置可见性。
        - **绝对不要**在 `OnReady()` 里 `Send(...)`、开计时器循环、发请求、写文件、改设置。
          这些只能写在 `Click` / `Toggled` / `ValueChanged` 回调里。
        - 「一进来就显示结果」就直接算好写进控件，不要走 `Send`。

        ### 其他约束

        - 基类三件事：`Find<T>(name)`、`Send(text)`（把消息发回对话）、`Notice(text, error)`。
        - 配色用 `CardPalette.AccentBrush` / `TextBrush` / `MutedBrush` / `GoodBrush` / `WarnBrush` / `BadBrush`。
        - 默认导入 `System`、`System.Linq`、`System.Threading.Tasks`、`Microsoft.UI.Xaml*`、`Windows.UI`。
        - **不可用**文件、网络、进程、注册表、反射：编译前策略会拒绝并说明原因。
        - 编译失败会在卡片底部显示行号，据此修正后重新提交即可。

        ## 回复用户

        提交成功后，用一两句话说明这几张卡各自解决什么、哪个滑块值得拖一下。
        **不要在回复正文里贴 XAML/C# 源码**——用户要看的是卡片，不是代码。
        """;
}
