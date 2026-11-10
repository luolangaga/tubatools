@testable import ComputerUseProviderKit
import Foundation
import Testing

@Test
func appCatalogHidesComputerUseProvider() {
    #expect(
        !AppDiscovery.isDiscoverableBundleIdentifier(
            "org.computer-use.provider.v2"
        )
    )
    #expect(AppDiscovery.isDiscoverableBundleIdentifier("com.apple.Notes"))
}

@Test
func runningAppEnumerationHidesComputerUseProvider() {
    #expect(
        !AppDiscovery.isDiscoverableRunningApplication(
            isTerminated: false,
            bundleIdentifier: "org.computer-use.provider.v2"
        )
    )
    #expect(
        AppDiscovery.isDiscoverableRunningApplication(
            isTerminated: false,
            bundleIdentifier: "com.apple.Notes"
        )
    )
    #expect(
        !AppDiscovery.isDiscoverableRunningApplication(
            isTerminated: true,
            bundleIdentifier: "com.apple.Notes"
        )
    )
}
