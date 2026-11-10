@testable import ComputerUseProviderKit
import CoreGraphics
import Testing

private func layout(
    title: String = "Document",
    x: Int = 10,
    activeWindow: String = "42"
) -> [String: Any] {
    [
        "desktop": ["x": 0, "y": 0, "width": 1920, "height": 1080],
        "activeWindow": activeWindow,
        "windows": [[
            "id": "42",
            "pid": 100,
            "title": title,
            "x": x,
            "y": 20,
            "width": 800,
            "height": 600,
        ]],
    ]
}

@Test
func guiFrameIgnoresWindowTitleChanges() {
    #expect(
        desktopLayoutFrameID(layout(title: "Document A"))
            == desktopLayoutFrameID(layout(title: "Document B"))
    )
}

@Test
func guiFrameChangesWithWindowGeometry() {
    #expect(
        desktopLayoutFrameID(layout(x: 10))
            != desktopLayoutFrameID(layout(x: 11))
    )
}

@Test
func guiFrameChangesWithActiveWindow() {
    #expect(
        desktopLayoutFrameID(layout(activeWindow: "42"))
            != desktopLayoutFrameID(layout(activeWindow: "84"))
    )
}

@Test
func gui1080pDesktopIsDeliveredAtTheSizeTheModelSees() {
    let delivered = desktopDeliveredBounds(
        CGRect(x: 0, y: 0, width: 1920, height: 1080)
    )

    #expect(delivered == CGRect(x: 0, y: 0, width: 1365, height: 768))
}

@Test
func guiLargerDesktopIsAspectBoundedTo768p() {
    let delivered = desktopDeliveredBounds(
        CGRect(x: 0, y: 0, width: 2560, height: 1440)
    )

    #expect(delivered == CGRect(x: 0, y: 0, width: 1365, height: 768))
}

@Test
func guiHeightLimitIsIndependentFromWidthLimit() {
    let delivered = desktopDeliveredBounds(
        CGRect(x: 0, y: 0, width: 1920, height: 1200)
    )

    #expect(delivered == CGRect(x: 0, y: 0, width: 1229, height: 768))
}

@Test
func guiDeliveredCoordinatesMapByPixelCenterWithDesktopOrigin() {
    let native = CGRect(x: -1920, y: -100, width: 1920, height: 1080)
    let delivered = desktopDeliveredBounds(native)
    let point = guiPoint(
        ["x": 682, "y": 383.5],
        deliveredBounds: delivered,
        nativeBounds: native
    )

    // 1920/1365 is not exact in binary floating point.
    #expect(abs(point.x - -960.5) < 1e-9)
    #expect(abs(point.y - 439.5) < 1e-9)
}

@Test
func guiBoundedCoordinatesMapByPixelCenter() {
    let native = CGRect(x: 0, y: 0, width: 2560, height: 1440)
    let delivered = desktopDeliveredBounds(native)
    let point = guiPoint(
        ["x": 682, "y": 383.5],
        deliveredBounds: delivered,
        nativeBounds: native
    )

    #expect(abs(point.x - 1279.5) < 1e-9)
    #expect(abs(point.y - 719.5) < 1e-9)
}

@Test(arguments: [
    [-0.1, 0], [0, -1], [1279.4, 0], [0, 719.5], [1280, 10], [Double.nan, 0],
])
func guiPointOutsideTheLastDeliveredPixelIsRejected(_ point: [Double]) {
    let bounds = CGRect(x: 0, y: 0, width: 1280, height: 720)
    #expect(throws: ComputerUseError.self) {
        try validateGUIPoint(["x": point[0], "y": point[1]], desktopBounds: bounds)
    }
}

@Test
func guiPointOnTheFirstAndLastDeliveredPixelIsAccepted() throws {
    let bounds = CGRect(x: 0, y: 0, width: 1280, height: 720)
    try validateGUIPoint(["x": 0.0, "y": 0.0], desktopBounds: bounds)
    try validateGUIPoint(["x": 1279.0, "y": 719.0], desktopBounds: bounds)
}
