@testable import ComputerUseProviderKit
import ApplicationServices
import Foundation
import Testing

private func orderedTransactionRequest(
    keys: [String] = ["A", "B", "C"]
) -> [String: Any] {
    [
        "contract_version": 1,
        "app": "TextEdit",
        "window_handle": "w2:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "steps": keys.enumerated().map { offset, key in
            ["action_index": 4 + offset, "key": key] as [String: Any]
        },
        "final_observation": ["capture_snapshot": true],
    ]
}

private func orderedStepResults(
    _ result: [String: Any]
) -> [[String: Any]] {
    result["steps"] as? [[String: Any]] ?? []
}

@Test
func attachedTransientResolvesToItsContainingLogicalWindow() {
    let roles = [
        0: "AXTextField",
        1: "AXSheet",
        2: "AXWindow",
    ]
    let parents = [
        0: 1,
        1: 2,
    ]

    #expect(nearestLogicalWindow(
        from: 0,
        roleOf: { roles[$0] },
        parentOf: { parents[$0] }
    ) == 2)
    #expect(nearestLogicalWindow(
        from: 1,
        roleOf: { roles[$0] },
        parentOf: { parents[$0] }
    ) == 2)
    #expect(nearestLogicalWindow(
        from: 2,
        roleOf: { roles[$0] },
        parentOf: { parents[$0] }
    ) == 2)
    #expect(nearestLogicalWindow(
        from: 3,
        roleOf: { roles[$0] },
        parentOf: { parents[$0] }
    ) == nil)
}

@Test
func strictFocusedWindowAcceptsOwnedFocusedDescendant() {
    let pids: [Int: pid_t] = [
        10: 41,
        11: 41,
        12: 41,
    ]
    let containingWindows = [
        10: 12,
        11: 12,
        12: 12,
    ]

    #expect(strictFocusedWindowCandidate(
        targetPID: 41,
        systemFocusedWindow: nil,
        systemFocusedElement: 10,
        applicationFocusedWindow: 11,
        pidOf: { pids[$0] },
        containingWindow: { containingWindows[$0] },
        isUsableWindow: { $0 == 12 }
    ) == 12)
}

@Test
func strictFocusedWindowRejectsSiblingOwnership() {
    let pids: [Int: pid_t] = [
        20: 42,
        21: 42,
        22: 42,
    ]
    let containingWindows = [
        20: 22,
        21: 22,
        22: 22,
    ]

    #expect(strictFocusedWindowCandidate(
        targetPID: 41,
        systemFocusedWindow: 22,
        systemFocusedElement: 20,
        applicationFocusedWindow: 21,
        pidOf: { pids[$0] },
        containingWindow: { containingWindows[$0] },
        isUsableWindow: { _ in true }
    ) == nil)
}

@Test
func uniqueUsableWindowFallbackRequiresExactlyOneWindow() {
    #expect(uniqueUsableWindowCandidate(
        [10],
        isUsableWindow: { $0 == 10 }
    ) == 10)
    #expect(uniqueUsableWindowCandidate(
        [10, 11],
        isUsableWindow: { _ in true }
    ) == nil)
    #expect(uniqueUsableWindowCandidate(
        [10],
        isUsableWindow: { _ in false }
    ) == nil)
}

@Test
func orderedInputUsableWindowRequiresKnownNonMinimizedState() {
    #expect(orderedInputWindowIsUsable(
        role: "AXWindow",
        minimized: false
    ))
    #expect(!orderedInputWindowIsUsable(
        role: "AXWindow",
        minimized: true
    ))
    #expect(!orderedInputWindowIsUsable(
        role: "AXWindow",
        minimized: nil
    ))
    #expect(!orderedInputWindowIsUsable(
        role: "AXDialog",
        minimized: false
    ))
}

@Test
func canonicalWindowHandleFallsBackToAXIdentityWithoutNativeWindowNumber() {
    let window = AXUIElementCreateSystemWide()

    #expect(windowNumber(window) == nil)
    #expect(
        canonicalMacAXWindowHandle(pid: getpid(), window: window)
            == macAXWindowHandle(pid: getpid(), window: window)
    )
}

@Test
func orderedInputDispatchUsesForegroundGlobalRoute() throws {
    var globallyPosted: [String] = []

    try dispatchFocusedOrderedKeypress("SUPER+SHIFT+G") {
        globallyPosted.append($0)
    }

    #expect(globallyPosted == ["SUPER+SHIFT+G"])
}

@Test
func orderedInputRequiresTheOwnerApplicationToBeFrontmost() {
    #expect(orderedInputApplicationContinuity(
        targetPID: 41,
        frontmostPID: 41
    ) == .same)
    #expect(orderedInputApplicationContinuity(
        targetPID: 41,
        frontmostPID: 42
    ) == .changed)
    #expect(orderedInputApplicationContinuity(
        targetPID: 41,
        frontmostPID: nil
    ) == .unknown)
}

@Test
func orderedInputCompletesWithOneFinalSnapshot() {
    var inspected = 0
    var dispatched: [String] = []
    var preparations = 0
    var barriers = 0
    var snapshots = 0
    let result = executeOrderedInputTransaction(
        request: orderedTransactionRequest(),
        dependencies: OrderedInputRuntimeDependencies(
            inspectWindow: {
                inspected += 1
                return .same
            },
            dispatchKey: {
                dispatched.append($0)
            },
            prepareFinalObservationReadiness: {
                preparations += 1
            },
            awaitFinalObservationReadiness: {
                barriers += 1
            },
            finalSnapshot: {
                snapshots += 1
                return [
                    "windowHandle":
                        "w2:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                ]
            }
        )
    )

    #expect(result["ok"] as? Bool == true)
    #expect(inspected == 3)
    #expect(dispatched == ["A", "B", "C"])
    #expect(preparations == 1)
    #expect(barriers == 1)
    #expect(snapshots == 1)
    #expect(orderedStepResults(result).allSatisfy {
        $0["status"] as? String == "completed"
    })
}

@Test
func orderedInputRunsOneBarrierAfterAllDispatchesBeforeSnapshot() {
    var events: [String] = []
    let result = executeOrderedInputTransaction(
        request: orderedTransactionRequest(),
        dependencies: OrderedInputRuntimeDependencies(
            inspectWindow: { .same },
            dispatchKey: { events.append("dispatch:\($0)") },
            prepareFinalObservationReadiness: {
                events.append("prepare")
            },
            awaitFinalObservationReadiness: {
                events.append("barrier")
            },
            finalSnapshot: {
                events.append("snapshot")
                return [:]
            }
        )
    )

    #expect(result["ok"] as? Bool == true)
    #expect(events == [
        "dispatch:A",
        "dispatch:B",
        "prepare",
        "dispatch:C",
        "barrier",
        "snapshot",
    ])
}

@Test
func orderedInputStopsBeforeDispatchWhenWindowChanges() {
    var inspections = [
        OrderedInputWindowContinuity.same,
        .changed,
        .same,
    ]
    var dispatched: [String] = []
    var preparations = 0
    var barriers = 0
    var snapshots = 0
    let result = executeOrderedInputTransaction(
        request: orderedTransactionRequest(),
        dependencies: OrderedInputRuntimeDependencies(
            inspectWindow: { inspections.removeFirst() },
            dispatchKey: { dispatched.append($0) },
            prepareFinalObservationReadiness: {
                preparations += 1
            },
            awaitFinalObservationReadiness: {
                barriers += 1
            },
            finalSnapshot: {
                snapshots += 1
                return [:]
            }
        )
    )
    let steps = orderedStepResults(result)

    #expect(result["ok"] as? Bool == false)
    #expect(dispatched == ["A"])
    #expect(preparations == 0)
    #expect(barriers == 0)
    #expect(snapshots == 0)
    #expect(steps.map { $0["status"] as? String } == [
        "completed", "failed", "not_attempted",
    ])
    #expect(steps[1]["dispatch_attempted"] as? Bool == false)
    #expect(steps[1]["window_continuity"] as? String == "changed")
    #expect(result["stopped_action_index"] as? Int == 5)
    #expect(result["first_unattempted_action_index"] as? Int == 5)
}

@Test
func orderedInputTypedStalePreconditionsPreserveEarlierEmission() {
    for completedCount in [0, 1] {
        for (error, code, subject) in [
            (ComputerUseError.staleAppRef("owner replaced"), "stale_app_ref", "app"),
            (ComputerUseError.staleWindowRef("owner replaced"), "stale_window_ref", "window"),
        ] {
            var inspected = 0
            var emitted: [String] = []
            var finalEvents = 0
            let result = executeOrderedInputTransaction(
                request: orderedTransactionRequest(),
                dependencies: OrderedInputRuntimeDependencies(
                    inspectWindow: {
                        inspected += 1
                        if inspected == completedCount + 1 {
                            throw error
                        }
                        return .same
                    },
                    dispatchKey: { emitted.append($0) },
                    prepareFinalObservationReadiness: { finalEvents += 1 },
                    awaitFinalObservationReadiness: { finalEvents += 1 },
                    finalSnapshot: {
                        finalEvents += 1
                        return [:]
                    }
                )
            )
            let steps = orderedStepResults(result)
            let failure = result["failure"] as? [String: Any]
            let mutation = result["mutation"] as? [String: Any]

            #expect(result["ok"] as? Bool == false)
            #expect(inspected == completedCount + 1)
            #expect(emitted == Array(["A", "B", "C"].prefix(completedCount)))
            #expect(finalEvents == 0)
            #expect(steps.prefix(completedCount).allSatisfy {
                $0["status"] as? String == "completed"
                    && $0["emission"] as? String == "complete"
            })
            #expect(steps[completedCount]["status"] as? String == "failed")
            #expect(steps[completedCount]["dispatch_attempted"] as? Bool == false)
            #expect(steps[completedCount]["emission"] as? String == "none")
            #expect(steps[completedCount]["window_continuity"] as? String == "changed")
            #expect(steps.dropFirst(completedCount + 1).allSatisfy {
                $0["status"] as? String == "not_attempted"
            })
            #expect(failure?["code"] as? String == code)
            #expect(failure?["subject"] as? String == subject)
            #expect(failure?["phase"] as? String == "precondition")
            #expect(result["stopped_action_index"] as? Int == 4 + completedCount)
            #expect(result["first_unattempted_action_index"] as? Int == 4 + completedCount)
            #expect(mutation?["input_events_emitted"] as? Bool == (completedCount > 0))
            #expect(mutation?["partial_input_possible"] as? Bool == false)
            #expect(mutation?["mutation_unknown"] as? Bool == (completedCount > 0))
            #expect(result["retry_same_request"] as? Bool == false)
        }
    }
}

@Test
func orderedInputDispatchIdentityRaceReportsNoNewEmission() {
    for completedCount in [0, 1] {
        for (error, code, subject) in [
            (ComputerUseError.staleAppRef("owner replaced"), "stale_app_ref", "app"),
            (ComputerUseError.staleWindowRef("owner replaced"), "stale_window_ref", "window"),
        ] {
            var inspected = 0
            var dispatched = 0
            var emitted: [String] = []
            var snapshots = 0
            let result = executeOrderedInputTransaction(
                request: orderedTransactionRequest(),
                dependencies: OrderedInputRuntimeDependencies(
                    inspectWindow: {
                        inspected += 1
                        return .same
                    },
                    dispatchKey: { key in
                        dispatched += 1
                        if dispatched == completedCount + 1 {
                            throw error
                        }
                        emitted.append(key)
                    },
                    prepareFinalObservationReadiness: {},
                    awaitFinalObservationReadiness: {},
                    finalSnapshot: {
                        snapshots += 1
                        return [:]
                    }
                )
            )
            let steps = orderedStepResults(result)
            let failure = result["failure"] as? [String: Any]
            let mutation = result["mutation"] as? [String: Any]

            #expect(inspected == completedCount + 1)
            #expect(dispatched == completedCount + 1)
            #expect(emitted == Array(["A", "B", "C"].prefix(completedCount)))
            #expect(snapshots == 0)
            #expect(steps.prefix(completedCount).allSatisfy {
                $0["status"] as? String == "completed"
                    && $0["emission"] as? String == "complete"
            })
            #expect(steps[completedCount]["dispatch_attempted"] as? Bool == false)
            #expect(steps[completedCount]["emission"] as? String == "none")
            #expect(steps[completedCount]["window_continuity"] as? String == "changed")
            #expect(steps.dropFirst(completedCount + 1).allSatisfy {
                $0["status"] as? String == "not_attempted"
            })
            #expect(failure?["code"] as? String == code)
            #expect(failure?["subject"] as? String == subject)
            #expect(failure?["phase"] as? String == "precondition")
            #expect(result["stopped_action_index"] as? Int == 4 + completedCount)
            #expect(result["first_unattempted_action_index"] as? Int == 4 + completedCount)
            #expect(mutation?["input_events_emitted"] as? Bool == (completedCount > 0))
            #expect(mutation?["partial_input_possible"] as? Bool == false)
            #expect(mutation?["mutation_unknown"] as? Bool == (completedCount > 0))
        }
    }
}

@Test
func orderedInputTemporaryInspectionFailureRemainsUnknown() {
    for throwsError in [false, true] {
        var dispatched = 0
        let result = executeOrderedInputTransaction(
            request: orderedTransactionRequest(),
            dependencies: OrderedInputRuntimeDependencies(
                inspectWindow: {
                    if throwsError {
                        throw ComputerUseError.stateUnavailable(
                            "stale_app_ref is diagnostic prose, not identity evidence"
                        )
                    }
                    return .unknown
                },
                dispatchKey: { _ in dispatched += 1 },
                prepareFinalObservationReadiness: {},
                awaitFinalObservationReadiness: {},
                finalSnapshot: { [:] }
            )
        )
        let steps = orderedStepResults(result)
        let failure = result["failure"] as? [String: Any]
        let mutation = result["mutation"] as? [String: Any]

        #expect(dispatched == 0)
        #expect(steps[0]["window_continuity"] as? String == "unknown")
        #expect(steps[0]["emission"] as? String == "none")
        #expect(failure?["code"] as? String == "state_unavailable")
        #expect(failure?["subject"] == nil)
        #expect(failure?["underlying_code"] == nil)
        #expect(mutation?["input_events_emitted"] as? Bool == false)
        #expect(mutation?["partial_input_possible"] as? Bool == false)
    }
}

@Test
func orderedInputStaleFinalSnapshotPreservesCompletedKeys() {
    for (error, code, subject, reason) in [
        (ComputerUseError.staleAppRef("owner replaced"), "stale_app_ref", "app", "provider_process_changed"),
        (ComputerUseError.staleWindowRef("owner replaced"), "stale_window_ref", "window", "provider_window_changed"),
    ] {
        var emitted: [String] = []
        var barriers = 0
        var snapshots = 0
        let result = executeOrderedInputTransaction(
            request: orderedTransactionRequest(),
            dependencies: OrderedInputRuntimeDependencies(
                inspectWindow: { .same },
                dispatchKey: { emitted.append($0) },
                prepareFinalObservationReadiness: {},
                awaitFinalObservationReadiness: { barriers += 1 },
                finalSnapshot: {
                    snapshots += 1
                    throw error
                }
            )
        )
        let failure = result["failure"] as? [String: Any]
        let mutation = result["mutation"] as? [String: Any]

        #expect(result["ok"] as? Bool == false)
        #expect(emitted == ["A", "B", "C"])
        #expect(barriers == 1)
        #expect(snapshots == 1)
        #expect(orderedStepResults(result).allSatisfy {
            $0["status"] as? String == "completed"
                && $0["emission"] as? String == "complete"
        })
        #expect(failure?["code"] as? String == code)
        #expect(failure?["subject"] as? String == subject)
        #expect(failure?["underlying_code"] as? String == reason)
        #expect(failure?["phase"] as? String == "observation")
        #expect(result["stopped_action_index"] == nil)
        #expect(result["first_unattempted_action_index"] == nil)
        #expect(result["snapshot"] == nil)
        #expect(result["retry_same_request"] as? Bool == false)
        #expect(mutation?["input_events_emitted"] as? Bool == true)
        #expect(mutation?["partial_input_possible"] as? Bool == false)
        #expect(mutation?["mutation_unknown"] as? Bool == true)
    }
}

@Test
func orderedInputPreservesPartialEmissionAndStopsLaterDispatch() {
    var dispatched: [String] = []
    var preparations = 0
    var barriers = 0
    let result = executeOrderedInputTransaction(
        request: orderedTransactionRequest(),
        dependencies: OrderedInputRuntimeDependencies(
            inspectWindow: { .same },
            dispatchKey: { key in
                dispatched.append(key)
                if key == "B" {
                    throw InputDeliveryError(
                        message: "injected partial emission",
                        inputEventsEmitted: true
                    )
                }
            },
            prepareFinalObservationReadiness: {
                preparations += 1
            },
            awaitFinalObservationReadiness: {
                barriers += 1
            },
            finalSnapshot: { [:] }
        )
    )
    let steps = orderedStepResults(result)
    let mutation = result["mutation"] as? [String: Any]

    #expect(dispatched == ["A", "B"])
    #expect(preparations == 0)
    #expect(barriers == 0)
    #expect(steps.map { $0["status"] as? String } == [
        "completed", "failed", "not_attempted",
    ])
    #expect(steps[1]["emission"] as? String == "partial")
    #expect(mutation?["input_events_emitted"] as? Bool == true)
    #expect(mutation?["partial_input_possible"] as? Bool == true)
    #expect(mutation?["mutation_unknown"] as? Bool == true)
    #expect(result["first_unattempted_action_index"] as? Int == 6)
}

@Test
func orderedInputInvalidKeyFailureReportsZeroEmission() {
    let result = executeOrderedInputTransaction(
        request: orderedTransactionRequest(keys: ["SUPER+NOT_A_REAL_KEY"]),
        dependencies: OrderedInputRuntimeDependencies(
            inspectWindow: { .same },
            dispatchKey: { key in
                _ = try KeyPressParser.parse(key)
            },
            prepareFinalObservationReadiness: {},
            awaitFinalObservationReadiness: {},
            finalSnapshot: { [:] }
        )
    )
    let steps = orderedStepResults(result)
    let failure = result["failure"] as? [String: Any]
    let mutation = result["mutation"] as? [String: Any]

    #expect(result["ok"] as? Bool == false)
    #expect(steps[0]["dispatch_attempted"] as? Bool == true)
    #expect(steps[0]["emission"] as? String == "none")
    #expect(failure?["phase"] as? String == "precondition")
    #expect(mutation?["input_events_emitted"] as? Bool == false)
    #expect(mutation?["partial_input_possible"] as? Bool == false)
    #expect(mutation?["mutation_unknown"] as? Bool == false)
}

@Test
func orderedInputDoesNotAwaitAfterFinalDispatchFailure() {
    var preparations = 0
    var barriers = 0
    let result = executeOrderedInputTransaction(
        request: orderedTransactionRequest(keys: ["A"]),
        dependencies: OrderedInputRuntimeDependencies(
            inspectWindow: { .same },
            dispatchKey: { _ in
                throw InputDeliveryError(
                    message: "injected final dispatch failure",
                    inputEventsEmitted: false
                )
            },
            prepareFinalObservationReadiness: {
                preparations += 1
            },
            awaitFinalObservationReadiness: {
                barriers += 1
            },
            finalSnapshot: { [:] }
        )
    )

    #expect(result["ok"] as? Bool == false)
    #expect(preparations == 1)
    #expect(barriers == 0)
}

@Test
func orderedInputReportsFinalSnapshotFailureWithoutReclassifyingKeys() {
    var preparations = 0
    var barriers = 0
    let result = executeOrderedInputTransaction(
        request: orderedTransactionRequest(keys: ["A", "B"]),
        dependencies: OrderedInputRuntimeDependencies(
            inspectWindow: { .same },
            dispatchKey: { _ in },
            prepareFinalObservationReadiness: {
                preparations += 1
            },
            awaitFinalObservationReadiness: {
                barriers += 1
            },
            finalSnapshot: {
                throw ComputerUseError.stateUnavailable(
                    "injected final snapshot failure"
                )
            }
        )
    )
    let steps = orderedStepResults(result)
    let failure = result["failure"] as? [String: Any]
    let mutation = result["mutation"] as? [String: Any]

    #expect(result["ok"] as? Bool == false)
    #expect(preparations == 1)
    #expect(barriers == 1)
    #expect(steps.allSatisfy {
        $0["status"] as? String == "completed"
    })
    #expect(failure?["phase"] as? String == "observation")
    #expect(failure?["code"] as? String == "state_unavailable")
    #expect(failure?["underlying_code"] as? String == "final_snapshot_failed")
    #expect(failure?["subject"] == nil)
    #expect(result["stopped_action_index"] == nil)
    #expect(result["first_unattempted_action_index"] == nil)
    #expect(result["snapshot"] == nil)
    #expect(result["retry_same_request"] as? Bool == false)
    #expect(mutation?["mutation_unknown"] as? Bool == true)
}

@Test
func orderedInputFocusConvergenceRequiresChangedStableIdentity() {
    var clock = 0.0
    var samples = ["document", "textbox", "textbox", "textbox"]
    let converged = awaitOrderedInputFocusConvergence(
        baseline: "document",
        timeout: 0.75,
        stableFor: 0.08,
        now: { clock },
        pause: { clock += $0 },
        current: {
            samples.isEmpty ? "textbox" : samples.removeFirst()
        },
        isSame: ==
    )

    #expect(converged)
    #expect(clock >= 0.08)
    #expect(clock < 0.75)
}

@Test
func orderedInputFocusConvergenceIgnoresTransientIdentity() {
    var clock = 0.0
    var samples = [
        "document",
        "transient",
        "document",
        "textbox",
    ]
    let converged = awaitOrderedInputFocusConvergence(
        baseline: "document",
        timeout: 0.75,
        stableFor: 0.08,
        now: { clock },
        pause: { clock += $0 },
        current: {
            samples.isEmpty ? "textbox" : samples.removeFirst()
        },
        isSame: ==
    )

    #expect(converged)
    #expect(clock >= 0.10)
}

@Test
func orderedInputFocusConvergenceStopsAtDeadlineWithoutChange() {
    var clock = 0.0
    let converged = awaitOrderedInputFocusConvergence(
        baseline: "document",
        timeout: 0.25,
        stableFor: 0.08,
        now: { clock },
        pause: { clock += $0 },
        current: { "document" },
        isSame: ==
    )

    #expect(!converged)
    #expect(clock >= 0.25)
    #expect(clock < 0.26)
}
