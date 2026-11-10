using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider : IDisposable
{
    internal static ProviderFailureException? FocusPreparationFailureForTest { get; set; }

    internal JsonObject ObserveApps() => throw new NotSupportedException();
    internal JsonObject Observe(JsonObject request)
    {
        var app = request["app"]?.GetValue<string>();
        if (app == "StaleApp")
        {
            throw WindowsProviderFailures.StaleApp(
                "Synthetic stale application identity.");
        }
        if (app == "StaleWindow")
        {
            throw WindowsProviderFailures.StaleWindow(
                "Synthetic stale window identity.",
                new JsonObject
                {
                    ["sourceAppStatus"] = "alive",
                    ["sourceWindowStatus"] = "gone",
                    ["inventoryComplete"] = true,
                });
        }
        throw new NotSupportedException();
    }
    internal JsonObject ResolveAndAct(JsonObject request)
    {
        if (request["app"]?.GetValue<string>() == "FocusPreparationFailure")
        {
            if (FocusPreparationFailureForTest is { } focusFailure)
            {
                throw focusFailure;
            }
            throw new InvalidOperationException("Focus preparation fixture is missing.");
        }
        if (request["app"]?.GetValue<string>() == "EvidenceFailure")
        {
            throw new ProviderFailureException(
                "native_write_failed",
                "emission",
                "Synthetic post-emission failure.",
                actionEvidence: new JsonObject
                {
                    ["route"] = "uia.value.set",
                    ["geometry_sampled"] = false,
                    ["readback_sampled"] = false,
                    ["input_emitted"] = true,
                });
        }
        if (request["app"]?.GetValue<string>() == "StaleElement")
        {
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "Synthetic stale element reference.");
        }
        if (request["app"]?.GetValue<string>() == "ActivationFailure")
        {
            throw new ProviderFailureException(
                "window_activation_failed",
                "emission",
                "Synthetic verified activation failure; no input was emitted.",
                inputEventsEmitted: false,
                mutationUnknown: false,
                actionEvidence: new JsonObject
                {
                    ["route"] = "input.send_input.keyboard",
                    ["geometry_sampled"] = false,
                    ["focus_verified"] = false,
                    ["readback_sampled"] = false,
                    ["input_emitted"] = false,
                });
        }
        if (request["app"]?.GetValue<string>() == "DispatchOutcomeUnknown")
        {
            throw UiaMutationFailureAttribution.FromUnexpected(
                new InvalidOperationException(
                    "Synthetic UIA invoke returned an ambiguous failure."),
                inputEmitted: false,
                actionEvidence: new JsonObject
                {
                    ["route"] = "uia.pattern.invoke",
                    ["geometry_sampled"] = false,
                    ["readback_sampled"] = false,
                    ["input_emitted"] = false,
                });
        }
        throw new NotSupportedException();
    }
    internal JsonObject ListWindows(JsonObject _) => throw new NotSupportedException();
    internal JsonObject ActOnWindow(JsonObject _) => throw new NotSupportedException();
    public void Dispose() {}
}

internal static class DesktopProvider
{
    internal static ProviderFailureException? FailureForTest { get; set; }

    internal static JsonObject ExecuteBatch(JsonObject request)
    {
        if (FailureForTest is { } failure) { throw failure; }
        if (request["frame_id"]?.GetValue<string>() == "stale-fixture")
        {
            throw new ProviderFailureException(
                "stale_layout",
                "precondition",
                "Synthetic stale desktop layout.");
        }
        if (request["frame_id"]?.GetValue<string>() ==
            "stale-after-input-fixture")
        {
            throw new ProviderFailureException(
                "stale_layout",
                "precondition",
                "Synthetic stale desktop layout after prior input.",
                inputEventsEmitted: true,
                mutationUnknown: false,
                dispatchedActions: 2,
                dispatchedInputActions: 1);
        }
        throw new NotSupportedException();
    }
}

internal static class NativeDesktop
{
    internal sealed record ScreenFrame(string FrameId);

    internal static ScreenFrame Capture() => new("fresh-fixture");

    internal static JsonObject SnapshotJson(ScreenFrame frame)
    {
        return new JsonObject
        {
            ["app"] = new JsonObject { ["name"] = string.Empty, ["pid"] = 0 },
            ["sampledAt"] = "2000-01-01T00:00:00.0000000+00:00",
            ["screenshotPngBase64"] = "fixture-png",
            ["screenshotMimeType"] = "image/png",
            ["screenshotEncoding"] = "png",
            ["coordinateSpace"] = "screen_image_px",
            ["frameId"] = frame.FrameId,
            ["width"] = 1920,
            ["height"] = 1080,
        };
    }
}
