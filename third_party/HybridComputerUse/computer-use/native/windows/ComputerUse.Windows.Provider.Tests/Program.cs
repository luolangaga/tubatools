using System.Text.Json;
using System.Text.Json.Nodes;
using ComputerUse.Windows.Runtime;

SelectionTests.Run();
KeyboardFocusTests.Run();
DocumentTextTests.Run();
WindowReferenceReadTests.Run();
if (args is ["--selection-contract"])
{
    return 0;
}
ExactFocusSettlementTests.Run();
FocusPreparationTests.Run();
LogicalWindowBoundaryTests.Run();
BoundedTreeDiscoveryTests.Run();
TransientDiscoveryBoundaryTests.Run();
SpreadsheetGridTests.Run();
SpreadsheetReadbackTests.Run();
CanonicalNodeIdentitySetTests.Run();
ProjectedRootSlotTests.Run();
WindowsFactTests.Run();
HandlelessSemanticWindowTests.Run();
if (OptionalRoleRegression.Run() != 0)
{
    throw new InvalidOperationException("Optional role ownership regression failed.");
}

if (args is ["--focus-preparation"])
{
    Console.WriteLine("Exact focus settlement and preparation tests passed.");
    return 0;
}

if (args.Length != 1)
{
    Console.Error.WriteLine(
        "usage: ComputerUse.Windows.Provider.Tests <provider-wire-fixture-dir>");
    return 2;
}

var fixtureRoot = Path.GetFullPath(args[0]);
NativeManifestTests.Run(fixtureRoot);
var manifest = JsonNode.Parse(
    File.ReadAllText(Path.Combine(fixtureRoot, "manifest.json"))
) as JsonObject ?? throw new InvalidDataException("manifest must be an object");
var fixtures = manifest["fixtures"] as JsonArray ??
    throw new InvalidDataException("manifest fixtures must be an array");
var validCount = 0;
foreach (var node in fixtures)
{
    var fixture = node as JsonObject ??
        throw new InvalidDataException("fixture entry must be an object");
    if (fixture["direction"]?.GetValue<string>() != "request")
    {
        continue;
    }
    var file = fixture["file"]?.GetValue<string>() ??
        throw new InvalidDataException("fixture file is required");
    var request = JsonNode.Parse(
        File.ReadAllText(Path.Combine(fixtureRoot, file))
    ) as JsonObject ?? throw new InvalidDataException($"{file} must be an object");
    ProviderServer.ValidateRequest(request);
    if (request["action"]?["target"] is JsonObject target)
    {
        target["automationId"] = "save-button";
        ProviderServer.ValidateRequest(request);
        target["automationId"] = 42;
        try
        {
            ProviderServer.ValidateRequest(request);
            throw new InvalidDataException("Numeric target automationId was accepted.");
        }
        catch (ProviderFailureException)
        {
        }
    }
    validCount++;
}

var invalid = JsonNode.Parse(
    File.ReadAllText(Path.Combine(fixtureRoot, "invalid-requests.json"))
) as JsonArray ?? throw new InvalidDataException(
    "invalid-requests.json must be an array");
var rejectedCount = 0;
foreach (var node in invalid)
{
    var fixture = node as JsonObject ??
        throw new InvalidDataException("invalid fixture must be an object");
    var name = fixture["name"]?.GetValue<string>() ?? "unnamed";
    var request = fixture["request"] as JsonObject ??
        throw new InvalidDataException($"{name}: request must be an object");
    try
    {
        ProviderServer.ValidateRequest(request);
        throw new InvalidDataException(
            $"C# provider validator accepted invalid fixture: {name}");
    }
    catch (ProviderFailureException)
    {
        rejectedCount++;
    }
}

UiaRootProjectionTests.Run();
ValueReadbackRoutingTests.Run();
SemanticResolutionTests.Run();

var validSchemaCases = new[]
{
    (
        "target allowProviderRebind",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"valid-rebind",
          "operation":"resolve_and_act",
          "action":{
            "protocol_version":2,
            "intent":"click",
            "app":"Editor",
            "app_identity":{
              "providerBusName":"uia3:pid:42",
              "processGeneration":"123"
            },
            "window_handle":"w2:fixture",
            "target":{
              "selector":"button \"Save\"",
              "allowProviderRebind":true,
              "providerObservedName":"Save  now",
              "hasProviderObservedName":true,
              "windowHandle":"w2:fixture",
              "hasName":false
            },
            "input":{"click_count":1}
          }
        }
        """
    ),
    (
        "empty app identity fields remain schema-valid",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"valid-empty-identity",
          "operation":"observe",
          "observe":{
            "protocol_version":2,
            "app":"Editor",
            "app_identity":{
              "providerBusName":"",
              "processGeneration":""
            },
            "window_handle":""
          }
        }
        """
    ),
    (
        "action expected window identity",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"valid-expected-window",
          "operation":"resolve_and_act",
          "action":{
            "protocol_version":2,
            "intent":"keypress",
            "app":"Editor",
            "window_handle":"w2:fixture",
            "expected_window":{
              "windowHandle":"w2:fixture",
              "providerBusName":"win32:pid:42",
              "providerObjectPath":"w2:fixture",
              "processGeneration":"123",
              "role":"window",
              "name":"Editor"
            },
            "input":{"key":"CTRL+,"}
          }
        }
        """
    ),
    (
        "screenshot may appear inside a desktop batch",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"valid-screenshot-batch",
          "operation":"desktop_execute_batch",
          "desktop":{
            "protocol_version":2,
            "actions":[
              {"type":"screenshot"},
              {"type":"move","x":10,"y":20}
            ],
            "settle_ms":0,
            "frame_id":"layout-1"
          }
        }
        """
    ),
};
foreach (var (name, json) in validSchemaCases)
{
    var request = JsonNode.Parse(json) as JsonObject ??
        throw new InvalidDataException($"{name}: request must be an object");
    ProviderServer.ValidateRequest(request);
    validCount++;
}

TargetResolutionPolicyTests.Run();
DesktopLayoutPolicyTests.Run();
AppInventoryTests.Run();
DesktopZoomTests.Run(fixtureRoot);

var validIntents = new Dictionary<string, string>
{
    ["resolve_target"] = """
        "target":{"selector":"button \"Save\"","hasName":false}
        """,
    ["click"] = """
        "target":{"selector":"button \"Save\"","hasName":false},
        "input":{"click_count":1}
        """,
    ["scroll"] = """
        "window_handle":"w2:fixture",
        "input":{"direction":"down","pages":1}
        """,
    ["type"] = """
        "window_handle":"w2:fixture",
        "input":{"text":"hello"}
        """,
    ["keypress"] = """
        "window_handle":"w2:fixture",
        "input":{"key":"ENTER"}
        """,
    ["set_value"] = """
        "target":{"selector":"entry \"Name\"","hasName":false},
        "input":{"value":"Ada"}
        """,
};
foreach (var (intent, body) in validIntents)
{
    var request = JsonNode.Parse(
        $$"""
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"valid-intent-{{intent}}",
          "operation":"resolve_and_act",
          "action":{
            "protocol_version":2,
            "intent":"{{intent}}",
            "app":"Editor",
            {{body}}
          }
        }
        """) as JsonObject ??
        throw new InvalidDataException($"{intent}: request must be an object");
    ProviderServer.ValidateRequest(request);
    validCount++;
}

InputPolicyTests.Run();

var captureSnapshotRequest = JsonNode.Parse(
    """
    {
      "protocol":"computer-use-provider",
      "schema_version":2,
      "request_id":"valid-capture-snapshot",
      "operation":"resolve_and_act",
      "action":{
        "protocol_version":2,
        "intent":"click",
        "app":"Editor",
        "target":{"selector":"button \"Save\"","hasName":false},
        "input":{"click_count":1},
        "observation":{"capture_snapshot":false}
      }
    }
    """) as JsonObject ??
    throw new InvalidDataException("capture snapshot request must be an object");
ProviderServer.ValidateRequest(captureSnapshotRequest);
validCount++;

var invalidSchemaCases = new[]
{
    (
        "observation rejects a window screenshot",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"invalid-observe-screenshot",
          "operation":"observe",
          "observe":{
            "protocol_version":2,
            "app":"Editor",
            "observation":{"include_screenshot":true}
          }
        }
        """
    ),
    (
        "observation rejects screenshot_scope",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"invalid-observe-scope",
          "operation":"observe",
          "observe":{
            "protocol_version":2,
            "app":"Editor",
            "observation":{"include_screenshot":false,"screenshot_scope":"window"}
          }
        }
        """
    ),
    (
        "target rejects unknown field",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"invalid-target-field",
          "operation":"resolve_and_act",
          "action":{
            "protocol_version":2,
            "intent":"click",
            "app":"Editor",
            "target":{
              "selector":"button \"Save\"",
              "allowProviderRebind":true,
              "compatibilityTarget":"hidden",
              "hasName":false
            },
            "input":{"click_count":1}
          }
        }
        """
    ),
    (
        "target rejects string allowProviderRebind",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"invalid-rebind-type",
          "operation":"resolve_and_act",
          "action":{
            "protocol_version":2,
            "intent":"click",
            "app":"Editor",
            "target":{
              "selector":"button \"Save\"",
              "allowProviderRebind":"true",
              "hasName":false
            },
            "input":{"click_count":1}
          }
        }
        """
    ),
    (
        "expected window rejects missing process generation",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"invalid-expected-window",
          "operation":"resolve_and_act",
          "action":{
            "protocol_version":2,
            "intent":"keypress",
            "app":"Editor",
            "window_handle":"w2:fixture",
            "expected_window":{
              "windowHandle":"w2:fixture",
              "providerBusName":"win32:pid:42",
              "role":"window"
            },
            "input":{"key":"CTRL+,"}
          }
        }
        """
    ),
    (
        "observe rejects numeric window_handle",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"invalid-window-handle",
          "operation":"observe",
          "observe":{
            "protocol_version":2,
            "app":"Editor",
            "window_handle":42
          }
        }
        """
    ),
    (
        "desktop rejects numeric frame_id",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"invalid-frame-id",
          "operation":"desktop_execute_batch",
          "desktop":{
            "protocol_version":2,
            "actions":[{"type":"screenshot"}],
            "settle_ms":0,
            "frame_id":42
          }
        }
        """
    ),
    (
        "desktop rejects hidden action field",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"invalid-hidden-action",
          "operation":"desktop_execute_batch",
          "desktop":{
            "protocol_version":2,
            "actions":[{"type":"wait","seconds":1}],
            "settle_ms":0
          }
        }
        """
    ),
    (
        "seventh AX intent remains rejected",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"invalid-extra-intent",
          "operation":"resolve_and_act",
          "action":{
            "protocol_version":2,
            "intent":"invoke",
            "app":"Editor",
            "target":{"selector":"button \"Save\"","hasName":false}
          }
        }
        """
    ),
    (
        "seventh operation remains rejected",
        """
        {
          "protocol":"computer-use-provider",
          "schema_version":2,
          "request_id":"invalid-extra-operation",
          "operation":"shutdown",
          "observe_apps":{"protocol_version":2}
        }
        """
    ),
};
foreach (var (name, json) in invalidSchemaCases)
{
    var request = JsonNode.Parse(json) as JsonObject ??
        throw new InvalidDataException($"{name}: request must be an object");
    try
    {
        ProviderServer.ValidateRequest(request);
        throw new InvalidDataException(
            $"C# provider validator accepted invalid fixture: {name}");
    }
    catch (ProviderFailureException)
    {
        rejectedCount++;
    }
}

ObservationRetryTests.Run();
ProviderServerRequestTests.Run();
TraversalAndActivationTests.Run();

Console.WriteLine(JsonSerializer.Serialize(new
{
    passed = true,
    valid_requests = validCount,
    invalid_requests_rejected = rejectedCount,
}));
return 0;

internal sealed record TestWindow(
    long Handle,
    bool Active,
    long OwnerHandle = 0,
    int ProcessId = 11);

internal sealed record TestSiblingNode(
    string Name,
    TestSiblingNode? Next);

internal sealed class ThrowingDisposable : IDisposable
{
    public void Dispose()
    {
        throw new TimeoutException("Synthetic cleanup timeout.");
    }
}
