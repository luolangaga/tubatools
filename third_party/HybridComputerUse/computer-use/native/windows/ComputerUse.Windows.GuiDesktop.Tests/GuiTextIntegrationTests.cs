using System.Text.Json.Nodes;
using ComputerUse.Windows.Runtime;

internal static class GuiTextIntegrationTests
{
    internal static void Run()
    {
        NativeDesktop.Reset();
        var malformed = Batch(
            new JsonObject { ["type"] = "click", ["x"] = 5d, ["y"] = 5d },
            new JsonObject { ["type"] = "type", ["text"] = "later\ud800" });
        var failure = Fails(() => DesktopProvider.ExecuteBatch(malformed));
        Check(failure.Code == "invalid_argument" && !failure.InputEventsEmitted &&
            NativeDesktop.Calls.Count == 0 && NativeDesktop.TextMessages.Count == 0 &&
            NativeDesktop.Captures.Count == 0,
            "Malformed later text must reject before the preceding click or any capture.");

        NativeDesktop.Reset();
        NativeDesktop.OnTextMessage = _ => false;
        failure = Fails(() => DesktopProvider.ExecuteBatch(Batch(Text("ab"))));
        Check(!failure.InputEventsEmitted && failure.MutationUnknown &&
            failure.DispatchedActions == 0 &&
            failure.ActionEvidence?["attempted_messages"]?.GetValue<int>() == 1 &&
            failure.ActionEvidence?["unknown_message_index"]?.GetValue<int>() == 0 &&
            NativeDesktop.TextMessages.Count == 1,
            "The first unknown message must retain uncertainty and its attempted index without claiming delivery.");

        NativeDesktop.Reset();
        NativeDesktop.OnTextMessage = count => count < 2;
        failure = Fails(() => DesktopProvider.ExecuteBatch(Batch(
            new JsonObject { ["type"] = "screenshot" },
            Text("abc"), new JsonObject { ["type"] = "click", ["x"] = 5d, ["y"] = 5d })));
        Check(failure.InputEventsEmitted && failure.MutationUnknown &&
            failure.DispatchedActions == 1 && failure.DispatchedInputActions == 0 &&
            failure.ActionEvidence?["returned_messages"]?.GetValue<int>() == 1 &&
            failure.ActionEvidence?["unknown_message_index"]?.GetValue<int>() == 1 &&
            failure.ActionEvidence?["route"]?.GetValue<string>() == "input.window_messages" &&
            NativeDesktop.TextMessages.Count == 2 && NativeDesktop.Calls.Count == 0,
            "GUI wrapping must retain the completed prefix, native failure evidence, and stop before the later click.");

        NativeDesktop.Reset();
        var result = DesktopProvider.ExecuteBatch(Batch(Text("")));
        Check(result["mutation"]?["input_events_emitted"]?.GetValue<bool>() == false &&
            result["gui_result"]?["input_actions_dispatched"]?.GetValue<int>() == 0 &&
            NativeDesktop.TextMessages.Count == 0 && NativeDesktop.InputIdleWaits == 0,
            "Empty text must not claim emission or consume a keyboard processing barrier.");
        // A returned SendInput does not settle pointer focus. Neither a wait,
        // screenshot nor a keyboard receipt proves the pointer was processed.
        foreach (var pointer in new[] {
            new JsonObject { ["type"] = "click", ["x"] = 5d, ["y"] = 5d },
            new JsonObject { ["type"] = "double_click", ["x"] = 5d, ["y"] = 5d },
            new JsonObject { ["type"] = "move", ["x"] = 5d, ["y"] = 5d },
            new JsonObject { ["type"] = "scroll", ["x"] = 5d, ["y"] = 5d, ["scroll_y"] = 100d },
            new JsonObject { ["type"] = "drag", ["path"] = new JsonArray(
                new JsonObject { ["x"] = 5d, ["y"] = 5d },
                new JsonObject { ["x"] = 6d, ["y"] = 6d }) },
        })
        foreach (var intermediate in new[] {
            new JsonObject { ["type"] = "screenshot" },
            new JsonObject { ["type"] = "wait" },
            new JsonObject { ["type"] = "keypress", ["keys"] = new JsonArray("LEFT") },
        })
        {
            NativeDesktop.Reset();
            var oldFocus = NativeDesktop.Focus;
            failure = Fails(() => DesktopProvider.ExecuteBatch(Batch(
                pointer.DeepClone(), intermediate.DeepClone(), Text("marker"),
                new JsonObject { ["type"] = "click", ["x"] = 9d, ["y"] = 9d })));
            Check(failure.Code == "desktop_input_failed" && failure.InputEventsEmitted &&
                failure.MutationUnknown && failure.DispatchedActions == 2 &&
                NativeDesktop.Focus == oldFocus && NativeDesktop.TextMessages.Count == 0 &&
                NativeDesktop.TextSelections.Count == 0 && NativeDesktop.TextWakes.Count == 0,
                $"Pending {pointer["type"]} then {intermediate["type"]} must stop text: " +
                $"{failure.Code}, dispatched={failure.DispatchedActions}, emitted={failure.InputEventsEmitted}, unknown={failure.MutationUnknown}: {failure.Message}");
        }
        foreach (var keys in new[] { "CTRL+A", "TAB", "LEFT", "CTRL", "ALT+TAB" })
        {
            NativeDesktop.Reset();
            failure = Fails(() => DesktopProvider.ExecuteBatch(Batch(
                new JsonObject { ["type"] = "keypress", ["keys"] = new JsonArray(keys) },
                Text("marker"))));
            Check(failure.InputEventsEmitted && failure.MutationUnknown &&
                failure.DispatchedActions == 1 && NativeDesktop.Calls.Count == 2 &&
                NativeDesktop.TextMessages.Count == 0 && NativeDesktop.TextSelections.Count == 0,
                "An unchanged focus/selection cannot prove that a queued key was processed.");
        }
        NativeDesktop.Reset();
        result = DesktopProvider.ExecuteBatch(Batch(
            new JsonObject { ["type"] = "click", ["x"] = 5d, ["y"] = 5d }, Text("")));
        Check(result["ok"]!.GetValue<bool>() && NativeDesktop.TextMessages.Count == 0,
            "Empty text after pointer input needs no focus settlement.");
        NativeDesktop.Reset();
        result = DesktopProvider.ExecuteBatch(Batch(Text("first"),
            new JsonObject { ["type"] = "click", ["x"] = 5d, ["y"] = 5d }));
        Check(result["ok"]!.GetValue<bool>() && NativeDesktop.TextMessages.Count == 5,
            "Text preceding pointer input must retain its existing behavior.");
        Console.WriteLine("GUI text validation and failure integration tests passed (21 cases).");
    }

    private static JsonObject Text(string value) => new() { ["type"] = "type", ["text"] = value };
    private static JsonObject Batch(params JsonNode[] actions) => new() { ["actions"] = new JsonArray(actions) };
    private static ProviderFailureException Fails(Action action)
    {
        try { action(); }
        catch (ProviderFailureException error) { return error; }
        throw new InvalidOperationException("Expected a provider failure.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
