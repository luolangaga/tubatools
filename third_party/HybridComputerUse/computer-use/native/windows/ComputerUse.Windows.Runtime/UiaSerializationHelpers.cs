using System.Diagnostics;
using System.Drawing;
using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider
{
    private static JsonObject ActionEvidence(
        string route,
        bool geometrySampled,
        bool? focusVerified,
        bool readbackSampled,
        bool? readbackVerified,
        bool inputEmitted,
        bool? pointOwnerVerified = null)
    {
        var result = new JsonObject
        {
            ["route"] = route,
            ["geometry_sampled"] = geometrySampled,
            ["readback_sampled"] = readbackSampled,
            ["input_emitted"] = inputEmitted,
        };
        if (focusVerified.HasValue)
        {
            result["focus_verified"] = focusVerified.Value;
        }
        if (readbackVerified.HasValue)
        {
            result["readback_verified"] = readbackVerified.Value;
        }
        if (pointOwnerVerified.HasValue)
        {
            result["point_owner_verified"] = pointOwnerVerified.Value;
        }
        return result;
    }

    private static JsonObject MutationFacts(
        bool inputEventsEmitted,
        bool mutationUnknown)
    {
        return new JsonObject
        {
            ["input_events_emitted"] = inputEventsEmitted,
            ["partial_input_possible"] = mutationUnknown,
            ["desktop_state_mutated"] = inputEventsEmitted,
            ["mutation_unknown"] = mutationUnknown,
        };
    }

    // appProcessId names the application a window shows when another
    // process owns the window (a UWP app inside ApplicationFrameHost). The
    // identity stays the owner's: every operation runs against its HWNDs.
    private static JsonObject AppJson(Process process, int appProcessId = 0)
    {
        var name = process.ProcessName;
        if (appProcessId != 0 && appProcessId != process.Id)
        {
            try
            {
                using var app = Process.GetProcessById(appProcessId);
                name = app.ProcessName;
            }
            catch (ArgumentException)
            {
                // The hosted process exited; keep the host's name.
            }
        }
        return new JsonObject
        {
            ["name"] = name,
            ["bundleIdentifier"] = name,
            ["pid"] = process.Id,
            ["providerBusName"] = $"uia3:pid:{process.Id}",
            ["processGeneration"] = ProcessGeneration(process),
            ["inputCapable"] = true,
        };
    }

    private static JsonObject RectangleJson(Rectangle rectangle)
    {
        return new JsonObject
        {
            ["x"] = rectangle.X,
            ["y"] = rectangle.Y,
            ["width"] = rectangle.Width,
            ["height"] = rectangle.Height,
        };
    }

    private static string ProcessGeneration(Process process)
    {
        return WindowsProcessIdentity.ReadGeneration(() => process.StartTime) ??
            throw new ProviderFailureException(
                "app_identity_unavailable", "precondition",
                "The application process generation could not be read; no reliable reference can be issued.",
                factsComplete: false,
                subject: "app");
    }

    private static void AddAction(JsonArray actions, bool available, string name)
    {
        if (available)
        {
            actions.Add(name);
        }
    }

    private static string Normalize(string value)
    {
        return new string(
            value.Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    internal static string RequiredString(JsonObject value, string name)
    {
        var result = value[name]?.GetValue<string>()?.Trim();
        return string.IsNullOrEmpty(result)
            ? throw new ProviderFailureException(
                "invalid_request",
                "precondition",
                $"{name} is required.")
            : result;
    }

    internal static string OptionalString(
        JsonObject? value,
        string name,
        string fallback)
    {
        return value?[name]?.GetValue<string>() ?? fallback;
    }

    internal static int OptionalInt(JsonObject? value, string name, int fallback)
    {
        return value?[name]?.GetValue<int>() ?? fallback;
    }

    internal static double OptionalDouble(
        JsonObject? value,
        string name,
        double fallback)
    {
        return value?[name]?.GetValue<double>() ?? fallback;
    }
}
