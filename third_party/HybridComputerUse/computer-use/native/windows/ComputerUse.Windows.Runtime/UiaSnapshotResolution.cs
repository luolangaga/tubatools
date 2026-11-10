using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider
{
    private AutomationElement? ResolveElement(
        AutomationElement root,
        JsonObject? target,
        JsonObject? record,
        Process process,
        NativeDesktop.WindowRecord selectedWindow,
        Func<AutomationElement>? refreshRoot = null)
    {
        if (target is null && record is null)
        {
            return null;
        }
        var requestedProviderObjectPath = OptionalString(
            target,
            "providerObjectPath",
            OptionalString(record, "providerObjectPath", string.Empty));
        var allowSemanticProviderRebind =
            ProviderTargetResolutionPolicy.AllowsSemanticProviderRebind(
                target);
        if (requestedProviderObjectPath.StartsWith(
                SpreadsheetCellScope + ":",
                StringComparison.Ordinal))
        {
            return ResolveSpreadsheetCell(
                process, selectedWindow, requestedProviderObjectPath, target, record);
        }
        if (requestedProviderObjectPath.StartsWith(
                "runtime:",
                StringComparison.Ordinal))
        {
            var logicalWindow = ProjectNativeWindows(
                process,
                NativeDesktop.ListWindows(process.Id))
                .LogicalRecord(selectedWindow);
            var leaseFound = _observedElementLeases.TryGet(
                process.Id,
                ProcessGeneration(process),
                logicalWindow.Handle.ToInt64(),
                requestedProviderObjectPath,
                out var observedLease,
                out var leaseDiagnostic);
            var nameMatches = leaseFound &&
                observedLease is not null &&
                CanonicalResolutionRecord.NameMatches(
                observedLease.CanonicalRecord,
                target,
                record);
            var roleMatches = leaseFound &&
                observedLease is not null &&
                CanonicalResolutionRecord.RoleMatches(
                observedLease.CanonicalRecord,
                target,
                record);
            var predicatesMatch = leaseFound &&
                observedLease is not null &&
                CanonicalResolutionRecord.PredicatesMatch(
                observedLease.CanonicalRecord,
                target,
                record);
            if (leaseFound &&
                observedLease is not null &&
                nameMatches &&
                roleMatches &&
                predicatesMatch)
            {
                EnsureObservedElementLeaseUsable(
                    observedLease.Element,
                    target,
                    record);
                return observedLease.Element;
            }
            leaseDiagnostic["name_matches"] = nameMatches;
            leaseDiagnostic["role_matches"] = roleMatches;
            leaseDiagnostic["predicates_match"] = predicatesMatch;
            leaseDiagnostic["result"] = leaseFound
                ? "canonical_mismatch"
                : "lookup_miss";
            Console.Error.WriteLine(leaseDiagnostic.ToJsonString());
        }
        if (AuthorizedCanonicalResolutionPolicy
            .UseCanonicalBeforeProviderIdentity(
                allowSemanticProviderRebind,
                requestedProviderObjectPath))
        {
            return ResolveCanonicalSemanticElement(
                root,
                target,
                record,
                process,
                selectedWindow,
                refreshRoot,
                requestedProviderObjectPath,
                allowSemanticProviderRebind);
        }
        if (requestedProviderObjectPath.StartsWith(
                "runtime:",
                StringComparison.Ordinal))
        {
            try
            {
                return ResolveProviderObjectPath(
                    LogicalAutomationRoots(process, selectedWindow),
                    requestedProviderObjectPath,
                    resolved =>
                    {
                        EnsureTargetStillMatches(resolved, target, record);
                    });
            }
            catch (ProviderFailureException error) when (
                error.Code == "stale_element_ref" &&
                allowSemanticProviderRebind)
            {
                // The wrapper authorizes this only after a fresh, complete,
                // same-window semantic observation. Continue below and prove
                // that the selector has exactly one live match before input.
            }
        }
        else if (!string.IsNullOrEmpty(requestedProviderObjectPath))
        {
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "The provider-native target identity is unsupported or stale; no input was emitted.");
        }
        var identityScope = OptionalString(
            target,
            "providerIdentityScope",
            OptionalString(record, "providerIdentityScope", string.Empty));
        if (identityScope == "snapshot_only")
        {
            return ResolveSnapshotPath(
                LogicalAutomationRoots(process, selectedWindow),
                target,
                record);
        }
        return ResolveCanonicalSemanticElement(
            root,
            target,
            record,
            process,
            selectedWindow,
            refreshRoot,
            requestedProviderObjectPath,
            allowSemanticProviderRebind);
    }

    private AutomationElement ResolveCanonicalSemanticElement(
        AutomationElement root,
        JsonObject? target,
        JsonObject? record,
        Process process,
        NativeDesktop.WindowRecord selectedWindow,
        Func<AutomationElement>? refreshRoot,
        string requestedProviderObjectPath,
        bool allowSemanticProviderRebind)
    {
        var requestedName = OptionalString(
            target,
            "name",
            OptionalString(record, "name", string.Empty));
        var hasName = target?["hasName"]?.GetValue<bool>() ??
            !string.IsNullOrEmpty(requestedName);
        var readableCandidates = 0;
        var nameMatches = 0;
        var roleMatches = 0;
        var readFailures = 0;
        var resolutionAttempts = 0;
        var diagnosticRoot = root;
        var semanticCandidateCount = 0;
        var resolvedSemanticTarget =
            SemanticResolutionRetry.ResolveWithContext<
                AutomationElement?,
                AutomationElement>(
            allowSemanticProviderRebind,
            attempt =>
                attempt == 0 || refreshRoot is null
                    ? root
                    : TryRefreshResolutionRoot(refreshRoot),
            (attempt, currentRoot) =>
            {
                resolutionAttempts++;
                if (currentRoot is null)
                {
                    semanticCandidateCount = 0;
                    readableCandidates = 0;
                    nameMatches = 0;
                    roleMatches = 0;
                    readFailures = 0;
                    return null;
                }
                diagnosticRoot = currentRoot;
                readableCandidates = 0;
                nameMatches = 0;
                roleMatches = 0;
                readFailures = 0;
                var snapshot = BuildSnapshot(
                    process,
                    selectedWindow,
                    null);
                if (snapshot["annotations"] is JsonArray gaps && gaps.Count != 0)
                {
                    throw new ProviderFailureException(
                        "window_ownership_unavailable", "precondition",
                        "An incomplete canonical sample cannot prove a unique semantic target; no input was emitted.");
                }
                var candidates = snapshot["elements"] as JsonArray ??
                    new JsonArray();
                semanticCandidateCount = candidates.Count;
                readableCandidates = candidates.Count;
                var matches = new List<JsonObject>();
                foreach (var candidate in candidates.OfType<JsonObject>())
                {
                    if (!CanonicalResolutionRecord.NameMatches(
                            candidate,
                            target,
                            record))
                    {
                        continue;
                    }
                    nameMatches++;
                    if (!CanonicalResolutionRecord.RoleMatches(
                            candidate,
                            target,
                            record))
                    {
                        continue;
                    }
                    roleMatches++;
                    if (!CanonicalResolutionRecord.PredicatesMatch(
                            candidate,
                            target,
                            record))
                    {
                        continue;
                    }
                    matches.Add(candidate);
                    if (matches.Count > 1)
                    {
                        break;
                    }
                }
                if (matches.Count > 1)
                {
                    LogSemanticResolutionDiagnostic(
                        currentRoot,
                        requestedProviderObjectPath,
                        requestedName,
                        hasName,
                        semanticCandidateCount,
                        readableCandidates,
                        nameMatches,
                        roleMatches,
                        readFailures,
                        matches.Count,
                        resolutionAttempts,
                        target);
                    throw new ProviderFailureException(
                        "semantic_target_ambiguous",
                        "precondition",
                        $"The exact target matched {matches.Count} elements; no input was emitted.");
                }
                if (matches.Count != 1)
                {
                    return null;
                }
                var liveRoots = LogicalAutomationRoots(
                    process,
                    selectedWindow);
                var currentProviderObjectPath = OptionalString(
                    matches[0],
                    "providerObjectPath",
                    string.Empty);
                if (currentProviderObjectPath.StartsWith(
                        "runtime:",
                        StringComparison.Ordinal))
                {
                    return ResolveProviderObjectPath(
                        liveRoots,
                        currentProviderObjectPath);
                }
                return ResolveCanonicalSnapshotRecord(liveRoots, matches[0]);
            });
        if (resolvedSemanticTarget is null)
        {
            LogSemanticResolutionDiagnostic(
                diagnosticRoot,
                requestedProviderObjectPath,
                requestedName,
                hasName,
                semanticCandidateCount,
                readableCandidates,
                nameMatches,
                roleMatches,
                readFailures,
                0,
                resolutionAttempts,
                target);
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "The exact target is not present; no input was emitted.");
        }
        return resolvedSemanticTarget;
    }

    private static void EnsureObservedElementLeaseUsable(
        AutomationElement element,
        JsonObject? target,
        JsonObject? record)
    {
        if (!RoleMatchesTarget(element, target, record) ||
            !TargetPredicatesMatch(element, target, record))
        {
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "The observed provider element is no longer usable; no input was emitted.");
        }
    }

    private AutomationElement ResolveCanonicalSnapshotRecord(
        IReadOnlyList<AutomationElement> roots,
        JsonObject candidate)
    {
        if (!CanonicalResolutionRecord.TryTreePath(
                candidate,
                out var path))
        {
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "The canonical target has no valid tree path; no input was emitted.");
        }
        if (path.Length == 0 ||
            path[0] < 0 ||
            path[0] >= roots.Count)
        {
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "The canonical target root is no longer present; no input was emitted.");
        }
        var current = roots[path[0]];
        var controlWalker =
            _automation.TreeWalkerFactory.GetControlViewWalker();
        for (var depth = 1; depth < path.Length; depth++)
        {
            var childIndex = path[depth];
            if (childIndex >= DefaultNodeLimit)
            {
                throw new ProviderFailureException(
                    "stale_element_ref",
                    "precondition",
                    "The canonical target tree path exceeds the bounded child budget; no input was emitted.");
            }
            AutomationElement[] children;
            try
            {
                using (FlaUI.Core.CacheRequest.ForceNoCache())
                {
                    children = ReadControlChildren(
                        current,
                        controlWalker,
                        childIndex + 1);
                }
            }
            catch
            {
                throw new ProviderFailureException(
                    "stale_element_ref",
                    "precondition",
                    "The canonical target tree path is no longer readable; no input was emitted.");
            }
            if (childIndex >= children.Length)
            {
                throw new ProviderFailureException(
                    "stale_element_ref",
                    "precondition",
                    "The canonical target tree path no longer exists; no input was emitted.");
            }
            current = children[childIndex];
        }
        string liveProviderObjectPath;
        using (FlaUI.Core.CacheRequest.ForceNoCache())
        {
            liveProviderObjectPath = RuntimeObjectPath(current);
        }
        if (!CanonicalResolutionRecord.ProviderIdentityMatches(
                candidate,
                liveProviderObjectPath))
        {
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "The canonical target identity changed before input; no input was emitted.");
        }
        return current;
    }

    private static AutomationElement? TryRefreshResolutionRoot(
        Func<AutomationElement> refreshRoot)
    {
        try
        {
            return refreshRoot();
        }
        catch
        {
            // A provider root can disappear during a bounded read-only sample.
            // Treat it as absence and let the remaining samples decide.
            return null;
        }
    }

    private static void LogSemanticResolutionDiagnostic(
        AutomationElement root,
        string requestedProviderObjectPath,
        string requestedName,
        bool hasName,
        int candidateCount,
        int readableCandidates,
        int nameMatches,
        int roleMatches,
        int readFailures,
        int finalMatches,
        int resolutionAttempts,
        JsonObject? target)
    {
        try
        {
            var diagnostic = new JsonObject
            {
                ["event"] = "uia_semantic_resolution_diagnostic",
                ["requested_provider_path_present"] =
                    !string.IsNullOrEmpty(requestedProviderObjectPath),
                ["requested_name"] = requestedName,
                ["has_name"] = hasName,
                ["requested_role"] = OptionalString(
                    target,
                    "role",
                    string.Empty),
                ["provider_role_aliases"] =
                    target?["providerRoleAliases"]?.DeepClone(),
                ["candidate_count"] = candidateCount,
                ["readable_candidates"] = readableCandidates,
                ["name_matches"] = nameMatches,
                ["role_matches"] = roleMatches,
                ["read_failures"] = readFailures,
                ["final_matches"] = finalMatches,
                ["resolution_attempts"] = resolutionAttempts,
                ["root_name"] = Safe(
                    () => root.Name,
                    string.Empty),
                ["root_role"] = Safe(
                    () => CanonicalRole(root.ControlType),
                    string.Empty),
                ["root_runtime_path"] = RuntimeObjectPath(root),
            };
            Console.Error.WriteLine(diagnostic.ToJsonString());
        }
        catch
        {
            // Diagnostics must never change target-resolution behavior.
        }
    }

    private int VisitResolutionCandidates(
        IReadOnlyList<AutomationElement> roots,
        Func<AutomationElement, bool> visit)
    {
        var seenProviderObjectPaths = new HashSet<string>(
            StringComparer.Ordinal);
        var candidateCount = 0;
        var controlWalker =
            _automation.TreeWalkerFactory.GetControlViewWalker();
        ResolutionCandidateScope.Visit<IDisposable, AutomationElement>(
            CacheRequest.ForceNoCache,
            scopedVisit =>
            {
                foreach (var root in roots)
                {
                    if (!Walk(root, 0, scopedVisit))
                    {
                        break;
                    }
                }
            },
            element =>
            {
                candidateCount++;
                return visit(element);
            });
        return candidateCount;

        bool Walk(
            AutomationElement element,
            int depth,
            Func<AutomationElement, bool> scopedVisit)
        {
            if (candidateCount >= DefaultNodeLimit ||
                depth > DefaultDepthLimit)
            {
                return candidateCount < DefaultNodeLimit;
            }
            var providerObjectPath = RuntimeObjectPath(element);
            if (!ProviderResolutionCandidatePolicy.ShouldInclude(
                    providerObjectPath,
                    seenProviderObjectPaths))
            {
                return true;
            }
            if (!scopedVisit(element))
            {
                return false;
            }
            AutomationElement[] children;
            try
            {
                children = ReadControlChildren(
                    element,
                    controlWalker,
                    DefaultNodeLimit - candidateCount);
            }
            catch
            {
                return true;
            }
            foreach (var child in children)
            {
                if (!Walk(child, depth + 1, scopedVisit))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
