import CoreGraphics
import Foundation

func guiDragPoints(_ path: [CGPoint]) -> [CGPoint] {
    guard path.count >= 2 else { return [] }
    let segments = path.count - 1
    let totalSteps = max(24, segments)
    return (0..<segments).flatMap { index in
        InputSimulation.interpolatedDragPoints(
            from: path[index], to: path[index + 1],
            steps: totalSteps / segments + (index < totalSteps % segments ? 1 : 0)
        )
    }
}

func executeGUIDrag(
    path: [CGPoint], flags: CGEventFlags,
    post: (CGEventType, CGPoint, CGEventFlags) throws -> Void,
    sleep: (TimeInterval) -> Void = { Thread.sleep(forTimeInterval: $0) }
) throws {
    guard path.count >= 2, let start = path.first, let end = path.last else {
        throw ComputerUseError.invalidArguments("gui drag requires at least two points")
    }
    let points = guiDragPoints(path)
    var emitted = false
    var held = false
    var lastPoint = start
    do {
        try post(.mouseMoved, start, flags)
        emitted = true
        sleep(0.11)
        try post(.leftMouseDown, start, flags)
        held = true
        sleep(0.21)
        for point in points {
            try post(.leftMouseDragged, point, flags)
            lastPoint = point
            // Distribute a fixed total drag duration across all waypoints.
            sleep(24 * 0.042 / Double(points.count))
        }
        sleep(0.1)
        try post(.leftMouseUp, end, flags)
        held = false
        sleep(0.03)
    } catch {
        if held { try? post(.leftMouseUp, lastPoint, flags) }
        throw InputDeliveryError(
            message: "GUI drag failed: \(error)", inputEventsEmitted: emitted,
            partialInputPossible: emitted
        )
    }
}
