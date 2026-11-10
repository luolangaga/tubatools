@testable import ComputerUseProviderKit
import AppKit
import ApplicationServices
import Foundation
import Testing

@Test
func semanticProviderRebindFallsThroughToCurrentRuntimeLineage() throws {
    let elements: [[String: Any]] = [
        [
            "index": 12,
            "runtimeId": [12],
            "name": "New Folder",
            "controlType": "textbox",
            "providerObjectPath": "ax:new",
        ],
        [
            "index": 18,
            "runtimeId": [18],
            "name": "Other",
            "controlType": "button",
            "providerObjectPath": "ax:old",
        ],
    ]
    let target: [String: Any] = [
        "name": "New Folder",
        "hasName": true,
        "role": "textbox",
        "providerRoleAliases": ["entry", "text area"],
        "providerObjectPath": "ax:old",
        "runtimeId": [12],
        "allowProviderRebind": true,
        "locatorKind": "semantic",
    ]

    let index = try resolveProviderElementIndex(
        elements: elements,
        target: target,
        targetLabel: "target"
    )

    #expect(index == 12)
}

@Test
func providerBoundTargetDoesNotUseSemanticRebind() {
    let elements: [[String: Any]] = [[
        "index": 12,
        "runtimeId": [12],
        "name": "New Folder",
        "controlType": "textbox",
        "providerObjectPath": "ax:new",
    ]]
    let target: [String: Any] = [
        "name": "New Folder",
        "hasName": true,
        "role": "textbox",
        "providerObjectPath": "ax:old",
        "runtimeId": [12],
        "allowProviderRebind": true,
        "locatorKind": "provider_bound",
    ]

    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: elements,
            target: target,
            targetLabel: "target"
        )
    }
}

@Test
func recycledProviderIdentityCannotOverrideSemanticName() throws {
    let elements: [[String: Any]] = [
        [
            "index": 12,
            "runtimeId": [12],
            "name": "Classic",
            "controlType": "menuitem",
            "providerObjectPath": "ax:reused",
        ],
        [
            "index": 18,
            "runtimeId": [18],
            "name": "Beacon",
            "controlType": "menuitem",
            "providerObjectPath": "ax:current",
        ],
    ]
    let target: [String: Any] = [
        "name": "Beacon",
        "hasName": true,
        "role": "menuitem",
        "providerObjectPath": "ax:reused",
        "runtimeId": [18],
        "allowProviderRebind": true,
        "locatorKind": "semantic",
    ]

    let index = try resolveProviderElementIndex(
        elements: elements,
        target: target,
        targetLabel: "target"
    )

    #expect(index == 18)
}

@Test
func providerPathCollisionUsesCompleteIdentityEvidence() throws {
    let elements: [[String: Any]] = [
        [
            "index": 31,
            "runtimeId": [0, 1, 31],
            "name": "Search",
            "controlType": "textbox",
            "providerObjectPath": "ax:collision",
        ],
        [
            "index": 48,
            "runtimeId": [0, 1, 34, 48],
            "name": "Pink",
            "controlType": "button",
            "providerObjectPath": "ax:collision",
        ],
    ]
    let target: [String: Any] = [
        "runtimeId": [0, 1, 34, 48],
        "name": "Pink",
        "hasName": true,
        "role": "button",
        "providerObjectPath": "ax:collision",
        "locatorKind": "semantic",
    ]

    let index = try resolveProviderElementIndex(
        elements: elements,
        target: target,
        targetLabel: "target"
    )

    #expect(index == 48)
}

@Test
func providerPathCollisionStillFailsWhenCompleteIdentityIsAmbiguous() {
    let elements: [[String: Any]] = [
        [
            "index": 48,
            "runtimeId": [0, 1, 34, 48],
            "name": "Pink",
            "controlType": "button",
            "providerObjectPath": "ax:collision",
        ],
        [
            "index": 49,
            "runtimeId": [0, 1, 34, 48],
            "name": "Pink",
            "controlType": "button",
            "providerObjectPath": "ax:collision",
        ],
    ]
    let target: [String: Any] = [
        "runtimeId": [0, 1, 34, 48],
        "name": "Pink",
        "hasName": true,
        "role": "button",
        "providerObjectPath": "ax:collision",
        "locatorKind": "provider_bound",
    ]

    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: elements,
            target: target,
            targetLabel: "target"
        )
    }
}

@Test
func providerAmbiguityDiagnosticUsesExactCandidatesWithoutChangingRefusal() throws {
    let elements: [[String: Any]] = [
        ["index": 1, "providerObjectPath": "ax:one", "runtimeId": [1], "controlType": "spinbutton"],
        ["index": 2, "providerObjectPath": "ax:one", "runtimeId": [2], "controlType": "spinbutton"],
        ["index": 3, "providerObjectPath": "ax:one", "runtimeId": [3], "controlType": "button"],
    ]
    var target: [String: Any] = [
        "providerObjectPath": "ax:one", "providerIdentityScope": "process_instance",
        "runtimeId": [1], "runtimeAliases": [[2], [3]], "role": "spinbutton",
    ]
    var callbacks = 0
    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: elements, target: target, targetLabel: "target",
            onProviderAmbiguity: { exact, count in
                callbacks += 1
                #expect(count == 3)
                #expect(exact.compactMap { $0["index"] as? Int } == [1, 2])
            }
        )
    }
    #expect(callbacks == 1)
    target["runtimeAliases"] = [[3]]
    let index = try resolveProviderElementIndex(
        elements: elements, target: target, targetLabel: "target",
        onProviderAmbiguity: { _, _ in callbacks += 1 }
    )
    #expect(index == 1)
    #expect(callbacks == 1)
}

@Test
func ambiguityFactsBoundPairsAndExposeTruncationWithoutContent() throws {
    let records = (0..<5).map { index in
        ElementRecord(
            index: index, runtimeID: Array(repeating: 1, count: 65) + [index],
            identifier: "private-id", element: AXUIElementCreateApplication(getpid()),
            localFrame: nil, rawActions: [], prettyActions: [],
            providerRole: "spinbutton", localizedRole: "spinbutton", name: "private-name"
        )
    }
    try bindMacProviderObjectIdentities(records, scope: "diagnostic-test")
    let snapshot = AppSnapshot(
        app: RunningAppDescriptor(name: "private-app", bundleIdentifier: nil,
            pid: getpid(), runningApplication: NSRunningApplication.current),
        windowTitle: "private-title", windowBounds: nil,
        rootWindowElement: AXUIElementCreateSystemWide(), targetWindowID: nil,
        targetWindowHandle: nil, targetWindowLayer: nil,
        treeLines: [], focusedSummary: nil, focusedElement: nil, selectedText: nil,
        elements: Dictionary(uniqueKeysWithValues: records.map { ($0.index, $0) }),
        diagnosticSnapshotSerial: 7
    )
    let candidates: [[String: Any]] = records.map {
        ["index": $0.index, "runtimeId": $0.runtimeID,
         "providerObjectPath": macProviderObjectPath($0), "controlType": "spinbutton",
         "name": "private-name", "value": "private-value", "automationId": "private-id"]
    }
    let facts = providerAmbiguityFacts(
        target: ["runtimeId": records[0].runtimeID, "runtimeAliases": records.map(\.runtimeID)],
        candidates: candidates, providerMatchCount: 6, snapshot: snapshot
    )
    #expect(facts["exact_match_count"] as? Int == 5)
    #expect(facts["sampled_count"] as? Int == 4)
    #expect(facts["truncated"] as? Bool == true)
    #expect(facts["resolved_snapshot_serial"] as? Int == 7)
    let pairs = try #require(facts["pairs"] as? [[String: Any]])
    #expect(pairs.count == 6)
    #expect(pairs.allSatisfy { $0["equal_forward"] as? Bool == true })
    let sampled = try #require(facts["candidates"] as? [[String: Any]])
    #expect(sampled.allSatisfy { $0["runtime_path_length"] as? Int == 66 })
    #expect(sampled.allSatisfy { ($0["runtime_path"] as? [Int])?.count == 64 })
    #expect(sampled.allSatisfy { $0["record_path_matches"] as? Bool == true })
    let data = try JSONSerialization.data(withJSONObject: facts)
    let json = try #require(String(data: data, encoding: .utf8))
    #expect(!json.contains("private-"))
}

@Test
func recycledProviderIdentityFailsClosedForProviderBoundRef() {
    let elements: [[String: Any]] = [[
        "index": 12,
        "runtimeId": [12],
        "name": "Classic",
        "controlType": "menuitem",
        "providerObjectPath": "ax:reused",
    ]]
    let target: [String: Any] = [
        "name": "Beacon",
        "hasName": true,
        "role": "menuitem",
        "providerObjectPath": "ax:reused",
        "runtimeId": [12],
        "allowProviderRebind": false,
        "locatorKind": "provider_bound",
    ]

    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: elements,
            target: target,
            targetLabel: "target"
        )
    }
}

@Test
func stableAutomationIdentityAllowsMutableNameOnExactProviderObject() throws {
    let elements: [[String: Any]] = [[
        "index": 12,
        "runtimeId": [0, 2],
        "automationId": "First Text View",
        "name": "entry area First Text View AB",
        "controlType": "textbox",
        "providerObjectPath": "ax:stable",
    ]]
    let target: [String: Any] = [
        "runtimeId": [0, 2],
        "automationId": "First Text View",
        "name": "entry area First Text View A",
        "hasName": true,
        "role": "textbox",
        "providerObjectPath": "ax:stable",
        "locatorKind": "provider_bound",
    ]

    let index = try resolveProviderElementIndex(
        elements: elements,
        target: target,
        targetLabel: "target"
    )

    #expect(index == 12)
}

@Test
func changedAutomationIdentityFailsClosedDespiteMatchingProviderPath() {
    let elements: [[String: Any]] = [[
        "index": 12,
        "runtimeId": [0, 2],
        "automationId": "Replacement Editor",
        "name": "Document",
        "controlType": "textbox",
        "providerObjectPath": "ax:reused",
    ]]
    let target: [String: Any] = [
        "runtimeId": [0, 2],
        "automationId": "Original Editor",
        "name": "Document",
        "hasName": true,
        "role": "textbox",
        "providerObjectPath": "ax:reused",
        "locatorKind": "provider_bound",
    ]

    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: elements,
            target: target,
            targetLabel: "target"
        )
    }
}

@Test
func mutableNameWithoutAutomationIdentityStillFailsClosed() {
    let elements: [[String: Any]] = [[
        "index": 12,
        "runtimeId": [0, 2],
        "name": "Classic",
        "controlType": "menuitem",
        "providerObjectPath": "ax:reused",
    ]]
    let target: [String: Any] = [
        "runtimeId": [0, 2],
        "name": "Beacon",
        "hasName": true,
        "role": "menuitem",
        "providerObjectPath": "ax:reused",
        "locatorKind": "provider_bound",
    ]

    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: elements,
            target: target,
            targetLabel: "target"
        )
    }
}

@Test
func stableAutomationIdentityDoesNotOverrideChangedRuntimeLineage() {
    let elements: [[String: Any]] = [[
        "index": 12,
        "runtimeId": [0, 3],
        "automationId": "First Text View",
        "name": "entry area First Text View AB",
        "controlType": "textbox",
        "providerObjectPath": "ax:stable",
    ]]
    let target: [String: Any] = [
        "runtimeId": [0, 2],
        "automationId": "First Text View",
        "name": "entry area First Text View A",
        "hasName": true,
        "role": "textbox",
        "providerObjectPath": "ax:stable",
        "locatorKind": "provider_bound",
    ]

    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: elements,
            target: target,
            targetLabel: "target"
        )
    }
}

@Test
func stableAutomationIdentityAllowsAuthorizedMutableNameRebind() throws {
    let elements: [[String: Any]] = [[
        "index": 12,
        "runtimeId": [0, 2],
        "automationId": "First Text View",
        "name": "entry area First Text View AB",
        "controlType": "textbox",
        "providerObjectPath": "ax:new-wrapper",
    ]]
    let target: [String: Any] = [
        "runtimeId": [0, 2],
        "automationId": "First Text View",
        "name": "entry area First Text View A",
        "hasName": true,
        "role": "textbox",
        "providerObjectPath": "ax:old-wrapper",
        "allowProviderRebind": true,
        "locatorKind": "semantic",
    ]

    let index = try resolveProviderElementIndex(
        elements: elements,
        target: target,
        targetLabel: "target"
    )

    #expect(index == 12)
}

@Test
func stableAutomationIdentityAllowsAuthorizedProviderBoundRebind() throws {
    let elements: [[String: Any]] = [[
        "index": 9,
        "runtimeId": [0, 9],
        "automationId": "start-timepicker",
        "name": "",
        "controlType": "date time area",
        "providerObjectPath": "ax:new-wrapper",
        "providerWindowBusName": "mac:891",
        "providerWindowObjectPath": "window:calendar",
        "providerWindowProcessGeneration": "99123",
    ]]
    let target: [String: Any] = [
        "runtimeId": [0, 9],
        "automationId": "start-timepicker",
        "name": "",
        "hasName": false,
        "role": "date time area",
        "providerObjectPath": "ax:recycled",
        "providerWindowBusName": "mac:891",
        "providerWindowObjectPath": "window:calendar",
        "providerWindowProcessGeneration": "99123",
        "allowProviderRebind": true,
        "locatorKind": "provider_bound",
    ]

    let index = try resolveProviderElementIndex(
        elements: elements,
        target: target,
        targetLabel: "target"
    )

    #expect(index == 9)
}

@Test
func providerBoundRebindWithoutAutomationIdentityFailsClosed() {
    let elements: [[String: Any]] = [[
        "index": 9,
        "runtimeId": [0, 9],
        "name": "",
        "controlType": "date time area",
        "providerObjectPath": "ax:new-wrapper",
    ]]
    let target: [String: Any] = [
        "runtimeId": [0, 9],
        "name": "",
        "hasName": false,
        "role": "date time area",
        "providerObjectPath": "ax:recycled",
        "allowProviderRebind": true,
        "locatorKind": "provider_bound",
    ]

    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: elements,
            target: target,
            targetLabel: "target"
        )
    }
}

@Test
func providerBoundRebindRejectsChangedWindowIdentity() {
    let elements: [[String: Any]] = [[
        "index": 9,
        "runtimeId": [0, 9],
        "automationId": "start-timepicker",
        "name": "",
        "controlType": "date time area",
        "providerObjectPath": "ax:new-wrapper",
        "providerWindowBusName": "mac:891",
        "providerWindowObjectPath": "window:replacement",
        "providerWindowProcessGeneration": "99123",
    ]]
    let target: [String: Any] = [
        "runtimeId": [0, 9],
        "automationId": "start-timepicker",
        "name": "",
        "hasName": false,
        "role": "date time area",
        "providerObjectPath": "ax:recycled",
        "providerWindowBusName": "mac:891",
        "providerWindowObjectPath": "window:calendar",
        "providerWindowProcessGeneration": "99123",
        "allowProviderRebind": true,
        "locatorKind": "provider_bound",
    ]

    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: elements,
            target: target,
            targetLabel: "target"
        )
    }
}

@Test
func duplicateAutomationIdentityFailsClosedDuringSemanticRebind() {
    let elements: [[String: Any]] = [
        [
            "index": 12,
            "automationId": "Repeated",
            "name": "First",
            "controlType": "button",
            "providerObjectPath": "ax:first",
        ],
        [
            "index": 18,
            "automationId": "Repeated",
            "name": "Second",
            "controlType": "button",
            "providerObjectPath": "ax:second",
        ],
    ]
    let target: [String: Any] = [
        "automationId": "Repeated",
        "name": "Old",
        "hasName": true,
        "role": "button",
        "providerObjectPath": "ax:old",
        "allowProviderRebind": true,
        "locatorKind": "semantic",
    ]

    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(
            elements: elements,
            target: target,
            targetLabel: "target"
        )
    }
}
