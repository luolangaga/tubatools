import Foundation
import Testing
@testable import ComputerUseProviderKit

@Test
func guiStillnessRequiresFourSamplesAndThreeHundredMilliseconds() throws {
    var now = 0.0
    var captures = 0
    let result = try sampleGUIStillness(settleMS: 800, capture: { _ in
        captures += 1
        return Data([1, 2, 3])
    }, identical: ==, clock: { now }, sleep: { now += $0 })
    #expect(result.quiescent)
    #expect(captures == 4)
    #expect(abs(now - 0.3) < 1e-9)
}

@Test
func guiStillnessResetsOnChangeAndReturnsLatestSample() throws {
    var now = 0.0
    var captures = 0
    let result = try sampleGUIStillness(settleMS: 800, capture: { _ in
        captures += 1
        return (revision: captures, pixels: captures < 3 ? Data([0]) : Data([1]))
    }, identical: { $0.pixels == $1.pixels }, clock: { now }, sleep: { now += $0 })
    #expect(result.quiescent)
    #expect(result.sample.revision == 6)
    #expect(abs(now - 0.5) < 1e-9)
}

@Test
func guiStillnessHonorsZeroAndSmallBudgetsWithoutClaimingStability() throws {
    for budget in [0, 1, 100, 250] {
        var now = 0.0
        var captures = 0
        let result = try sampleGUIStillness(settleMS: budget, capture: { _ in
            captures += 1
            return captures
        }, identical: { _, _ in true }, clock: { now }, sleep: { now += $0 })
        #expect(!result.quiescent)
        #expect(result.sample == captures)
        #expect(abs(now - Double(budget) / 1000) < 1e-9)
        if budget <= 100 { #expect(captures == (budget == 0 ? 1 : 2)) }
    }
}

@Test
func guiStillnessCapsAnimationAndIncludesCaptureTimeInPollBudget() throws {
    var now = 0.0
    var captures = 0
    var deadline = 0.0
    let result = try sampleGUIStillness(settleMS: 3000, capture: { budget in
        captures += 1
        if captures == 1 {
            #expect(budget == screenshotCaptureTimeout)
            now += 0.4
            deadline = now + 3
        } else {
            #expect(budget == screenshotCaptureTimeout)
            now += 0.04
        }
        return captures
    }, identical: ==, clock: { now }, sleep: { now += $0 })
    #expect(!result.quiescent)
    #expect(result.sample == captures)
    #expect(captures < 31)
    #expect(now >= deadline - 1e-9)
    #expect(now <= deadline + 0.04 + 1e-9)
}

@Test
func guiStillnessShortRemainingBudgetDoesNotShortenCaptureTimeout() throws {
    for budget in [101, 250, 350] {
        var now = 0.0
        var captures = 0
        let result = try sampleGUIStillness(settleMS: budget, capture: { timeout in
            captures += 1
            #expect(timeout == screenshotCaptureTimeout)
            if captures > 1 { now += 0.4 }
            return captures
        }, identical: { _, _ in true }, clock: { now }, sleep: { now += $0 })
        #expect(!result.quiescent)
        #expect(result.sample == 2)
        #expect(captures == 2)
        #expect(abs(now - 0.5) < 1e-9)
    }
}

@Test
func guiStillnessCapturesFinalStateWhenSleepReachesDeadline() throws {
    for overshoot in [0.0, 0.001] {
        var now = 0.0
        var captures = 0
        let result = try sampleGUIStillness(settleMS: 100, capture: { timeout in
            captures += 1
            #expect(timeout == screenshotCaptureTimeout)
            return now
        }, identical: { (lhs: TimeInterval, rhs: TimeInterval) in lhs == rhs },
        clock: { now }, sleep: { now += $0 + overshoot })
        #expect(!result.quiescent)
        #expect(captures == 2)
        #expect(abs(result.sample - 0.1 - overshoot) < 1e-9)
    }
}

@Test
func guiStillnessFourthSampleAtDeadlineDoesNotClaimQuiescence() throws {
    var now = 0.0
    var captures = 0
    let result = try sampleGUIStillness(settleMS: 350, capture: { _ in
        captures += 1
        if captures == 4 { now = 0.35 }
        return captures
    }, identical: { _, _ in true }, clock: { now }, sleep: { now += $0 })
    #expect(!result.quiescent)
    #expect(captures == 4)
    #expect(result.sample == 4)
}

@Test
func guiStillnessRejectsInvalidBudgetsBeforeCapture() {
    for budget in [-1, 3001, Int.max] {
        #expect(throws: ComputerUseError.self) {
            _ = try sampleGUIStillness(settleMS: budget, capture: { _ in
                Issue.record("Invalid budget captured desktop")
                return 0
            }, identical: { (lhs: Int, rhs: Int) in lhs == rhs })
        }
    }
}

@Test
func guiStillnessPropagatesInitialAndLaterCaptureFailures() {
    for failAt in [1, 3] {
        var now = 0.0
        var captures = 0
        #expect(throws: ComputerUseError.self) {
            _ = try sampleGUIStillness(settleMS: 800, capture: { _ in
                captures += 1
                if captures == failAt {
                    throw ComputerUseError.providerResetRequired("capture stalled")
                }
                return 0
            }, identical: { (lhs: Int, rhs: Int) in lhs == rhs },
            clock: { now }, sleep: { now += $0 })
        }
        #expect(captures == failAt)
        #expect(now <= 0.2 + 1e-9)
    }
}

@Test
func guiStillnessRetriesInitialLayoutChangesWithinOneBudget() throws {
    var now = 0.0
    var captures = 0
    let result = try sampleGUIStillness(settleMS: 800, capture: { timeout -> Int in
        #expect(timeout == screenshotCaptureTimeout)
        captures += 1
        if captures <= 2 { throw DesktopCaptureLayoutChangedError() }
        return captures
    }, identical: { _, _ in true }, clock: { now }, sleep: { now += $0 })
    #expect(result.quiescent)
    #expect(result.sample == 6)
    #expect(captures == 6)
    #expect(abs(now - 0.5) < 1e-9)
}

@Test
func guiStillnessLayoutChangeBreaksConsecutiveStabilityEvidence() throws {
    var now = 0.0
    var captures = 0
    let result = try sampleGUIStillness(settleMS: 800, capture: { _ -> Int in
        captures += 1
        if captures == 3 { throw DesktopCaptureLayoutChangedError() }
        return captures
    }, identical: { _, _ in true }, clock: { now }, sleep: { now += $0 })
    #expect(result.quiescent)
    #expect(result.sample == 7)
    #expect(abs(now - 0.6) < 1e-9)
}

@Test
func guiStillnessRepeatedLayoutChangesDoNotExtendDeadline() {
    var now = 0.0
    var captures = 0
    #expect(throws: DesktopCaptureLayoutChangedError.self) {
        _ = try sampleGUIStillness(settleMS: 250, capture: { _ -> Int in
            captures += 1
            throw DesktopCaptureLayoutChangedError()
        }, identical: ==, clock: { now }, sleep: { now += $0 })
    }
    #expect(captures == 4)
    #expect(abs(now - 0.25) < 1e-9)
}

@Test
func guiStillnessDoesNotRetryZeroBudgetOrExpiredCapture() {
    for budget in [0, 200] {
        var now = 0.0
        var captures = 0
        #expect(throws: DesktopCaptureLayoutChangedError.self) {
            _ = try sampleGUIStillness(settleMS: budget, capture: { _ -> Int in
                captures += 1
                if captures > 1 { now += 0.2 }
                if budget == 0 || captures > 1 { throw DesktopCaptureLayoutChangedError() }
                return 0
            }, identical: ==, clock: { now }, sleep: { now += $0 })
        }
        #expect(captures == (budget == 0 ? 1 : 2))
    }
}

@Test
func guiStillnessReturnsValidFinalSampleAfterEarlierLayoutChange() throws {
    var now = 0.0
    var captures = 0
    let result = try sampleGUIStillness(settleMS: 100, capture: { _ -> TimeInterval in
        captures += 1
        if captures == 1 { throw DesktopCaptureLayoutChangedError() }
        return now
    }, identical: ==, clock: { now }, sleep: { now += $0 })
    #expect(!result.quiescent)
    #expect(captures == 2)
    #expect(abs(result.sample - 0.1) < 1e-9)
}

@Test
func guiStillnessDoesNotRetryMatchingErrorTextOrPermissionFailures() {
    for failure in [
        ComputerUseError.message("Desktop layout changed during screenshot capture."),
        ComputerUseError.permissionDenied("permission denied"),
        ComputerUseError.providerResetRequired("capture stalled"),
    ] {
        var now = 0.0
        var captures = 0
        #expect(throws: ComputerUseError.self) {
            _ = try sampleGUIStillness(settleMS: 800, capture: { _ -> Int in
                captures += 1
                if captures == 1 { throw DesktopCaptureLayoutChangedError() }
                throw failure
            }, identical: ==, clock: { now }, sleep: { now += $0 })
        }
        #expect(captures == 2)
        #expect(abs(now - 0.1) < 1e-9)
    }
}
