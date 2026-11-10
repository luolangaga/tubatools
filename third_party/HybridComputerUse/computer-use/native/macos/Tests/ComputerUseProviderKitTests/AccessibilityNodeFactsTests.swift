@testable import ComputerUseProviderKit
import ApplicationServices
import Foundation
import Testing

@Test
func snapshotNodeReadsShareOneBatchedRequestPerElement() {
    var batches = 0
    var singles = 0
    var settableReads = 0
    let cache = AXNodeFactsCache(
        attributes: ["AXRole", "AXValue"],
        multiRead: { _, _ in
            batches += 1
            return .values([
                AXRead(status: .success, value: "AXButton" as CFString),
                AXRead(status: .attributeUnsupported, value: nil),
            ])
        },
        singleRead: { _, _ in
            singles += 1
            return AXRead(status: .noValue, value: nil)
        },
        settableRead: { _, _ in
            settableReads += 1
            return true
        }
    )
    // Two references to one element share the cached sample.
    let element = AXUIElementCreateApplication(4242)
    let sameElement = AXUIElementCreateApplication(4242)
    #expect(cache.attribute(element, "AXRole").value as? String == "AXButton")
    #expect(cache.attribute(sameElement, "AXValue").status == .attributeUnsupported)
    #expect(cache.isSettable(element, "AXValue") == false)
    #expect(cache.attribute(element, "AXTitle").status == .noValue)
    #expect(cache.attribute(element, "AXTitle").status == .noValue)
    #expect(cache.isSettable(element, "AXTitle"))
    #expect(cache.isSettable(element, "AXTitle"))
    #expect(batches == 1)
    #expect(singles == 1)
    #expect(settableReads == 1)
}

@Test
func failedBatchFallsBackToSingleReads() {
    var singles = 0
    let cache = AXNodeFactsCache(
        attributes: ["AXRole"],
        multiRead: { _, _ in .failed(.failure) },
        singleRead: { _, _ in
            singles += 1
            return AXRead(status: .success, value: "AXGroup" as CFString)
        }
    )
    let element = AXUIElementCreateApplication(4243)
    #expect(cache.attribute(element, "AXRole").value as? String == "AXGroup")
    #expect(cache.attribute(element, "AXRole").value as? String == "AXGroup")
    #expect(singles == 1)
}

@Test
func snapshotScopeInstallsOneCacheAndRemovesIt() {
    #expect(AXNodeFactsCache.current == nil)
    AXNodeFactsCache.withSnapshotScope {
        let outer = AXNodeFactsCache.current
        #expect(outer != nil)
        AXNodeFactsCache.withSnapshotScope {
            #expect(AXNodeFactsCache.current === outer)
        }
    }
    #expect(AXNodeFactsCache.current == nil)
}

@Test
func unresponsiveApplicationStopsReadingTheNodeAfterOneProbe() {
    var singles = 0
    var settableReads = 0
    let cache = AXNodeFactsCache(
        attributes: ["AXRole", "AXValue"],
        multiRead: { _, _ in .failed(.cannotComplete) },
        singleRead: { _, _ in
            singles += 1
            return AXRead(status: .cannotComplete, value: nil)
        },
        settableRead: { _, _ in
            settableReads += 1
            return true
        }
    )
    let element = AXUIElementCreateApplication(4244)
    #expect(cache.attribute(element, "AXValue").status == .cannotComplete)
    #expect(cache.attribute(element, "AXTitle").status == .cannotComplete)
    #expect(cache.attributeNames(element).status == .cannotComplete)
    #expect(cache.actionNames(element).status == .cannotComplete)
    #expect(!cache.isSettable(element, "AXValue"))
    #expect(singles == 1)
    #expect(settableReads == 0)
    #expect(cache.unresponsiveNodeCount == 1)
}

@Test
func slowBatchFallsBackToSingleReadsWhenTheApplicationAnswers() {
    var singles = 0
    let cache = AXNodeFactsCache(
        attributes: ["AXRole", "AXRows"],
        multiRead: { _, _ in .failed(.cannotComplete) },
        singleRead: { _, attribute in
            singles += 1
            return AXRead(status: .success, value: attribute as CFString)
        }
    )
    let element = AXUIElementCreateApplication(4245)
    #expect(cache.attribute(element, "AXRole").value as? String == "AXRole")
    #expect(cache.attribute(element, "AXTitle").value as? String == "AXTitle")
    #expect(singles == 2)
    #expect(cache.unresponsiveNodeCount == 0)
}

@Test
func traversalDeadlineSkipsLaterNodesAndCountsThem() throws {
    var renderer = TreeRenderer(
        context: RenderContext(
            windowBounds: nil,
            focusedElement: nil,
            focusedAncestry: [],
            textLimit: .defaults,
            treeLimits: .defaults,
            traversalDeadline: 0
        ),
        readTitle: { _ in "Window" },
        readExpansion: { _ in nil }
    )
    try renderer.render(AXUIElementCreateApplication(4246))
    try renderer.render(AXUIElementCreateApplication(4247), relativeRuntimeID: [1])
    #expect(renderer.records.count == 1)
    #expect(renderer.deadlineOmittedCount == 1)
}

@Test
func traversalLimitsReportUnreadBranchesWithoutReadingThem() throws {
    for (limits, depth, reason) in [
        (AccessibilityTreeLimits(maxNodeCount: 1), 0, "max_tree_nodes"),
        (AccessibilityTreeLimits(maxDepth: 1), 1, "max_tree_depth")
    ] {
        var reads = 0
        var renderer = TreeRenderer(
            context: RenderContext(windowBounds: nil, focusedElement: nil,
                                   focusedAncestry: [], textLimit: .defaults, treeLimits: limits),
            readTitle: { _ in reads += 1; return "Window" },
            readExpansion: { _ in nil }
        )
        try renderer.render(AXUIElementCreateApplication(4250))
        #expect(renderer.omissions(unresponsiveNodes: 0).isEmpty)
        let before = reads
        try renderer.render(AXUIElementCreateApplication(4251), depth: depth)
        #expect(reads == before)
        #expect(renderer.records.count == 1)
        let omissions = renderer.omissions(unresponsiveNodes: 0)
        #expect(omissions.count == 1)
        #expect(omissions.first?.reason == reason)
        #expect(omissions.first?.count == 1)
        #expect(omissions.first?.detail.contains("descendants are unknown") == true)
    }
}

@Test
func snapshotOmissionsReportDeadlineAndUnresponsiveReads() {
    #expect(snapshotOmissions(deadlineOmitted: 0, unresponsiveNodes: 0).isEmpty)
    let omissions = snapshotOmissions(deadlineOmitted: 3, unresponsiveNodes: 2, deadlineSeconds: 20)
    #expect(omissions.map(\.reason) == ["time", "provider_probe_error"])
    #expect(omissions.map(\.count) == [3, 2])
    #expect(omissions[0].detail.hasPrefix("3 not visited: time budget exhausted (20s); tree is partial"))
}
