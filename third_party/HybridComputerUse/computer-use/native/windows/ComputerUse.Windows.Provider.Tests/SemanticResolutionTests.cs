using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal static class SemanticResolutionTests
{
    internal static void Run()
    {
        var semanticResolutionSamples = 0;
        var semanticResolutionDelays = new List<TimeSpan>();
        var semanticResolutionTarget = new object();
        var recoveredSemanticTarget = SemanticResolutionRetry.Resolve(
            enabled: true,
            attempt =>
            {
                semanticResolutionSamples++;
                return attempt == SemanticResolutionRetry.MaxAttempts - 1
                    ? semanticResolutionTarget
                    : null;
            },
            semanticResolutionDelays.Add);
        if (!ReferenceEquals(recoveredSemanticTarget, semanticResolutionTarget) ||
            semanticResolutionSamples != SemanticResolutionRetry.MaxAttempts ||
            semanticResolutionDelays.Count !=
                SemanticResolutionRetry.MaxAttempts - 1 ||
            semanticResolutionDelays.Any(
                delay => delay != SemanticResolutionRetry.RetryDelay))
        {
            throw new InvalidDataException(
                "Transient semantic resolution did not use its exact bounded retry policy.");
        }

        semanticResolutionSamples = 0;
        semanticResolutionDelays.Clear();
        var missingSemanticTarget = SemanticResolutionRetry.Resolve<object>(
            enabled: true,
            _ =>
            {
                semanticResolutionSamples++;
                return null;
            },
            semanticResolutionDelays.Add);
        if (missingSemanticTarget is not null ||
            semanticResolutionSamples != SemanticResolutionRetry.MaxAttempts ||
            semanticResolutionDelays.Count !=
                SemanticResolutionRetry.MaxAttempts - 1)
        {
            throw new InvalidDataException(
                "Missing semantic target escaped the bounded retry limit.");
        }

        semanticResolutionSamples = 0;
        semanticResolutionDelays.Clear();
        var nonRebindableTarget = SemanticResolutionRetry.Resolve<object>(
            enabled: false,
            _ =>
            {
                semanticResolutionSamples++;
                return null;
            },
            semanticResolutionDelays.Add);
        if (nonRebindableTarget is not null ||
            semanticResolutionSamples != 1 ||
            semanticResolutionDelays.Count != 0)
        {
            throw new InvalidDataException(
                "Non-rebindable target consumed a semantic retry barrier.");
        }

        var semanticResolutionContexts = new List<object>();
        var initialSemanticResolutionContext = new object();
        var liveSemanticResolutionTarget = new object();
        var liveRootSemanticTarget =
            SemanticResolutionRetry.ResolveWithContext<object, object>(
                enabled: true,
                attempt =>
                    attempt == 0
                        ? initialSemanticResolutionContext
                        : new object(),
                (attempt, context) =>
                {
                    semanticResolutionContexts.Add(context);
                    return attempt == SemanticResolutionRetry.MaxAttempts - 1
                        ? liveSemanticResolutionTarget
                        : null;
                },
                _ => { });
        if (!ReferenceEquals(liveRootSemanticTarget, liveSemanticResolutionTarget) ||
            semanticResolutionContexts.Count !=
                SemanticResolutionRetry.MaxAttempts ||
            !ReferenceEquals(
                semanticResolutionContexts[0],
                initialSemanticResolutionContext) ||
            semanticResolutionContexts
                .Skip(1)
                .Any(context => ReferenceEquals(
                    context,
                    initialSemanticResolutionContext)) ||
            semanticResolutionContexts.Distinct().Count() !=
                SemanticResolutionRetry.MaxAttempts)
        {
            throw new InvalidDataException(
                "Semantic retries did not reacquire a fresh provider context.");
        }

        var resolutionScopeActive = false;
        var resolutionScopeDisposed = false;
        var readableCandidate = 0;
        var visitedCandidates = new List<int>();
        ResolutionCandidateScope.Visit<CallbackDisposable, int>(
            () =>
            {
                resolutionScopeActive = true;
                return new CallbackDisposable(
                    () =>
                    {
                        resolutionScopeActive = false;
                        resolutionScopeDisposed = true;
                    });
            },
            visit =>
            {
                foreach (var candidate in new[] { 1, 2, 3 })
                {
                    readableCandidate = candidate;
                    if (!visit(candidate))
                    {
                        break;
                    }
                    readableCandidate = 0;
                }
            },
            candidate =>
            {
                if (!resolutionScopeActive ||
                    resolutionScopeDisposed ||
                    readableCandidate != candidate)
                {
                    throw new InvalidDataException(
                        "Resolution candidate properties were not consumed while " +
                        "that candidate remained current in the active cache scope.");
                }
                visitedCandidates.Add(candidate);
                return true;
            });
        if (!visitedCandidates.SequenceEqual(new[] { 1, 2, 3 }) ||
            resolutionScopeActive ||
            !resolutionScopeDisposed)
        {
            throw new InvalidDataException(
                "Resolution candidates were not streamed through their cache scope.");
        }

        if (!AuthorizedCanonicalResolutionPolicy
                .UseCanonicalBeforeProviderIdentity(
                    semanticRebindAuthorized: true,
                    providerObjectPath: "runtime:42.100.7") ||
            AuthorizedCanonicalResolutionPolicy
                .UseCanonicalBeforeProviderIdentity(
                    semanticRebindAuthorized: false,
                    providerObjectPath: "runtime:42.100.7") ||
            AuthorizedCanonicalResolutionPolicy
                .UseCanonicalBeforeProviderIdentity(
                    semanticRebindAuthorized: true,
                    providerObjectPath: "window:opaque"))
        {
            throw new InvalidDataException(
                "Canonical-first routing escaped the explicitly authorized runtime path.");
        }

        var observedLeaseRegistry = new ObservedElementLeaseRegistry<object>();
        var firstObservedElement = new object();
        observedLeaseRegistry.Replace(
            processId: 41,
            processGeneration: "generation-a",
            windowHandle: 7001,
            new Dictionary<string, ObservedElementLease<object>>
            {
                ["runtime:42.100.7"] = new(
                    firstObservedElement,
                    new JsonObject
                    {
                        ["name"] = "Origin",
                        ["controlType"] = "combobox",
                        ["providerObjectPath"] = "runtime:42.100.7",
                    }),
            });
        if (!observedLeaseRegistry.TryGet(
                41,
                "generation-a",
                7001,
                "runtime:42.100.7",
                out var currentObservedLease,
                out var currentLeaseDiagnostic) ||
            currentObservedLease is null ||
            !ReferenceEquals(currentObservedLease.Element, firstObservedElement) ||
            currentLeaseDiagnostic["entry_count"]?.GetValue<int>() != 1 ||
            currentLeaseDiagnostic["process_id_matches"]?.GetValue<bool>() != true ||
            currentLeaseDiagnostic["process_generation_matches"]?.GetValue<bool>() != true ||
            currentLeaseDiagnostic["window_handle_matches"]?.GetValue<bool>() != true ||
            currentLeaseDiagnostic["provider_object_path_present"]?.GetValue<bool>() != true ||
            observedLeaseRegistry.TryGet(
                41,
                "generation-b",
                7001,
                "runtime:42.100.7",
                out _) ||
            observedLeaseRegistry.TryGet(
                41,
                "generation-a",
                7002,
                "runtime:42.100.7",
                out _))
        {
            throw new InvalidDataException(
                "Observed element leases escaped their exact process/window generation.");
        }
        if (observedLeaseRegistry.TryGet(
                41,
                "generation-b",
                7001,
                "runtime:42.100.7",
                out _,
                out var generationMissDiagnostic) ||
            generationMissDiagnostic["process_generation_matches"]?.GetValue<bool>() != false ||
            generationMissDiagnostic["provider_object_path_present"]?.GetValue<bool>() != true)
        {
            throw new InvalidDataException(
                "Observed element lease diagnostics did not isolate a generation mismatch.");
        }
        if (observedLeaseRegistry.TryGet(
                41,
                "generation-a",
                7001,
                "runtime:42.100.8",
                out _,
                out var pathMissDiagnostic) ||
            pathMissDiagnostic["process_generation_matches"]?.GetValue<bool>() != true ||
            pathMissDiagnostic["provider_object_path_present"]?.GetValue<bool>() != false)
        {
            throw new InvalidDataException(
                "Observed element lease diagnostics did not isolate a provider-path miss.");
        }
        observedLeaseRegistry.Replace(
            processId: 41,
            processGeneration: "generation-a",
            windowHandle: 7001,
            new Dictionary<string, ObservedElementLease<object>>());
        if (observedLeaseRegistry.TryGet(
                41,
                "generation-a",
                7001,
                "runtime:42.100.7",
                out _))
        {
            throw new InvalidDataException(
                "A replaced snapshot retained an expired observed element lease.");
        }

        var canonicalCandidate = JsonNode.Parse(
            """
            {
              "runtimeId":[0,2,4],
              "providerObjectPath":"runtime:42.100.7",
              "name":"Origin",
              "controlType":"combobox",
              "description":"Departure airport",
              "states":"enabled focused",
              "actions":["setValue","invoke"]
            }
            """)!.AsObject();
        var canonicalTarget = JsonNode.Parse(
            """
            {
              "name":"Origin",
              "hasName":true,
              "role":"combobox",
              "providerRoleAliases":["combobox","combo box"],
              "predicates":[
                {"kind":"state","value":"focused"},
                {"kind":"desc","value":"Departure airport"},
                {"kind":"action","value":"invoke"}
              ]
            }
            """)!.AsObject();
        if (!CanonicalResolutionRecord.NameMatches(
                canonicalCandidate,
                canonicalTarget,
                null) ||
            !CanonicalResolutionRecord.RoleMatches(
                canonicalCandidate,
                canonicalTarget,
                null) ||
            !CanonicalResolutionRecord.PredicatesMatch(
                canonicalCandidate,
                canonicalTarget,
                null) ||
            !CanonicalResolutionRecord.TryTreePath(
                canonicalCandidate,
                out var canonicalPath) ||
            !canonicalPath.SequenceEqual(new[] { 0, 2, 4 }) ||
            !CanonicalResolutionRecord.ProviderIdentityMatches(
                canonicalCandidate,
                "runtime:42.100.7"))
        {
            throw new InvalidDataException(
                "Canonical snapshot records did not preserve exact target identity.");
        }
        var wrongCanonicalTarget =
            (JsonObject)canonicalTarget.DeepClone();
        wrongCanonicalTarget["name"] = "Destination";
        if (CanonicalResolutionRecord.NameMatches(
                canonicalCandidate,
                wrongCanonicalTarget,
                null) ||
            CanonicalResolutionRecord.ProviderIdentityMatches(
                canonicalCandidate,
                "runtime:42.100.8"))
        {
            throw new InvalidDataException(
                "Canonical snapshot resolution accepted a changed identity.");
        }
        var rawNameCandidate =
            (JsonObject)canonicalCandidate.DeepClone();
        rawNameCandidate["name"] = "Where  from?";
        var providerNameBoundTarget =
            (JsonObject)canonicalTarget.DeepClone();
        providerNameBoundTarget["name"] = "Where from?";
        providerNameBoundTarget["providerObservedName"] = "Where  from?";
        providerNameBoundTarget["hasProviderObservedName"] = true;
        if (!CanonicalResolutionRecord.NameMatches(
                rawNameCandidate,
                providerNameBoundTarget,
                null))
        {
            throw new InvalidDataException(
                "A provider-bound raw name did not match its canonical ref identity.");
        }
        providerNameBoundTarget["providerObservedName"] = "Where  to?";
        if (CanonicalResolutionRecord.NameMatches(
                rawNameCandidate,
                providerNameBoundTarget,
                null))
        {
            throw new InvalidDataException(
                "A provider-bound target accepted a different raw provider name.");
        }
        providerNameBoundTarget.Remove("providerObservedName");
        providerNameBoundTarget["hasProviderObservedName"] = false;
        if (CanonicalResolutionRecord.NameMatches(
                rawNameCandidate,
                providerNameBoundTarget,
                null))
        {
            throw new InvalidDataException(
                "An unbound selector silently received provider-name normalization.");
        }
        var invalidCanonicalPath =
            (JsonObject)canonicalCandidate.DeepClone();
        invalidCanonicalPath["runtimeId"] = new JsonArray(1, -1);
        if (CanonicalResolutionRecord.TryTreePath(
                invalidCanonicalPath,
                out _))
        {
            throw new InvalidDataException(
                "Canonical snapshot resolution accepted an invalid tree path.");
        }
    }
}
