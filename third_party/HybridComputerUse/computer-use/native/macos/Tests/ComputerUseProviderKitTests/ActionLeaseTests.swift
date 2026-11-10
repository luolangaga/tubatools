@testable import ComputerUseProviderKit
import ApplicationServices
import CoreGraphics
import Foundation
import Testing

private func identityReads(
    role: String = "AXButton",
    title: String? = "Save",
    position: CGPoint = CGPoint(x: 10, y: 20),
    size: CGSize = CGSize(width: 80, height: 24)
) -> [AXRead] {
    var origin = position
    var extent = size
    func string(_ value: String?) -> AXRead {
        value.map { AXRead(status: .success, value: $0 as CFString) }
            ?? AXRead(status: .noValue, value: nil)
    }
    return [
        string(role),
        string(nil),
        string(title),
        string(nil),
        string(nil),
        AXRead(status: .success, value: AXValueCreate(.cgPoint, &origin)),
        AXRead(status: .success, value: AXValueCreate(.cgSize, &extent)),
    ]
}

@Test
func liveIdentityMatchesOnlyTheSameRoleNamesAndFrame() throws {
    let recorded = try #require(AXLiveIdentity(reads: identityReads()))
    #expect(AXLiveIdentity(reads: identityReads()) == recorded)
    #expect(recorded.frame == CGRect(x: 10, y: 20, width: 80, height: 24))
    #expect(AXLiveIdentity(reads: identityReads(title: "Saved")) != recorded)
    #expect(AXLiveIdentity(reads: identityReads(position: CGPoint(x: 10, y: 60))) != recorded)
    #expect(AXLiveIdentity(reads: identityReads(role: "AXCheckBox")) != recorded)
    var failed = identityReads()
    failed[0] = AXRead(status: .invalidUIElement, value: nil)
    #expect(AXLiveIdentity(reads: failed) == nil)
}

@Test
func actionLeaseIsLimitedToBoundMutationsOfTheSameProcessAndWindow() {
    func lease(
        tool: String = "set_value",
        target: Bool = true,
        coordinates: Bool = false,
        generation: String = "g1",
        cachedHandle: String? = "w2:a",
        requestedHandle: String = "w2:a"
    ) -> Bool {
        actionMaySkipInitialSnapshot(
            tool: tool,
            hasElementTarget: target,
            hasCoordinates: coordinates,
            cachedGeneration: "g1",
            currentGeneration: generation,
            cachedWindowHandle: cachedHandle,
            requestedWindowHandle: requestedHandle
        )
    }
    #expect(lease())
    #expect(lease(tool: "type"))
    #expect(lease(tool: "keypress", target: false))
    #expect(lease(tool: "scroll", target: false))
    #expect(!lease(tool: "click", target: false, coordinates: true))
    #expect(!lease(tool: "resolve_target"))
    #expect(!lease(generation: "g2"))
    #expect(!lease(cachedHandle: "w2:b"))
    #expect(!lease(requestedHandle: ""))
    #expect(!lease(tool: "set_value", target: false))
}

@Test
func insertedTextReplacesTheSelectedUTF16Range() {
    #expect(textAfterInsertion("hello world", selection: CFRange(location: 6, length: 5), text: "there") == "hello there")
    #expect(textAfterInsertion("a😀b", selection: CFRange(location: 3, length: 0), text: "Ω") == "a😀Ωb")
    #expect(textAfterInsertion("abc", selection: CFRange(location: 4, length: 0), text: "x") == nil)
}
