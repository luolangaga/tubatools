using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class SelectionTests
{
    internal static void Run()
    {
        foreach (var (content, requested, expected) in new[]
        {
            ("a😀e\u0301中z", "😀e\u0301中", "😀e\u0301中"),
            ("first\r\nsecond", "", "first\r\nsecond"),
            ("alpha ALPHA", "ALPHA", "ALPHA"),
        })
        {
            if (UiaSelectionPolicy.RequireUniqueText(content, requested) != expected)
            {
                throw new InvalidOperationException("Exact selection text was changed.");
            }
        }
        foreach (var (content, requested) in new[]
        {
            ("", ""), ("aaa", "aa"), ("😀x😀", "😀"), ("alpha", "ALPHA"),
        })
        {
            ExpectFailure(() => UiaSelectionPolicy.RequireUniqueText(content, requested),
                "invalid_request");
        }

        var request = JsonNode.Parse("""
            {"protocol":"computer-use-provider","schema_version":2,
             "request_id":"selection-test","operation":"resolve_and_act",
             "action":{"protocol_version":2,"intent":"select","app":"Editor",
                       "target":{"providerObjectPath":"runtime:1","hasName":false},
                       "input":{"text":"😀"}}}
            """)!.AsObject();
        ProviderServer.ValidateRequest(request);
        var action = request["action"]!.AsObject();
        ProviderServer.ValidateWindowsActionCapabilities(action);
        action["input"] = new JsonObject();
        ProviderServer.ValidateRequest(request);

        action["input"]!["to_target"] = new JsonObject { ["hasName"] = false };
        ProviderServer.ValidateRequest(request);
        ExpectFailure(() => ProviderServer.ValidateWindowsActionCapabilities(action),
            "capability_unsupported");
        action["input"]!["text"] = "text";
        ExpectFailure(() => ProviderServer.ValidateRequest(request), "invalid_request");
        action["input"] = new JsonObject { ["text"] = 123 };
        ExpectFailure(() => ProviderServer.ValidateRequest(request), "invalid_request");

        action["intent"] = "set_value";
        action["input"] = new JsonObject { ["value"] = "replacement" };
        var edit = new JsonObject
        {
            ["providerBusName"] = "win32:pid:1",
            ["providerObjectPath"] = "runtime:2",
            ["processGeneration"] = "generation",
        };
        action["target"]!["editTarget"] = edit;
        ProviderServer.ValidateRequest(request);
        ExpectFailure(() => ProviderServer.ValidateWindowsActionCapabilities(action),
            "capability_unsupported");
        using var server = new ProviderServer();
        var result = server.Dispatch(request)["accessibility"]!;
        if (result["failure"]?["code"]?.GetValue<string>() != "capability_unsupported" ||
            result["mutation"]?["input_events_emitted"]?.GetValue<bool>() != false)
        {
            throw new InvalidOperationException("Unsupported edit target reached UIA or lost zero-input evidence.");
        }
        foreach (var field in new[] { "providerBusName", "providerObjectPath", "processGeneration" })
        {
            var saved = edit[field]!.DeepClone();
            edit.Remove(field);
            ExpectFailure(() => ProviderServer.ValidateRequest(request), "invalid_request");
            edit[field] = saved;
        }
        edit["role"] = "entry";
        ExpectFailure(() => ProviderServer.ValidateRequest(request), "invalid_request");
        Console.WriteLine("Windows selection text and capability boundary tests passed.");
    }

    private static void ExpectFailure(Action action, string code)
    {
        try
        {
            action();
        }
        catch (ProviderFailureException error) when (error.Code == code &&
            !error.InputEventsEmitted && !error.MutationUnknown)
        {
            return;
        }
        throw new InvalidOperationException($"Expected {code} before input.");
    }
}
