import ApplicationServices
@testable import ComputerUseProviderKit
import Testing

private func prioritized(_ children: [TraversalChild<String>], focus: String) -> [TraversalChild<String>] {
    prioritizeFocusedBranchChild(
        children, parent: "parent", focusedAncestry: [focus, "parent"],
        elementOf: { $0.element }, isSame: ==
    )
}

@Test
func traversalFocusMovesRecordsWithoutRebindingTheirPaths() {
    let children = structuralChildren(["a", "b", "c"], source: .children)
    for focus in ["a", "b", "c", "absent"] {
        let ordered = prioritized(children, focus: focus)
        if focus != "absent" {
            #expect(ordered.first?.element == focus)
        }
        for child in ordered {
            #expect(child.relativePath == children.first { $0.element == child.element }?.relativePath)
        }
    }
    #expect(prioritized(children, focus: "c").map(\.element) == ["c", "a", "b"])
    // Even a one-node budget retains the original path of its prioritized node.
    #expect(prioritized(children, focus: "c").prefix(1).first?.relativePath == children[2].relativePath)
}

@Test
func traversalSourceSlotsSurviveVisibilityFilteringAndRemainDistinct() {
    let rows = structuralChildren(Array(0..<40), source: .rows)
    let before = rows.filter { (10..<30).contains($0.element) }
    let after = rows.filter { (15..<35).contains($0.element) }
    #expect(before.first { $0.element == 20 }?.relativePath == after.first { $0.element == 20 }?.relativePath)
    let allPaths = TraversalSource.allCases.flatMap {
        structuralChildren(Array(0..<40), source: $0).map(\.relativePath)
    }
    #expect(Set(allPaths).count == allPaths.count)
    #expect(!allPaths.contains([-1]))
    let reordered = structuralChildren([1, 0], source: .rows)
    #expect(rows[0].relativePath != reordered[1].relativePath)
}

@Test
func traversalRendererPreservesHiddenAncestorsAndIndependentRootSlots() throws {
    let element = AXUIElementCreateApplication(-1)
    let context = RenderContext(
        windowBounds: nil, focusedElement: nil, focusedAncestry: [],
        textLimit: .defaults, treeLimits: .defaults
    )
    var renderer = TreeRenderer(context: context, readTitle: { _ in "Visible" }, readExpansion: { _ in nil })
    try renderer.render(element, parentRuntimeID: [0, 4], relativeRuntimeID: [8, 12])
    try renderer.render(element, parentRuntimeID: [0, 16], relativeRuntimeID: [8, 12])
    try renderer.render(element, relativeRuntimeID: [1])
    #expect(renderer.records[0]?.runtimeID == [0, 4, 8, 12])
    #expect(renderer.records[1]?.runtimeID == [0, 16, 8, 12])
    #expect(renderer.records[2]?.runtimeID == [1])
}

@Test
func traversalIdentityStillRejectsReplacementOwnerAndReparenting() throws {
    let path = [0] + structuralChildren(["a", "target"], source: .children)[1].relativePath
    let target: [String: Any] = [
        "providerObjectPath": "ax:target", "providerWindowObjectPath": "window:1",
        "role": "textbox", "runtimeId": path,
    ]
    let record: [String: Any] = [
        "index": 29, "providerObjectPath": "ax:target", "providerWindowObjectPath": "window:1",
        "controlType": "textbox", "runtimeId": path,
    ]
    var focused = record
    focused["index"] = 11
    #expect(try resolveProviderElementIndex(elements: [focused], target: target, targetLabel: "element") == 11)
    for (field, value) in [
        ("providerObjectPath", "ax:replacement" as Any),
        ("providerWindowObjectPath", "window:2" as Any),
        ("controlType", "button" as Any),
        ("runtimeId", [0, 8, 4] as Any),
    ] {
        var changed = focused
        changed[field] = value
        #expect(throws: ComputerUseError.self) {
            _ = try resolveProviderElementIndex(elements: [changed], target: target, targetLabel: "element")
        }
    }
    #expect(throws: ComputerUseError.self) {
        _ = try resolveProviderElementIndex(elements: [focused, focused], target: target, targetLabel: "element")
    }
}
