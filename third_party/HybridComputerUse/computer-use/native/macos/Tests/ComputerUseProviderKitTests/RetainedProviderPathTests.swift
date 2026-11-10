@testable import ComputerUseProviderKit
import AppKit
import ApplicationServices
import Foundation
import Testing

private func relocatedSnapshot(
    _ paths: [[Int]], registry: ProviderObjectIdentityRegistry<AXUIElement>,
    identifier: String? = nil, name: String = "Size", role: String = "spinbutton",
    object: AXUIElement? = nil, window: String = "retained-window",
    generation: String = "retained-generation", forgedToken: String? = nil,
    secondActions: [String]? = nil, secondFrame: CGRect? = nil
) throws -> AppSnapshot {
    let records = paths.enumerated().map { index, path in
        ElementRecord(
            index: index + 1, runtimeID: path, identifier: identifier,
            element: object ?? AXUIElementCreateApplication(getpid()),
            localFrame: index == 1 ? secondFrame : nil,
            rawActions: index == 1 ? (secondActions ?? ["AXIncrement"]) : ["AXIncrement"],
            prettyActions: ["increment"],
            providerRole: role, name: name
        )
    }
    if let forgedToken {
        for record in records { record.bindProviderObjectPath(forgedToken) }
    } else {
        try bindMacProviderObjectIdentities(
            records, scope: "mac:\(getpid()):\(generation)", registry: registry
        )
    }
    return AppSnapshot(
        app: RunningAppDescriptor(name: "fixture", bundleIdentifier: nil,
            pid: getpid(), runningApplication: NSRunningApplication.current),
        windowTitle: nil, windowBounds: nil,
        rootWindowElement: AXUIElementCreateApplication(getpid()),
        targetWindowID: nil, targetWindowHandle: window, targetWindowLayer: nil,
        treeLines: [], focusedSummary: nil,
        focusedElement: nil, selectedText: nil,
        elements: Dictionary(uniqueKeysWithValues: records.map { ($0.index, $0) }),
        providerProcessGeneration: generation
    )
}

private func relocatedTarget(_ snapshot: AppSnapshot) throws -> [String: Any] {
    let elements = try snapshot.providerElementDictionaries
    var target = elements[0]
    target["runtimeAliases"] = elements.compactMap { $0["runtimeId"] as? [Int] }
    target["role"] = "spinbutton"
    target["hasName"] = true
    target["locatorKind"] = "provider_bound"
    return target
}

@Test
func retainedProviderPathMovesAcrossRepeatedSnapshots() throws {
    for identifier in [nil, "size-id"] as [String?] {
        let registry = ProviderObjectIdentityRegistry<AXUIElement>(
            hash: { CFHash($0) }, equal: { CFEqual($0, $1) }
        )
        let original = try relocatedSnapshot([[0, 8], [0, 1, 8]],
            registry: registry, identifier: identifier)
        let target = try relocatedTarget(original)
        for step in 0..<4 {
            let path = 12 + step
            let snapshot = try relocatedSnapshot(
                step.isMultiple(of: 2) ? [[0, path], [0, 1, path]] : [[0, path]],
                registry: registry, identifier: identifier
            )
            #expect(try resolveProviderElementIndex(
                elements: snapshot.providerElementDictionaries,
                target: target, targetLabel: "target", snapshot: snapshot,
                identityRegistry: registry
            ) == 1)
        }
    }
}

@Test
func retainedProviderPathRejectsChangedIdentityAndOwner() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { _ in 7 }, equal: { CFEqual($0, $1) }
    )
    let original = try relocatedSnapshot([[0, 8]], registry: registry, identifier: "size-id")
    let target = try relocatedTarget(original)
    let variants = [
        try relocatedSnapshot([[0, 12]], registry: registry, identifier: "other-id"),
        try relocatedSnapshot([[0, 12]], registry: registry, identifier: "size-id", role: "button"),
        try relocatedSnapshot([[0, 12]], registry: registry, identifier: "size-id", window: "other-window"),
        try relocatedSnapshot([[0, 12]], registry: registry, identifier: "size-id", generation: "other-generation"),
        try relocatedSnapshot([[0, 12]], registry: registry, identifier: "size-id",
            object: AXUIElementCreateSystemWide()),
        try relocatedSnapshot([[0, 12]], registry: registry, identifier: "size-id",
            object: AXUIElementCreateSystemWide(),
            forgedToken: target["providerObjectPath"] as? String),
    ]
    for snapshot in variants {
        #expect(throws: ComputerUseError.self) {
            _ = try resolveProviderElementIndex(
                elements: snapshot.providerElementDictionaries,
                target: target, targetLabel: "target", snapshot: snapshot,
                identityRegistry: registry
            )
        }
    }
    let unnamed = try relocatedSnapshot([[0, 8]], registry: registry)
    let changedName = try relocatedSnapshot([[0, 12]], registry: registry, name: "Other")
    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: changedName.providerElementDictionaries,
            target: relocatedTarget(unnamed), targetLabel: "target", snapshot: changedName,
            identityRegistry: registry
        )
    }
    let foreignAnchor = try registry.bind(
        [AXUIElementCreateSystemWide()], scope: "mac:\(getpid()):retained-generation"
    )[0]
    let forged = try relocatedSnapshot([[0, 12]], registry: registry,
        identifier: "size-id", forgedToken: foreignAnchor)
    var forgedTarget = target
    forgedTarget["providerObjectPath"] = foreignAnchor
    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: forged.providerElementDictionaries,
            target: forgedTarget, targetLabel: "target", snapshot: forged,
            identityRegistry: registry
        )
    }
}

@Test
func retainedProviderPathRequiresScopeOwnershipAndRegistryProof() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { CFHash($0) }, equal: { CFEqual($0, $1) }
    )
    let original = try relocatedSnapshot([[0, 8]], registry: registry)
    let snapshot = try relocatedSnapshot([[0, 12]], registry: registry)
    let target = try relocatedTarget(original)
    for key in [
        "providerIdentityScope", "providerBusName", "processGeneration",
        "providerWindowBusName", "providerWindowObjectPath", "providerWindowProcessGeneration",
        "role",
    ] {
        var missing = target
        missing.removeValue(forKey: key)
        #expect(throws: ComputerUseError.self) {
            _ = try resolveProviderElementIndex(
                elements: snapshot.providerElementDictionaries,
                target: missing, targetLabel: "target", snapshot: snapshot,
                identityRegistry: registry
            )
        }
    }
    for locator in ["semantic", ""] {
        var other = target
        other["locatorKind"] = locator
        #expect(throws: ComputerUseError.self) {
            _ = try resolveProviderElementIndex(
                elements: snapshot.providerElementDictionaries,
                target: other, targetLabel: "target", snapshot: snapshot,
                identityRegistry: registry
            )
        }
    }
    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: snapshot.providerElementDictionaries,
            target: target, targetLabel: "target", snapshot: snapshot
        )
    }
}

@Test
func retainedRegistryProofRejectsCollisionScopeAndAsymmetricEquality() throws {
    let registry = ProviderObjectIdentityRegistry<Int>(
        hash: { _ in 7 }, equal: ==
    )
    let tokens = try registry.bind([1, 2], scope: "scope")
    #expect(registry.matchesRetained(1, token: tokens[0], scope: "scope"))
    #expect(!registry.matchesRetained(2, token: tokens[0], scope: "scope"))
    #expect(!registry.matchesRetained(1, token: tokens[0], scope: "other"))
    #expect(!registry.matchesRetained(1, token: "unknown", scope: "scope"))
    let asymmetric = ProviderObjectIdentityRegistry<Int>(
        hash: { _ in 7 }, equal: { $0 <= $1 }
    )
    let anchor = try asymmetric.bind([1], scope: "scope")[0]
    #expect(!asymmetric.matchesRetained(2, token: anchor, scope: "scope"))
}

@Test
func retainedProviderPathDoesNotTrustForgedRecordsOrConflictingProjections() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { CFHash($0) }, equal: { CFEqual($0, $1) }
    )
    let original = try relocatedSnapshot([[0, 8]], registry: registry)
    let target = try relocatedTarget(original)
    let snapshot = try relocatedSnapshot([[0, 12], [0, 1, 12]], registry: registry)
    let elements = try snapshot.providerElementDictionaries
    for (key, value) in [("index", 99 as Any), ("runtimeId", [0, 99] as Any),
                         ("providerWindowObjectPath", "window:other" as Any)] {
        var forged = elements
        forged[1][key] = value
        #expect(throws: ComputerUseError.self) {
            _ = try resolveProviderElementIndex(
                elements: forged, target: target, targetLabel: "target", snapshot: snapshot,
                identityRegistry: registry
            )
        }
    }
    for conflicting in [
        try relocatedSnapshot([[0, 12], [0, 1, 12]], registry: registry,
            secondActions: ["AXPress"]),
        try relocatedSnapshot([[0, 12], [0, 1, 12]], registry: registry,
            secondFrame: CGRect(x: 1, y: 2, width: 3, height: 4)),
    ] {
        #expect(throws: ComputerUseError.self) {
            _ = try resolveProviderElementIndex(
                elements: conflicting.providerElementDictionaries,
                target: target, targetLabel: "target", snapshot: conflicting,
                identityRegistry: registry
            )
        }
    }
    for changedRecord in [
        try relocatedSnapshot([[0, 12]], registry: registry, identifier: "wrong-id"),
        try relocatedSnapshot([[0, 12]], registry: registry, name: "Wrong"),
        try relocatedSnapshot([[0, 12]], registry: registry, role: "button"),
    ] {
        var forged = try changedRecord.providerElementDictionaries
        forged[0]["automationId"] = ""
        forged[0]["name"] = "Size"
        forged[0]["controlType"] = "spinbutton"
        #expect(throws: ComputerUseError.self) {
            _ = try resolveProviderElementIndex(
                elements: forged, target: target, targetLabel: "target", snapshot: changedRecord,
                identityRegistry: registry
            )
        }
    }
}
