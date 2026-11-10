import AppKit
import ApplicationServices
@testable import ComputerUseProviderKit
import Foundation
import Testing

@Test
func instanceRegistrySeparatesCollisionsAndKeepsReorderedAliases() throws {
    let registry = ProviderObjectIdentityRegistry<Int>(hash: { _ in 7 }, equal: ==)
    let first = try registry.bind([1, 2, 1], scope: "app1")
    #expect(first[0] == first[2])
    #expect(first[0] != first[1])
    let reordered = try registry.bind([2, 1, 3], scope: "app1")
    #expect(reordered[0] == first[1])
    #expect(reordered[1] == first[0])
    #expect(!first.contains(reordered[2]))
    #expect(try registry.bind([1], scope: "app2")[0] != first[0])
    let restarted = ProviderObjectIdentityRegistry<Int>(hash: { _ in 7 }, equal: ==)
    #expect(try restarted.bind([1], scope: "app1")[0] != first[0])
}

@Test
func instanceRegistryCapacityFailureCommitsNothing() throws {
    let registry = ProviderObjectIdentityRegistry<Int>(
        capacity: 2, hash: { _ in 7 }, equal: ==
    )
    let first = try registry.bind([1, 1, 1], scope: "app")
    do {
        _ = try registry.bind([2, 3], scope: "app")
        Issue.record("Expected a bounded identity-capacity rejection")
    } catch {
        #expect(requiresProviderReset(error))
    }
    #expect(try registry.bind([1], scope: "app")[0] == first[0])
    let second = try registry.bind([3, 3], scope: "app")
    #expect(second[0] == second[1])
    #expect(second[0].hasSuffix(":2"))
}

@Test
func instanceRegistryComparisonFailureCommitsNothing() throws {
    let registry = ProviderObjectIdentityRegistry<Int>(
        capacity: 1024, hash: { _ in 7 }, equal: ==
    )
    do {
        _ = try registry.bind(Array(0..<600), scope: "app")
        Issue.record("Expected bounded collision-comparison rejection")
    } catch {
        #expect(requiresProviderReset(error))
    }
    let first = try registry.bind([999], scope: "app")
    #expect(first[0].hasSuffix(":1"))
}

@Test
func unrelatedInstanceDoesNotDisableLegacySyntheticContinuity() throws {
    let elements: [[String: Any]] = [
        ["index": 1, "providerObjectPath": "ax:epoch:1",
         "providerIdentityScope": "process_instance", "controlType": "button", "name": "Action"],
        ["index": 2, "providerObjectPath": "synthetic:new",
         "providerIdentityScope": "process", "controlType": "text",
         "name": "Caption", "runtimeId": [0, -1]],
    ]
    let index = try resolveProviderElementIndex(
        elements: elements,
        target: ["providerObjectPath": "synthetic:old", "providerIdentityScope": "process",
                 "allowProviderRebind": true, "locatorKind": "semantic",
                 "role": "text", "name": "Caption", "hasName": true, "runtimeId": [0, -1]],
        targetLabel: "target"
    )
    #expect(index == 2)
}

private final class IdentityResults: @unchecked Sendable {
    let lock = NSLock()
    var tokens: [[String]] = []
    var failures = 0

    func append(_ result: Result<[String], Error>) {
        lock.lock()
        defer { lock.unlock() }
        switch result {
        case .success(let value): tokens.append(value)
        case .failure: failures += 1
        }
    }
}

@Test
func concurrentIdentityBindsPublishOneTokenPerObject() {
    let registry = ProviderObjectIdentityRegistry<Int>(hash: { _ in 7 }, equal: ==)
    let results = IdentityResults()
    DispatchQueue.concurrentPerform(iterations: 32) { index in
        let values = index.isMultiple(of: 2) ? [1, 2] : [2, 1]
        results.append(Result {
            let tokens = try registry.bind(values, scope: "app")
            return index.isMultiple(of: 2) ? tokens : Array(tokens.reversed())
        })
    }
    #expect(results.failures == 0)
    #expect(results.tokens.count == 32)
    #expect(Set(results.tokens).count == 1)
    #expect(results.tokens.first?[0] != results.tokens.first?[1])
}

private func identityRecord(_ index: Int, _ element: AXUIElement) -> ElementRecord {
    ElementRecord(
        index: index, runtimeID: [0, index], identifier: "same-id",
        element: element, localFrame: nil, rawActions: ["AXPress"],
        prettyActions: ["press"], providerRole: "button",
        localizedRole: "button", name: "Same"
    )
}

private func identitySnapshot(_ records: [ElementRecord]) -> AppSnapshot {
    AppSnapshot(
        app: RunningAppDescriptor(
            name: "Identity Test", bundleIdentifier: "test.identity",
            pid: getpid(), runningApplication: NSRunningApplication.current
        ),
        windowTitle: "Identity Test", windowBounds: nil,
        rootWindowElement: AXUIElementCreateSystemWide(),
        targetWindowID: nil, targetWindowHandle: "window-one",
        targetWindowLayer: nil, treeLines: [],
        focusedSummary: nil, focusedElement: nil, selectedText: nil,
        elements: Dictionary(uniqueKeysWithValues: records.map { ($0.index, $0) }),
        providerProcessGeneration: "captured-generation"
    )
}

@Test
func nativeCollisionBindingSurvivesProjectionAndResolver() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { _ in 7 }, equal: { CFEqual($0, $1) }
    )
    let a = AXUIElementCreateApplication(getpid())
    let equalA = AXUIElementCreateApplication(getpid())
    let b = AXUIElementCreateSystemWide()
    let first = identityRecord(1, a)
    let other = identityRecord(2, b)
    try bindMacProviderObjectIdentities([first, other], scope: "app", registry: registry)
    let original = identitySnapshot([first, other])
    let projected = try original.providerElementDictionaries
    let aPath = try #require(projected[0]["providerObjectPath"] as? String)
    let bPath = try #require(projected[1]["providerObjectPath"] as? String)
    #expect(!aPath.isEmpty && aPath != bPath)
    #expect(projected[0]["processGeneration"] as? String == "captured-generation")
    #expect(projected[0]["providerIdentityScope"] as? String == "process_instance")
    #expect(try original.providerElementDictionaries[0]["providerObjectPath"] as? String == aPath)

    let again = identityRecord(19, equalA)
    let peer = identityRecord(3, b)
    try bindMacProviderObjectIdentities([peer, again], scope: "app", registry: registry)
    #expect(macProviderObjectPath(again) == aPath)
    #expect(macProviderObjectPath(peer) == bPath)
    let current = try identitySnapshot([again, peer]).providerElementDictionaries
    #expect(try resolveProviderElementIndex(
        elements: current,
        target: ["providerObjectPath": aPath, "providerIdentityScope": "process_instance",
                 "role": "button", "name": "Same", "hasName": true],
        targetLabel: "target"
    ) == 19)
}

@Test
func strictInstanceCannotRebindReplacementOrDifferentWindow() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { _ in 7 }, equal: { CFEqual($0, $1) }
    )
    let a = identityRecord(1, AXUIElementCreateApplication(getpid()))
    let b = identityRecord(1, AXUIElementCreateSystemWide())
    try bindMacProviderObjectIdentities([a], scope: "app", registry: registry)
    try bindMacProviderObjectIdentities([b], scope: "app", registry: registry)
    let target: [String: Any] = [
        "providerObjectPath": macProviderObjectPath(a),
        "providerIdentityScope": "process_instance",
        "allowProviderRebind": true, "locatorKind": "semantic",
        "role": "button", "name": "Same", "hasName": true,
        "automationId": "same-id", "runtimeId": [0, 1],
        "providerWindowObjectPath": "window:window-one",
    ]
    for record in [b, a] {
        var current = try identitySnapshot([record]).providerElementDictionaries
        if record === a {
            current[0]["providerWindowObjectPath"] = "window:window-two"
        }
        do {
            _ = try resolveProviderElementIndex(elements: current, target: target, targetLabel: "target")
            Issue.record("A strict old identity rebound to a replacement/window")
        } catch {
            let result = runtimeFailureResult(error)
            #expect(result["failureCode"] as? String == "stale_element_ref")
            #expect(result["failurePhase"] as? String == "precondition")
            #expect(result["input_events_emitted"] as? Bool == false)
            #expect(result["partial_input_possible"] as? Bool == false)
        }
    }
}

@Test
func nativeBindingFailureLeavesEveryRecordUnpublished() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        capacity: 1, hash: { _ in 7 }, equal: { CFEqual($0, $1) }
    )
    let a = identityRecord(1, AXUIElementCreateApplication(getpid()))
    let b = identityRecord(2, AXUIElementCreateSystemWide())
    do {
        try bindMacProviderObjectIdentities([a, b], scope: "app", registry: registry)
        Issue.record("Expected capacity failure")
    } catch {
        #expect(requiresProviderReset(error))
        #expect(a.boundProviderObjectPath == nil && b.boundProviderObjectPath == nil)
        #expect(throws: Error.self) {
            try identitySnapshot([a, b]).providerElementDictionaries
        }
    }
    try bindMacProviderObjectIdentities([a], scope: "app", registry: registry)
    do {
        try bindMacProviderObjectIdentities([a], scope: "app", registry: registry)
        Issue.record("Expected duplicate binding rejection")
    } catch {
        #expect((error as? ComputerUseError) != nil)
    }
}

@Test
func identityCapacityAfterInputPreservesEmissionAndReset() {
    let registry = ProviderObjectIdentityRegistry<Int>(
        capacity: 0, hash: { _ in 7 }, equal: ==
    )
    do {
        _ = try registry.bind([1], scope: "app")
        Issue.record("Expected post-input identity capacity failure")
    } catch {
        let result = postInputObservationFailureResult(
            error,
            evidence: ["route": "test.input"]
        )
        #expect(result["input_events_emitted"] as? Bool == true)
        #expect(result["partial_input_possible"] as? Bool == false)
        #expect(result["providerResetRequired"] as? Bool == true)
        #expect(result["windowAbsent"] == nil)
        #expect(result["failurePhase"] as? String == "observation")
    }
}

@Test
func longSessionEvictsOldIdentitiesInsteadOfRequiringARestart() throws {
    let registry = ProviderObjectIdentityRegistry<Int>(
        capacity: 64, hash: { UInt($0) }, equal: ==
    )
    var seen = Set<String>()
    for snapshot in 0..<100 {
        let tokens = try registry.bind(Array(snapshot * 10..<snapshot * 10 + 10), scope: "app")
        #expect(seen.isDisjoint(with: tokens))
        seen.formUnion(tokens)
        #expect(registry.identityCount <= 64)
    }
}

@Test
func evictionTakesTheLeastRecentlyUsedIdentity() throws {
    let registry = ProviderObjectIdentityRegistry<Int>(
        capacity: 3, protectedBinds: 1, hash: { UInt($0) }, equal: ==
    )
    let one = try registry.bind([1], scope: "app")[0]
    let two = try registry.bind([2], scope: "app")[0]
    let three = try registry.bind([3], scope: "app")[0]
    // A ref that resolves counts as a use, like a bind.
    #expect(registry.matchesRetained(1, token: one, scope: "app"))
    _ = try registry.bind([4], scope: "app")
    #expect(!registry.matchesRetained(2, token: two, scope: "app"))
    #expect(registry.matchesRetained(1, token: one, scope: "app"))
    #expect(registry.matchesRetained(3, token: three, scope: "app"))
    // An evicted object seen again gets a new token, never its old one.
    let again = try registry.bind([2], scope: "app")[0]
    #expect(again != two && again != one && again != three)
}

@Test
func identitiesOfRecentBindsAreNeverEvicted() throws {
    let registry = ProviderObjectIdentityRegistry<Int>(
        capacity: 2, protectedBinds: 2, hash: { UInt($0) }, equal: ==
    )
    let one = try registry.bind([1], scope: "app")[0]
    let two = try registry.bind([2], scope: "app")[0]
    do {
        _ = try registry.bind([3, 4], scope: "app")
        Issue.record("Expected a rejection: the previous bind is protected")
    } catch {
        #expect(requiresProviderReset(error))
    }
    #expect(registry.identityCount == 2)
    // One bind later, the first identity is outside the protected binds.
    _ = try registry.bind([3], scope: "app")
    #expect(!registry.matchesRetained(1, token: one, scope: "app"))
    #expect(registry.matchesRetained(2, token: two, scope: "app"))
}

@Test
func evictedRefFailsAsStaleInsteadOfBindingAnotherElement() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        capacity: 1, protectedBinds: 1, hash: { _ in 7 }, equal: { CFEqual($0, $1) }
    )
    let original = identityRecord(1, AXUIElementCreateApplication(getpid()))
    try bindMacProviderObjectIdentities([original], scope: "app", registry: registry)
    let oldToken = macProviderObjectPath(original)
    let other = identityRecord(2, AXUIElementCreateSystemWide())
    try bindMacProviderObjectIdentities([other], scope: "app", registry: registry)
    let returned = identityRecord(1, AXUIElementCreateApplication(getpid()))
    try bindMacProviderObjectIdentities([returned], scope: "app", registry: registry)
    #expect(macProviderObjectPath(returned) != oldToken)
    let target: [String: Any] = [
        "providerObjectPath": oldToken, "providerIdentityScope": "process_instance",
        "allowProviderRebind": true, "locatorKind": "semantic",
        "role": "button", "name": "Same", "hasName": true,
        "automationId": "same-id", "runtimeId": [0, 1],
    ]
    let current = try identitySnapshot([returned]).providerElementDictionaries
    do {
        _ = try resolveProviderElementIndex(elements: current, target: target, targetLabel: "target")
        Issue.record("An evicted identity resolved to an element")
    } catch {
        let result = runtimeFailureResult(error)
        #expect(result["failureCode"] as? String == "stale_element_ref")
        #expect(result["input_events_emitted"] as? Bool == false)
    }
    var candidate = current[0]
    candidate["providerObjectPath"] = oldToken
    #expect(equivalentProviderProjectionIndex(
        [candidate], target: target, snapshot: identitySnapshot([returned]),
        requireRetainedIdentity: true, identityRegistry: registry
    ) == nil)
}
