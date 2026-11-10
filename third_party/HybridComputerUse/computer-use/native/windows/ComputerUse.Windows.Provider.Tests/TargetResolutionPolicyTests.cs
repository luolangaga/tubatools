using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class TargetResolutionPolicyTests
{
    internal static void Run()
    {
        var semanticRebindTarget = JsonNode.Parse(
            """
            {
              "selector":"listitem \"TestImage1.jpg\"",
              "locatorKind":"semantic",
              "allowProviderRebind":true,
              "providerObjectPath":"runtime:1.2.3"
            }
            """) as JsonObject ??
            throw new InvalidDataException("semantic rebind target must be an object");
        if (!ProviderTargetResolutionPolicy.AllowsSemanticProviderRebind(
                semanticRebindTarget))
        {
            throw new InvalidDataException(
                "Fresh semantic refs must permit an explicitly authorized provider rebind.");
        }
        foreach (var json in new[]
        {
            """
            {
              "selector":"listitem \"TestImage1.jpg\"",
              "locatorKind":"provider_bound",
              "allowProviderRebind":true,
              "providerObjectPath":"runtime:1.2.3"
            }
            """,
            """
            {
              "selector":"listitem \"TestImage1.jpg\"",
              "locatorKind":"semantic",
              "allowProviderRebind":false,
              "providerObjectPath":"runtime:1.2.3"
            }
            """,
            """
            {
              "locatorKind":"semantic",
              "allowProviderRebind":true,
              "providerObjectPath":"runtime:1.2.3"
            }
            """,
        })
        {
            var target = JsonNode.Parse(json) as JsonObject ??
                throw new InvalidDataException("rebind rejection target must be an object");
            if (ProviderTargetResolutionPolicy.AllowsSemanticProviderRebind(target))
            {
                throw new InvalidDataException(
                    "Provider rebind escaped its explicit semantic-selector boundary.");
            }
        }

        foreach (var (app, expectedCode, expectedSubject, expectRecovery) in new[]
        {
            ("StaleApp", "stale_app_ref", "app", false),
            ("StaleWindow", "stale_window_ref", "window", true),
        })
        {
            var request = JsonNode.Parse(
                $$"""
                {
                  "protocol":"computer-use-provider",
                  "schema_version":2,
                  "request_id":"{{app}}",
                  "operation":"observe",
                  "observe":{
                    "protocol_version":2,
                    "app":"{{app}}",
                    "window_handle":"w2:fixture"
                  }
                }
                """) as JsonObject ??
                throw new InvalidDataException("stale identity request must be an object");
            using var server = new ProviderServer();
            var response = server.Dispatch(request);
            var accessibility = response["accessibility"] as JsonObject ??
                throw new InvalidDataException("stale identity payload is missing");
            var failure = accessibility["failure"] as JsonObject ??
                throw new InvalidDataException("stale identity failure is missing");
            if (failure["code"]?.GetValue<string>() != expectedCode ||
                failure["phase"]?.GetValue<string>() != "precondition" ||
                failure["facts_complete"]?.GetValue<bool>() != true ||
                failure["subject"]?.GetValue<string>() != expectedSubject)
            {
                throw new InvalidDataException(
                    $"Canonical stale identity facts were not preserved for {app}.");
            }
            var recovery = accessibility["recovery_context"] as JsonObject;
            if (expectRecovery)
            {
                if (recovery?["sourceAppStatus"]?.GetValue<string>() != "alive" ||
                    recovery["sourceWindowStatus"]?.GetValue<string>() != "gone" ||
                    recovery["inventoryComplete"]?.GetValue<bool>() != true)
                {
                    throw new InvalidDataException(
                        "Stale window recovery inventory is incomplete.");
                }
            }
            else if (recovery is not null)
            {
                throw new InvalidDataException(
                    "Stale app failure unexpectedly included window recovery context.");
            }
        }

        var seenResolutionPaths = new HashSet<string>(StringComparer.Ordinal);
        var acceptedResolutionPaths = new List<string>();
        foreach (var providerObjectPath in new[]
        {
            "runtime:root",
            "runtime:duplicate",
            "runtime:duplicate",
            "runtime:duplicate",
            "runtime:target",
        })
        {
            if (ProviderResolutionCandidatePolicy.ShouldInclude(
                    providerObjectPath,
                    seenResolutionPaths))
            {
                acceptedResolutionPaths.Add(providerObjectPath);
            }
        }
        if (!acceptedResolutionPaths.SequenceEqual(new[]
            {
                "runtime:root",
                "runtime:duplicate",
                "runtime:target",
            }))
        {
            throw new InvalidDataException(
                "Action resolution did not apply the snapshot runtime-identity deduplication rule.");
        }
    }
}
