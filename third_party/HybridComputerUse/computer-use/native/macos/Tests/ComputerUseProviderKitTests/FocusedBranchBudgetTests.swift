@testable import ComputerUseProviderKit
import Testing

@Test
func focusedBranchPrecedesLargeBackgroundSiblingWithinBudget() {
    let children = [
        0: [1, 2],
        1: [3],
        3: [4],
        4: [6],
        2: [5],
    ]
    let focusedAncestry = [5, 2, 0]
    var visited: [Int] = []

    func visit(_ node: Int) {
        guard visited.count < 3 else {
            return
        }
        visited.append(node)
        let ordered = prioritizeFocusedBranchChild(
            children[node] ?? [],
            parent: node,
            focusedAncestry: focusedAncestry,
            isSame: ==
        )
        for child in ordered {
            visit(child)
        }
    }

    visit(0)

    #expect(visited == [0, 2, 5])
}

@Test
func focusedPriorityPreservesUnrelatedSiblingOrder() {
    #expect(prioritizeFocusedBranchChild(
        [10, 11, 12],
        parent: 0,
        focusedAncestry: [12, 0],
        isSame: ==
    ) == [12, 10, 11])

    #expect(prioritizeFocusedBranchChild(
        [20, 21, 22],
        parent: 9,
        focusedAncestry: [12, 0],
        isSame: ==
    ) == [20, 21, 22])
}

@Test
func focusedAncestryStopsAtProviderCycles() {
    let parents = [
        5: 2,
        2: 0,
        0: 2,
    ]

    #expect(boundedAncestry(
        from: 5,
        parentOf: { parents[$0] },
        isSame: ==
    ) == [5, 2, 0])
}
