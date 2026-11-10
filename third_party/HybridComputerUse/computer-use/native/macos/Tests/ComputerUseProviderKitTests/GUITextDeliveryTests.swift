import AppKit
import Foundation
@testable import ComputerUseProviderKit
import Testing

private func uniquePasteboard() -> NSPasteboard {
    NSPasteboard(
        name: NSPasteboard.Name(
            "computer-use-tests-\(UUID().uuidString)"
        )
    )
}

private func seededPasteboard() -> NSPasteboard {
    let pasteboard = uniquePasteboard()
    let first = NSPasteboardItem()
    first.setString("original text", forType: .string)
    first.setData(
        Data([0x00, 0x7f, 0xff]),
        forType: NSPasteboard.PasteboardType(
            "com.openai.computer-use.test-binary"
        )
    )
    let second = NSPasteboardItem()
    second.setData(
        Data("second item".utf8),
        forType: NSPasteboard.PasteboardType(
            "com.openai.computer-use.test-second"
        )
    )
    pasteboard.clearContents()
    #expect(pasteboard.writeObjects([first, second]))
    return pasteboard
}

@Suite(.serialized)
struct GUITextDeliveryTests {

@Test
func temporaryPasteboardTextRestoresEveryItemAndType() throws {
    let pasteboard = seededPasteboard()
    defer {
        pasteboard.releaseGlobally()
    }
    let before = try PasteboardSnapshot.capture(
        from: pasteboard,
        attempts: 1
    )
    var observedText: String?

    try InputSimulation.withTemporaryPasteboardText(
        "replacement 中文 😀",
        pasteboard: pasteboard
    ) {
        observedText = pasteboard.string(forType: .string)
    }

    let after = try PasteboardSnapshot.capture(
        from: pasteboard,
        attempts: 1
    )
    #expect(observedText == "replacement 中文 😀")
    #expect(after == before)
}

@Test
func temporaryPasteboardTextRestoresEmptyPasteboard() throws {
    let pasteboard = uniquePasteboard()
    defer {
        pasteboard.releaseGlobally()
    }
    pasteboard.clearContents()

    try InputSimulation.withTemporaryPasteboardText(
        "temporary",
        pasteboard: pasteboard
    ) {
        #expect(pasteboard.string(forType: .string) == "temporary")
    }

    #expect(pasteboard.pasteboardItems?.isEmpty != false)
}

@Test
func temporaryPasteboardTextPreservesConcurrentClipboardChange() throws {
    let pasteboard = seededPasteboard()
    defer {
        pasteboard.releaseGlobally()
    }

    try InputSimulation.withTemporaryPasteboardText(
        "temporary",
        pasteboard: pasteboard
    ) {
        pasteboard.clearContents()
        #expect(pasteboard.setString("external update", forType: .string))
    }

    #expect(pasteboard.string(forType: .string) == "external update")
}

@Test
func temporaryPasteboardTextRemainsStagedThroughAcknowledgement() throws {
    let pasteboard = seededPasteboard()
    defer {
        pasteboard.releaseGlobally()
    }
    let before = try PasteboardSnapshot.capture(
        from: pasteboard,
        attempts: 1
    )
    var pasteCommandObservedText: String?
    var acknowledgementObservedText: String?

    try InputSimulation.withTemporaryPasteboardText(
        "delayed consumer",
        pasteboard: pasteboard,
        awaitConsumption: {
            acknowledgementObservedText = pasteboard.string(forType: .string)
        }
    ) {
        pasteCommandObservedText = pasteboard.string(forType: .string)
    }

    let after = try PasteboardSnapshot.capture(
        from: pasteboard,
        attempts: 1
    )
    #expect(pasteCommandObservedText == "delayed consumer")
    #expect(acknowledgementObservedText == "delayed consumer")
    #expect(after == before)
}

@Test
func acknowledgementFailureRestoresClipboardAndReportsPossibleInput() throws {
    let pasteboard = seededPasteboard()
    defer {
        pasteboard.releaseGlobally()
    }
    let before = try PasteboardSnapshot.capture(
        from: pasteboard,
        attempts: 1
    )

    do {
        try InputSimulation.withTemporaryPasteboardText(
            "temporary",
            pasteboard: pasteboard,
            awaitConsumption: {
                throw ComputerUseError.stateUnavailable(
                    "synthetic acknowledgement timeout"
                )
            }
        ) {}
        Issue.record("expected acknowledgement to fail")
    } catch {
        let deliveryError = error as? InputDeliveryError
        #expect(deliveryError?.inputEventsEmitted == true)
        #expect(
            deliveryError?.localizedDescription.contains(
                "target consumption was not acknowledged"
            ) == true
        )
    }

    let after = try PasteboardSnapshot.capture(
        from: pasteboard,
        attempts: 1
    )
    #expect(after == before)
}

@Test
func emptyTemporaryPasteboardTextDoesNotMutateClipboard() throws {
    let pasteboard = seededPasteboard()
    defer {
        pasteboard.releaseGlobally()
    }
    let before = try PasteboardSnapshot.capture(
        from: pasteboard,
        attempts: 1
    )
    var pasteAttempted = false

    try InputSimulation.withTemporaryPasteboardText(
        "",
        pasteboard: pasteboard
    ) {
        pasteAttempted = true
    }

    let after = try PasteboardSnapshot.capture(
        from: pasteboard,
        attempts: 1
    )
    #expect(!pasteAttempted)
    #expect(after == before)
}

@Test
func pasteFailureRestoresClipboardAndReportsPossibleInput() throws {
    let pasteboard = seededPasteboard()
    defer {
        pasteboard.releaseGlobally()
    }
    let before = try PasteboardSnapshot.capture(
        from: pasteboard,
        attempts: 1
    )

    do {
        try InputSimulation.withTemporaryPasteboardText(
            "temporary",
            pasteboard: pasteboard
        ) {
            throw ComputerUseError.message("synthetic paste failure")
        }
        Issue.record("expected temporary paste to fail")
    } catch {
        let deliveryError = error as? InputDeliveryError
        #expect(deliveryError?.inputEventsEmitted == true)
    }

    let after = try PasteboardSnapshot.capture(
        from: pasteboard,
        attempts: 1
    )
    #expect(after == before)
}

}
