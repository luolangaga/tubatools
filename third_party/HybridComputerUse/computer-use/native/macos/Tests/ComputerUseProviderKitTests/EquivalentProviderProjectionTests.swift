@testable import ComputerUseProviderKit
import AppKit
import ApplicationServices
import Foundation
import Testing

private func projectionFixture(
    secondObject: AXUIElement? = AXUIElementCreateApplication(getpid()),
    synthetic: Bool = false,
    secondActions: [String] = [],
    secondFrame: CGRect? = nil
) throws -> (AppSnapshot, [[String: Any]], [String: Any]) {
    let records = [
        ElementRecord(index: 7, runtimeID: [0, 2], identifier: "control",
            element: AXUIElementCreateApplication(getpid()), localFrame: nil,
            rawActions: [], prettyActions: [], providerRole: "spinbutton"),
        ElementRecord(index: 12, runtimeID: [0, 3, 2], identifier: "control",
            element: secondObject, localFrame: secondFrame, rawActions: secondActions, prettyActions: [],
            isSyntheticText: synthetic, providerRole: "spinbutton"),
    ]
    // Deliberately permit a forged token: the resolver must check native
    // objects, not assume that matching serialized tokens prove equality.
    for record in records { record.bindProviderObjectPath("ax:projection-test") }
    let snapshot = AppSnapshot(
        app: RunningAppDescriptor(name: "fixture", bundleIdentifier: nil,
            pid: getpid(), runningApplication: NSRunningApplication.current),
        windowTitle: nil, windowBounds: nil,
        rootWindowElement: AXUIElementCreateApplication(getpid()),
        targetWindowID: nil, targetWindowHandle: "fixture-window", targetWindowLayer: nil,
        treeLines: [], focusedSummary: nil,
        focusedElement: nil, selectedText: nil,
        elements: Dictionary(uniqueKeysWithValues: records.map { ($0.index, $0) }),
        providerProcessGeneration: "fixture-generation"
    )
    let candidates = try snapshot.providerElementDictionaries
    var target = candidates[1]
    target["runtimeAliases"] = [records[0].runtimeID]
    target["role"] = "spinbutton"
    target["locatorKind"] = "provider_bound"
    return (snapshot, candidates, target)
}

@Test
func equivalentProviderProjectionsResolveRequestedPrimary() throws {
    let (snapshot, candidates, target) = try projectionFixture()
    for ordered in [candidates, Array(candidates.reversed())] {
        #expect(try resolveProviderElementIndex(
            elements: ordered, target: target, targetLabel: "target", snapshot: snapshot
        ) == 12)
    }
    // Dictionary equality without retained native objects remains insufficient.
    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: candidates, target: target, targetLabel: "target"
        )
    }
}

@Test
func equivalentProviderProjectionsResolveAliasWhenPrimaryAbsent() throws {
    let (snapshot, candidates, original) = try projectionFixture()
    var target = original
    target["runtimeId"] = [0, 99]
    target["runtimeAliases"] = candidates.compactMap { $0["runtimeId"] as? [Int] }
    #expect(try resolveProviderElementIndex(
        elements: Array(candidates.reversed()), target: target,
        targetLabel: "target", snapshot: snapshot
    ) == 7)
}

@Test
func equivalentProviderProjectionsRejectMissingSyntheticAndForeignObjects() throws {
    for fixture in [
        try projectionFixture(secondObject: nil),
        try projectionFixture(synthetic: true),
        try projectionFixture(secondObject: AXUIElementCreateApplication(1)),
    ] {
        let (snapshot, original, target) = fixture
        var candidates = original
        // Give malformed projections the same complete serialized identity.
        candidates[1]["providerObjectPath"] = candidates[0]["providerObjectPath"]
        var requested = target
        requested["providerObjectPath"] = candidates[0]["providerObjectPath"]
        #expect(equivalentProviderProjectionIndex(
            candidates, target: requested, snapshot: snapshot
        ) == nil)
        #expect(throws: ComputerUseError.self) {
            _ = try resolveProviderElementIndex(
                elements: candidates, target: requested, targetLabel: "target", snapshot: snapshot
            )
        }
    }
}

@Test
func equivalentProviderProjectionsRejectUnboundAndIncoherentOwnership() throws {
    let (snapshot, candidates, target) = try projectionFixture()
    for key in [
        "providerIdentityScope", "providerBusName", "processGeneration",
        "providerWindowBusName", "providerWindowObjectPath", "providerWindowProcessGeneration",
    ] {
        var changed = candidates
        changed[1][key] = "wrong-owner"
        #expect(equivalentProviderProjectionIndex(changed, target: target, snapshot: snapshot) == nil)
        var requested = target
        requested.removeValue(forKey: key)
        #expect(equivalentProviderProjectionIndex(candidates, target: requested, snapshot: snapshot) == nil)
        requested[key] = "wrong-owner"
        #expect(equivalentProviderProjectionIndex(candidates, target: requested, snapshot: snapshot) == nil)
    }
    var unbound = snapshot
    unbound.providerProcessGeneration = nil
    #expect(equivalentProviderProjectionIndex(candidates, target: target, snapshot: unbound) == nil)
}

@Test
func equivalentProviderProjectionsRejectRecordPathTokenAndIndexDrift() throws {
    let (snapshot, candidates, target) = try projectionFixture()
    let mutations: [(String, Any)] = [
        ("runtimeId", [0, 99]), ("providerObjectPath", "ax:other"),
        ("index", 999), ("index", 7),
    ]
    for mutation in mutations {
        var changed = candidates
        changed[1][mutation.0] = mutation.1
        #expect(equivalentProviderProjectionIndex(changed, target: target, snapshot: snapshot) == nil)
    }
}

@Test
func equivalentProviderProjectionsDoNotBroadenStaleRuntimeOrRoleMatching() throws {
    let (snapshot, candidates, original) = try projectionFixture()
    let changes: [[String: Any]] = [
        ["runtimeId": [0, 99], "runtimeAliases": [[0, 98]]],
        ["role": "button"],
        ["providerObjectPath": "ax:replaced"],
    ]
    for changed in changes {
        var target = original
        for (key, value) in changed { target[key] = value }
        #expect(throws: ComputerUseError.self) {
            _ = try resolveProviderElementIndex(
                elements: candidates, target: target, targetLabel: "target", snapshot: snapshot
            )
        }
    }
}

@Test
func equivalentProviderProjectionsRejectFailedOrAsymmetricEquality() throws {
    let (snapshot, candidates, target) = try projectionFixture()
    let firstObject = try #require(snapshot.elements[7]?.element)
    let secondObject = try #require(snapshot.elements[12]?.element)
    for outcomes in [[false], [true, false]] {
        var calls = 0
        let result = equivalentProviderProjectionIndex(
            candidates, target: target, snapshot: snapshot,
            objectsEqual: { left, right in
                #expect(left === (calls == 0 ? firstObject : secondObject))
                #expect(right === (calls == 0 ? secondObject : firstObject))
                let answer = outcomes[calls]
                calls += 1
                return answer
            }
        )
        #expect(result == nil)
        #expect(calls == outcomes.count)
    }
}

@Test
func equivalentProviderProjectionsRejectActionOrGeometryConflict() throws {
    for fixture in [
        try projectionFixture(secondActions: ["AXPress"]),
        try projectionFixture(secondFrame: CGRect(x: 1, y: 2, width: 3, height: 4)),
    ] {
        let (snapshot, candidates, target) = fixture
        #expect(equivalentProviderProjectionIndex(
            candidates, target: target, snapshot: snapshot
        ) == nil)
        #expect(throws: ComputerUseError.self) {
            _ = try resolveProviderElementIndex(
                elements: candidates, target: target, targetLabel: "target", snapshot: snapshot
            )
        }
    }
}
