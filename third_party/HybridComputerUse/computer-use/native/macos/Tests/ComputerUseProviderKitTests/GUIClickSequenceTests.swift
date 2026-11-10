import CoreGraphics
import Foundation
@testable import ComputerUseProviderKit
import Testing

@Test
func guiDoubleClickPostsOneFirstPairAndOneSecondPair() throws {
    for name in ["left", "right", "middle"] {
        let button = mouseButton(name)
        let point = CGPoint(x: 120, y: 230)
        let flags: CGEventFlags = [.maskShift, .maskCommand]
        var emitted: [(CGEventType, Int)] = []
        var waits: [TimeInterval] = []
        var sequence: [String] = []
        try executeGUIClick(
            button: button, clickCount: 2, point: point, flags: flags,
            post: { type, actualPoint, actualButton, state, actualFlags in
                #expect(actualPoint == point)
                #expect(actualButton == button.button)
                #expect(actualFlags == flags)
                emitted.append((type, state))
                sequence.append("post-\(type.rawValue)")
            },
            sleep: {
                waits.append($0)
                sequence.append("wait")
            }
        )
        #expect(emitted.map { $0.0.rawValue } == [
            CGEventType.mouseMoved.rawValue, button.down.rawValue, button.up.rawValue,
            button.down.rawValue, button.up.rawValue,
        ])
        #expect(emitted.map { $0.1 } == [2, 1, 1, 2, 2])
        #expect(emitted.filter { $0.0 == button.down && $0.1 == 2 }.count == 1)
        #expect(waits == [0.03])
        #expect(sequence == [
            "post-\(CGEventType.mouseMoved.rawValue)", "wait",
            "post-\(button.down.rawValue)", "post-\(button.up.rawValue)",
            "post-\(button.down.rawValue)", "post-\(button.up.rawValue)",
        ])
    }
}

@Test
func guiSingleClickKeepsOnePairAndMovementBarrier() throws {
    var states: [Int] = []
    var waits: [TimeInterval] = []
    try executeGUIClick(
        button: mouseButton("left"), clickCount: 1, point: .zero, flags: [],
        post: { _, _, _, state, _ in states.append(state) },
        sleep: { waits.append($0) }
    )
    #expect(states == [1, 1, 1])
    #expect(waits == [0.03])
}

@Test
func guiClickSequenceStopsAtFirstPostingErrorWithoutTailReplay() {
    struct Failure: Error {}
    for failureIndex in 0..<5 {
        var attempts = 0
        var posted = 0
        var waits = 0
        #expect(throws: Failure.self) {
            try executeGUIClick(
                button: mouseButton("left"), clickCount: 2, point: .zero, flags: [],
                post: { _, _, _, _, _ in
                    let index = attempts
                    attempts += 1
                    if index == failureIndex { throw Failure() }
                    posted += 1
                },
                sleep: { _ in waits += 1 }
            )
        }
        #expect(attempts == failureIndex + 1)
        #expect(posted == failureIndex)
        #expect(waits == (failureIndex == 0 ? 0 : 1))
    }
}
