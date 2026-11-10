import CoreGraphics
import Foundation

// GUI zoom: an enlarged region of the current full-desktop GUI frame. The
// contract is shared with the Linux and Windows providers and pinned by
// testdata/contract/zoom_geometry.json: the region is in frame pixels, it is
// snapped outward to whole native pixels, each axis maps back by its own
// ratio, and the zoom is bound to the frame its region came from.

let guiZoomMinSide: Double = 8
let guiZoomMaxScale: Double = 4

struct DesktopZoomGeometry: Equatable {
    // The native crop in desktop-relative native pixels.
    let nativeX: Int
    let nativeY: Int
    let nativeWidth: Int
    let nativeHeight: Int
    // The snapped crop in frame pixels, unrounded.
    let region: [Double]
    let width: Int
    let height: Int
}

enum DesktopZoomError: Error, Equatable {
    case invalidRegion(String)
}

/// Maps a zoom region of the frame delivered for a native desktop of
/// nativeWidth x nativeHeight to a native crop and an image size.
func desktopZoomGeometry(
    region: [Double], nativeWidth: Int, nativeHeight: Int
) throws -> DesktopZoomGeometry {
    let frame = desktopDeliveredBounds(
        CGRect(x: 0, y: 0, width: nativeWidth, height: nativeHeight))
    let frameWidth = Double(frame.width)
    let frameHeight = Double(frame.height)
    guard region.count == 4 else {
        throw DesktopZoomError.invalidRegion("zoom region must be [x0, y0, x1, y1]")
    }
    let x0 = region[0], y0 = region[1], x1 = region[2], y1 = region[3]
    guard 0 <= x0, x0 < x1, x1 <= frameWidth, 0 <= y0, y0 < y1, y1 <= frameHeight else {
        throw DesktopZoomError.invalidRegion(
            "zoom region must lie inside the \(Int(frameWidth))x\(Int(frameHeight)) "
                + "frame with x0 < x1 and y0 < y1")
    }
    guard x1 - x0 >= guiZoomMinSide, y1 - y0 >= guiZoomMinSide else {
        throw DesktopZoomError.invalidRegion(
            "zoom region sides must be at least \(Int(guiZoomMinSide)) frame pixels")
    }
    let perX = Double(nativeWidth) / frameWidth
    let perY = Double(nativeHeight) / frameHeight
    let left = max(0, Int((x0 * perX + 1e-9).rounded(.down)))
    let top = max(0, Int((y0 * perY + 1e-9).rounded(.down)))
    let right = min(nativeWidth, Int((x1 * perX - 1e-9).rounded(.up)))
    let bottom = min(nativeHeight, Int((y1 * perY - 1e-9).rounded(.up)))
    let cropWidth = right - left
    let cropHeight = bottom - top
    let enlarge = min(
        guiZoomMaxScale,
        Double(guiScreenshotResultMaxWidth) / Double(cropWidth),
        Double(guiScreenshotResultMaxHeight) / Double(cropHeight))
    return DesktopZoomGeometry(
        nativeX: left, nativeY: top, nativeWidth: cropWidth, nativeHeight: cropHeight,
        region: [
            Double(left) / perX, Double(top) / perY,
            Double(right) / perX, Double(bottom) / perY,
        ],
        width: max(1, Int((Double(cropWidth) * enlarge).rounded(.toNearestOrAwayFromZero))),
        height: max(1, Int((Double(cropHeight) * enlarge).rounded(.toNearestOrAwayFromZero))))
}

func desktopZoomNumber(_ value: Any?) -> Double {
    switch value {
    case let number as Double: return number
    case let number as CGFloat: return Double(number)
    case let number as Int: return Double(number)
    case let number as NSNumber: return number.doubleValue
    default: return 0
    }
}

/// The windows over a desktop-relative native rectangle, bottom first, as
/// the coordinate-relevant facts the frame id hashes.
func desktopZoomWindowsOver(
    _ layout: [String: Any], rect: CGRect
) -> [[String: Double]] {
    let desktop = layout["desktop"] as? [String: Any] ?? [:]
    let originX = desktopZoomNumber(desktop["x"])
    let originY = desktopZoomNumber(desktop["y"])
    let windows = layout["windows"] as? [[String: Any]] ?? []
    // CGWindowList lists front to back; the comparison only needs a stable
    // order, so keep it.
    return windows.compactMap { window in
        let x = desktopZoomNumber(window["x"]) - originX
        let y = desktopZoomNumber(window["y"]) - originY
        let width = desktopZoomNumber(window["width"])
        let height = desktopZoomNumber(window["height"])
        guard CGRect(x: x, y: y, width: width, height: height).intersects(rect) else {
            return nil
        }
        return [
            "id": Double(string(window["id"])) ?? -1,
            "pid": desktopZoomNumber(window["pid"]),
            "x": x, "y": y, "width": width, "height": height,
        ]
    }
}

/// Published layouts by frame id, so a zoom can tell whether the windows
/// over its region still match the frame its coordinates came from.
final class DesktopPublishedLayouts: @unchecked Sendable {
    static let shared = DesktopPublishedLayouts()
    private let lock = NSLock()
    private var layouts: [String: [String: Any]] = [:]
    private var order: [String] = []
    private let limit = 32

    func remember(_ snapshot: [String: Any]?) {
        guard let snapshot,
              let frameID = snapshot["frameId"] as? String, !frameID.isEmpty,
              let layout = snapshot["desktopLayout"] as? [String: Any]
        else {
            return
        }
        lock.lock()
        defer { lock.unlock() }
        if layouts[frameID] == nil {
            order.append(frameID)
        }
        layouts[frameID] = layout
        while order.count > limit {
            layouts.removeValue(forKey: order.removeFirst())
        }
    }

    func layout(_ frameID: String) -> [String: Any]? {
        lock.lock()
        defer { lock.unlock() }
        return layouts[frameID]
    }
}

/// Whether the zoom may read the region now. It may when the current frame
/// is the named frame, or when every window over the region (and its
/// stacking) matches the published layout of the named frame.
func desktopZoomRegionStillValid(
    published: [String: Any]?, current: [String: Any], rect: CGRect
) -> Bool {
    guard let published else {
        return false
    }
    let publishedDesktop = published["desktop"] as? [String: Any] ?? [:]
    let currentDesktop = current["desktop"] as? [String: Any] ?? [:]
    for key in ["x", "y", "width", "height"]
    where desktopZoomNumber(publishedDesktop[key]) != desktopZoomNumber(currentDesktop[key]) {
        return false
    }
    return desktopZoomWindowsOver(published, rect: rect)
        == desktopZoomWindowsOver(current, rect: rect)
}

func desktopZoomStale(_ message: String) -> [String: Any] {
    var snapshot: [String: Any]?
    if let fresh = try? desktopSnapshot() {
        snapshot = fresh
        DesktopPublishedLayouts.shared.remember(fresh)
    }
    var failure = guiExecutionFailure(
        error: message + " Nothing was captured for the zoom and no input was emitted.",
        text: "GUI zoom was rejected against a fresh desktop layout sample.",
        code: "stale_gui_frame",
        phase: "precondition",
        failedActionIndex: 0,
        inputActionsDispatched: 0,
        inputEventsEmitted: false,
        partialInputPossible: false,
        mutationUnknown: false,
        snapshot: snapshot
    )
    failure["failureFactsComplete"] = true
    return failure
}

func executeDesktopZoom(
    _ action: [String: Any], frameID: String
) -> [String: Any] {
    let rawRegion: [Any] = action["region"] as? [Any] ?? []
    let region: [Double] = rawRegion.map { desktopZoomNumber($0) }
    let bounds = desktopBounds()
    let geometry: DesktopZoomGeometry
    do {
        geometry = try desktopZoomGeometry(
            region: region,
            nativeWidth: max(1, Int(bounds.width.rounded())),
            nativeHeight: max(1, Int(bounds.height.rounded())))
    } catch DesktopZoomError.invalidRegion(let message) {
        return [
            "ok": false,
            "error": "gui zoom region rejected: \(message); nothing was captured",
            "failureCode": "invalid_gui_action",
            "failurePhase": "preflight",
            "input_events_emitted": false,
            "failureFactsComplete": true,
        ]
    } catch {
        return ["ok": false, "error": String(describing: error), "input_events_emitted": false]
    }
    guard !frameID.isEmpty else {
        return desktopZoomStale("Zoom regions are frame pixels, and no GUI frame was named.")
    }
    let rect = CGRect(
        x: geometry.nativeX, y: geometry.nativeY,
        width: geometry.nativeWidth, height: geometry.nativeHeight)
    let layoutBefore = desktopLayout()
    if desktopLayoutFrameID(layoutBefore) != frameID,
       !desktopZoomRegionStillValid(
           published: DesktopPublishedLayouts.shared.layout(frameID),
           current: layoutBefore, rect: rect)
    {
        return desktopZoomStale(
            "The windows over the zoom region changed since the frame its coordinates came from.")
    }
    let image: CGImage
    do {
        guard let captured = try captureDesktopImage(bounds: bounds) else {
            throw ComputerUseError.message("Desktop capture returned no image.")
        }
        image = captured
    } catch {
        let normalized = normalizedScreenCaptureError(error)
        let message = (normalized as? LocalizedError)?.errorDescription
            ?? String(describing: normalized)
        var failure: [String: Any] = [
            "ok": false,
            "error": "gui zoom capture failed: \(message)",
            "input_events_emitted": false,
        ]
        if let computerError = normalized as? ComputerUseError,
           case .permissionDenied = computerError {
            failure["failureCode"] = "permission_denied"
            failure["failurePhase"] = "precondition"
        }
        return failure
    }
    // The grab must show the same windows over the region as the check.
    if !desktopZoomRegionStillValid(
        published: layoutBefore, current: desktopLayout(), rect: rect)
    {
        return desktopZoomStale("The windows over the zoom region changed during the zoom capture.")
    }
    guard let data = desktopZoomPNG(image, geometry: geometry) else {
        return ["ok": false, "error": "gui zoom encoding failed; no input was sent",
                "input_events_emitted": false]
    }
    return [
        "ok": true,
        "text": "GUI zoom; no input was sent.",
        "input_events_emitted": false,
        "guiResult": [
            "dispatched": 0,
            "input_events_emitted": false,
            "input_actions_dispatched": 0,
            "zoom": [
                "region": geometry.region,
                "width": geometry.width,
                "height": geometry.height,
            ],
        ],
        // Not a frame: no frameId and no layout, so it is never provenance.
        "snapshot": [
            "app": ["name": "", "pid": 0],
            "screenshotPngBase64": data.base64EncodedString(),
            "screenshotMimeType": "image/png",
            "screenshotEncoding": "png",
            "coordinateSpace": "zoom_image_px",
            "width": geometry.width,
            "height": geometry.height,
        ],
    ]
}

func desktopZoomPNG(_ image: CGImage, geometry: DesktopZoomGeometry) -> Data? {
    // CGImage is top-left origin; the image is the desktop at native size.
    guard let crop = image.cropping(to: CGRect(
        x: geometry.nativeX, y: geometry.nativeY,
        width: geometry.nativeWidth, height: geometry.nativeHeight))
    else {
        return nil
    }
    guard let context = CGContext(
        data: nil, width: geometry.width, height: geometry.height,
        bitsPerComponent: 8, bytesPerRow: 0,
        space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
    else {
        return nil
    }
    context.interpolationQuality = .high
    context.draw(crop, in: CGRect(x: 0, y: 0, width: geometry.width, height: geometry.height))
    guard let scaled = context.makeImage() else {
        return nil
    }
    return pngData(scaled)
}
