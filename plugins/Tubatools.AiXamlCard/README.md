# AI 界面卡片（Tubatools.AiXamlCard）

让图吧工具箱的 AI 助手直接写**原生 WinUI 界面**，并动态编译 **C# 代码后置**。
每张卡片是消息流里的一行，而不是另开一个网页。

## 它做什么

1. 助手把界面写成 `cards/<slug>/card.xaml`（可选再写 `cards/<slug>/card.cs`）。
2. 插件读取这些文件，先过白名单校验，再用 Roslyn 把 C# 编译成可卸载的程序集。
3. 界面渲染出来，`card.cs` 里的 `OnReady()` 订阅事件、计算、更新控件。

## 安装

```powershell
dotnet pack plugins/Tubatools.AiXamlCard/Tubatools.AiXamlCard.csproj -c Release -o artifacts/plugins
```

然后在工具箱「插件」页安装生成的 `Tubatools.AiXamlCard.1.0.0.nupkg`。
插件声明了 `fullTrust`，因为它需要把卡片挂到 AI 助手页面。

## 卡片格式

```
cards/<slug>/card.xaml      # 必须有，一个根元素
cards/<slug>/card.cs        # 可选，dynamic C# code-behind
cards/<slug>/assets/*.png   # 可选图片
```

- XAML 元素与属性走白名单：布局、文本、按钮、开关、滑块、进度条、常用形状。
  `WebView2`、`MediaPlayerElement`、`Frame` 等能加载外部内容的类型一律拒绝。
- 颜色写令牌即可（`TubaText`、`TubaAccent`、`TubaCard`…），加载时按当前主题替换。
- 图片只能是 `assets/` 下的 png/jpg/bmp/gif。
- **事件不写在 XAML 里**：用 `x:Name` 命名控件，在 `card.cs` 里订阅。

## 代码后置

```csharp
namespace Tubatools.AiXamlCard.Generated;

public sealed class CardController : CardControllerBase
{
    public override void OnReady()
    {
        if (Find<Slider>("Hours") is { } slider) slider.ValueChanged += (_, _) => Update();
        Update();
    }

    private void Update()
    {
        if (Find<Slider>("Hours") is not { } slider || Find<TextBlock>("Out") is not { } label) return;
        label.Text = $"{slider.Value * 365:0} 小时/年";
    }
}
```

基类只给三件事：`Find<T>(name)`、`Send(text)`（把消息发回对话）、`Notice(text, error)`。

## 能力边界

编译期只允许界面与纯计算。**文件、网络、进程、注册表、反射一律拒绝**——
这不是靠程序集引用拦（核心库自带 `System.IO`），而是编译前的语法策略。
它是一道护栏，不是安全沙箱：卡片代码与宿主同权限运行，
所以本插件必须是用户显式安装后才会存在的能力。

## 启动阶段不执行副作用

卡片会在工具箱启动时自动渲染——**显示是自动的，动作不是**。

- `OnReady()` 只做界面初始化：取控件、订阅事件、算一次初始显示值。
- `Send(...)`、计时器循环、请求、写文件这类动作只能写在用户触发的回调里。
- 渲染期的 `Send` 会被 `CardStartupGuard` 拒绝并在卡片底部提示，
  避免「用户什么都没点，程序自己发起了一轮对话」。
- 同样的规则写在 `tuba-xaml-card` 技能里，模型生成时就该遵守。

## 目录

```
CardGuard.cs          XAML 白名单校验
CardCodePolicy.cs     C# 语法级能力检查
CardStartupGuard.cs   启动渲染期的副作用拦截
CardCompiler.cs       Roslyn 编译 + 可卸载加载上下文
CardMarkup.cs         配色令牌 / 图片路径改写 / 标题
CardPalette.cs        双主题配色
CardStore.cs          卡片磁盘布局与示例
CardView.cs           单张卡片的加载与错误兜底
CardPage.cs           卡片工作台（列表 + 预览）
CardSkill.cs          写入 AI 工作区的技能说明
CardHostBridge.cs     通过反射把卡片挂进消息流的辅助
```

## 许可

MIT。Roslyn（`Microsoft.CodeAnalysis.CSharp`）随插件包分发，Apache-2.0。
