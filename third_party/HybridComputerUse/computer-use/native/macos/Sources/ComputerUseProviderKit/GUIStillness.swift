import Foundation

struct DesktopCaptureLayoutChangedError: Error, LocalizedError {
    var errorDescription: String? {
        "Desktop layout changed during screenshot capture."
    }
}

struct GUIStillnessResult<Sample> {
    let sample: Sample
    let quiescent: Bool
}

func sampleGUIStillness<Sample>(
    settleMS: Int,
    capture: (TimeInterval) throws -> Sample,
    identical: (Sample, Sample) -> Bool,
    clock: () -> TimeInterval = { ProcessInfo.processInfo.systemUptime },
    sleep: (TimeInterval) -> Void = { Thread.sleep(forTimeInterval: $0) }
) throws -> GUIStillnessResult<Sample> {
    guard (0...3000).contains(settleMS) else {
        throw ComputerUseError.invalidArguments("gui settle_ms must be between 0 and 3000")
    }
    var previous: Sample?
    do {
        let first = try capture(screenshotCaptureTimeout)
        guard settleMS > 0 else {
            return GUIStillnessResult(sample: first, quiescent: false)
        }
        previous = first
    } catch is DesktopCaptureLayoutChangedError where settleMS > 0 {
        // A rejected image supplies neither coordinates nor stability evidence.
        previous = nil
    }
    // The first capture attempt precedes the settle budget, including when its
    // layout changed. Retries share this deadline; they never replay input.
    // Require four identical samples spanning 300ms, 100ms apart.
    let start = clock()
    let deadline = start + Double(settleMS) / 1000
    var stableStartedAt = start
    var stableSamples = previous == nil ? 0 : 1
    while true {
        let remaining = deadline - clock()
        guard remaining > 1e-9 else { break }
        sleep(min(0.1, remaining))
        guard deadline - clock() > 1e-9 else { break }
        // As on Linux, an in-flight capture may finish beyond the settle cap.
        // Keep its normal bounded timeout: a short settle budget must not turn
        // a healthy ScreenCaptureKit request into a provider reset.
        let current: Sample
        do {
            current = try capture(screenshotCaptureTimeout)
        } catch let error as DesktopCaptureLayoutChangedError {
            guard deadline - clock() > 1e-9 else { throw error }
            previous = nil
            stableSamples = 0
            continue
        }
        let sampledAt = clock()
        if let previous, identical(previous, current) {
            stableSamples += 1
        } else {
            stableStartedAt = sampledAt
            stableSamples = 1
        }
        previous = current
        if sampledAt >= deadline {
            return GUIStillnessResult(sample: current, quiescent: false)
        }
        if stableSamples >= 4, sampledAt - stableStartedAt + 1e-9 >= 0.3 {
            return GUIStillnessResult(sample: current, quiescent: true)
        }
    }
    // Even a budget too short for a comparison must return a post-wait image.
    return GUIStillnessResult(
        sample: try capture(screenshotCaptureTimeout), quiescent: false
    )
}
