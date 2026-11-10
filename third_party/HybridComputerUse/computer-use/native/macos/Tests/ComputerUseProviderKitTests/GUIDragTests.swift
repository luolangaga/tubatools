import CoreGraphics
import Foundation
import Testing
@testable import ComputerUseProviderKit

@Test
func guiDragVisitsEveryWaypointWithOnePressAndRelease() throws {
    for count in [2, 3, 24, 25, 256] {
        let path = (0..<count).map {
            CGPoint(x: Double($0) + 0.3, y: $0.isMultiple(of: 2) ? 20 : 90)
        }
        let flags: CGEventFlags = [.maskShift, .maskCommand]
        var events: [(CGEventType, CGPoint)] = []
        var waits: [TimeInterval] = []
        try executeGUIDrag(path: path, flags: flags, post: { type, point, actualFlags in
            #expect(actualFlags == flags)
            events.append((type, point))
        }, sleep: { waits.append($0) })
        #expect(events.first?.0 == .mouseMoved)
        #expect(events.first?.1 == path.first)
        #expect(events.filter { $0.0 == .leftMouseDown }.count == 1)
        #expect(events.filter { $0.0 == .leftMouseUp }.count == 1)
        #expect(events.last?.0 == .leftMouseUp)
        #expect(events.last?.1 == path.last)
        let dragged = events.filter { $0.0 == .leftMouseDragged }.map { $0.1 }
        var next = dragged.startIndex
        for waypoint in path.dropFirst() {
            let index = try #require(dragged[next...].firstIndex(of: waypoint))
            next = index + 1
        }
        #expect(dragged.count == max(24, count - 1))
        #expect(abs(waits.reduce(0, +) - 1.458) < 1e-9)
    }
}

@Test
func guiDragInterpolatesSegmentsNotEndpointChord() {
    let points = guiDragPoints([
        CGPoint(x: 0, y: 0), CGPoint(x: 0, y: 100), CGPoint(x: 100, y: 100),
    ])
    #expect(points.allSatisfy { $0.x == 0 || $0.y == 100 })
    #expect(points.contains(CGPoint(x: 0, y: 100)))
}

@Test
func guiDragFailureReleasesAtLastPointWithoutExecutingTail() throws {
    struct PostingFailure: Error {}
    for failureIndex in [0, 1, 2, 10, 26] {
        var attempts = 0
        var events: [(CGEventType, CGPoint)] = []
        do {
            try executeGUIDrag(path: [.zero, CGPoint(x: 100, y: 100)], flags: [], post: {
                type, point, _ in
                let index = attempts
                attempts += 1
                if index == failureIndex { throw PostingFailure() }
                events.append((type, point))
            }, sleep: { _ in })
            Issue.record("Drag unexpectedly succeeded")
        } catch let error as InputDeliveryError {
            #expect(error.inputEventsEmitted == (failureIndex > 0))
            #expect(error.partialInputPossible == (failureIndex > 0))
        }
        if failureIndex >= 2 {
            #expect(attempts == failureIndex + 2)
            #expect(events.last?.0 == .leftMouseUp)
            let lastDrag = events.dropLast().last?.1
            #expect(events.last?.1 == lastDrag)
        } else {
            #expect(attempts == failureIndex + 1)
        }
    }
}
