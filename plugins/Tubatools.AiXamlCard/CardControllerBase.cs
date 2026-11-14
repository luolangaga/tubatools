using Microsoft.UI.Xaml;

namespace Tubatools.AiXamlCard;

/// <summary>
/// AI 生成的代码后置所继承的基类。只暴露三件事：按名称取控件、把消息发回对话、
/// 显示一行提示。刻意不暴露文件系统、网络或进程能力——本插件由用户显式安装，
/// 但卡片本身仍是「界面 + 纯计算」，这是它的能力边界。
///
/// 生命周期约定（很重要）：<see cref="OnReady"/> 只用于初始化界面与订阅事件。
/// 卡片可能在启动阶段被自动渲染，因此**不要**在 OnReady 里执行有副作用的动作
/// （写文件、发请求、启动进程、改系统设置）。这类动作必须绑定到用户点击。
/// </summary>
public abstract class CardControllerBase
{
    private IReadOnlyDictionary<string, FrameworkElement> _named = new Dictionary<string, FrameworkElement>();
    private Action<string> _send = _ => { };
    private Action<string, bool> _notice = (_, _) => { };

    internal void Attach(IReadOnlyDictionary<string, FrameworkElement> named, Action<string> send, Action<string, bool> notice)
    {
        _named = named;
        _send = send;
        _notice = notice;
    }

    /// <summary>按 x:Name 取控件；找不到返回 null，便于写防御式代码。</summary>
    protected T? Find<T>(string name) where T : FrameworkElement =>
        _named.TryGetValue(name, out var element) ? element as T : null;

    /// <summary>把一段文字作为用户消息发回对话，触发新一轮。</summary>
    protected void Send(string text)
    {
        if (CardStartupGuard.IsRendering) CardStartupGuard.RejectSideEffect("发消息");
        if (!string.IsNullOrWhiteSpace(text)) _send(text.Trim());
    }

    /// <summary>在卡片底部显示一行提示；error 决定是否用错误色。</summary>
    protected void Notice(string text, bool error = false) => _notice(text, error);

    /// <summary>
    /// 卡片渲染完成、所有 x:Name 可用时调用一次。
    /// 只做界面初始化与事件订阅：此时可能是一次自动渲染，不允许产生副作用。
    /// </summary>
    public virtual void OnReady() { }

    /// <summary>卡片从界面移除时调用，用于停止计时器 / 退订事件。</summary>
    public virtual void OnClosed() { }
}
