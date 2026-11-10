import AppKit
import ApplicationServices
import CoreGraphics
import Foundation
import ScreenCaptureKit

func enableBestEffortAccessibilityModes(_ appElement: AXUIElement) {
    // Chromium/Electron apps may withhold parts of their AX tree until manual
    // accessibility is enabled. These private attributes are best-effort and
    // harmlessly fail on apps that do not support them.
    _ = AXUIElementSetAttributeValue(appElement, "AXManualAccessibility" as CFString, kCFBooleanTrue)
    _ = AXUIElementSetAttributeValue(appElement, "AXEnhancedUserInterface" as CFString, kCFBooleanTrue)
}

struct WindowCapture {
    let windowID: CGWindowID?
    let windowHandle: String
    let layer: Int
    let bounds: CGRect

    static func resolve(
        for pid: pid_t,
        windowIDHint: CGWindowID?,
        titleHint: String?
    ) -> WindowCapture? {
        guard let infoList = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] else {
            return nil
        }

        let candidates = infoList.enumerated().compactMap { offset, info -> WindowCaptureCandidate? in
            guard
                let ownerPID = info[kCGWindowOwnerPID as String] as? pid_t,
                ownerPID == pid,
                let number = info[kCGWindowNumber as String] as? NSNumber,
                let layer = info[kCGWindowLayer as String] as? Int,
                let boundsDictionary = info[kCGWindowBounds as String] as? NSDictionary,
                let bounds = CGRect(dictionaryRepresentation: boundsDictionary)
            else {
                return nil
            }

            let title = info[kCGWindowName as String] as? String
            let area = Int(bounds.width * bounds.height)
            return WindowCaptureCandidate(
                windowID: CGWindowID(number.uint32Value),
                layer: layer,
                bounds: bounds,
                title: title,
                area: area,
                frontToBackIndex: offset
            )
        }

        guard let best = preferredWindowCaptureCandidate(
            candidates,
            windowIDHint: windowIDHint,
            titleHint: titleHint
        ) else {
            return nil
        }

        return WindowCapture(
            windowID: best.windowID,
            windowHandle: macWindowHandle(pid: pid, windowID: best.windowID),
            layer: best.layer,
            bounds: best.bounds
        )
    }
}

struct WindowCaptureCandidate {
    let windowID: CGWindowID
    let layer: Int
    let bounds: CGRect
    let title: String?
    let area: Int
    let frontToBackIndex: Int
}

func preferredWindowCaptureCandidate(
    _ candidates: [WindowCaptureCandidate],
    windowIDHint: CGWindowID?,
    titleHint: String?
) -> WindowCaptureCandidate? {
    if let windowIDHint {
        // A known AX window must never borrow a sibling's capture or geometry.
        let matches = candidates.filter { $0.windowID == windowIDHint }
        return matches.count == 1 ? matches[0] : nil
    }
    let usable = candidates
        .filter { $0.layer == 0 && $0.area >= 20_000 }
        .sorted { lhs, rhs in
            lhs.frontToBackIndex < rhs.frontToBackIndex
        }

    guard !usable.isEmpty else {
        return candidates.sorted { lhs, rhs in
            lhs.area > rhs.area
        }.first
    }

    guard let titleHint, !titleHint.isEmpty,
          let hinted = usable.first(where: { $0.title == titleHint })
    else {
        return usable.first
    }

    guard let frontmost = usable.first else {
        return hinted
    }

    if frontmost.windowID != hinted.windowID,
       frontmost.bounds.intersects(hinted.bounds)
    {
        return frontmost
    }

    return hinted
}

func boundedScreenshotPNGData(
    for image: CGImage,
    maxBytes: Int = screenshotResultMaxPNGBytes,
    maxWidth: CGFloat = screenshotResultMaxDimension,
    maxHeight: CGFloat = screenshotResultMaxDimension,
    minScale: CGFloat = screenshotResultMinScale
) -> Data? {
    guard image.width > 0, image.height > 0, maxBytes > 0,
          maxWidth > 0, maxHeight > 0
    else {
        return nil
    }

    let original = pngData(for: image)
    var scale = min(
        1,
        min(
            maxWidth / CGFloat(image.width),
            maxHeight / CGFloat(image.height)
        )
    )

    if scale >= 1, let original, original.count <= maxBytes {
        return original
    }

    var best = original
    while scale >= minScale {
        guard let resized = resizedCGImage(image, scale: scale),
              let data = pngData(for: resized)
        else {
            break
        }

        best = data
        if data.count <= maxBytes {
            return data
        }

        scale *= 0.85
    }

    return best
}

private func pngData(for image: CGImage) -> Data? {
    let bitmap = NSBitmapImageRep(cgImage: image)
    return bitmap.representation(using: .png, properties: [:])
}

private func resizedCGImage(_ image: CGImage, scale: CGFloat) -> CGImage? {
    let width = max(1, Int((CGFloat(image.width) * scale).rounded()))
    let height = max(1, Int((CGFloat(image.height) * scale).rounded()))
    let colorSpace = CGColorSpaceCreateDeviceRGB()
    let bitmapInfo = CGImageAlphaInfo.premultipliedLast.rawValue

    guard let context = CGContext(
        data: nil,
        width: width,
        height: height,
        bitsPerComponent: 8,
        bytesPerRow: 0,
        space: colorSpace,
        bitmapInfo: bitmapInfo
    ) else {
        return nil
    }

    context.interpolationQuality = .medium
    context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
    return context.makeImage()
}

private final class AsyncResultBox<T>: @unchecked Sendable {
    var result: Result<T, Error>?
}

enum BlockingAsyncBridge {
    static func run<T>(timeout: TimeInterval? = nil, _ operation: @escaping @Sendable () async throws -> T) throws -> T {
        let semaphore = DispatchSemaphore(value: 0)
        let resultBox = AsyncResultBox<T>()

        let task = Task.detached {
            do {
                resultBox.result = .success(try await operation())
            } catch {
                resultBox.result = .failure(error)
            }

            semaphore.signal()
        }

        guard waitForSignal(semaphore, timeout: timeout) else {
            task.cancel()
            throw ComputerUseError.providerResetRequired(
                "ScreenCaptureKit screenshot task timed out after \(timeout ?? 0) seconds. The native provider must restart before another request."
            )
        }

        return try resultBox.result?.get() ?? {
            throw ComputerUseError.message("ScreenCaptureKit screenshot task finished without producing a result.")
        }()
    }

    private static func waitForSignal(_ semaphore: DispatchSemaphore, timeout: TimeInterval?) -> Bool {
        // Callers run on the accessibility thread, never on the main thread
        // or the Swift concurrency pool that runs `operation`.
        guard let timeout else {
            semaphore.wait()
            return true
        }
        return semaphore.wait(timeout: .now() + timeout) == .success
    }
}
