using System.Text.Json;
using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class ProviderServer : IDisposable
{
    private readonly UiaProvider _uia = new();

    internal void Run()
    {
        string? line;
        while ((line = Console.ReadLine()) is not null)
        {
            JsonObject response;
            try
            {
                var request = JsonNode.Parse(line) as JsonObject ??
                    throw new ProviderFailureException(
                        "invalid_request",
                        "precondition",
                        "Provider request must be a JSON object.");
                response = Dispatch(request);
            }
            catch (Exception error)
            {
                response = FatalResponse(error);
            }
            Console.Out.WriteLine(response.ToJsonString());
            Console.Out.Flush();
        }
    }

    internal JsonObject Dispatch(JsonObject request)
    {
        var requestId = StringValue(request, "request_id") ?? "invalid";
        var operation = StringValue(request, "operation") ?? "observe_apps";

        try
        {
            ValidateRequest(request);
            var response = Envelope(requestId, operation);
            switch (operation)
            {
                case "observe_apps":
                    response["accessibility"] = _uia.ObserveApps();
                    break;
                case "observe":
                    response["accessibility"] = _uia.Observe(
                        RequiredPayload(request, "observe"));
                    break;
                case "resolve_and_act":
                    ValidateWindowsActionCapabilities(RequiredPayload(request, "action"));
                    response["accessibility"] = _uia.ResolveAndAct(
                        RequiredPayload(request, "action"));
                    break;
                case "desktop_execute_batch":
                    response["desktop"] = DesktopProvider.ExecuteBatch(
                        RequiredPayload(request, "desktop"));
                    break;
                case "window_list":
                    response["window"] = _uia.ListWindows(
                        RequiredPayload(request, "window_list"));
                    break;
                case "window_action":
                    ValidateWindowsActionCapabilities(RequiredPayload(request, "window_action"));
                    response["window"] = _uia.ActOnWindow(
                        RequiredPayload(request, "window_action"));
                    break;
                default:
                    throw new ProviderFailureException(
                        "unsupported_operation",
                        "precondition",
                        $"Unsupported provider operation {operation}.");
            }
            return response;
        }
        catch (Exception error)
        {
            return FailureEnvelope(
                requestId,
                OperationPayloads.ContainsKey(operation)
                    ? operation
                    : "observe_apps",
                error,
                IsMutationOperation(request, operation));
        }
    }

    public void Dispose()
    {
        _uia.Dispose();
    }
}
