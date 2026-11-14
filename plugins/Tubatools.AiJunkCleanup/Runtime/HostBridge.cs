using System.Collections;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Tubatools.AiJunkCleanup.Runtime;

internal sealed record AssistantMessageView(string Id, string Role, string Text, bool Completed, IReadOnlyList<AssistantOperationView> Operations);
internal sealed record AssistantOperationView(string Id, string Kind, string Status, string Command, string Target);

/// <summary>
/// 通过公开属性/字段读取宿主状态，不修改宿主程序集。页面的会话状态是私有嵌套类型，
/// 因此这里只做只读反射；即使宿主改字段名，插件也只是停止弹清单，不会影响 AI 助手本身。
/// </summary>
internal static class HostBridge
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static string? TryGetAgentWorkspacePath()
    {
        var type = FindHostType("TubaWinUi3.Services.AiAgent.AgentWorkspace");
        return type?.GetProperty("DirectoryPath", StaticFlags)?.GetValue(null) as string;
    }

    internal static string? TryGetDataDirectory()
    {
        var type = FindHostType("TubaWinUi3.Services.ConfigManager");
        return type?.GetMethod("GetDataDir", StaticFlags)?.Invoke(null, null) as string;
    }

    internal static Type? FindHostType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.GetName().Name?.StartsWith("TubaWinUi3", StringComparison.OrdinalIgnoreCase) == true) continue;
            var type = assembly.GetType(fullName, throwOnError: false);
            if (type is not null) return type;
        }
        return Type.GetType(fullName + ", TubaWinUi3", throwOnError: false);
    }

    /// <summary>独立工具窗口（BuiltinToolWindow）里的 Frame，用于“工具在独立窗口打开”模式。</summary>
    internal static IReadOnlyList<Frame> GetToolWindowFrames()
    {
        try
        {
            var type = FindHostType("TubaWinUi3.Services.BuiltinToolWindow");
            if (type?.GetField("_openWindows", StaticFlags)?.GetValue(null) is not IEnumerable windows) return [];
            var frames = new List<Frame>();
            foreach (var window in windows)
            {
                if (window is null) continue;
                if (window.GetType().GetField("_frame", InstanceFlags)?.GetValue(window) is Frame frame)
                    frames.Add(frame);
            }
            frames.Reverse();
            return frames;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or TargetInvocationException or MissingMemberException)
        {
            return [];
        }
    }

    /// <summary>页面所在的实际 Window（独立工具窗口或主窗口）。</summary>
    internal static Window? TryGetHostingWindow(object page)
    {
        if (page is not FrameworkElement element) return null;
        try
        {
            var type = FindHostType("TubaWinUi3.Services.BuiltinToolWindow");
            var method = type?.GetMethod("FindHostingWindow", StaticFlags, null, new[] { typeof(FrameworkElement) }, null);
            return method?.Invoke(null, new object?[] { element }) as Window;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or TargetInvocationException or MissingMemberException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<AssistantMessageView> ReadMessages(object page)
    {
        try
        {
            var active = page.GetType().GetField("_active", InstanceFlags)?.GetValue(page);
            if (active is null) return [];
            var messages = active.GetType().GetField("Messages", InstanceFlags)?.GetValue(active) as IEnumerable;
            if (messages is null) return [];
            var result = new List<AssistantMessageView>();
            foreach (var item in messages)
            {
                if (item is null) continue;
                var type = item.GetType();
                var role = type.GetProperty("Role", InstanceFlags)?.GetValue(item) as string ?? "";
                if (role != "assistant") continue;
                var id = type.GetProperty("Id", InstanceFlags)?.GetValue(item) as string ?? "";
                var text = type.GetProperty("Text", InstanceFlags)?.GetValue(item) as string ?? "";
                var completed = type.GetProperty("Completed", InstanceFlags)?.GetValue(item) is bool done && done;
                var content = type.GetProperty("Content", InstanceFlags)?.GetValue(item) as IEnumerable;
                var operations = ReadOperations(content);
                if (text.Length == 0) text = JoinContent(content);
                if (id.Length > 0 && (text.Length > 0 || operations.Count > 0)) result.Add(new(id, role, text, completed, operations));
            }
            return result;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or TargetInvocationException or MissingMemberException)
        {
            return [];
        }
    }

    private static string JoinContent(object? content)
    {
        if (content is not IEnumerable parts) return "";
        var texts = new List<string>();
        foreach (var part in parts)
        {
            if (part is null) continue;
            var text = part.GetType().GetProperty("Text", InstanceFlags)?.GetValue(part) as string;
            if (!string.IsNullOrWhiteSpace(text)) texts.Add(text);
        }
        return string.Join("\n\n", texts);
    }

    private static IReadOnlyList<AssistantOperationView> ReadOperations(object? content)
    {
        if (content is not IEnumerable parts) return [];
        var operations = new List<AssistantOperationView>();
        foreach (var part in parts)
        {
            if (part is null) continue;
            var operation = part.GetType().GetProperty("Operation", InstanceFlags)?.GetValue(part);
            if (operation is null) continue;
            var type = operation.GetType();
            string Value(string name) => type.GetProperty(name, InstanceFlags)?.GetValue(operation) as string ?? "";
            var id = Value("Id");
            if (id.Length == 0) continue;
            operations.Add(new(id, Value("Kind"), Value("Status"), Value("Command"), Value("Target")));
        }
        return operations;
    }

    /// <summary>当前会话 ID；草稿状态为 null。用于在切换会话时重置只读守门。</summary>
    internal static string? TryGetActiveSessionId(object page)
    {
        try
        {
            var active = page.GetType().GetField("_active", InstanceFlags)?.GetValue(page);
            return active?.GetType().GetField("Id", InstanceFlags)?.GetValue(active) as string;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or TargetInvocationException or MissingMemberException)
        {
            return null;
        }
    }

    /// <summary>取消当前会话的本轮运行（只读守门触发时使用）。</summary>
    internal static void CancelRun(object page)
    {
        try
        {
            var active = page.GetType().GetField("_active", InstanceFlags)?.GetValue(page);
            if (active?.GetType().GetField("Run", InstanceFlags)?.GetValue(active) is CancellationTokenSource run)
                run.Cancel();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or TargetInvocationException or MissingMemberException) { }
    }
}
