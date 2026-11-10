import AppKit
import Foundation
import Testing
@testable import ComputerUseProviderKit

@MainActor
private final class WorkspaceReadCounter {
    var reads = 0
}

@Test @MainActor
func applicationDiscoveryReadsOnMainWithoutRedispatch() {
    let counter = WorkspaceReadCounter()
    let apps = AppDiscovery.runningApps {
        #expect(Thread.isMainThread)
        counter.reads += 1
        return []
    }
    #expect(counter.reads == 1)
    #expect(apps.isEmpty)
}

private func discoverTwiceFromWorker(_ counter: WorkspaceReadCounter) -> (Int, Int) {
    #expect(!Thread.isMainThread)
    let read: @MainActor () -> [NSRunningApplication] = {
        #expect(Thread.isMainThread)
        counter.reads += 1
        return []
    }
    let first = AppDiscovery.runningApps(readWorkspace: read)
    let second = AppDiscovery.runningApps(readWorkspace: read)
    return (first.count, second.count)
}

@Test
func applicationDiscoveryMovesEachWorkerReadToMain() async {
    let counter = await WorkspaceReadCounter()
    let results = await Task.detached {
        discoverTwiceFromWorker(counter)
    }.value
    #expect(results.0 == 0)
    #expect(results.1 == 0)
    #expect(await counter.reads == 2)
}
