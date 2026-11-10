using System.Text.Json;
using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class ProviderServer
{
    private const string Protocol = "computer-use-provider";
    private const int SchemaVersion = 2;
    private static readonly IReadOnlySet<string> EnvelopeFields =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "protocol",
            "schema_version",
            "request_id",
            "operation",
            "observe_apps",
            "observe",
            "action",
            "desktop",
            "window_list",
            "window_action",
        };
    private static readonly IReadOnlyDictionary<string, string> OperationPayloads =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["observe_apps"] = "observe_apps",
            ["observe"] = "observe",
            ["resolve_and_act"] = "action",
            ["desktop_execute_batch"] = "desktop",
            ["window_list"] = "window_list",
            ["window_action"] = "window_action",
        };
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>>
        PayloadFields =
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["observe_apps"] = Fields("protocol_version"),
                ["observe"] = Fields(
                    "protocol_version",
                    "app",
                    "app_identity",
                    "window_handle",
                    "observation"),
                ["action"] = Fields(
                    "protocol_version",
                    "intent",
                    "app",
                    "app_identity",
                    "window_handle",
                    "expected_window",
                    "element",
                    "target",
                    "input",
                    "window_bounds",
                    "observation"),
                ["desktop"] = Fields(
                    "protocol_version",
                    "actions",
                    "settle_ms",
                    "frame_id"),
                ["window_list"] = Fields(
                    "protocol_version",
                    "app",
                    "app_identity",
                    "window_handle"),
                ["window_action"] = Fields(
                    "protocol_version",
                    "action",
                    "app",
                    "app_identity",
                    "window_handle",
                    "target",
                    "observation"),
            };
    internal static void ValidateRequest(JsonObject request)
    {
        // Recognize the optional operation only to reject it. Windows has no
        // ordered-input backend and must never dispatch these requests.
        if (StringValue(request, "operation") == "ordered_input")
        {
            throw new ProviderFailureException(
                "unsupported_operation", "precondition",
                "Windows does not support ordered_input.");
        }
        RejectUnknownFields(request, EnvelopeFields, "Provider request");

        var protocol = RequiredStringValue(request, "protocol");
        var version = RequiredIntValue(request, "schema_version");
        var requestId = RequiredStringValue(request, "request_id");
        var operation = RequiredStringValue(request, "operation");
        if (!string.Equals(protocol, Protocol, StringComparison.Ordinal))
        {
            throw ProtocolMismatch(
                $"Provider protocol = {JsonSerializer.Serialize(protocol)}, " +
                $"want {JsonSerializer.Serialize(Protocol)}.");
        }
        if (version != SchemaVersion)
        {
            throw ProtocolMismatch(
                $"Provider schema version = {version}, want {SchemaVersion}.");
        }
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw InvalidRequest("Provider request_id is required.");
        }
        if (!OperationPayloads.TryGetValue(operation, out var expectedPayload))
        {
            throw new ProviderFailureException(
                "unsupported_operation",
                "precondition",
                $"Unsupported provider operation {operation}.");
        }

        var activePayloads = OperationPayloads.Values
            .Distinct(StringComparer.Ordinal)
            .Where(name => request[name] is not null)
            .ToArray();
        if (activePayloads.Length != 1)
        {
            throw InvalidRequest(
                "Provider request must contain exactly one payload, got " +
                $"{activePayloads.Length}.");
        }
        if (!string.Equals(
                activePayloads[0],
                expectedPayload,
                StringComparison.Ordinal))
        {
            throw InvalidRequest(
                $"Provider operation {operation} does not match payload " +
                $"{activePayloads[0]}.");
        }

        var payload = request[expectedPayload] as JsonObject ??
            throw InvalidRequest(
                $"Provider request payload {expectedPayload} must be an object.");
        RejectUnknownFields(
            payload,
            PayloadFields[expectedPayload],
            $"Provider request payload {expectedPayload}");
        var payloadVersion = RequiredIntValue(payload, "protocol_version");
        if (payloadVersion != SchemaVersion)
        {
            throw ProtocolMismatch(
                $"Provider payload protocol version = {payloadVersion}, " +
                $"want {SchemaVersion}.");
        }
        ValidateNestedPayload(expectedPayload, payload);
    }

    private static void ValidateNestedPayload(
        string payloadName,
        JsonObject payload)
    {
        switch (payloadName)
        {
            case "observe":
                ValidateAppIdentity(payload["app_identity"], "observe.app_identity");
                ValidateOptionalString(payload, "window_handle", "observe");
                ValidateObservation(payload["observation"], "observe.observation");
                RequireNonEmptyString(payload, "app", "observe");
                break;
            case "action":
                ValidateAppIdentity(payload["app_identity"], "action.app_identity");
                ValidateOptionalString(payload, "window_handle", "action");
                ValidateExpectedWindow(
                    payload["expected_window"],
                    "action.expected_window");
                ValidateElement(payload["element"], "action.element");
                ValidateTarget(payload["target"], "action.target");
                ValidateAction(payload);
                ValidateOptionalObject(
                    payload["window_bounds"],
                    "action.window_bounds",
                    Fields("x", "y", "width", "height"));
                ValidateObservation(payload["observation"], "action.observation");
                break;
            case "desktop":
                ValidateDesktopActions(payload["actions"]);
                ValidateOptionalString(payload, "frame_id", "desktop");
                if (RequiredIntValue(payload, "settle_ms") < 0)
                {
                    throw InvalidRequest(
                        "Provider desktop settle_ms must be non-negative.");
                }
                break;
            case "window_list":
                ValidateAppIdentity(
                    payload["app_identity"],
                    "window_list.app_identity");
                ValidateOptionalString(
                    payload,
                    "window_handle",
                    "window_list");
                RequireNonEmptyString(payload, "app", "window_list");
                break;
            case "window_action":
                ValidateAppIdentity(
                    payload["app_identity"],
                    "window_action.app_identity");
                ValidateOptionalString(
                    payload,
                    "window_handle",
                    "window_action");
                ValidateTarget(payload["target"], "window_action.target");
                ValidateObservation(
                    payload["observation"],
                    "window_action.observation");
                ValidateWindowAction(payload);
                break;
        }
    }

    private static void ValidateAppIdentity(JsonNode? node, string path)
    {
        var value = OptionalObject(node, path);
        if (value is null)
        {
            return;
        }
        RejectUnknownFields(
            value,
            Fields("providerBusName", "processGeneration"),
            path);
        ValidateOptionalString(value, "providerBusName", path);
        ValidateOptionalString(value, "processGeneration", path);
    }

    private static void ValidateExpectedWindow(JsonNode? node, string path)
    {
        var value = OptionalObject(node, path);
        if (value is null)
        {
            return;
        }
        RejectUnknownFields(
            value,
            Fields(
                "windowHandle",
                "providerBusName",
                "providerObjectPath",
                "processGeneration",
                "role",
                "name"),
            path);
        RequireString(value, "windowHandle", path, allowEmpty: false);
        RequireString(value, "providerBusName", path, allowEmpty: false);
        ValidateOptionalString(value, "providerObjectPath", path);
        RequireString(value, "processGeneration", path, allowEmpty: false);
        RequireString(value, "role", path, allowEmpty: false);
        ValidateOptionalString(value, "name", path);
    }

    private static void ValidateObservation(JsonNode? node, string path)
    {
        var value = OptionalObject(node, path);
        if (value is null)
        {
            return;
        }
        RejectUnknownFields(
            value,
            Fields(
                "text_limit", "max_tree_nodes", "max_tree_depth",
                "prune", "boxes", "include_screenshot",
                "capture_snapshot", "recheck_after_action"),
            path);
        ValidatePositiveTextLimit(value, path);
        ValidateNonNegativeInteger(value, "max_tree_nodes", path);
        ValidateNonNegativeInteger(value, "max_tree_depth", path);
        ValidateOptionalBoolean(value, "prune", path);
        ValidateOptionalBoolean(value, "boxes", path);
        ValidateOptionalBoolean(value, "include_screenshot", path);
        ValidateOptionalBoolean(value, "capture_snapshot", path);
        ValidateOptionalBoolean(value, "recheck_after_action", path);
        if (value["include_screenshot"]?.GetValue<bool>() == true)
        {
            // AX images are full-desktop GUI frames that the core captures
            // with a desktop batch; this provider never captures a window.
            throw InvalidRequest(
                $"{path}.include_screenshot is not supported; request a desktop batch screenshot.");
        }
    }

    private static void ValidateTarget(JsonNode? node, string path)
    {
        var value = OptionalObject(node, path);
        if (value is null)
        {
            return;
        }
        RejectUnknownFields(
            value,
            Fields(
                "selector", "locatorKind", "allowProviderRebind",
                "windowHandle",
                "providerBusName", "providerObjectPath",
                "processGeneration", "providerObservedName",
                "hasProviderObservedName", "providerWindowBusName",
                "providerWindowObjectPath",
                "providerWindowProcessGeneration", "providerWindowRole",
                "providerWindowName", "providerRoleAliases",
                "providerIdentityScope", "automationId", "runtimeId", "runtimeAliases",
                "role", "name", "hasName", "predicates", "editTarget"),
            path);
        var editTarget = OptionalObject(value["editTarget"], $"{path}.editTarget");
        if (editTarget is not null)
        {
            RejectUnknownFields(editTarget,
                Fields("providerBusName", "providerObjectPath", "processGeneration"),
                $"{path}.editTarget");
            foreach (var field in new[] { "providerBusName", "providerObjectPath", "processGeneration" })
            {
                RequireNonEmptyString(editTarget, field, $"{path}.editTarget");
            }
        }
        ValidateOptionalString(value, "selector", path);
        ValidateOptionalString(value, "locatorKind", path);
        ValidateOptionalBoolean(value, "allowProviderRebind", path);
        ValidateOptionalString(value, "windowHandle", path);
        ValidateOptionalString(value, "providerBusName", path);
        ValidateOptionalString(value, "providerObjectPath", path);
        ValidateOptionalString(value, "processGeneration", path);
        ValidateOptionalString(value, "providerObservedName", path);
        ValidateOptionalBoolean(value, "hasProviderObservedName", path);
        ValidateOptionalString(value, "providerWindowBusName", path);
        ValidateOptionalString(value, "providerWindowObjectPath", path);
        ValidateOptionalString(
            value,
            "providerWindowProcessGeneration",
            path);
        ValidateOptionalString(value, "providerWindowRole", path);
        ValidateOptionalString(value, "providerWindowName", path);
        ValidateOptionalString(value, "providerIdentityScope", path);
        ValidateOptionalString(value, "automationId", path);
        ValidateOptionalString(value, "role", path);
        ValidateOptionalString(value, "name", path);
        ValidateOptionalBoolean(value, "hasName", path);
        ValidateOptionalStringArray(value, "providerRoleAliases", path);
        ValidateOptionalIntegerArray(value, "runtimeId", path);
        ValidateOptionalIntegerArrayArray(value, "runtimeAliases", path);
        var predicatesNode = value["predicates"];
        if (predicatesNode is null)
        {
            return;
        }
        if (predicatesNode is not JsonArray predicates)
        {
            throw InvalidRequest($"{path}.predicates must be an array.");
        }
        for (var index = 0; index < predicates.Count; index++)
        {
            var predicate = RequiredObject(
                predicates[index],
                $"{path}.predicates[{index}]");
            RejectUnknownFields(
                predicate,
                Fields("kind", "value"),
                $"{path}.predicates[{index}]");
            RequireString(
                predicate,
                "kind",
                $"{path}.predicates[{index}]",
                allowEmpty: false);
            RequireString(
                predicate,
                "value",
                $"{path}.predicates[{index}]",
                allowEmpty: true);
        }
    }

}
