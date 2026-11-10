using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class ProviderServer
{
    private static JsonObject FailureEnvelope(
        string requestId,
        string operation,
        Exception error,
        bool mutationOperation)
    {
        var failure = error as ProviderFailureException;
        var evidenceInputEmitted =
            failure?.ActionEvidence?["input_emitted"] is JsonValue evidenceValue &&
            evidenceValue.TryGetValue<bool>(out var evidenceEmitted) &&
            evidenceEmitted;
        var inputEmitted =
            (failure?.InputEventsEmitted ?? false) || evidenceInputEmitted;
        var focusPreparationUnknown = !inputEmitted &&
            failure?.FocusPreparation ==
                FocusPreparationState.RequestAttemptedOutcomeUnknown;
        var hasPointerPreparation =
            failure?.PointerPreparation ==
                PointerPreparationState.ActivationAttemptedOutcomeUnknown;
        var pointerPreparationUnknown = !inputEmitted && hasPointerPreparation;
        var mutationUnknown =
            (failure?.MutationUnknown ?? false) || inputEmitted ||
            focusPreparationUnknown || pointerPreparationUnknown;
        var effectUnknown = inputEmitted || mutationUnknown;
        var failureFactsComplete =
            effectUnknown ? false : failure?.FactsComplete ?? true;
        var failureCode = failure?.Code ?? "provider_internal_error";
        var failurePhase = failure?.Phase ?? "execution";
        var failureMessage = error.Message;
        string? underlyingCode = null;
        if (effectUnknown && !focusPreparationUnknown && !pointerPreparationUnknown)
        {
            underlyingCode = failureCode;
            failureCode = "effect_unknown";
            if (failure?.Code == "desktop_input_failed")
            {
                // The native failure identifies an unsent suffix or unknown
                // message. Keep that recovery instruction with the prefix.
                failureMessage = error.Message;
            }
            else if (inputEmitted)
            {
                failurePhase = "post_action_observation";
                failureMessage =
                    "post_action_observation_failed; input was emitted; " +
                    "the final effect is unknown.";
            }
            else
            {
                failureMessage =
                    "action_dispatch_outcome_unknown; the provider cannot " +
                    "determine whether the action was emitted; verify current " +
                    "state and do not repeat the same request.";
            }
        }
        var payload = new JsonObject
        {
            ["ok"] = false,
            ["error"] = failureMessage,
            ["failure"] = new JsonObject
            {
                ["code"] = failureCode,
                ["phase"] = failurePhase,
                ["facts_complete"] = failureFactsComplete,
            },
            ["mutation"] = new JsonObject
            {
                ["input_events_emitted"] = inputEmitted,
                ["partial_input_possible"] = mutationUnknown,
                ["desktop_state_mutated"] = inputEmitted,
                ["mutation_unknown"] = mutationUnknown,
            },
        };
        // Zero requested input does not establish an unchanged desktop after
        // an uncertain dispatch or window preparation.
        if (effectUnknown && !inputEmitted)
        {
            ((JsonObject)payload["mutation"]!).Remove("desktop_state_mutated");
        }
        if (focusPreparationUnknown)
        {
            var mutation = (JsonObject)payload["mutation"]!;
            mutation["partial_input_possible"] = false;
            mutation.Remove("desktop_state_mutated");
            mutation["focus_preparation"] =
                "request_attempted_outcome_unknown";
        }
        if (pointerPreparationUnknown)
        {
            var mutation = (JsonObject)payload["mutation"]!;
            mutation["partial_input_possible"] = false;
            mutation["pointer_preparation"] =
                "activation_attempted_outcome_unknown";
        }
        if (operation == "desktop_execute_batch")
        {
            payload["gui_result"] = new JsonObject
            {
                ["dispatched"] = failure?.DispatchedActions ?? 0,
                ["input_events_emitted"] = inputEmitted,
                ["input_actions_dispatched"] =
                    failure?.DispatchedInputActions ?? 0,
                ["window_commands_dispatched"] = 0,
                ["desktop_state_mutated"] = inputEmitted,
                ["mutation_unknown"] = mutationUnknown,
                ["partial_input_possible"] = mutationUnknown,
                ["effect_unknown"] = inputEmitted || mutationUnknown,
                ["quiescent"] = false,
            };
            if (effectUnknown && !inputEmitted)
            {
                ((JsonObject)payload["gui_result"]!).Remove("desktop_state_mutated");
            }
            if (pointerPreparationUnknown)
            {
                ((JsonObject)payload["gui_result"]!)["partial_input_possible"] = false;
            }
            if (!hasPointerPreparation && failure?.Code == "stale_layout")
            {
                try
                {
                    var frame = DesktopObservationRetry.Execute(
                        NativeDesktop.Capture);
                    payload["snapshot"] = NativeDesktop.SnapshotJson(frame);
                    ((JsonObject)payload["gui_result"]!)["quiescent"] = true;
                }
                catch (Exception recoveryError)
                {
                    Console.Error.WriteLine(
                        "stale_layout_recovery_capture_failed " +
                        recoveryError.Message);
                }
            }
        }
        if (underlyingCode is not null)
        {
            ((JsonObject)payload["failure"]!)["underlying_code"] =
                underlyingCode;
        }
        if (!string.IsNullOrEmpty(failure?.Subject))
        {
            ((JsonObject)payload["failure"]!)["subject"] = failure.Subject;
        }
        if (!focusPreparationUnknown && !hasPointerPreparation &&
            failure?.RecoveryContext is not null)
        {
            payload["recovery_context"] =
                failure.RecoveryContext.DeepClone();
        }
        // Only the accessibility payload carries candidates on the wire.
        if (failure?.Candidates is { } candidates &&
            DomainPayload(operation) == "accessibility")
        {
            payload["candidates"] = candidates.DeepClone();
            payload["candidate_count"] = candidates.Count;
        }
        if ((mutationOperation || operation == "desktop_execute_batch") &&
            failure?.ActionEvidence is not null)
        {
            payload["action_evidence"] = failure.ActionEvidence.DeepClone();
        }
        var response = Envelope(requestId, operation);
        if (failure is null)
        {
            response["provider_reset_required"] = true;
        }
        response[DomainPayload(operation)] = payload;
        return response;
    }

    private static JsonObject FatalResponse(Exception error)
    {
        return new JsonObject
        {
            ["protocol"] = Protocol,
            ["schema_version"] = SchemaVersion,
            ["request_id"] = "invalid",
            ["operation"] = "observe_apps",
            ["accessibility"] = new JsonObject
            {
                ["ok"] = false,
                ["failure"] = new JsonObject
                {
                    ["code"] = "invalid_request",
                    ["phase"] = "precondition",
                    ["facts_complete"] = true,
                },
                ["mutation"] = new JsonObject
                {
                    ["input_events_emitted"] = false,
                    ["partial_input_possible"] = false,
                    ["desktop_state_mutated"] = false,
                    ["mutation_unknown"] = false,
                },
                ["error"] = error.Message,
            },
        };
    }

    private static JsonObject Envelope(string requestId, string operation)
    {
        return new JsonObject
        {
            ["protocol"] = Protocol,
            ["schema_version"] = SchemaVersion,
            ["request_id"] = requestId,
            ["operation"] = operation,
        };
    }

    private static JsonObject RequiredPayload(JsonObject request, string name)
    {
        return request[name] as JsonObject ??
            throw InvalidRequest(
                $"Provider request payload {name} is required.");
    }

    private static void RejectUnknownFields(
        JsonObject value,
        IReadOnlySet<string> allowed,
        string subject)
    {
        var unexpected = value
            .Select(property => property.Key)
            .Where(name => !allowed.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unexpected.Length > 0)
        {
            throw InvalidRequest(
                $"{subject} has unsupported field(s): " +
                string.Join(", ", unexpected) + ".");
        }
    }

    private static string RequiredStringValue(JsonObject value, string name)
    {
        var result = StringValue(value, name);
        return result is null
            ? throw InvalidRequest($"{name} must be a string.")
            : result;
    }

    private static string? StringValue(JsonObject value, string name)
    {
        return value[name] is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var result)
                ? result
                : null;
    }

    private static int RequiredIntValue(JsonObject value, string name)
    {
        return value[name] is JsonValue jsonValue &&
            jsonValue.TryGetValue<int>(out var result)
                ? result
                : throw InvalidRequest($"{name} must be an integer.");
    }

    private static IReadOnlySet<string> Fields(params string[] names)
    {
        return new HashSet<string>(names, StringComparer.Ordinal);
    }

    private static bool IsMutationOperation(
        JsonObject request,
        string operation)
    {
        if (operation == "window_action")
        {
            return true;
        }
        if (operation != "resolve_and_act")
        {
            return false;
        }
        var intent = (request["action"] as JsonObject)?["intent"] is JsonValue value &&
            value.TryGetValue<string>(out var parsed)
                ? parsed
                : string.Empty;
        return !string.Equals(
            intent,
            "resolve_target",
            StringComparison.Ordinal);
    }

    private static JsonObject? OptionalObject(JsonNode? node, string path)
    {
        return node is null ? null : RequiredObject(node, path);
    }

    private static JsonObject RequiredObject(JsonNode? node, string path)
    {
        return node as JsonObject ??
            throw InvalidRequest($"{path} must be an object.");
    }

    private static void ValidateOptionalObject(
        JsonNode? node,
        string path,
        IReadOnlySet<string> allowed)
    {
        var value = OptionalObject(node, path);
        if (value is not null)
        {
            RejectUnknownFields(value, allowed, path);
        }
    }

    private static void ValidateRequiredObject(
        JsonNode? node,
        string path,
        IReadOnlySet<string> allowed)
    {
        RejectUnknownFields(RequiredObject(node, path), allowed, path);
    }

    private static void RequireString(
        JsonObject value,
        string name,
        string path,
        bool allowEmpty)
    {
        if (value[name] is not JsonValue node ||
            !node.TryGetValue<string>(out var text) ||
            (!allowEmpty && string.IsNullOrWhiteSpace(text)))
        {
            throw InvalidRequest($"{path}.{name} must be a string.");
        }
    }

    private static void ValidateOptionalString(
        JsonObject value,
        string name,
        string path)
    {
        if (value[name] is not null)
        {
            RequireString(value, name, path, allowEmpty: true);
        }
    }

    private static void ValidateOptionalStringArray(
        JsonObject value,
        string name,
        string path)
    {
        if (value[name] is null)
        {
            return;
        }
        if (value[name] is not JsonArray items)
        {
            throw InvalidRequest($"{path}.{name} must be an array.");
        }
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not JsonValue item ||
                !item.TryGetValue<string>(out _))
            {
                throw InvalidRequest(
                    $"{path}.{name}[{index}] must be a string.");
            }
        }
    }

    private static void ValidateOptionalIntegerArray(
        JsonObject value,
        string name,
        string path)
    {
        if (value[name] is null)
        {
            return;
        }
        if (value[name] is not JsonArray items)
        {
            throw InvalidRequest($"{path}.{name} must be an array.");
        }
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index] is not JsonValue item ||
                !item.TryGetValue<int>(out _))
            {
                throw InvalidRequest(
                    $"{path}.{name}[{index}] must be an integer.");
            }
        }
    }

    private static void ValidateOptionalIntegerArrayArray(
        JsonObject value,
        string name,
        string path)
    {
        if (value[name] is null)
        {
            return;
        }
        if (value[name] is not JsonArray arrays)
        {
            throw InvalidRequest($"{path}.{name} must be an array.");
        }
        for (var index = 0; index < arrays.Count; index++)
        {
            var wrapper = new JsonObject
            {
                ["value"] = arrays[index]?.DeepClone(),
            };
            ValidateOptionalIntegerArray(
                wrapper,
                "value",
                $"{path}.{name}[{index}]");
        }
    }

    private static double RequiredNumber(
        JsonObject value,
        string name,
        string path)
    {
        return OptionalNumber(value, name) ??
            throw InvalidRequest($"{path}.{name} must be a number.");
    }

    private static double? OptionalNumber(JsonObject value, string name)
    {
        return value[name] is JsonValue node &&
            node.TryGetValue<double>(out var number)
                ? number
                : null;
    }

    private static void RequireNonEmptyString(
        JsonObject value,
        string name,
        string path)
    {
        RequireString(value, name, path, allowEmpty: false);
    }

    private static void RequirePositiveNumber(
        JsonObject value,
        string name,
        string path)
    {
        if (value[name] is not JsonValue node ||
            !node.TryGetValue<double>(out var number) ||
            number <= 0)
        {
            throw InvalidRequest(
                $"{path}.{name} must be greater than zero.");
        }
    }

    private static void ValidatePositiveTextLimit(
        JsonObject value,
        string path)
    {
        if (value["text_limit"] is null)
        {
            return;
        }
        if (value["text_limit"] is JsonValue node &&
            ((node.TryGetValue<string>(out var keyword) &&
                string.Equals(keyword, "max", StringComparison.Ordinal)) ||
             (node.TryGetValue<int>(out var number) && number > 0)))
        {
            return;
        }
        throw InvalidRequest(
            $"{path}.text_limit must be a positive integer or max.");
    }

    private static void ValidateNonNegativeInteger(
        JsonObject value,
        string name,
        string path)
    {
        if (value[name] is null)
        {
            return;
        }
        if (value[name] is not JsonValue node ||
            !node.TryGetValue<int>(out var number) ||
            number < 0)
        {
            throw InvalidRequest(
                $"{path}.{name} must be a non-negative integer.");
        }
    }

    private static void ValidateOptionalBoolean(
        JsonObject value,
        string name,
        string path)
    {
        if (value[name] is null)
        {
            return;
        }
        if (value[name] is not JsonValue node ||
            !node.TryGetValue<bool>(out _))
        {
            throw InvalidRequest($"{path}.{name} must be a boolean.");
        }
    }
}
