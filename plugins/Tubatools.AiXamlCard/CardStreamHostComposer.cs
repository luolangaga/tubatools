using Windows.ApplicationModel.DataTransfer;

namespace Tubatools.AiXamlCard;

/// <summary>
/// 卡片 → 对话的出口。卡片在自己的窗口里，无法直接操作助手页面的输入框，
/// 因此把文字放进剪贴板并提示用户粘贴——和画布 <c>tuba.insert</c> 的语义一致：
/// 由用户决定是否发送，卡片不替用户按发送键。
/// </summary>
internal static class CardStreamHostComposer
{
    public static void Insert(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }
}
