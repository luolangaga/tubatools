using System.Text.Json.Nodes;
using ComputerUse.Windows.Runtime;

internal static class NativeManifestTests
{
    internal static void Run(string fixtureRoot)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(
            Path.Combine(fixtureRoot, "native-manifest.json")))!.AsObject();
        if (manifest["protocol"]!.GetValue<string>() != "computer-use-provider" ||
            manifest["schema_version"]!.GetValue<int>() != 2 ||
            manifest["ordered_input_platforms"]!["windows"]!.GetValue<bool>())
        {
            throw new InvalidDataException("Unexpected Windows native manifest contract.");
        }
        var requests = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fixture in manifest["fixtures"]!.AsArray())
        {
            var direction = fixture!["direction"]!.GetValue<string>();
            var requestFile = direction is "request" or "invalid_request"
                ? fixture["file"]!.GetValue<string>()
                : fixture["request_file"]!.GetValue<string>();
            requests.Add(requestFile);
        }
        foreach (var file in requests)
        {
            var request = JsonNode.Parse(File.ReadAllText(Path.Combine(fixtureRoot, file)))!.AsObject();
            if (request["operation"]!.GetValue<string>() != "ordered_input")
                throw new InvalidDataException($"Unreviewed optional Windows operation in {file}.");
            try
            {
                ProviderServer.ValidateRequest(request);
                throw new InvalidDataException($"Windows accepted unsupported request {file}.");
            }
            catch (ProviderFailureException error) when (
                error.Code == "unsupported_operation" && error.Phase == "precondition" &&
                !error.InputEventsEmitted && !error.MutationUnknown)
            {
            }
        }
        if (requests.Count == 0)
            throw new InvalidDataException("Native manifest contains no optional requests.");
        Console.WriteLine($"PASS native manifest: {requests.Count} unsupported Windows requests");
    }
}
