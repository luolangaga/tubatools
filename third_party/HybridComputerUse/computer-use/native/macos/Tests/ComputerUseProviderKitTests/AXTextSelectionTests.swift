@testable import ComputerUseProviderKit
import Foundation
import Testing

@Test func selectionUsesUTF16OffsetsAndPreservesNewlines() throws {
    #expect(try uniqueAXTextSelectionRange(value: "😀中文\nnext", text: "中文\n") == NSRange(location: 2, length: 3))
    #expect(try uniqueAXTextSelectionRange(value: "x😀y", text: "😀") == NSRange(location: 1, length: 2))
    #expect(try uniqueAXTextSelectionRange(value: "e\u{301}😀", text: "") == NSRange(location: 0, length: 4))
}

@Test func selectionRejectsAmbiguousMissingAndEmptyBeforeWriting() {
    for (value, text) in [("banana", "ana"), ("a a", "a"), ("abc", "z"), ("", "")] {
        var writes = 0
        #expect(throws: (any Error).self) {
            try setVerifiedAXTextSelection(value: value, text: text, write: { _ in writes += 1 }, read: { (nil, nil, nil) })
        }
        #expect(writes == 0)
    }
}

@Test func selectionRequiresExactRangeTextAndUnchangedValue() throws {
    let expected = NSRange(location: 2, length: 2)
    var writes: [NSRange] = []
    try setVerifiedAXTextSelection(value: "😀中文", text: "中文", write: { writes.append($0) }, read: { ("😀中文", expected, "中文") })
    #expect(writes == [expected])
    let observations: [(String?, NSRange?, String?)] = [
        ("😀中文", NSRange(location: 0, length: 4), "😀中文"),
        ("😀中文", expected, nil),
        ("😀改字", expected, "中文"),
        ("😀中文", nil, "中文"),
    ]
    for observed in observations {
        do {
            try setVerifiedAXTextSelection(value: "😀中文", text: "中文", write: { _ in }, read: { observed })
            Issue.record("Expected exact readback failure")
        } catch let error as InputDeliveryError {
            #expect(error.inputEventsEmitted)
            #expect(error.readbackVerified == false)
            #expect(error.failureCode == "readback_mismatch")
        }
    }
}

@Test func failedSelectionWriteRetainsUnknownEffectEvidence() {
    var reads = 0
    do {
        try setVerifiedAXTextSelection(value: "abc", text: "b", write: { _ in
            throw InputDeliveryError(message: "failed", inputEventsEmitted: true, failureCode: "effect_unknown", partialInputPossible: true)
        }, read: { reads += 1; return (nil, nil, nil) })
        Issue.record("Expected write failure")
    } catch let error as InputDeliveryError {
        #expect(error.failureCode == "effect_unknown")
        #expect(error.partialInputPossible == true)
    } catch { Issue.record("Unexpected error: \(error)") }
    #expect(reads == 0)
}

@Test func unsupportedMacSelectionCapabilitiesFailBeforeRuntime() {
    let operations: [[String: Any]] = [
        ["to_target": ["providerObjectPath": "other"]],
        ["semanticTarget": ["editTarget": ["providerObjectPath": "editor"]]],
        ["element": ["editTarget": ["providerObjectPath": "editor"]]],
    ]
    for operation in operations {
        #expect(throws: (any Error).self) {
            try validateMacAccessibilityCapability(tool: "select", operation: operation)
        }
    }
}
