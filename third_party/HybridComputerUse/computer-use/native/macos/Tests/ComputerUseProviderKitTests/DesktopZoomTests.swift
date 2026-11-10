@testable import ComputerUseProviderKit
import CoreGraphics
import Foundation
import Testing

private func zoomFixture() throws -> [String: Any] {
    var root = URL(fileURLWithPath: #filePath)
    for _ in 0..<5 {
        root.deleteLastPathComponent()
    }
    let file = root.appendingPathComponent("testdata/contract/zoom_geometry.json")
    return try #require(
        JSONSerialization.jsonObject(with: Data(contentsOf: file)) as? [String: Any])
}

private func doubles(_ value: Any?) -> [Double] {
    (value as? [Any] ?? []).map { ($0 as? NSNumber)?.doubleValue ?? .nan }
}

/// Every provider computes the same zoom geometry as the Linux provider.
@Test
func zoomGeometryMatchesTheSharedFixture() throws {
    let cases = try #require(try zoomFixture()["cases"] as? [[String: Any]])
    #expect(cases.count > 50)
    for item in cases {
        let name = item["name"] as? String ?? ""
        let native = doubles(item["native"]).map(Int.init)
        let frame = desktopDeliveredBounds(
            CGRect(x: 0, y: 0, width: native[0], height: native[1]))
        #expect(doubles(item["frame"]) == [Double(frame.width), Double(frame.height)], "\(name)")
        let result = try? desktopZoomGeometry(
            region: doubles(item["region"]), nativeWidth: native[0], nativeHeight: native[1])
        guard let expect = item["expect"] as? [String: Any] else {
            #expect(result == nil, "\(name) must be rejected")
            continue
        }
        let geometry = try #require(result, "\(name)")
        #expect(
            [geometry.nativeX, geometry.nativeY, geometry.nativeWidth, geometry.nativeHeight]
                == doubles(expect["native"]).map(Int.init), "\(name)")
        #expect(geometry.width == (expect["width"] as? Int), "\(name)")
        #expect(geometry.height == (expect["height"] as? Int), "\(name)")
        for (got, want) in zip(geometry.region, doubles(expect["region"])) {
            #expect(abs(got - want) < 1e-9, "\(name)")
        }
        // The stated per-axis mapping lands within 2 frame pixels.
        let scaleX = Double(geometry.width) / (geometry.region[2] - geometry.region[0])
        let scaleY = Double(geometry.height) / (geometry.region[3] - geometry.region[1])
        let perX = Double(native[0]) / Double(frame.width)
        let perY = Double(native[1]) / Double(frame.height)
        for u in [0, Double(geometry.width) / 2, Double(geometry.width) - 0.5] {
            for v in [0, Double(geometry.height) / 2, Double(geometry.height) - 0.5] {
                let nativeX = Double(geometry.nativeX)
                    + u * Double(geometry.nativeWidth) / Double(geometry.width)
                let nativeY = Double(geometry.nativeY)
                    + v * Double(geometry.nativeHeight) / Double(geometry.height)
                #expect(abs(geometry.region[0] + u / scaleX - nativeX / perX) <= 2, "\(name)")
                #expect(abs(geometry.region[1] + v / scaleY - nativeY / perY) <= 2, "\(name)")
            }
        }
    }
}

private func layout(_ windows: [(String, Double, Double, Double, Double)]) -> [String: Any] {
    [
        "desktop": ["x": 0.0, "y": 0.0, "width": 100.0, "height": 100.0],
        "activeWindow": windows.first?.0 ?? "",
        "windows": windows.map {
            ["id": $0.0, "pid": 1, "x": $0.1, "y": $0.2, "width": $0.3, "height": $0.4]
        },
    ]
}

@Test
func zoomChecksEveryWindowOverTheRegion() {
    let before = layout([("1", 0, 0, 50, 50), ("2", 60, 60, 30, 30)])
    let region = CGRect(x: 10, y: 10, width: 30, height: 20)
    let cases: [(String, [String: Any], Bool)] = [
        ("unrelated moved", layout([("1", 0, 0, 50, 50), ("2", 65, 60, 30, 30)]), true),
        ("window over region moved", layout([("1", 2, 0, 50, 50), ("2", 60, 60, 30, 30)]), false),
        ("new window over a corner",
         layout([("3", 38, 28, 5, 5), ("1", 0, 0, 50, 50), ("2", 60, 60, 30, 30)]), false),
    ]
    for (name, current, valid) in cases {
        #expect(
            desktopZoomRegionStillValid(published: before, current: current, rect: region) == valid,
            "\(name)")
    }
    #expect(!desktopZoomRegionStillValid(published: nil, current: before, rect: region))
}

@Test
func zoomRequestsPassNativeValidation() throws {
    let request: [String: Any] = [
        "protocol": "computer-use-provider", "schema_version": 2, "request_id": "z",
        "operation": "desktop_execute_batch",
        "desktop": [
            "protocol_version": 2, "settle_ms": 0, "frame_id": "f",
            "actions": [["type": "zoom", "region": [0, 0, 100, 50]]],
        ],
    ]
    try ProviderRequestValidator.validate(request)
}
