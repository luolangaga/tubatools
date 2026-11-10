import ApplicationServices
import CoreGraphics
import Foundation
import ImageIO

extension ComputerUseService {
    enum SemanticGeometryClickRoute: Equatable {
        case boundGlobalHID
        case globalHID
    }

    func semanticGeometryClickRoute(
        hasWindowBounds: Bool,
        hasWindowID: Bool
    ) -> SemanticGeometryClickRoute {
        if hasWindowBounds, hasWindowID {
            return .boundGlobalHID
        }
        return .globalHID
    }

    func semanticAXClickRequiresBoundWindowPreparation(
        method: ClickMethod,
        hasWindowBounds: Bool,
        hasWindowID: Bool
    ) -> Bool {
        guard method == .auto || method == .accessibility else {
            return false
        }
        return hasWindowBounds && hasWindowID
    }

    func windowPointToGlobalPoint(snapshot: AppSnapshot, point: CGPoint) throws -> CGPoint {
        guard let windowBounds = snapshot.windowBounds else {
            let appReference = snapshot.app.bundleIdentifier ?? snapshot.app.name
            throw ComputerUseError.stateUnavailable("No window bounds are available for \(appReference). Run get_app_state after bringing the app on screen.")
        }

        return CGPoint(x: windowBounds.minX + point.x, y: windowBounds.minY + point.y)
    }

    private func debugInputFallback(tool: String, targetDescription: String, snapshot: AppSnapshot) {
        guard inputFallbackDebugEnabled(environment: ProcessInfo.processInfo.environment) else {
            return
        }

        let appReference = snapshot.app.bundleIdentifier ?? snapshot.app.name
        fputs(
            "[computer-use] global pointer fallback tool=\(tool) app=\(appReference) target=\(targetDescription)\n",
            stderr
        )
    }

    func integralScrollPageCount(_ pages: Double) -> Int? {
        let rounded = pages.rounded(.toNearestOrAwayFromZero)
        guard abs(pages - rounded) < 0.000001 else {
            return nil
        }
        return max(Int(rounded), 1)
    }

    func performScrollEvent(
        at point: CGPoint,
        direction: String,
        pages: Double,
        targetDescription: String,
        snapshot: AppSnapshot
    ) throws {
        let eventPoint = inputEventPoint(fromScreenStatePoint: point)

        if globalPointerFallbacksEnabled(environment: ProcessInfo.processInfo.environment) {
            debugInputFallback(
                tool: "scroll",
                targetDescription: targetDescription,
                snapshot: snapshot
            )
            try InputSimulation.prepareAppForGlobalPointerInput(
                snapshot.app,
                windowID: snapshot.targetWindowID
            )
            try InputSimulation.scrollGlobally(at: eventPoint, direction: direction, pages: pages)
            return
        }

        try InputSimulation.scrollTargeted(at: eventPoint, direction: direction, pages: pages, pid: snapshot.app.pid)
    }

    func performNonAXClickFallback(
        at point: CGPoint,
        button: MouseButtonKind,
        clickCount: Int,
        targetDescription: String,
        snapshot: AppSnapshot
    ) throws {
        let eventPoint = inputEventPoint(fromScreenStatePoint: point)

        if globalPointerFallbacksEnabled(environment: ProcessInfo.processInfo.environment) {
            debugInputFallback(
                tool: "click",
                targetDescription: targetDescription,
                snapshot: snapshot
            )
            try InputSimulation.prepareAppForGlobalPointerInput(
                snapshot.app,
                windowID: snapshot.targetWindowID
            )
            try InputSimulation.clickGlobally(at: eventPoint, button: button, clickCount: clickCount)
            return
        }

        do {
            try InputSimulation.clickTargeted(
                at: eventPoint,
                button: button,
                clickCount: clickCount,
                pid: snapshot.app.pid
            )
            return
        } catch {
            guard globalPointerFallbacksEnabled(environment: ProcessInfo.processInfo.environment) else {
                throw ComputerUseError.message(
                    "click could not be handled through accessibility, and global pointer fallback is disabled. Set COMPUTER_USE_ALLOW_GLOBAL_POINTER_FALLBACKS=1 to allow physical-pointer fallback for this process."
                )
            }
        }
    }

    func performSemanticallyBoundGlobalClick(
        on record: ElementRecord,
        snapshot: AppSnapshot,
        button: MouseButtonKind,
        clickCount: Int
    ) throws {
        guard let windowPoint = clickPoint(for: record, snapshot: snapshot) else {
            throw ComputerUseError.stateUnavailable(
                "semantic target has no clickable frame"
            )
        }

        let screenPoint = try windowPointToGlobalPoint(
            snapshot: snapshot,
            point: windowPoint
        )
        let eventPoint = inputEventPoint(fromScreenStatePoint: screenPoint)
        let route = semanticGeometryClickRoute(
            hasWindowBounds: snapshot.windowBounds != nil,
            hasWindowID: snapshot.targetWindowID != nil
        )

        switch route {
        case .boundGlobalHID:
            debugClickDecision(
                "executed=bound_global_hid reason=verified_snapshot_window_geometry target=\(clickDebugDescription(record))"
            )
            try InputSimulation.prepareBoundWindowForSemanticPointerInput(
                snapshot
            )
            try InputSimulation.clickGlobally(
                at: eventPoint,
                button: button,
                clickCount: clickCount
            )
        case .globalHID:
            debugClickDecision(
                "executed=global_hid reason=window_bound_route_unavailable target=\(clickDebugDescription(record))"
            )
            try InputSimulation.prepareAppForGlobalPointerInput(
                snapshot.app,
                windowID: snapshot.targetWindowID
            )
            try InputSimulation.clickGlobally(
                at: eventPoint,
                button: button,
                clickCount: clickCount
            )
        }
    }

    func performExplicitMouseClick(
        method: ClickMethod,
        at point: CGPoint,
        windowPoint: CGPoint,
        button: MouseButtonKind,
        clickCount: Int,
        targetDescription: String,
        snapshot: AppSnapshot
    ) throws {
        let eventPoint = inputEventPoint(fromScreenStatePoint: point)

        switch method {
        case .appPost:
            debugClickDecision("requested=app_post executed=pid_post target=\(targetDescription)")
            try InputSimulation.clickTargeted(
                at: eventPoint,
                button: button,
                clickCount: clickCount,
                pid: snapshot.app.pid
            )
        case .skyClick:
            guard let windowBounds = snapshot.windowBounds, let windowID = snapshot.targetWindowID else {
                throw ComputerUseError.stateUnavailable(
                    "click_method 'sky_click' requires a current on-screen target window. Run get_app_state again."
                )
            }
            debugClickDecision("requested=sky_click executed=skylight_pid_post target=\(targetDescription)")
            try InputSimulation.clickWithSkyLight(
                at: eventPoint,
                windowPoint: windowPoint,
                windowBounds: windowBounds,
                windowID: windowID,
                clickCount: clickCount,
                pid: snapshot.app.pid
            )
        case .global:
            guard globalPointerFallbacksEnabled(environment: ProcessInfo.processInfo.environment) else {
                throw ComputerUseError.message(
                    "click_method 'global' requires COMPUTER_USE_ALLOW_GLOBAL_POINTER_FALLBACKS=1 because it may move the system pointer and change foreground focus"
                )
            }
            debugClickDecision("requested=global executed=global_hid target=\(targetDescription)")
            try InputSimulation.prepareAppForGlobalPointerInput(
                snapshot.app,
                windowID: snapshot.targetWindowID
            )
            try InputSimulation.clickGlobally(at: eventPoint, button: button, clickCount: clickCount)
        case .auto, .accessibility:
            throw ComputerUseError.message(
                "click_method '\(method.rawValue)' is not a direct mouse event method"
            )
        }
    }
}
