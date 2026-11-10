using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class ProviderServer
{
    private static void ValidateAction(JsonObject payload)
    {
        var intent = RequiredStringValue(payload, "intent");
        var app = RequiredStringValue(payload, "app");
        if (string.IsNullOrWhiteSpace(app))
        {
            throw InvalidRequest("Provider accessibility app is required.");
        }
        var hasTarget = payload["target"] is not null ||
            payload["element"] is not null;
        var input = payload["input"];
        var windowHandle = StringValue(payload, "window_handle") ?? string.Empty;
        switch (intent)
        {
            case "resolve_target":
                if (!hasTarget)
                {
                    throw InvalidRequest(
                        "Provider resolve_target requires an exact target.");
                }
                if (input is not null)
                {
                    throw InvalidRequest(
                        "Provider resolve_target does not accept action input.");
                }
                break;
            case "click":
                RequireTargetAndInput(
                    hasTarget,
                    input,
                    "click",
                    Fields(
                        "x", "y", "click_count", "mouse_button",
                        "click_method"));
                break;
            case "scroll":
                ValidateActionInput(
                    input,
                    "scroll",
                    Fields("direction", "pages", "window_centre"));
                RequireNonEmptyString((JsonObject)input!, "direction", "action.input");
                RequirePositiveNumber((JsonObject)input!, "pages", "action.input");
                RequireTargetOrWindow(hasTarget, windowHandle, "scroll");
                break;
            case "type":
                ValidateActionInput(input, "type", Fields("text"));
                RequireNonEmptyString((JsonObject)input!, "text", "action.input");
                RequireTargetOrWindow(hasTarget, windowHandle, "type");
                break;
            case "keypress":
                ValidateActionInput(input, "keypress", Fields("key"));
                RequireNonEmptyString((JsonObject)input!, "key", "action.input");
                RequireTargetOrWindow(hasTarget, windowHandle, "keypress");
                break;
            case "set_value":
                RequireTargetAndInput(
                    hasTarget,
                    input,
                    "set_value",
                    Fields("value", "require_semantic_write"));
                ValidateOptionalBoolean((JsonObject)input!, "require_semantic_write", "action.input");
                if (!((JsonObject)input!).ContainsKey("value") ||
                    ((JsonObject)input!)["value"] is not JsonValue value ||
                    !value.TryGetValue<string>(out _))
                {
                    throw InvalidRequest(
                        "Provider set_value value is required.");
                }
                break;
            case "select":
                RequireTargetAndInput(hasTarget, input, "select", Fields("text", "to_target"));
                var selection = (JsonObject)input!;
                ValidateOptionalString(selection, "text", "action.input");
                ValidateTarget(selection["to_target"], "action.input.to_target");
                if (!string.IsNullOrEmpty(StringValue(selection, "text")) &&
                    selection["to_target"] is not null)
                {
                    throw InvalidRequest("Provider select accepts text or to_target, not both.");
                }
                break;
            default:
                throw InvalidRequest(
                    $"Unsupported provider accessibility intent {intent}.");
        }
    }

    internal static void ValidateWindowsActionCapabilities(JsonObject payload)
    {
        // The Windows projection never issues editable-partner identities.
        // Accept the shared wire shape, but do not silently write its owner.
        if (payload["target"]?["editTarget"] is not null)
        {
            throw new ProviderFailureException(
                "capability_unsupported", "precondition",
                "Windows does not issue editable-partner targets; observe and target the editable UIA control directly.",
                inputEventsEmitted: false);
        }
        if (StringValue(payload, "intent") == "select" &&
            payload["input"]?["to_target"] is not null)
        {
            throw new ProviderFailureException(
                "capability_unsupported", "precondition",
                "Windows cannot verify selection across two targets; select text within one UIA text control instead.",
                inputEventsEmitted: false);
        }
    }

    private static void ValidateActionInput(
        JsonNode? node,
        string intent,
        IReadOnlySet<string> fields)
    {
        var input = node as JsonObject ??
            throw InvalidRequest(
                $"Provider {intent} requires {intent} input only.");
        RejectUnknownFields(input, fields, "action.input");
    }

    private static void RequireTargetAndInput(
        bool hasTarget,
        JsonNode? input,
        string intent,
        IReadOnlySet<string> fields)
    {
        if (!hasTarget)
        {
            throw InvalidRequest(
                $"Provider {intent} requires an exact target.");
        }
        ValidateActionInput(input, intent, fields);
    }

    private static void RequireTargetOrWindow(
        bool hasTarget,
        string windowHandle,
        string intent)
    {
        if (!hasTarget && string.IsNullOrWhiteSpace(windowHandle))
        {
            throw InvalidRequest(
                $"Provider untargeted {intent} requires a logical window.");
        }
    }

    private static void ValidateWindowAction(JsonObject payload)
    {
        var action = RequiredStringValue(payload, "action");
        if (action is not "activate" and not "close")
        {
            throw InvalidRequest(
                $"Unsupported provider window action {action}.");
        }
        if (string.IsNullOrWhiteSpace(RequiredStringValue(payload, "app")))
        {
            throw InvalidRequest("Provider window-action app is required.");
        }
        if (payload["target"] is null)
        {
            throw InvalidRequest(
                "Provider window action requires an exact target.");
        }
    }

    private static void ValidateElement(JsonNode? node, string path)
    {
        var value = OptionalObject(node, path);
        if (value is null)
        {
            return;
        }
        RejectUnknownFields(
            value,
            Fields(
                "index", "runtimeId", "automationId", "providerBusName",
                "providerObjectPath", "processGeneration",
                "providerIdentityScope", "providerWindowBusName",
                "providerWindowObjectPath",
                "providerWindowProcessGeneration", "providerWindowRole",
                "providerWindowName", "showing", "visible", "nameHash",
                "name", "controlType", "localizedControlType",
                "className", "value", "nativeWindowHandle", "frame",
                "actions", "states", "description", "textAttributes",
                "placeholder", "relations", "relationReadComplete",
                "nameSource"),
            path);
        ValidateOptionalObject(
            value["frame"],
            $"{path}.frame",
            Fields("x", "y", "width", "height"));
        var relations = OptionalObject(value["relations"], $"{path}.relations");
        if (relations is null)
        {
            return;
        }
        foreach (var relation in relations)
        {
            if (relation.Value is not JsonArray targets)
            {
                throw InvalidRequest(
                    $"{path}.relations.{relation.Key} must be an array.");
            }
            for (var index = 0; index < targets.Count; index++)
            {
                ValidateRequiredObject(
                    targets[index],
                    $"{path}.relations.{relation.Key}[{index}]",
                    Fields(
                        "role", "name", "providerBusName",
                        "providerObjectPath", "processGeneration"));
            }
        }
    }

    private static ProviderFailureException InvalidRequest(string message)
    {
        return new ProviderFailureException(
            "invalid_request",
            "precondition",
            message);
    }

    private static ProviderFailureException ProtocolMismatch(string message)
    {
        return new ProviderFailureException(
            "protocol_mismatch",
            "precondition",
            message);
    }

    private static string DomainPayload(string operation)
    {
        return operation switch
        {
            "observe_apps" or "observe" or "resolve_and_act" => "accessibility",
            "desktop_execute_batch" => "desktop",
            "window_list" or "window_action" => "window",
            _ => "accessibility",
        };
    }
}
