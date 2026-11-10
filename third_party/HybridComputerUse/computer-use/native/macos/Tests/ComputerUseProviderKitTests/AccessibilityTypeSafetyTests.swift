import ApplicationServices
import CoreGraphics
import Darwin
import Foundation
@testable import ComputerUseProviderKit
import Testing

@Test
func checkedAXElementRejectsUnexpectedCFTypes() {
    let text = "not-an-element" as CFString
    #expect(checkedAXElement(text) == nil)

    let element = AXUIElementCreateApplication(getpid())
    let checked = checkedAXElement(element)
    #expect(checked != nil)
    if let checked {
        #expect(CFEqual(checked, element))
    }
}

@Test
func checkedAXValueRejectsUnexpectedCFTypes() {
    let number = 42 as CFNumber
    #expect(checkedAXValue(number) == nil)

    var point = CGPoint(x: 12, y: 34)
    let value = AXValueCreate(.cgPoint, &point)
    #expect(checkedAXValue(value) != nil)
}

@Test
func publicStringSetValueRequiresWritableStringStorage() {
    let text = "editable" as CFString
    let number = 42 as CFNumber
    var point = CGPoint(x: 12, y: 34)
    let structuredValue = AXValueCreate(.cgPoint, &point)
    let textFieldRole = kAXTextFieldRole as String

    #expect(publicStringSetValueSupported(settable: true, value: text, role: textFieldRole))
    #expect(!publicStringSetValueSupported(settable: false, value: text, role: textFieldRole))
    #expect(!publicStringSetValueSupported(settable: true, value: number, role: textFieldRole))
    #expect(!publicStringSetValueSupported(settable: true, value: structuredValue, role: textFieldRole))
    #expect(!publicStringSetValueSupported(settable: true, value: nil, role: textFieldRole))
    #expect(!publicStringSetValueSupported(settable: true, value: text, role: nil))
}

@Test
func focusedEditableTargetUsesTheExactFocusedElementFirst() throws {
    let parents = ["leaf": "owner", "owner": "root"]
    let parentOf: (String) -> String? = { parents[$0] }
    let isSame: (String, String) -> Bool = { $0 == $1 }
    let isEditable: (String) -> Bool = {
        $0 == "leaf" || $0 == "owner"
    }
    let selected = nearestFocusedEditableTarget(
        from: "leaf",
        root: "root",
        parentOf: parentOf,
        isSame: isSame,
        isEditable: isEditable
    )

    #expect(selected == "leaf")
}

@Test
func focusedEditableTargetUsesTheNearestEditableAncestor() throws {
    let parents = [
        "focused-static-text": "wrapper",
        "wrapper": "editable-owner",
        "editable-owner": "root",
    ]
    let parentOf: (String) -> String? = { parents[$0] }
    let isSame: (String, String) -> Bool = { $0 == $1 }
    let isEditable: (String) -> Bool = { $0 == "editable-owner" }
    let selected = nearestFocusedEditableTarget(
        from: "focused-static-text",
        root: "root",
        parentOf: parentOf,
        isSame: isSame,
        isEditable: isEditable
    )

    #expect(selected == "editable-owner")
}

@Test
func focusedEditableTargetStopsAtTheBoundWindowRoot() throws {
    let parents = [
        "focused-static-text": "root",
        "root": "editable-outside-window",
    ]
    let parentOf: (String) -> String? = { parents[$0] }
    let isSame: (String, String) -> Bool = { $0 == $1 }
    let isEditable: (String) -> Bool = {
        $0 == "editable-outside-window"
    }
    let selected = nearestFocusedEditableTarget(
        from: "focused-static-text",
        root: "root",
        parentOf: parentOf,
        isSame: isSame,
        isEditable: isEditable
    )

    #expect(selected == nil)
}

@Test
func focusedEditableTargetNeverSearchesSiblingBranches() throws {
    let parents = [
        "focused-static-text": "wrapper",
        "wrapper": "root",
        "editable-sibling": "root",
    ]
    let parentOf: (String) -> String? = { parents[$0] }
    let isSame: (String, String) -> Bool = { $0 == $1 }
    let isEditable: (String) -> Bool = { $0 == "editable-sibling" }
    let selected = nearestFocusedEditableTarget(
        from: "focused-static-text",
        root: "root",
        parentOf: parentOf,
        isSame: isSame,
        isEditable: isEditable
    )

    #expect(selected == nil)
}

@Test
func focusedEditableTargetRejectsAncestryCycles() throws {
    let parents = ["focused-static-text": "wrapper", "wrapper": "focused-static-text"]
    let parentOf: (String) -> String? = { parents[$0] }
    let isSame: (String, String) -> Bool = { $0 == $1 }
    let isEditable: (String) -> Bool = { _ in false }
    let selected = nearestFocusedEditableTarget(
        from: "focused-static-text",
        root: "root",
        parentOf: parentOf,
        isSame: isSame,
        isEditable: isEditable
    )

    #expect(selected == nil)
}

@Test
func pasteboardRetentionEndsOnPositiveMutationEvidence() {
    let before = AXTextMutationFingerprint(
        value: nil,
        selectedText: nil,
        selectedRangeLocation: nil,
        selectedRangeLength: nil
    )
    let after = AXTextMutationFingerprint(
        value: "typed",
        selectedText: nil,
        selectedRangeLocation: nil,
        selectedRangeLength: nil
    )

    #expect(
        pasteboardRetentionBarrierSatisfied(
            before: before,
            after: after,
            notificationObserved: false,
            deadlineReached: false
        )
    )
    #expect(
        pasteboardRetentionBarrierSatisfied(
            before: before,
            after: before,
            notificationObserved: true,
            deadlineReached: false
        )
    )
}

@Test
func pasteboardRetentionWaitsWithoutEvidenceBeforeDeadline() {
    let fingerprint = AXTextMutationFingerprint(
        value: "",
        selectedText: nil,
        selectedRangeLocation: 0,
        selectedRangeLength: 0
    )

    #expect(
        !pasteboardRetentionBarrierSatisfied(
            before: fingerprint,
            after: fingerprint,
            notificationObserved: false,
            deadlineReached: false
        )
    )
}

@Test
func pasteboardRetentionDeadlineIsNotAConsumptionFailure() {
    let fingerprint = AXTextMutationFingerprint(
        value: "",
        selectedText: nil,
        selectedRangeLocation: 0,
        selectedRangeLength: 0
    )

    #expect(
        pasteboardRetentionBarrierSatisfied(
            before: fingerprint,
            after: fingerprint,
            notificationObserved: false,
            deadlineReached: true
        )
    )
}

@Test
func formattedRangeValuesDoNotPromiseDirectStringReplacement() {
    let formattedValue = "15 minutes" as CFString

    for role in [
        kAXSliderRole,
        kAXIncrementorRole,
        kAXScrollBarRole,
        kAXProgressIndicatorRole,
    ] {
        #expect(
            roleUsesRelativeValueMutation(role)
        )
        #expect(
            !publicStringSetValueSupported(
                settable: true,
                value: formattedValue,
                role: role
            )
        )
    }

    #expect(!roleUsesRelativeValueMutation(kAXTextFieldRole as String))
    #expect(
        publicStringSetValueSupported(
            settable: true,
            value: formattedValue,
            role: kAXTextFieldRole as String
        )
    )
}

@Test
func exactStringSetValueReadbackRequiresTheRequestedString() {
    let requested = "3 minutes"
    let matching = "3 minutes" as CFString
    let different = "15 minutes" as CFString
    let nonString = 3 as CFNumber

    #expect(exactStringReadbackMatches(requested: requested, observed: matching))
    #expect(!exactStringReadbackMatches(requested: requested, observed: different))
    #expect(!exactStringReadbackMatches(requested: requested, observed: nonString))
    #expect(!exactStringReadbackMatches(requested: requested, observed: nil))
}

@Test
func relativeAdjustmentMapsOnlyTypedNativeActions() {
    #expect(
        ComputerUseService.nativeRelativeAdjustmentAction(
            for: "increment"
        ) == kAXIncrementAction as String
    )
    #expect(
        ComputerUseService.nativeRelativeAdjustmentAction(
            for: "decrement"
        ) == kAXDecrementAction as String
    )
    #expect(
        ComputerUseService.nativeRelativeAdjustmentAction(
            for: "show-menu"
        ) == nil
    )
}

@Test
func relativeAdjustmentReadbackClassifiesScalarChanges() {
    let beforeString = relativeAdjustmentScalarReadback(
        "15 minutes" as CFString
    )
    let afterString = relativeAdjustmentScalarReadback(
        "14 minutes" as CFString
    )
    #expect(
        relativeAdjustmentReadbackOutcome(
            before: beforeString,
            after: afterString
        ) == .changed
    )

    let beforeNumber = relativeAdjustmentScalarReadback(15 as CFNumber)
    let afterNumber = relativeAdjustmentScalarReadback(14 as CFNumber)
    #expect(
        relativeAdjustmentReadbackOutcome(
            before: beforeNumber,
            after: afterNumber
        ) == .changed
    )
}

@Test
func relativeAdjustmentReadbackRejectsUnchangedScalarValues() {
    let stringValue = relativeAdjustmentScalarReadback(
        "15 minutes" as CFString
    )
    #expect(
        relativeAdjustmentReadbackOutcome(
            before: stringValue,
            after: stringValue
        ) == .unchanged
    )

    let numberValue = relativeAdjustmentScalarReadback(15 as CFNumber)
    #expect(
        relativeAdjustmentReadbackOutcome(
            before: numberValue,
            after: numberValue
        ) == .unchanged
    )
}

@Test
func relativeAdjustmentReadbackKeepsNonScalarValuesUnknown() {
    var point = CGPoint(x: 12, y: 34)
    let structuredValue = AXValueCreate(.cgPoint, &point)

    #expect(relativeAdjustmentScalarReadback(nil) == nil)
    #expect(relativeAdjustmentScalarReadback(structuredValue) == nil)
    #expect(
        relativeAdjustmentReadbackOutcome(
            before: .string("15 minutes"),
            after: nil
        ) == .unavailable
    )
    #expect(
        relativeAdjustmentReadbackOutcome(
            before: nil,
            after: .number("14")
        ) == .unavailable
    )
}

@Test
func expansionUnadvertisedAttributeDoesNotReadValue() throws {
    for names in [[], [kAXRoleAttribute as String]] {
        for unusedValue in [false, true] {
            var namesReads = 0
            var valueReads = 0
            let expansion = try readAXExpansion(
                readNames: {
                    namesReads += 1
                    return (.success, names as CFArray)
                },
                readValue: {
                    valueReads += 1
                    return (.success, unusedValue ? kCFBooleanTrue : kCFBooleanFalse)
                }
            )
            #expect(expansion == nil)
            #expect(namesReads == 1 && valueReads == 0)
        }
    }
}

@Test
func expansionFailureDiagnosticPreservesOriginalReadAndError() {
    for stage in ["names", "value"] {
        var namesReads = 0
        var valueReads = 0
        var failures: [[String: Any]] = []
        do {
            _ = try readAXExpansion(
                readNames: {
                    namesReads += 1
                    return stage == "names"
                        ? (.invalidUIElement, nil)
                        : (.success, [kAXExpandedAttribute] as CFArray)
                },
                readValue: {
                    valueReads += 1
                    return (.noValue, nil)
                },
                reportFailure: { failures.append($0) }
            )
            Issue.record("Failed original read must still throw")
        } catch {
            guard case ComputerUseError.stateUnavailable = error else {
                Issue.record("Unexpected error \(error)")
                continue
            }
        }
        #expect(namesReads == 1)
        #expect(valueReads == (stage == "names" ? 0 : 1))
        #expect(failures.count == 1)
        #expect(failures.first?["stage"] as? String == stage)
        let expected = stage == "names" ? AXError.invalidUIElement : AXError.noValue
        #expect(failures.first?["status"] as? Int == Int(expected.rawValue))
        #expect(failures.first?["value_nil"] as? Bool == true)
    }
}

@Test
func failureDiagnosticBudgetPrecedesAdditionalReads() {
    let context = Thread.current.threadDictionary
    let keys = ["computer-use.liveness.request", "computer-use.failure.count"]
    let saved = keys.map { context[$0] }
    defer {
        for (key, value) in zip(keys, saved) {
            if let value { context[key] = value } else { context.removeObject(forKey: key) }
        }
    }
    var reads = 0
    context.removeObject(forKey: keys[0])
    providerFailureDiagnostic("test") { reads += 1; return [:] }
    #expect(reads == 0)
    context[keys[0]] = "diagnostic-test"
    context[keys[1]] = 8
    providerFailureDiagnostic("test") { reads += 1; return [:] }
    #expect(reads == 0)
    context[keys[1]] = 7
    providerFailureDiagnostic("test") { reads += 1; return [:] }
    #expect(reads == 1)
    #expect(context[keys[1]] as? Int == 8)
    providerFailureDiagnostic("test") { reads += 1; return [:] }
    #expect(reads == 1)
}

@Test
func failureDiagnosticFragmentsPreserveBytesAndBoundLogStrings() throws {
    for length in [1, 479, 480, 481, 16_384] {
        let data = Data((0..<length).map { UInt8($0 % 256) })
        let fragments = providerFailureDiagnosticFragments(data)
        #expect(fragments.count == (length + 479) / 480)
        var reconstructed = Data()
        var identifiers = Set<String>()
        for (index, fragment) in fragments.enumerated() {
            #expect(fragment.utf8.count <= 900)
            let value = try #require(JSONSerialization.jsonObject(
                with: Data(fragment.utf8)) as? [String: Any])
            #expect(value["part"] as? Int == index)
            #expect(value["parts"] as? Int == fragments.count)
            #expect(value["bytes"] as? Int == length)
            identifiers.insert(try #require(value["id"] as? String))
            let encoded = try #require(value["data"] as? String)
            reconstructed.append(try #require(Data(base64Encoded: encoded)))
        }
        #expect(identifiers.count == 1)
        #expect(reconstructed == data)
    }
    #expect(providerFailureDiagnosticFragments(Data()).isEmpty)
    #expect(providerFailureDiagnosticFragments(Data(count: 16_385)).isEmpty)
    let unicode = Data(String(repeating: "\u{1F642}\"\\\n", count: 1000).utf8)
    let chunks = try providerFailureDiagnosticFragments(unicode).map { fragment in
        let value = try #require(JSONSerialization.jsonObject(
            with: Data(fragment.utf8)) as? [String: Any])
        let encoded = try #require(value["data"] as? String)
        return try #require(Data(base64Encoded: encoded))
    }
    #expect(chunks.reduce(into: Data()) { $0.append($1) } == unicode)
}

@Test
func expansionAdvertisedBooleanIsSampledOnceInOrder() throws {
    for expected in [false, true] {
        var calls: [String] = []
        let expansion = try readAXExpansion(
            readNames: {
                calls.append("names")
                return (.success, [kAXExpandedAttribute] as CFArray)
            },
            readValue: {
                calls.append("value")
                return (.success, expected ? kCFBooleanTrue : kCFBooleanFalse)
            }
        )
        #expect(expansion == expected)
        #expect(calls == ["names", "value"])
    }
}

@Test
func expansionUnknownApplicabilityThrowsWithoutReadingValue() {
    let cases: [(AXError, CFTypeRef?)] = [
        (.cannotComplete, nil),
        (.attributeUnsupported, nil),
        (.noValue, nil),
        (.cannotComplete, [kAXExpandedAttribute] as CFArray),
        (.success, nil),
        (.success, "AXExpanded" as CFString),
        (.success, [NSNumber(value: 1)] as CFArray),
        (.success, [kAXExpandedAttribute as Any, NSNumber(value: 1)] as CFArray),
    ]
    for (status, names) in cases {
        var valueReads = 0
        do {
            _ = try readAXExpansion(
                readNames: { (status, names) },
                readValue: {
                    valueReads += 1
                    return (.success, kCFBooleanFalse)
                }
            )
            Issue.record("Unknown applicability must reject the snapshot")
        } catch {
            guard case ComputerUseError.stateUnavailable = error else {
                Issue.record("Expected stateUnavailable, got \(error)")
                continue
            }
        }
        #expect(valueReads == 0)
    }
}

@Test
func expansionAdvertisedInvalidValueThrowsInsteadOfCoercingNumbers() {
    let cases: [(AXError, CFTypeRef?)] = [
        (.cannotComplete, kCFBooleanFalse),
        (.attributeUnsupported, nil),
        (.noValue, nil),
        (.success, nil),
        (.success, "false" as CFString),
        (.success, NSNumber(value: 0)),
        (.success, NSNumber(value: 1)),
        (.success, NSNumber(value: 2)),
        (.success, [String]() as CFArray),
    ]
    for (status, value) in cases {
        var valueReads = 0
        do {
            _ = try readAXExpansion(
                readNames: { (.success, [kAXExpandedAttribute] as CFArray) },
                readValue: {
                    valueReads += 1
                    return (status, value)
                }
            )
            Issue.record("Invalid advertised value must reject the snapshot")
        } catch {
            guard case ComputerUseError.stateUnavailable = error else {
                Issue.record("Expected stateUnavailable, got \(error)")
                continue
            }
        }
        #expect(valueReads == 1)
    }
}

@Test
func expansionApplicabilityHasNoRoleCacheOrDiagnosticBudget() throws {
    var namesReads = 0
    var valueReads = 0
    // Same-role objects must each receive an independent expansion read.
    let elements = (1...40).map { AXUIElementCreateApplication(pid_t(-$0)) }
    for (index, element) in elements.enumerated() {
        let advertised = index % 2 == 0
        let expected = index % 4 == 0
        let expansion = try readAXExpansion(
            readNames: {
                namesReads += 1
                let names = advertised ? [kAXExpandedAttribute as String] : []
                return (.success, names as CFArray)
            },
            readValue: {
                valueReads += 1
                return (.success, expected ? kCFBooleanTrue : kCFBooleanFalse)
            }
        )
        #expect(expansion == (advertised ? expected : nil))
        let traits = summarizeTraits(of: element, expansion: expansion, valueSettable: false)
        let states = structuredStateTokens(
            of: element, role: kAXGroupRole as String,
            focusedElement: nil, expansion: expansion
        )
        #expect(traits.contains("expanded") == (advertised && expected))
        #expect(states == (advertised ? (expected ? "[expanded]" : "[collapsed]") : nil))
    }
    #expect(namesReads == 40 && valueReads == 20)
}

@Test
func expansionRendererSharesOneSampleAndResamplesEachObject() throws {
    let root = AXUIElementCreateApplication(-1)
    let other = AXUIElementCreateApplication(-2)
    let samples: [Bool?] = [true, false, nil]
    var reads = 0
    var valueReads = 0
    var renderer = TreeRenderer(
        context: RenderContext(
            windowBounds: nil, focusedElement: nil, focusedAncestry: [],
            textLimit: .defaults, treeLimits: .defaults
        ),
        readTitle: { _ in "Target" },
        readExpansion: { element in
            #expect(CFEqual(element, reads == 1 ? other : root))
            let sample = samples[min(reads, samples.count - 1)]
            defer { reads += 1 }
            return try readAXExpansion(
                readNames: {
                    let names = sample == nil ? [] : [kAXExpandedAttribute as String]
                    return (.success, names as CFArray)
                },
                readValue: {
                    valueReads += 1
                    return (.success, sample == true ? kCFBooleanTrue : kCFBooleanFalse)
                }
            )
        }
    )
    for (index, element) in [root, other, root].enumerated() {
        try renderer.render(element, relativeRuntimeID: [index])
        #expect(reads == index + 1)
    }
    #expect(renderer.records[0]?.states == "[expanded]")
    #expect(renderer.records[1]?.states == "[collapsed]")
    #expect(renderer.records[2]?.states == nil)
    // Invalid AX wrappers render AXUnknown, outside the former role whitelist.
    #expect(renderer.records[0]?.providerRole == canonicalAXRole("AXUnknown", subrole: nil))
    #expect(valueReads == 2)
    #expect(renderer.lines.count == 3)
    #expect(renderer.lines[0].contains("(expanded)"))
    #expect(!renderer.lines[1].contains("expanded"))
    #expect(!renderer.lines[2].contains("expanded"))
}

@Test
func expansionReaderFailureEscapesRendererBeforePublishingRecord() {
    let root = AXUIElementCreateApplication(-1)
    var reads = 0
    var renderer = TreeRenderer(
        context: RenderContext(
            windowBounds: nil, focusedElement: nil, focusedAncestry: [],
            textLimit: .defaults, treeLimits: .defaults
        ),
        readTitle: { _ in "Target" },
        readExpansion: { _ in
            reads += 1
            return try readAXExpansion(
                readNames: { (.cannotComplete, nil) },
                readValue: {
                    Issue.record("Failed applicability must not read the value")
                    return (.success, kCFBooleanFalse)
                }
            )
        }
    )
    do {
        try renderer.render(root)
        Issue.record("Renderer must propagate expansion failure")
    } catch {
        guard case ComputerUseError.stateUnavailable = error else {
            Issue.record("Expected stateUnavailable, got \(error)")
            return
        }
    }
    #expect(reads == 1)
    #expect(renderer.nextIndex == 0)
    #expect(renderer.records.isEmpty && renderer.lines.isEmpty)
}

@Test
func openMenuBarItemsExposeTheirChildren() throws {
    func suppressesMenuChildren(traits: [String]) -> Bool {
        shouldSuppressChildren(
            role: kAXMenuBarItemRole as String,
            title: "File",
            label: nil,
            help: nil,
            value: nil,
            identifier: nil,
            traits: traits,
            actions: [],
            children: [],
            genericTextSummary: nil
        )
    }

    #expect(suppressesMenuChildren(traits: []))
    #expect(!suppressesMenuChildren(traits: ["selected"]))
    #expect(!suppressesMenuChildren(traits: ["expanded"]))
    let element = AXUIElementCreateApplication(-1)
    for advertised in [false, true] {
        let expansion = try readAXExpansion(
            readNames: {
                let names = advertised ? [kAXExpandedAttribute as String] : []
                return (.success, names as CFArray)
            },
            readValue: { (.success, kCFBooleanFalse) }
        )
        var traits = summarizeTraits(of: element, expansion: expansion, valueSettable: false)
        #expect(suppressesMenuChildren(traits: traits))
        // Selection remains an independent reason to expose the open menu.
        traits.append("selected")
        #expect(!suppressesMenuChildren(traits: traits))
    }
}

@Test
func disabledNamedCheckboxWrapperPromotesOneEnabledUnnamedActionChild() {
    let parent = SemanticControlProxyFacts(
        role: kAXCheckBoxRole as String,
        enabled: false,
        hasSemanticLabel: true,
        hasPrimaryAction: false,
        valueSettable: false
    )
    let children = [
        SemanticControlProxyFacts(
            role: kAXCheckBoxRole as String,
            enabled: true,
            hasSemanticLabel: false,
            hasPrimaryAction: true,
            valueSettable: false
        ),
        SemanticControlProxyFacts(
            role: kAXImageRole as String,
            enabled: true,
            hasSemanticLabel: true,
            hasPrimaryAction: false,
            valueSettable: false
        ),
    ]

    #expect(
        semanticControlProxyChildIndex(
            parent: parent,
            children: children
        ) == 0
    )
}

@Test
func semanticControlProxyRejectsAmbiguousOrIndependentChildren() {
    let parent = SemanticControlProxyFacts(
        role: kAXCheckBoxRole as String,
        enabled: false,
        hasSemanticLabel: true,
        hasPrimaryAction: false,
        valueSettable: false
    )
    let candidate = SemanticControlProxyFacts(
        role: kAXCheckBoxRole as String,
        enabled: true,
        hasSemanticLabel: false,
        hasPrimaryAction: true,
        valueSettable: false
    )

    #expect(
        semanticControlProxyChildIndex(
            parent: parent,
            children: [candidate, candidate]
        ) == nil
    )
    #expect(
        semanticControlProxyChildIndex(
            parent: parent,
            children: [
                SemanticControlProxyFacts(
                    role: kAXCheckBoxRole as String,
                    enabled: true,
                    hasSemanticLabel: true,
                    hasPrimaryAction: true,
                    valueSettable: false
                ),
            ]
        ) == nil
    )
    #expect(
        semanticControlProxyChildIndex(
            parent: parent,
            children: [
                candidate,
                SemanticControlProxyFacts(
                    role: kAXButtonRole as String,
                    enabled: true,
                    hasSemanticLabel: true,
                    hasPrimaryAction: true,
                    valueSettable: false
                ),
            ]
        ) == nil
    )
}

@Test
func semanticControlProxyRejectsNonWrapperShapes() {
    let candidate = SemanticControlProxyFacts(
        role: kAXCheckBoxRole as String,
        enabled: true,
        hasSemanticLabel: false,
        hasPrimaryAction: true,
        valueSettable: false
    )

    for parent in [
        SemanticControlProxyFacts(
            role: kAXCheckBoxRole as String,
            enabled: true,
            hasSemanticLabel: true,
            hasPrimaryAction: false,
            valueSettable: false
        ),
        SemanticControlProxyFacts(
            role: kAXCheckBoxRole as String,
            enabled: false,
            hasSemanticLabel: false,
            hasPrimaryAction: false,
            valueSettable: false
        ),
        SemanticControlProxyFacts(
            role: kAXButtonRole as String,
            enabled: false,
            hasSemanticLabel: true,
            hasPrimaryAction: false,
            valueSettable: false
        ),
    ] {
        #expect(
            semanticControlProxyChildIndex(
                parent: parent,
                children: [candidate]
            ) == nil
        )
    }
}
