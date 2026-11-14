namespace Tubatools.AiXamlCard;

/// <summary>
/// 启动阶段护栏。卡片可能在工具箱启动时被自动渲染，而「渲染」这个动作本身不该产生任何
/// 外部影响——它只是把界面画出来。
///
/// 编译期已经禁掉了文件、网络、进程、注册表与反射，这里补的是**语义层**的那一半：
/// 渲染期间调用 <see cref="CardControllerBase.Send"/> 会把消息塞进对话、可能直接触发新一轮
/// 任务，这属于「用户什么都没点，程序自己动起来了」。所以渲染期一律拒绝，并明确告诉
/// 卡片作者应该把它绑到按钮或开关上。
/// </summary>
internal static class CardStartupGuard
{
    private static int _depth;

    /// <summary>渲染（含 OnReady）期间为 true。</summary>
    public static bool IsRendering => _depth > 0;

    /// <summary>进入自动渲染阶段；返回的 scope 在 Dispose 时退出。</summary>
    public static Scope EnterRender()
    {
        _depth++;
        return new Scope();
    }

    /// <summary>拒绝渲染期的主动副作用；用户体验上表现为卡片底部的一行错误。</summary>
    public static void RejectSideEffect(string what) =>
        throw new InvalidOperationException($"{what}只能在用户点击后执行，不能在卡片加载时自动执行。");

    internal readonly struct Scope : IDisposable
    {
        public void Dispose()
        {
            if (_depth > 0) _depth--;
        }
    }
}
