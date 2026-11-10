struct TraversalChild<Node> {
    let element: Node
    let relativePath: [Int]
}

enum TraversalSource: Int, CaseIterable {
    case children = 0
    case rows = 1
    case contents = 2
    case visibleChildren = 3
}

func structuralChildren<Node>(_ elements: [Node], source: TraversalSource) -> [TraversalChild<Node>] {
    elements.enumerated().map { offset, element in
        TraversalChild(
            element: element,
            relativePath: [offset * TraversalSource.allCases.count + source.rawValue]
        )
    }
}
