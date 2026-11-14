using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Tubatools.AiXamlCard;

/// <summary>
/// 消息流里的一张卡片：一个 Border 包住助手写的 XAML，底部一行提示。
/// 加载顺序是 校验 → 改写 → XamlReader.Load → 编译代码后置 → 反射实例化并调用 OnReady。
/// 任一步失败都退化成一张「错误卡片」，绝不让异常冒到消息流外面。
/// </summary>
internal sealed class CardView : UserControl
{
    private const int MaxNoticeLength = 300;

    private readonly Border _frame;
    private readonly ContentControl _slot = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _notice = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, FrameworkElement> _named = new(StringComparer.Ordinal);

    private CardControllerBase? _controller;
    private Assembly? _assembly;
    private bool _closed;

    public event Action<string>? SendRequested;

    public CardView()
    {
        _notice.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(_slot);
        stack.Children.Add(_notice);
        _frame = new Border
        {
            Background = CardPalette.BrushFor("TubaCard"),
            BorderBrush = CardPalette.BrushFor("TubaStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = stack
        };
        Content = _frame;
        Unloaded += (_, _) => Close();
    }

    public string? Slug { get; private set; }

    /// <summary>从磁盘上的卡片文件渲染。</summary>
    public bool Load(CardFiles card) => Load(card.Slug, card.ReadXaml(), card.ReadCode(), card.Directory);

    /// <summary>直接渲染助手交来的内容。</summary>
    public bool Load(CardContent content) => Load("card", content.Xaml, content.Code, null);

    /// <summary>把一段 XAML 与可选代码渲染进控件；返回是否成功。</summary>
    public bool Load(string slug, string xaml, string code, string? assetDirectory)
    {
        if (_closed) return false;
        Slug = slug;
        var guard = CardGuard.Validate(xaml);
        if (!guard.Ok)
        {
            ShowError(guard.Status == CardGuardStatus.Incomplete ? "卡片还没写完，稍后会自动重试。" : guard.Error);
            return false;
        }

        FrameworkElement root;
        try
        {
            root = XamlReader.Load(CardMarkup.Rewrite(xaml, assetDirectory)) as FrameworkElement
                   ?? throw new InvalidDataException("卡片根元素不是 FrameworkElement。");
        }
        catch (Exception ex) when (ex is XamlParseException or InvalidDataException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            ShowError("XAML 未能加载：" + ex.Message);
            return false;
        }

        CollectNamed(root);
        _slot.Content = root;
        AutomationProperties.SetAutomationId(this, "AiXamlCard:" + slug);

        if (!string.IsNullOrWhiteSpace(code)) CompileAndAttach(code, slug);
        else _controller = null;

        // OnReady 可能发生在自动渲染阶段，用护栏包住：
        // 界面初始化放行，主动副作用（例如替用户发消息）被拒绝。
        try
        {
            using (CardStartupGuard.EnterRender())
            {
                _controller?.OnReady();
            }
        }
        catch (InvalidOperationException ex) { Notice(ex.Message, true); }
        catch (Exception ex) { Notice("卡片代码出错：" + ex.Message, true); }
        return true;
    }

    /// <summary>按 x:Name 收集控件，供代码后置通过 Find&lt;T&gt; 拿到。</summary>
    private void CollectNamed(FrameworkElement root)
    {
        _named.Clear();
        if (root.Name.Length > 0) _named[root.Name] = root;
        Walk(root);
        void Walk(DependencyObject node)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is FrameworkElement { Name.Length: > 0 } element) _named[element.Name] = element;
                Walk(child);
            }
        }
    }

    private void CompileAndAttach(string code, string slug)
    {
        var compiled = CardCompiler.Compile(code, "AiCard_" + slug + "_" + Guid.NewGuid().ToString("N")[..8]);
        if (!compiled.Success || compiled.Controller is null)
        {
            Notice("代码后置未编译，卡片以静态界面显示：" + compiled.Error, true);
            _controller = null;
            return;
        }
        _assembly = compiled.Assembly;
        try
        {
            _controller = Activator.CreateInstance(compiled.Controller) as CardControllerBase;
            if (_controller is null) { Notice("无法创建卡片控制器。", true); return; }
            Attach(_controller);
        }
        catch (Exception ex) when (ex is TargetInvocationException or MissingMethodException or MemberAccessException)
        {
            Notice("卡片控制器初始化失败：" + ex.Message, true);
            _controller = null;
        }
    }

    /// <summary>基类的 Attach 是 internal，这里用反射调用，避免把内部契约做成公共 API。</summary>
    private void Attach(CardControllerBase controller)
    {
        var method = typeof(CardControllerBase).GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException("CardControllerBase.Attach");
        method.Invoke(controller, [_named, (Action<string>)(text => SendRequested?.Invoke(text)), (Action<string, bool>)Notice]);
    }

    private void ShowError(string message)
    {
        _slot.Content = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = CardPalette.BrushFor("TubaBad")
        };
        _frame.BorderBrush = CardPalette.BrushFor("TubaBad");
    }

    private void Notice(string text, bool error)
    {
        if (_closed) return;
        var value = text.Length > MaxNoticeLength ? text[..MaxNoticeLength] + "…" : text;
        _notice.Text = value;
        _notice.Foreground = new SolidColorBrush(error ? CardPalette.ColorOf("TubaBad") : CardPalette.ColorOf("TubaMuted"));
        _notice.Visibility = value.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        try { _controller?.OnClosed(); }
        catch (Exception) { /* 关闭阶段的异常不应影响消息流。 */ }
        _controller = null;
        _slot.Content = null;
        if (_assembly?.IsCollectible == true)
        {
            try { System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(_assembly)?.Unload(); }
            catch (InvalidOperationException) { }
        }
        _assembly = null;
    }

    /// <summary>当前卡片是否需要重新加载（文件变了）。</summary>
    public DateTimeOffset? LoadedAt { get; private set; }

    public void MarkLoaded(DateTimeOffset stamp) => LoadedAt = stamp;
}
