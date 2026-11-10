import CoreGraphics
import Foundation
import Testing
@testable import ComputerUseProviderKit

private func visualSample(
    pixels: Data = Data([1, 2, 3, 255]), width: Int = 1, height: Int = 1,
    frameID: String = "layout-a", revision: Int = 1
) -> DesktopVisualSample {
    DesktopVisualSample(
        snapshot: ["revision": revision, "frameId": frameID, "width": width, "height": height],
        pixels: pixels, width: width, height: height, frameID: frameID
    )
}

@Test
func guiStillnessComparesPixelsDimensionsAndLayoutNotSnapshotMetadata() {
    let sample = visualSample()
    #expect(sample.hasSamePixelsAndLayout(as: visualSample(revision: 2)))
    #expect(!sample.hasSamePixelsAndLayout(as: visualSample(pixels: Data([1, 2, 4, 255]))))
    #expect(!sample.hasSamePixelsAndLayout(as: visualSample(width: 2)))
    #expect(!sample.hasSamePixelsAndLayout(as: visualSample(height: 2)))
    #expect(!sample.hasSamePixelsAndLayout(as: visualSample(frameID: "layout-b")))
}

@Test
func guiFinalObservationKeepsLatestSampleAndMatchingLayout() throws {
    var now = 0.0
    var captures = 0
    let result = guiFinalObservationResult(
        actionCount: 2, inputActionsDispatched: 2, emitted: true
    ) {
        try sampleGUIStillness(settleMS: 800, capture: { _ in
            captures += 1
            return visualSample(frameID: captures < 3 ? "layout-a" : "layout-b", revision: captures)
        }, identical: { $0.hasSamePixelsAndLayout(as: $1) }, clock: { now }, sleep: { now += $0 })
    }
    let snapshot = try #require(result["snapshot"] as? [String: Any])
    let gui = try #require(result["guiResult"] as? [String: Any])
    #expect(captures == 6)
    #expect(snapshot["revision"] as? Int == captures)
    #expect(snapshot["frameId"] as? String == "layout-b")
    #expect(gui["quiescent"] as? Bool == true)
}

@Test
func guiSamplingFailurePreservesInputAndResetEvidence() throws {
    for emitted in [false, true] {
        for failAt in [1, 3] {
            var now = 0.0
            var captures = 0
            let result = guiFinalObservationResult(
                actionCount: 3, inputActionsDispatched: emitted ? 2 : 0,
                emitted: emitted
            ) {
                try sampleGUIStillness(settleMS: 800, capture: { _ in
                    captures += 1
                    if captures == failAt {
                        throw ComputerUseError.providerResetRequired("capture timed out")
                    }
                    return visualSample()
                }, identical: { $0.hasSamePixelsAndLayout(as: $1) }, clock: { now }, sleep: { now += $0 })
            }
            #expect(result["ok"] as? Bool == false)
            #expect(result["snapshot"] == nil)
            #expect(result["providerResetRequired"] as? Bool == true)
            #expect(result["input_events_emitted"] as? Bool == emitted)
            #expect(result["partial_input_possible"] as? Bool == emitted)
            #expect(result["mutation_unknown"] as? Bool == emitted)
            #expect(result["failureCode"] as? String == "gui_snapshot_failed")
            let gui = try #require(result["guiResult"] as? [String: Any])
            #expect(gui["input_actions_dispatched"] as? Int == (emitted ? 2 : 0))
            #expect(captures == failAt)
        }
    }
}

@Test
func guiLayoutCaptureRetryKeepsEmissionCountsAndMatchingSnapshot() throws {
    var now = 0.0
    var captures = 0
    let result = guiFinalObservationResult(
        actionCount: 2, inputActionsDispatched: 2, emitted: true
    ) {
        try sampleGUIStillness(settleMS: 100, capture: { _ -> DesktopVisualSample in
            captures += 1
            if captures == 1 { throw DesktopCaptureLayoutChangedError() }
            return visualSample(frameID: "layout-b", revision: captures)
        }, identical: { $0.hasSamePixelsAndLayout(as: $1) }, clock: { now }, sleep: { now += $0 })
    }
    let gui = try #require(result["guiResult"] as? [String: Any])
    let snapshot = try #require(result["snapshot"] as? [String: Any])
    #expect(result["ok"] as? Bool == true)
    #expect(gui["input_actions_dispatched"] as? Int == 2)
    #expect(gui["input_events_emitted"] as? Bool == true)
    #expect(gui["quiescent"] as? Bool == false)
    #expect(snapshot["revision"] as? Int == 2)
    #expect(snapshot["frameId"] as? String == "layout-b")
}

@Test
func guiExhaustedLayoutCaptureDoesNotReturnOlderImage() throws {
    var now = 0.0
    var captures = 0
    let result = guiFinalObservationResult(
        actionCount: 2, inputActionsDispatched: 2, emitted: true
    ) {
        try sampleGUIStillness(settleMS: 250, capture: { _ -> DesktopVisualSample in
            captures += 1
            if captures > 1 { throw DesktopCaptureLayoutChangedError() }
            return visualSample()
        }, identical: { $0.hasSamePixelsAndLayout(as: $1) }, clock: { now }, sleep: { now += $0 })
    }
    let gui = try #require(result["guiResult"] as? [String: Any])
    #expect(result["ok"] as? Bool == false)
    #expect(result["snapshot"] == nil)
    #expect(result["failureCode"] as? String == "gui_snapshot_failed")
    #expect(result["input_events_emitted"] as? Bool == true)
    #expect(result["partial_input_possible"] as? Bool == true)
    #expect(result["mutation_unknown"] as? Bool == true)
    #expect(gui["input_actions_dispatched"] as? Int == 2)
    #expect(captures == 4)
}

@Test
func guiPixelSamplingDetectsSinglePixelChanges() throws {
    let context = try #require(CGContext(
        data: nil, width: 2, height: 2, bitsPerComponent: 8, bytesPerRow: 8,
        space: CGColorSpaceCreateDeviceRGB(),
        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
    ))
    context.setFillColor(CGColor(red: 1, green: 0, blue: 0, alpha: 1))
    context.fill(CGRect(x: 0, y: 0, width: 2, height: 2))
    let first = try desktopPixelBytes(#require(context.makeImage()))
    #expect(first == (try desktopPixelBytes(#require(context.makeImage()))))
    context.setFillColor(CGColor(red: 0, green: 1, blue: 0, alpha: 1))
    context.fill(CGRect(x: 0, y: 0, width: 1, height: 1))
    #expect(first != (try desktopPixelBytes(#require(context.makeImage()))))
    #expect(first.count == 16)
}
