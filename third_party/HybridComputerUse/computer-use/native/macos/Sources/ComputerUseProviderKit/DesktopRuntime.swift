import AppKit
import ApplicationServices
import CoreGraphics
import CryptoKit
import Foundation
import ImageIO
import ScreenCaptureKit

func normalizedScreenCaptureError(_ error: Error) -> Error {
    let nativeError = error as NSError
    guard nativeError.domain == SCStreamErrorDomain,
          nativeError.code == SCStreamError.Code.userDeclined.rawValue else {
        return error
    }
    return ComputerUseError.permissionDenied(
        "Screen recording permission was denied for \(PermissionSupport.bundleDisplayName). "
            + "Ask the user to enable Screen Recording for the current \(PermissionSupport.bundleDisplayName) app "
            + "in System Settings > Privacy & Security, then restart the provider before taking another screenshot."
    )
}

func desktopBounds() -> CGRect {
    CGGetActiveDisplayList(0, nil, nil)
    var count: UInt32 = 0
    CGGetActiveDisplayList(0, nil, &count)
    var displays = [CGDirectDisplayID](repeating: 0, count: Int(count))
    CGGetActiveDisplayList(count, &displays, &count)
    return displays.prefix(Int(count)).reduce(CGRect.null) {
        $0.union(CGDisplayBounds($1))
    }
}

func desktopDeliveredBounds(_ nativeBounds: CGRect) -> CGRect {
    let nativeWidth = max(1, nativeBounds.width)
    let nativeHeight = max(1, nativeBounds.height)
    let scale = min(
        1,
        min(
            guiScreenshotResultMaxWidth / nativeWidth,
            guiScreenshotResultMaxHeight / nativeHeight
        )
    )
    return CGRect(
        x: 0,
        y: 0,
        width: max(1, (nativeWidth * scale).rounded()),
        height: max(1, (nativeHeight * scale).rounded())
    )
}

func desktopLayout() -> [String: Any] {
    let bounds = desktopBounds()
    let info = CGWindowListCopyWindowInfo(
        [.optionOnScreenOnly, .excludeDesktopElements],
        kCGNullWindowID
    ) as? [[String: Any]] ?? []
    let windows: [[String: Any]] = info.compactMap { item in
        guard let number = item[kCGWindowNumber as String] as? NSNumber,
              let ownerPID = item[kCGWindowOwnerPID as String] as? NSNumber,
              let layer = (item[kCGWindowLayer as String] as? NSNumber)?.intValue,
              layer == 0,
              let rawBounds = item[kCGWindowBounds as String] as? NSDictionary,
              let frame = CGRect(dictionaryRepresentation: rawBounds)
        else {
            return nil
        }
        return [
            "id": String(number.uint32Value),
            "pid": ownerPID.intValue,
            "title": item[kCGWindowName as String] as? String ?? "",
            "x": frame.origin.x,
            "y": frame.origin.y,
            "width": frame.size.width,
            "height": frame.size.height,
        ]
    }
    return [
        "desktop": frameDictionary(bounds),
        "activeWindow": windows.first?["id"] as? String ?? "",
        "windows": windows,
    ]
}

func desktopLayoutCoordinateState(_ layout: [String: Any]) -> [String: Any] {
    let windows = (layout["windows"] as? [[String: Any]] ?? []).map { window in
        [
            "id": window["id"] ?? "",
            "pid": window["pid"] ?? 0,
            "x": window["x"] ?? 0,
            "y": window["y"] ?? 0,
            "width": window["width"] ?? 0,
            "height": window["height"] ?? 0,
        ]
    }
    return [
        "desktop": layout["desktop"] ?? [:],
        "activeWindow": layout["activeWindow"] ?? "",
        "windows": windows,
    ]
}

func desktopLayoutFrameID(_ layout: [String: Any]) -> String {
    let data = try! JSONSerialization.data(
        withJSONObject: desktopLayoutCoordinateState(layout),
        options: [.sortedKeys]
    )
    return "layout-v2-" + SHA256.hash(data: data).map {
        String(format: "%02x", $0)
    }.joined()
}

func desktopSnapshot() throws -> [String: Any] {
    try desktopVisualSample(captureTimeout: screenshotCaptureTimeout).snapshot
}

struct DesktopVisualSample {
    let snapshot: [String: Any]
    let pixels: Data
    let width: Int
    let height: Int
    let frameID: String

    func hasSamePixelsAndLayout(as other: DesktopVisualSample) -> Bool {
        width == other.width && height == other.height && frameID == other.frameID
            && pixels == other.pixels
    }
}

func desktopPixelBytes(_ image: CGImage) throws -> Data {
    guard let context = CGContext(
        data: nil, width: image.width, height: image.height,
        bitsPerComponent: 8, bytesPerRow: image.width * 4,
        space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
            | CGBitmapInfo.byteOrder32Big.rawValue
    ), let bytes = context.data else {
        throw ComputerUseError.message("Failed to allocate desktop pixel sample.")
    }
    context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
    return Data(bytes: bytes, count: context.bytesPerRow * context.height)
}

func desktopVisualSample(captureTimeout: TimeInterval) throws -> DesktopVisualSample {
    try autoreleasepool {
        let bounds = desktopBounds()
        let layout = desktopLayout()
        let frameID = desktopLayoutFrameID(layout)
        guard let image = try captureDesktopImage(bounds: bounds, timeout: captureTimeout) else {
            throw ComputerUseError.message("Desktop capture returned no image.")
        }
        guard desktopBounds() == bounds,
              desktopLayoutFrameID(desktopLayout()) == frameID else {
            throw DesktopCaptureLayoutChangedError()
        }
        var snapshot: [String: Any] = [
            "app": ["name": "", "pid": 0],
            "sampledAt": ISO8601DateFormatter().string(from: Date()),
            "coordinateSpace": "screen_image_px",
            "frameId": frameID,
            "desktopLayout": layout,
        ]
        guard let data = boundedScreenshotPNGData(
               for: image,
               maxBytes: 64 << 20,
               maxWidth: guiScreenshotResultMaxWidth,
               maxHeight: guiScreenshotResultMaxHeight
           ),
           let dimensions = imageDimensions(data)
        else {
            throw ComputerUseError.message("Failed to encode desktop screenshot.")
        }
        snapshot["width"] = dimensions.width
        snapshot["height"] = dimensions.height
        snapshot["screenshotPngBase64"] = data.base64EncodedString()
        snapshot["screenshotMimeType"] = "image/png"
        snapshot["screenshotEncoding"] = "png"
        return DesktopVisualSample(
            snapshot: snapshot, pixels: try desktopPixelBytes(image),
            width: image.width, height: image.height, frameID: frameID
        )
    }
}

func captureDesktopImage(
    bounds: CGRect, timeout: TimeInterval = screenshotCaptureTimeout
) throws -> CGImage? {
    try BlockingAsyncBridge.run(timeout: timeout) {
        let content = try await SCShareableContent.current
        let displays = content.displays.sorted {
            let left = CGDisplayBounds($0.displayID)
            let right = CGDisplayBounds($1.displayID)
            if left.minY != right.minY {
                return left.minY < right.minY
            }
            return left.minX < right.minX
        }
        guard !displays.isEmpty else {
            return nil
        }

        var captures: [(frame: CGRect, image: CGImage)] = []
        captures.reserveCapacity(displays.count)
        for display in displays {
            let frame = CGDisplayBounds(display.displayID)
            let configuration = SCStreamConfiguration()
            configuration.width = max(1, Int(ceil(frame.width)))
            configuration.height = max(1, Int(ceil(frame.height)))
            configuration.showsCursor = true
            // GUI coordinates are expressed in the logical CGDisplayBounds
            // space. Force the captured image into that same size so every
            // model-visible pixel maps directly to one input coordinate.
            configuration.scalesToFit = true

            let filter = SCContentFilter(
                display: display,
                excludingWindows: []
            )
            let image = try await SCScreenshotManager.captureImage(
                contentFilter: filter,
                configuration: configuration
            )
            captures.append((frame: frame, image: image))
        }

        if captures.count == 1,
           captures[0].frame.size == bounds.size
        {
            return captures[0].image
        }
        return compositeDesktopImage(captures: captures, bounds: bounds)
    }
}

func compositeDesktopImage(
    captures: [(frame: CGRect, image: CGImage)],
    bounds: CGRect
) -> CGImage? {
    let width = max(1, Int(ceil(bounds.width)))
    let height = max(1, Int(ceil(bounds.height)))
    let colorSpace = CGColorSpaceCreateDeviceRGB()
    guard let context = CGContext(
        data: nil,
        width: width,
        height: height,
        bitsPerComponent: 8,
        bytesPerRow: 0,
        space: colorSpace,
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
    ) else {
        return nil
    }
    for capture in captures {
        let destination = CGRect(
            x: capture.frame.minX - bounds.minX,
            y: bounds.maxY - capture.frame.maxY,
            width: capture.frame.width,
            height: capture.frame.height
        )
        context.draw(capture.image, in: destination)
    }
    return context.makeImage()
}

func pngData(_ image: CGImage) -> Data? {
    let data = NSMutableData()
    guard let destination = CGImageDestinationCreateWithData(
        data,
        "public.png" as CFString,
        1,
        nil
    ) else {
        return nil
    }
    CGImageDestinationAddImage(destination, image, nil)
    guard CGImageDestinationFinalize(destination) else {
        return nil
    }
    return data as Data
}
