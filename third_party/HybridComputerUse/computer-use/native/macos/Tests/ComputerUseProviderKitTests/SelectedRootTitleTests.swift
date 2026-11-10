import ApplicationServices
@testable import ComputerUseProviderKit
import Testing

@Test
func runtimeIdentityDoesNotUseDisplayIndex() throws {
    let root = AXUIElementCreateApplication(-1)
    var before = TreeRenderer(context: titleRenderContext(), readTitle: { _ in "Target" }, readExpansion: { _ in nil })
    var after = TreeRenderer(context: titleRenderContext(), readTitle: { _ in "Target" }, readExpansion: { _ in nil })
    before.nextIndex = 29
    after.nextIndex = 11
    try before.render(root, parentRuntimeID: [0, 4, 8])
    try after.render(root, parentRuntimeID: [0, 4, 8])
    #expect(before.records[29] != nil)
    #expect(after.records[11] != nil)
    #expect(before.records[29]?.runtimeID == after.records[11]?.runtimeID)
}

private func titleRenderContext(
    textLimit: SnapshotTextLimit = .defaults
) -> RenderContext {
    RenderContext(
        windowBounds: nil,
        focusedElement: nil,
        focusedAncestry: [],
        textLimit: textLimit,
        treeLimits: .defaults
    )
}

@Test
func selectedRootTitleUsesRenderSampleInsteadOfCaptureHint() throws {
    // Invalid PIDs provide distinct opaque elements without reading a live app.
    let root = AXUIElementCreateApplication(-1)
    let samples = ["Before capture", "Rendered title", "Later title"]
    var reads = 0
    let readTitle: (AXUIElement) -> String? = { element in
        #expect(CFEqual(element, root))
        let value = samples[min(reads, samples.count - 1)]
        reads += 1
        return value
    }
    let captureTitleHint = readTitle(root)
    var renderer = TreeRenderer(
        context: titleRenderContext(),
        selectedRoot: root,
        readTitle: readTitle,
        readExpansion: { _ in nil }
    )

    try renderer.render(root)

    #expect(captureTitleHint == "Before capture")
    #expect(reads == 2)
    #expect(renderer.selectedRootTitle == "Rendered title")
    #expect(renderer.records[0]?.name == "Rendered title")
    #expect(renderer.lines.contains { $0.contains("Rendered title") })
    #expect(!renderer.lines.contains { $0.contains("Before capture") })

    #expect(readTitle(root) == "Later title")
    #expect(reads == 3)
    #expect(renderer.selectedRootTitle == "Rendered title")
    #expect(renderer.records[0]?.name == "Rendered title")

    var nextRenderer = TreeRenderer(
        context: titleRenderContext(),
        selectedRoot: root,
        readTitle: readTitle,
        readExpansion: { _ in nil }
    )
    try nextRenderer.render(root)
    #expect(reads == 4)
    #expect(nextRenderer.selectedRootTitle == "Later title")
    #expect(nextRenderer.records[0]?.name == "Later title")
}

@Test
func selectedRootTitlePreservesEmptyAndMissingSamples() {
    let root = AXUIElementCreateApplication(-1)
    for rawTitle in [nil, ""] as [String?] {
        var reads = 0
        var renderer = TreeRenderer(
            context: titleRenderContext(),
            selectedRoot: root,
            readTitle: { _ in
                reads += 1
                return rawTitle
            }
        )
        let display = renderer.sampledDisplayTitle(
            for: root,
            role: kAXWindowRole as String,
            label: "Not a window title",
            identifier: "Not a window title either",
            explicitValue: "Not a title value",
            rowTexts: ["Not a title row"]
        )

        #expect(reads == 1)
        #expect(display == nil)
        #expect(renderer.selectedRootTitle == rawTitle)
    }
}

@Test
func selectedRootTitleKeepsRawLongValueWhileRenderingBoundedText() throws {
    let root = AXUIElementCreateApplication(-1)
    let rawTitle = "  Report\n" + String(repeating: "x", count: 80) + "  "
    var reads = 0
    var renderer = TreeRenderer(
        context: titleRenderContext(textLimit: SnapshotTextLimit(maxCount: 12)),
        selectedRoot: root,
        readTitle: { _ in
            reads += 1
            return rawTitle
        },
        readExpansion: { _ in nil }
    )

    try renderer.render(root)

    #expect(reads == 1)
    #expect(renderer.selectedRootTitle == rawTitle)
    #expect(renderer.records[0]?.name == "Report\\nxxxx...")
    #expect(renderer.lines.contains { $0.contains("Report\\nxxxx...") })
}

@Test
func selectedRootTitleIgnoresMenubarAndOtherWindowSamples() {
    let root = AXUIElementCreateApplication(-1)
    let menu = AXUIElementCreateApplication(-2)
    let otherWindow = AXUIElementCreateApplication(-3)
    var reads = 0
    var renderer = TreeRenderer(
        context: titleRenderContext(),
        selectedRoot: root,
        readTitle: { element in
            reads += 1
            if CFEqual(element, root) {
                return "Selected"
            }
            return CFEqual(element, menu) ? "Menu" : "Other window"
        }
    )
    let rootDisplay = renderer.sampledDisplayTitle(
        for: root, role: kAXWindowRole as String,
        label: nil, identifier: nil, explicitValue: nil, rowTexts: []
    )
    let menuDisplay = renderer.sampledDisplayTitle(
        for: menu, role: kAXMenuBarRole as String,
        label: nil, identifier: nil, explicitValue: nil, rowTexts: []
    )
    let otherDisplay = renderer.sampledDisplayTitle(
        for: otherWindow, role: kAXWindowRole as String,
        label: nil, identifier: nil, explicitValue: nil, rowTexts: []
    )

    #expect(reads == 3)
    #expect(rootDisplay == "Selected")
    #expect(menuDisplay == "Menu")
    #expect(otherDisplay == "Other window")
    #expect(renderer.selectedRootTitle == "Selected")
}

@Test
func selectedRootTitleDoesNotBindSameNamedNonselectedRoot() throws {
    let root = AXUIElementCreateApplication(-1)
    let otherWindow = AXUIElementCreateApplication(-2)
    var renderer = TreeRenderer(
        context: titleRenderContext(),
        selectedRoot: root,
        readTitle: { _ in "Same title" },
        readExpansion: { _ in nil }
    )
    try renderer.render(otherWindow)

    #expect(renderer.records[0]?.name == "Same title")
    #expect(renderer.selectedRootTitle == nil)

    try renderer.render(root)
    #expect(renderer.records[1]?.name == "Same title")
    #expect(renderer.selectedRootTitle == "Same title")
}

@Test
func selectedRootTitleFreshRendererClearsPreviousSampleWithoutBorrowingMenubarTitle() {
    let root = AXUIElementCreateApplication(-1)
    let menu = AXUIElementCreateApplication(-2)
    var currentTitle: String? = "Earlier title"
    var reads = 0
    let readTitle: (AXUIElement) -> String? = { element in
        reads += 1
        return CFEqual(element, root) ? currentTitle : "Menu title"
    }
    var renderer = TreeRenderer(
        context: titleRenderContext(),
        selectedRoot: root,
        readTitle: readTitle
    )
    _ = renderer.sampledDisplayTitle(
        for: root, role: kAXWindowRole as String,
        label: nil, identifier: nil, explicitValue: nil, rowTexts: []
    )
    #expect(renderer.selectedRootTitle == "Earlier title")

    for rawTitle in [nil, "", "   "] as [String?] {
        currentTitle = rawTitle
        var freshRenderer = TreeRenderer(
            context: titleRenderContext(),
            selectedRoot: root,
            readTitle: readTitle
        )
        let display = freshRenderer.sampledDisplayTitle(
            for: root, role: kAXWindowRole as String,
            label: nil, identifier: nil, explicitValue: nil, rowTexts: []
        )
        let menuDisplay = freshRenderer.sampledDisplayTitle(
            for: menu, role: kAXMenuBarRole as String,
            label: nil, identifier: nil, explicitValue: nil, rowTexts: []
        )

        #expect(display == (rawTitle == "   " ? "" : nil))
        #expect(menuDisplay == "Menu title")
        #expect(freshRenderer.selectedRootTitle == rawTitle)
        #expect(renderer.selectedRootTitle == "Earlier title")
    }
    #expect(reads == 7)
}

@Test
func selectedRootTitleRepeatedDescendantReusesFirstSample() throws {
    let root = AXUIElementCreateApplication(-1)
    let otherRoot = AXUIElementCreateApplication(-2)
    var reads = 0
    let readTitle: (AXUIElement) -> String? = { element in
        #expect(CFEqual(element, root))
        reads += 1
        return reads == 1 ? "First sample" : "Later sample"
    }
    var renderer = TreeRenderer(
        context: titleRenderContext(),
        selectedRoot: root,
        readTitle: readTitle,
        readExpansion: { _ in nil }
    )
    try renderer.render(root)
    // Simulate reaching the same element through another root's descendant path.
    try renderer.render(root, depth: 1, ancestors: [otherRoot], parentRuntimeID: [99])

    #expect(reads == 1)
    #expect(renderer.records[0]?.name == "First sample")
    #expect(renderer.records[1]?.name == "First sample")
    #expect(renderer.selectedRootTitle == "First sample")

    var freshRenderer = TreeRenderer(
        context: titleRenderContext(),
        selectedRoot: root,
        readTitle: readTitle,
        readExpansion: { _ in nil }
    )
    try freshRenderer.render(root)
    #expect(reads == 2)
    #expect(freshRenderer.records[0]?.name == "Later sample")
    #expect(freshRenderer.selectedRootTitle == "Later sample")
}

@Test
func selectedRootTitleRepeatedMissingSampleDoesNotReadAgain() {
    let root = AXUIElementCreateApplication(-1)
    var reads = 0
    let readTitle: (AXUIElement) -> String? = { _ in
        reads += 1
        return reads == 1 ? nil : "Newly available"
    }
    var renderer = TreeRenderer(
        context: titleRenderContext(),
        selectedRoot: root,
        readTitle: readTitle
    )
    for _ in 0..<2 {
        let display = renderer.sampledDisplayTitle(
            for: root, role: kAXWindowRole as String,
            label: nil, identifier: nil, explicitValue: nil, rowTexts: []
        )
        #expect(display == nil)
        #expect(renderer.selectedRootTitle == nil)
    }
    #expect(reads == 1)

    var freshRenderer = TreeRenderer(
        context: titleRenderContext(),
        selectedRoot: root,
        readTitle: readTitle
    )
    let display = freshRenderer.sampledDisplayTitle(
        for: root, role: kAXWindowRole as String,
        label: nil, identifier: nil, explicitValue: nil, rowTexts: []
    )
    #expect(reads == 2)
    #expect(display == "Newly available")
    #expect(freshRenderer.selectedRootTitle == "Newly available")
    #expect(renderer.selectedRootTitle == nil)
}
