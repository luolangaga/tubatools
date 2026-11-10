using System.Drawing;
using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class DesktopProvider
{
    private const int KeyboardInputIdleTimeoutMilliseconds = 250;
    private const double MaximumScrollDelta = 2000;

    internal static JsonObject ExecuteBatch(JsonObject request)
    {
        var actions = request["actions"] as JsonArray ?? new JsonArray();
        if (actions.Count == 1 &&
            actions[0] is JsonObject zoom &&
            zoom["type"]?.GetValue<string>() == "zoom")
        {
            return NativeDesktop.Zoom(
                zoom,
                request["frame_id"]?.GetValue<string>() ?? string.Empty);
        }
        var desktopBounds = NativeDesktop.DesktopBounds();
        var deliveredSize = WindowsDesktopImageGeometry.DeliveredSize(
            desktopBounds.Width,
            desktopBounds.Height);
        ValidateBatch(actions, deliveredSize.Width, deliveredSize.Height);
        var before = NativeDesktop.Capture();
        var requestedFrame = request["frame_id"]?.GetValue<string>() ?? string.Empty;
        var dispatched = 0;
        var inputActions = 0;
        var inputEmitted = false;
        var queuedInputPending = false;

        for (var index = 0; index < actions.Count; index++)
        {
            var currentIsInput = false;
            try
            {
                var node = actions[index];
                var action = node as JsonObject ??
                    throw new ProviderFailureException(
                        "invalid_desktop_action",
                        "precondition",
                        "Every desktop action must be an object.");
                var type = UiaProvider.RequiredString(action, "type");
                currentIsInput = IsInputAction(type) &&
                    (type != "type" || (action["text"]?.GetValue<string>() ?? string.Empty).Length > 0);
                if (RequiresCoordinate(type))
                {
                    var currentLayout = NativeDesktop.CurrentLayout();
                    if (!string.IsNullOrEmpty(requestedFrame) &&
                        !string.Equals(
                            requestedFrame,
                            currentLayout.FrameId,
                            StringComparison.Ordinal))
                    {
                        NativeDesktop.LogStaleLayout(
                            requestedFrame,
                            currentLayout);
                        throw new ProviderFailureException(
                            "stale_layout",
                            "precondition",
                            "Desktop layout changed before coordinate input.");
                    }
                }
                // SendInput is queued; a screenshot, sleep or queue-removal
                // receipt cannot prove its focus/selection effects completed.
                // Synchronous text must not overtake queued mouse or key input.
                if (type == "type" && currentIsInput && queuedInputPending)
                {
                    throw new ProviderFailureException(
                        "desktop_input_failed", "preparation",
                        "Earlier queued input has not been confirmed processed; no text was sent. " +
                        "Observe and confirm the intended focus and selection before sending text separately; do not repeat the earlier input.",
                        inputEventsEmitted: true, mutationUnknown: true);
                }
                Execute(action, type, before);
                queuedInputPending |= RequiresCoordinate(type) || type == "keypress";
                dispatched++;
                if (currentIsInput)
                {
                    inputActions++;
                    inputEmitted = true;
                }
                if (currentIsInput && IsKeyboardAction(type) &&
                    index + 1 < actions.Count &&
                    actions[index + 1] is JsonObject nextAction &&
                    IsInputAction(UiaProvider.RequiredString(nextAction, "type")))
                {
                    // The next input waits until the foreground application
                    // has processed this key, instead of a fixed delay.
                    NativeDesktop.WaitForForegroundInputIdle(KeyboardInputIdleTimeoutMilliseconds);
                }
            }
            catch (ProviderFailureException error)
            {
                throw new ProviderFailureException(
                    error.Code,
                    error.Phase,
                    error.Message,
                    inputEmitted || error.InputEventsEmitted,
                    error.MutationUnknown,
                    dispatched,
                    inputActions,
                    actionEvidence: error.ActionEvidence,
                    factsComplete: error.FactsComplete,
                    subject: error.Subject,
                    recoveryContext: error.RecoveryContext,
                    focusPreparation: error.FocusPreparation,
                    pointerPreparation: error.PointerPreparation,
                    candidates: error.Candidates);
            }
            catch (Exception error)
            {
                throw new ProviderFailureException(
                    "desktop_action_failed",
                    "emission",
                    error.Message,
                    inputEmitted,
                    currentIsInput,
                    dispatched,
                    inputActions);
            }
        }

        try
        {
            var settleMilliseconds = request["settle_ms"]?.GetValue<int>() ?? 0;
            var observation = DesktopStillness.Observe(
                inputEmitted ? settleMilliseconds : 0,
                () => DesktopObservationRetry.Execute(NativeDesktop.CaptureForSettling),
                NativeDesktop.PixelsIdentical);
            var after = observation.Sample;
            return new JsonObject
            {
                ["ok"] = true,
                ["snapshot"] = NativeDesktop.SnapshotJson(after),
                ["mutation"] = new JsonObject
                {
                    ["input_events_emitted"] = inputEmitted,
                    ["partial_input_possible"] = false,
                    ["desktop_state_mutated"] = inputEmitted,
                    ["mutation_unknown"] = false,
                },
                ["gui_result"] = new JsonObject
                {
                    ["dispatched"] = dispatched,
                    ["input_events_emitted"] = inputEmitted,
                    ["input_actions_dispatched"] = inputActions,
                    ["window_commands_dispatched"] = 0,
                    ["desktop_state_mutated"] = inputEmitted,
                    ["mutation_unknown"] = false,
                    ["partial_input_possible"] = false,
                    ["effect_unknown"] = inputEmitted,
                    ["quiescent"] = observation.Quiescent ? true : (bool?)null,
                },
            };
        }
        catch (Exception error)
        {
            throw new ProviderFailureException(
                "final_desktop_observation_failed",
                "post_action_observation",
                error.Message,
                inputEmitted,
                false,
                dispatched,
                inputActions);
        }
    }

    private static void ValidateBatch(
        JsonArray actions,
        int desktopWidth,
        int desktopHeight)
    {
        if (actions.Count == 0)
        {
            throw new ProviderFailureException(
                "invalid_desktop_batch",
                "precondition",
                "Desktop batch requires at least one action.");
        }
        foreach (var node in actions)
        {
            var action = node as JsonObject ??
                throw new ProviderFailureException(
                    "invalid_desktop_action",
                    "precondition",
                    "Every desktop action must be an object.");
            var type = UiaProvider.RequiredString(action, "type");
            ValidateFields(action, type);
            // Resolve every key before the first action, so a bad key in a
            // later action cannot follow input that was already sent.
            try
            {
                _ = WindowsKeyMapping.ResolveChord(MouseKeys(action));
            }
            catch (InvalidOperationException error)
            {
                throw new ProviderFailureException(
                    "unsupported_key",
                    "precondition",
                    error.Message);
            }
            switch (type)
            {
                case "screenshot":
                case "wait":
                case "keypress":
                    break;
                case "type":
                    NativeDesktop.ValidateTypedText(action["text"]?.GetValue<string>() ?? string.Empty);
                    break;
                case "click":
                    var button =
                        action["button"]?.GetValue<string>() ?? "left";
                    if (button.ToLowerInvariant() is not "left" and
                        not "right" and
                        not "wheel" and
                        not "back" and
                        not "forward")
                    {
                        throw new ProviderFailureException(
                            "invalid_mouse_button",
                            "precondition",
                            "Click button must be left, right, wheel, back, or forward.");
                    }
                    ValidatePoint(action, desktopWidth, desktopHeight);
                    break;
                case "double_click":
                case "move":
                    ValidatePoint(action, desktopWidth, desktopHeight);
                    break;
                case "scroll":
                    ValidatePoint(action, desktopWidth, desktopHeight);
                    var scrollX =
                        action["scroll_x"]?.GetValue<double>() ?? 0;
                    var scrollY =
                        action["scroll_y"]?.GetValue<double>() ?? 0;
                    if (!double.IsFinite(scrollX) || !double.IsFinite(scrollY) ||
                        Math.Abs(scrollX) > MaximumScrollDelta ||
                        Math.Abs(scrollY) > MaximumScrollDelta)
                    {
                        throw new ProviderFailureException(
                            "invalid_scroll_delta",
                            "precondition",
                            "Scroll deltas must be finite and between -2000 and 2000.");
                    }
                    if (scrollX == 0 && scrollY == 0)
                    {
                        throw new ProviderFailureException(
                            "invalid_scroll_delta",
                            "precondition",
                            "Scroll requires a non-zero scroll_x or scroll_y.");
                    }
                    break;
                case "drag":
                    var path = action["path"] as JsonArray;
                    if (path is null || path.Count < 2)
                    {
                        throw new ProviderFailureException(
                            "invalid_drag_path",
                            "precondition",
                            "Drag requires at least two points.");
                    }
                    foreach (var point in path)
                    {
                        ValidatePoint(
                            point as JsonObject ??
                                throw new ProviderFailureException(
                                    "invalid_drag_path",
                                    "precondition",
                                    "Every drag point must be an object."),
                            desktopWidth,
                            desktopHeight);
                    }
                    break;
                default:
                    throw new ProviderFailureException(
                        "unsupported_desktop_action",
                        "precondition",
                        $"Unsupported desktop action {type}.");
            }
        }
    }

    private static void Execute(
        JsonObject action,
        string type,
        NativeDesktop.ScreenFrame frame)
    {
        var (x, y) = Point(action, frame);
        switch (type)
        {
            case "screenshot":
                return;
            case "wait":
                Thread.Sleep(TimeSpan.FromSeconds(1));
                return;
            case "move":
                NativeDesktop.WithHeldKeys(
                    MouseKeys(action),
                    () => NativeDesktop.MovePointer(x, y));
                return;
            case "click":
                NativeDesktop.WithHeldKeys(
                    MouseKeys(action),
                    () => NativeDesktop.Click(
                        x,
                        y,
                        action["button"]?.GetValue<string>() ?? "left",
                        1));
                return;
            case "double_click":
                NativeDesktop.WithHeldKeys(
                    MouseKeys(action),
                    () => NativeDesktop.Click(x, y, "left", 2));
                return;
            case "drag":
                var path = ((JsonArray)action["path"]!)
                    .Select(
                        node =>
                        {
                            var point = ImagePoint((JsonObject)node!, frame);
                            return new PointF((float)point.X, (float)point.Y);
                        })
                    .ToArray();
                NativeDesktop.WithHeldKeys(
                    MouseKeys(action),
                    () => NativeDesktop.Drag(path));
                return;
            case "scroll":
                NativeDesktop.WithHeldKeys(
                    MouseKeys(action),
                    () => NativeDesktop.Scroll(
                        x,
                        y,
                        ComputerScrollToWheel(action["scroll_x"]?.GetValue<double>() ?? 0),
                        ComputerScrollToWheel(action["scroll_y"]?.GetValue<double>() ?? 0)));
                return;
            case "type":
                NativeDesktop.TypeText(
                    action["text"]?.GetValue<string>() ?? string.Empty);
                return;
            case "keypress":
                var keys = (action["keys"] as JsonArray ?? new JsonArray())
                    .Select(node => node?.GetValue<string>() ?? string.Empty)
                    .ToArray();
                NativeDesktop.PressKeys(keys);
                return;
        }
    }

    private static void ValidateFields(JsonObject action, string type)
    {
        IReadOnlySet<string> allowed = type switch
        {
            "screenshot" => new HashSet<string> { "type" },
            "wait" => new HashSet<string> { "type" },
            "click" => new HashSet<string>
                { "type", "x", "y", "button", "keys" },
            "double_click" or "move" => new HashSet<string>
                { "type", "x", "y", "keys" },
            "drag" => new HashSet<string> { "type", "path", "keys" },
            "scroll" => new HashSet<string>
                { "type", "x", "y", "scroll_x", "scroll_y", "keys" },
            "type" => new HashSet<string> { "type", "text" },
            "keypress" => new HashSet<string> { "type", "keys" },
            _ => new HashSet<string> { "type" },
        };
        var unexpected = action
            .Select(property => property.Key)
            .Where(name => !allowed.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unexpected.Length > 0)
        {
            throw new ProviderFailureException(
                "invalid_desktop_action_fields",
                "precondition",
                $"Desktop action {type} has unsupported field(s): " +
                string.Join(", ", unexpected) + ".");
        }
    }

    private static string[] MouseKeys(JsonObject action)
    {
        return (action["keys"] as JsonArray ?? new JsonArray())
            .Select(node => node?.GetValue<string>() ?? string.Empty)
            .ToArray();
    }

    // GUI scroll uses Computer deltas (100 per wheel notch, as on Linux);
    // NativeDesktop.Scroll takes wheel notches, which AX page scrolling uses.
    private static double ComputerScrollToWheel(double delta)
    {
        return delta == 0 ? 0 :
            Math.CopySign(Math.Max(1, Math.Abs(Math.Round(delta / 100))), delta);
    }

    private static bool IsKeyboardAction(string type)
    {
        return type is "type" or "keypress";
    }

    private static bool IsInputAction(string type)
    {
        return IsKeyboardAction(type) ||
            type is "move" or "click" or "double_click" or "drag" or "scroll";
    }

    private static bool RequiresCoordinate(string type)
    {
        return type is "move" or "click" or "double_click" or "drag" or "scroll";
    }

    private static void ValidatePoint(
        JsonObject action,
        int desktopWidth,
        int desktopHeight)
    {
        var x = action["x"]?.GetValue<double>() ??
            throw new ProviderFailureException(
                "invalid_coordinate",
                "precondition",
                "Coordinate action requires x.");
        var y = action["y"]?.GetValue<double>() ??
            throw new ProviderFailureException(
                "invalid_coordinate",
                "precondition",
                "Coordinate action requires y.");
        if (!WindowsDesktopImageGeometry.ContainsPoint(
                x, y, desktopWidth, desktopHeight))
        {
            // The core reports invalid_gui_action as invalid_argument; the
            // Linux and macOS providers reject the same points.
            throw new ProviderFailureException(
                "invalid_gui_action",
                "precondition",
                $"Coordinate ({x}, {y}) is outside the full-desktop image " +
                $"pixels 0..{desktopWidth - 1} x 0..{desktopHeight - 1}; " +
                "take a new screenshot.",
                factsComplete: true);
        }
    }

    private static (double X, double Y) Point(
        JsonObject action,
        NativeDesktop.ScreenFrame frame)
    {
        var type = action["type"]?.GetValue<string>() ?? string.Empty;
        if (!RequiresCoordinate(type) || type == "drag")
        {
            return (0, 0);
        }
        return (
            WindowsDesktopImageGeometry.ToNativeCoordinate(
                action["x"]!.GetValue<double>(),
                frame.Width,
                frame.OriginX,
                frame.NativeWidth),
            WindowsDesktopImageGeometry.ToNativeCoordinate(
                action["y"]!.GetValue<double>(),
                frame.Height,
                frame.OriginY,
                frame.NativeHeight));
    }

    private static (double X, double Y) ImagePoint(
        JsonObject point,
        NativeDesktop.ScreenFrame frame)
    {
        return (
            WindowsDesktopImageGeometry.ToNativeCoordinate(
                point["x"]!.GetValue<double>(),
                frame.Width,
                frame.OriginX,
                frame.NativeWidth),
            WindowsDesktopImageGeometry.ToNativeCoordinate(
                point["y"]!.GetValue<double>(),
                frame.Height,
                frame.OriginY,
                frame.NativeHeight));
    }
}
