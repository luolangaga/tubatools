import Darwin
import Testing
@testable import ComputerUseProviderKit

@Test func runningAppProcessProbeExcludesOnlyConfirmedMissingPIDs() {
    for (result, error, missing) in [
        (Int32(0), Int32(0), false),
        (0, ESRCH, false), // errno is not meaningful after a successful call.
        (-1, ESRCH, true),
        (-1, EPERM, false),
        (-1, EACCES, false),
        (-1, EINTR, false),
        (-1, EINVAL, false),
        (-1, 0, false),
    ] {
        var calls = 0
        let observed = AppDiscovery.processIsKnownMissing(812) { pid in
            calls += 1
            #expect(pid == 812)
            return (result, error)
        }
        #expect(observed == missing)
        #expect(calls == 1)
    }
}

@Test func runningAppProcessProbeDoesNotQueryProcessGroups() {
    for pid in [pid_t(0), -1, -812] {
        var calls = 0
        #expect(!AppDiscovery.processIsKnownMissing(pid) { _ in
            calls += 1
            return (-1, ESRCH)
        })
        #expect(calls == 0)
    }
}

@Test func runningAppProcessProbeResamplesWithoutCachingAbsence() {
    var status: (result: Int32, error: Int32) = (0, 0)
    let probe: (pid_t) -> (result: Int32, error: Int32) = { _ in status }
    #expect(!AppDiscovery.processIsKnownMissing(812, probe: probe))
    status = (-1, ESRCH)
    #expect(AppDiscovery.processIsKnownMissing(812, probe: probe))
    status = (-1, EPERM)
    #expect(!AppDiscovery.processIsKnownMissing(812, probe: probe))
    status = (0, 0)
    #expect(!AppDiscovery.processIsKnownMissing(812, probe: probe))
}
