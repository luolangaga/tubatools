@testable import ComputerUseProviderKit
import Foundation
import Testing

@Test
func resolvingAnApplicationThatIsNotRunningFailsWithoutLaunching() {
    let query = "HCU Read Only Probe 4242"
    let started = Date()
    do {
        _ = try AppDiscovery.resolve(query)
        Issue.record("Expected appNotFound for an application that is not running")
    } catch {
        let message = (error as? LocalizedError)?.errorDescription ?? ""
        #expect(message.hasPrefix("appNotFound(\"\(query)\")"))
        #expect(message.contains("does not launch"))
    }
    // The old path searched the application folders and then polled five
    // seconds for a launch.
    #expect(Date().timeIntervalSince(started) < 1)
}
