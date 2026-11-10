import ApplicationServices
@testable import ComputerUseProviderKit
import Testing

@Test
func providerObjectPathUsesAXIdentityInsteadOfTraversalIndex() throws {
    let element = AXUIElementCreateSystemWide()
    let first = ElementRecord(
        index: 4,
        identifier: nil,
        element: element,
        localFrame: nil,
        rawActions: [],
        prettyActions: []
    )
    let second = ElementRecord(
        index: 19,
        identifier: nil,
        element: element,
        localFrame: nil,
        rawActions: [],
        prettyActions: []
    )

    try bindMacProviderObjectIdentities([first, second], scope: "test")
    #expect(macProviderObjectPath(first) == macProviderObjectPath(second))
    #expect(macProviderObjectPath(first).hasPrefix("ax:"))
}

@Test
func syntheticProviderObjectPathDoesNotClaimNativeIdentity() {
    let element = AXUIElementCreateSystemWide()
    let first = ElementRecord(
        index: 4,
        identifier: nil,
        element: element,
        localFrame: nil,
        rawActions: [],
        prettyActions: [],
        isSyntheticText: true
    )
    let second = ElementRecord(
        index: 5,
        identifier: nil,
        element: element,
        localFrame: nil,
        rawActions: [],
        prettyActions: [],
        isSyntheticText: true
    )

    #expect(macProviderObjectPath(first).hasPrefix("synthetic:"))
    #expect(macProviderObjectPath(first) != macProviderObjectPath(second))
}

@Test
func windowHandleIsOpaqueStableAndWindowSpecific() {
    let first = macWindowHandle(pid: 42, windowID: 7)
    let same = macWindowHandle(pid: 42, windowID: 7)
    let other = macWindowHandle(pid: 42, windowID: 8)

    #expect(first == same)
    #expect(first != other)
    #expect(first.count == 35)
    #expect(first.hasPrefix("w2:"))
    #expect(first.dropFirst(3).allSatisfy { $0.isHexDigit })
}
