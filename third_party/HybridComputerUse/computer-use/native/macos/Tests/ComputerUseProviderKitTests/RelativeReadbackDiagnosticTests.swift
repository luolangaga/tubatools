import AppKit
import ApplicationServices
import Foundation
import Testing
@testable import ComputerUseProviderKit

private func readbackPayload(
    _ capture: RelativeReadbackDiagnostic
) throws -> [String: Any] {
    var fragments: [String] = []
    capture.emit { fragments.append($0) }
    var bytes = Data()
    var identifier: String?
    var totalBytes: Int?
    for (index, fragment) in fragments.enumerated() {
        #expect(fragment.utf8.count <= 900)
        let envelope = try #require(JSONSerialization.jsonObject(
            with: Data(fragment.utf8)) as? [String: Any])
        #expect(envelope["event"] as? String == "diagnostic_chunk")
        #expect(envelope["version"] as? Int == 1)
        #expect(envelope["part"] as? Int == index)
        #expect(envelope["parts"] as? Int == fragments.count)
        if index == 0 {
            identifier = envelope["id"] as? String
            totalBytes = envelope["bytes"] as? Int
        }
        #expect(envelope["id"] as? String == identifier)
        #expect(envelope["sha256"] == nil)
        #expect(envelope["bytes"] as? Int == totalBytes)
        let encoded = try #require(envelope["data"] as? String)
        bytes.append(try #require(Data(base64Encoded: encoded)))
    }
    #expect(!fragments.isEmpty)
    #expect(bytes.count <= 16_384)
    #expect(bytes.count == totalBytes)
    var duplicate = false
    capture.emit { _ in duplicate = true }
    #expect(!duplicate)
    return try #require(JSONSerialization.jsonObject(with: bytes) as? [String: Any])
}

@Test
func relativeReadbackOptInIsStrictAndActionScoped() {
    #expect(!relativeReadbackDiagnosticEnabled(tool: "increment", operation: [:]))
    let disabledValues: [Any] = [false, "true", 1, NSNull()]
    for value in disabledValues {
        #expect(!relativeReadbackDiagnosticEnabled(
            tool: "increment", operation: ["diagnostics": ["relative_readback": value]]
        ))
    }
    for tool in ["increment", "decrement", "click", "set_value", "ordered_keypress"] {
        #expect(relativeReadbackDiagnosticEnabled(
            tool: tool, operation: ["diagnostics": ["relative_readback": true]]
        ) == (tool == "increment" || tool == "decrement"))
    }
    let context = Thread.current.threadDictionary
    let key = "computer-use.liveness.request"
    let saved = context[key]
    defer {
        if let saved { context[key] = saved } else { context.removeObject(forKey: key) }
    }
    let operation: [String: Any] = ["diagnostics": ["relative_readback": true]]
    context.removeObject(forKey: key)
    #expect(RelativeReadbackDiagnostic.requested(tool: "increment", operation: operation) == nil)
    context[key] = String(repeating: "x", count: 129)
    #expect(RelativeReadbackDiagnostic.requested(tool: "increment", operation: operation) == nil)
    context[key] = "native-request-1"
    #expect(RelativeReadbackDiagnostic.requested(tool: "increment", operation: operation)?.requestID
        == "native-request-1")
    #expect(RelativeReadbackDiagnostic.requested(tool: "increment", operation: [:]) == nil)
}

@Test
func relativeReadbackDiagnosticsDoNotChangeReadsWaitsOrAcceptance() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { CFHash($0) }, equal: { CFEqual($0, $1) }
    )
    let record = readbackRecord(1, AXUIElementCreateApplication(getpid()))
    try bindMacProviderObjectIdentities(
        [record], scope: "mac:\(getpid()):test-generation", registry: registry
    )
    let snapshot = readbackSnapshot([record])
    let cases: [([CFTypeRef?], AXError, String)] = [
        (["12" as CFString, "12" as CFString, "13" as CFString, "14" as CFString], .success, "changed"),
        (["12" as CFString, "" as CFString], .success, "changed"),
        (["12" as CFString, NSNumber(value: 12)], .success, "changed"),
        (["12" as CFString, "11" as CFString], .success, "changed"),
        ([nil], .success, "before_unavailable"),
        (["12" as CFString, nil], .success, "after_unavailable"),
        (["12" as CFString], .success, "unchanged"),
        (["12" as CFString], .cannotComplete, "native_error"),
    ]
    for (values, nativeResult, expectedOutcome) in cases {
        var baseline: [String]?
        for enabled in [false, true] {
            var events: [String] = []
            var readCount = 0
            var uptime = 0.0
            var diagnosticTime = 100.0
            let capture = enabled ? RelativeReadbackDiagnostic(
                requestID: "read-sequence", phase: "increment", registry: registry,
                clock: { diagnosticTime += 0.001; return diagnosticTime }
            ) : nil
            capture?.bind(snapshot: snapshot, target: record)
            do {
                let result = try dispatchRelativeAdjustment(
                    action: "AXIncrement", direction: "increment", diagnostic: capture,
                    readValue: {
                        events.append("read")
                        defer { readCount += 1 }
                        return values[min(readCount, values.count - 1)]
                    },
                    performAction: { events.append("action"); return nativeResult },
                    now: { events.append("now"); return uptime },
                    sleep: { interval in
                        #expect(interval == 0.02)
                        events.append("sleep")
                        uptime += interval
                    }
                )
                #expect(expectedOutcome != "unchanged" && expectedOutcome != "native_error")
                #expect(result.readbackSampled == (expectedOutcome == "changed"))
                #expect(result.readbackVerified == (expectedOutcome == "changed" ? true : nil))
            } catch let error as InputDeliveryError {
                #expect(expectedOutcome == "unchanged" || expectedOutcome == "native_error")
                #expect(error.inputEventsEmitted)
                if expectedOutcome == "unchanged" {
                    #expect(error.failureCode == "readback_unchanged")
                    #expect(error.readbackSampled && error.readbackVerified == false)
                    #expect(error.partialInputPossible == false)
                } else {
                    #expect(!error.readbackSampled && error.readbackVerified == nil)
                    #expect(error.partialInputPossible == true)
                }
            }
            #expect(events.first == "read")
            #expect(events.dropFirst().first == "action")
            #expect(events.filter { $0 == "action" }.count == 1)
            if expectedOutcome == "native_error" || expectedOutcome == "before_unavailable" {
                #expect(events == ["read", "action"])
            } else if expectedOutcome == "changed" {
                if values.count == 4 {
                    #expect(readCount == 3)
                    #expect(events.filter { $0 == "sleep" }.count == 1)
                } else {
                    #expect(readCount == 2)
                    #expect(!events.contains("sleep"))
                }
            } else {
                #expect(readCount == 7)
                #expect(events.filter { $0 == "sleep" }.count == 6)
            }
            if let baseline { #expect(events == baseline) } else { baseline = events }
            if let capture {
                #expect(capture.before == relativeAdjustmentScalarReadback(values[0]))
                #expect(capture.pollCount == readCount - 1)
                let countBeforeEmit = readCount
                capture.bindFinal(snapshot: snapshot)
                let payload = try readbackPayload(capture)
                #expect(readCount == countBeforeEmit)
                #expect(payload["outcome"] as? String == expectedOutcome)
                let final = try #require(payload["final_target"] as? [String: Any])
                #expect(final["status"] as? String == "associated")
                if expectedOutcome == "changed" {
                    #expect(capture.decisiveAfter
                        == relativeAdjustmentScalarReadback(values[readCount - 1]))
                    #expect(capture.decisiveAfter == capture.lastAfter)
                    #expect(capture.times["decisive_after_read_end_uptime_s"]
                        == capture.times["last_after_read_end_uptime_s"])
                } else {
                    #expect(capture.decisiveAfter == nil)
                }
            }
        }
    }
}

@Test
func relativeReadbackDoesNotConsumeFailureDiagnosticBudget() throws {
    let context = Thread.current.threadDictionary
    let key = "computer-use.failure.count"
    let saved = context[key]
    defer {
        if let saved { context[key] = saved } else { context.removeObject(forKey: key) }
    }
    context[key] = 8
    let payload = try readbackPayload(RelativeReadbackDiagnostic(
        requestID: "independent-budget", phase: "increment"
    ))
    #expect(payload["event"] as? String == "relative_readback")
    #expect(context[key] as? Int == 8)
}

@Test
func relativeReadbackTaggedScalarsAndBoundsAreLossExplicit() throws {
    for scalar in [
        RelativeAdjustmentScalarReadback.string(""),
        .string("12"), .number("12"), .string("12 \n"),
        .string(String(repeating: "\u{00e9}", count: 512)),
    ] {
        let facts = relativeReadbackScalarFacts(scalar)
        #expect(facts["complete"] as? Bool == true)
        switch scalar {
        case .string(let text):
            #expect(facts["kind"] as? String == "string")
            #expect(facts["text"] as? String == text)
        case .number(let text):
            #expect(facts["kind"] as? String == "number")
            #expect(facts["text"] as? String == text)
        }
    }
    let oversized = String(repeating: "\u{00e9}", count: 513)
    let capture = RelativeReadbackDiagnostic(requestID: "oversize", phase: "increment")
    capture.didReadBefore(.string(oversized))
    capture.didReadAfter(.string(""))
    capture.finish("changed", sampled: true, verified: true)
    let payload = try readbackPayload(capture)
    #expect(payload["complete"] as? Bool == false)
    let before = try #require(payload["before"] as? [String: Any])
    #expect(before["kind"] as? String == "string")
    #expect(before["utf8_bytes"] as? Int == 1026)
    #expect(before["text"] == nil)
    #expect(before["reason"] as? String == "value_oversize")
    let after = try #require(payload["decisive_after"] as? [String: Any])
    #expect(after["text"] as? String == "")
    #expect(relativeReadbackScalarFacts(nil)["kind"] as? String == "unavailable")
}

@Test
func relativeReadbackFrameRoundtripAndErrorLifetimeAreIsolated() throws {
    enum TestFailure: Error { case failed }
    weak var released: RelativeReadbackDiagnostic?
    var firstPayload: [String: Any]?
    var finalAttemptFinished = false
    func failingRequest() throws {
        let capture = RelativeReadbackDiagnostic(requestID: "first-native-id", phase: "increment")
        released = capture
        defer {
            #expect(finalAttemptFinished)
            firstPayload = try? readbackPayload(capture)
        }
        let text = String(repeating: "\"\\\n\u{00e9}", count: 100)
        capture.didReadBefore(.string(text))
        capture.didReadAfter(.number("13"))
        capture.finish("changed", sampled: true, verified: true)
        capture.finalSnapshotStatus = "failed"
        finalAttemptFinished = true
        throw TestFailure.failed
    }
    #expect(throws: TestFailure.self) { try failingRequest() }
    #expect(released == nil)
    let first = try #require(firstPayload)
    #expect(first["request_id"] as? String == "first-native-id")
    let firstValue = try #require(first["before"] as? [String: Any])
    #expect(firstValue["text"] as? String == String(repeating: "\"\\\n\u{00e9}", count: 100))
    let second = try readbackPayload(RelativeReadbackDiagnostic(
        requestID: "second-native-id", phase: "decrement"
    ))
    #expect(second["request_id"] as? String == "second-native-id")
    #expect(second["poll_count"] as? Int == 0)
    #expect(second["outcome"] as? String == "not_observed")
    #expect(second["input_emitted"] == nil)
    #expect((second["before"] as? [String: Any])?["kind"] as? String == "unavailable")
}

@Test
func relativeReadbackTotalPayloadBoundDoesNotChangeVerification() throws {
    let text = String(repeating: "\u{0001}", count: 1024)
    let capture = RelativeReadbackDiagnostic(requestID: "payload-limit", phase: "increment")
    capture.didReadBefore(.string(text))
    capture.didReadAfter(.string(text + "x"))
    capture.finish("changed", sampled: true, verified: true)
    // Oversize individual values are omitted, while the existing decision is retained.
    let payload = try readbackPayload(capture)
    #expect(payload["complete"] as? Bool == false)
    #expect(payload["readback_verified"] as? Bool == true)
    let huge = RelativeReadbackDiagnostic(requestID: "payload-limit-2", phase: "increment")
    huge.didReadBefore(.string(text))
    huge.didReadAfter(.string(String(repeating: "\u{0002}", count: 1024)))
    huge.finish("changed", sampled: true, verified: true)
    let fallback = try readbackPayload(huge)
    #expect(fallback["complete"] as? Bool == false)
    #expect(fallback["reason"] as? String == "payload_oversize_or_invalid")
    #expect(fallback["before"] == nil)
}

private func readbackRecord(
    _ index: Int, _ element: AXUIElement?, synthetic: Bool = false, value: String? = "12"
) -> ElementRecord {
    ElementRecord(
        index: index, identifier: nil, element: element, localFrame: nil,
        rawActions: ["AXIncrement"], prettyActions: ["increment"],
        isSyntheticText: synthetic, value: value
    )
}

private func readbackSnapshot(
    _ records: [ElementRecord], generation: String? = "test-generation",
    window: String? = "test-window", pid: pid_t = getpid(), serial: Int = 3
) -> AppSnapshot {
    AppSnapshot(
        app: RunningAppDescriptor(
            name: "Test", bundleIdentifier: "test.readback", pid: pid,
            runningApplication: NSRunningApplication.current
        ),
        windowTitle: nil, windowBounds: nil, rootWindowElement: AXUIElementCreateSystemWide(),
        targetWindowID: nil, targetWindowHandle: window, targetWindowLayer: nil,
        treeLines: [], focusedSummary: nil, focusedElement: nil,
        selectedText: nil, elements: Dictionary(uniqueKeysWithValues: records.map { ($0.index, $0) }),
        providerProcessGeneration: generation, diagnosticSnapshotSerial: serial
    )
}

@Test
func relativeReadbackInitialValueRequiresUniqueRetainedRecord() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { CFHash($0) }, equal: { CFEqual($0, $1) }
    )
    let object = AXUIElementCreateApplication(getpid())
    let target = readbackRecord(1, object)
    let alias = readbackRecord(2, object)
    try bindMacProviderObjectIdentities(
        [target, alias], scope: "mac:\(getpid()):test-generation", registry: registry
    )
    let replacement = readbackRecord(1, AXUIElementCreateSystemWide())
    replacement.bindProviderObjectPath(try #require(target.boundProviderObjectPath))
    let synthetic = readbackRecord(1, object, synthetic: true)
    synthetic.bindProviderObjectPath(try #require(target.boundProviderObjectPath))
    for (snapshot, record, associated) in [
        (readbackSnapshot([target]), target, true),
        (readbackSnapshot([target, alias]), target, false),
        (readbackSnapshot([replacement]), replacement, false),
        (readbackSnapshot([synthetic]), synthetic, false),
        (readbackSnapshot([target], generation: nil), target, false),
        (readbackSnapshot([target], generation: "other-generation"), target, false),
        (readbackSnapshot([alias]), target, false),
    ] {
        let capture = RelativeReadbackDiagnostic(requestID: "identity", phase: "increment", registry: registry)
        capture.bind(snapshot: snapshot, target: record)
        let payload = try readbackPayload(capture)
        let initial = try #require(payload["initial_target"] as? [String: Any])
        #expect(initial["status"] as? String == (associated ? "associated" : "unavailable"))
        if associated {
            #expect((initial["value"] as? [String: Any])?["text"] as? String == "12")
        } else {
            #expect(initial["value"] == nil)
        }
    }
}

@Test
func relativeReadbackEquivalentProjectionsKeepConflictingValuesUnassociated() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { CFHash($0) }, equal: { CFEqual($0, $1) }
    )
    let object = AXUIElementCreateApplication(getpid())
    let selected = readbackRecord(7, object, value: "13")
    let alias = readbackRecord(2, object, value: "14")
    try bindMacProviderObjectIdentities(
        [selected, alias], scope: "mac:\(getpid()):test-generation", registry: registry
    )
    var uptime = 10.0
    let capture = RelativeReadbackDiagnostic(
        requestID: "aliases", phase: "increment", registry: registry, clock: { uptime }
    )
    let snapshot = readbackSnapshot([alias, selected])
    capture.bind(snapshot: snapshot, target: selected)
    uptime = 20
    capture.bindFinal(snapshot: snapshot)
    uptime = 30
    let payload = try readbackPayload(capture)
    #expect(payload["schema_version"] as? Int == 2)
    for (key, time) in [("initial_target", 10.0), ("final_target", 20.0)] {
        let facts = try #require(payload[key] as? [String: Any])
        #expect(facts["status"] as? String == "unavailable")
        #expect(facts["value"] == nil)
        #expect(facts["binding_uptime_s"] as? Double == time)
        #expect(facts["time_basis"] as? String == "snapshot_binding_not_attribute_sample")
        #expect(facts["token_candidate_count"] as? Int == 2)
        let checks = try #require(facts["checks"] as? [String: String])
        #expect(checks["token_candidate_unique"] == "fail")
        #expect(checks["candidate_equal_forward"] == "not_evaluated")
        #expect(checks["action_retained_match"] == "pass")
        #expect(facts["failed_checks"] as? [String] == ["token_candidate_unique"])
        let candidates = try #require(facts["candidates"] as? [[String: Any]])
        #expect(candidates.compactMap { $0["index"] as? Int } == [2, 7])
        #expect(candidates.compactMap { ($0["value"] as? [String: Any])?["text"] as? String } == ["14", "13"])
        for candidate in candidates {
            #expect(candidate["equal_forward"] as? String == "pass")
            #expect(candidate["equal_reverse"] as? String == "pass")
            #expect(candidate["retained_match"] as? String == "pass")
        }
        if key == "initial_target" {
            let record = try #require(facts["selected_record"] as? [String: Any])
            #expect(record["index"] as? Int == 7)
            #expect(record["evidence_scope"] as? String == "selected_record")
            #expect(record["association"] as? String == "not_claimed")
            #expect((record["value"] as? [String: Any])?["text"] as? String == "13")
        } else {
            #expect(facts["selected_record"] == nil)
        }
    }
}

@Test
func relativeReadbackInitialPrerequisitesHaveIndependentReasons() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { CFHash($0) }, equal: { CFEqual($0, $1) }
    )
    let object = AXUIElementCreateApplication(getpid())
    let target = readbackRecord(1, object)
    try bindMacProviderObjectIdentities(
        [target], scope: "mac:\(getpid()):test-generation", registry: registry
    )
    let token = try #require(target.boundProviderObjectPath)
    let synthetic = readbackRecord(1, object, synthetic: true)
    synthetic.bindProviderObjectPath(token)
    let noObject = readbackRecord(1, nil)
    noObject.bindProviderObjectPath(token)
    let unbound = readbackRecord(1, object)
    let different = readbackRecord(1, AXUIElementCreateSystemWide())
    different.bindProviderObjectPath(token)
    let cases: [(AppSnapshot, ElementRecord, String)] = [
        (readbackSnapshot([synthetic]), synthetic, "target_non_synthetic"),
        (readbackSnapshot([noObject]), noObject, "target_native_object_present"),
        (readbackSnapshot([unbound]), unbound, "target_token_present"),
        (readbackSnapshot([target], generation: nil), target, "initial_process_generation_present"),
        (readbackSnapshot([target], window: nil), target, "initial_window_present"),
        (readbackSnapshot([]), target, "selected_record_in_initial_snapshot"),
        (readbackSnapshot([different]), different, "action_retained_match"),
    ]
    for (snapshot, record, expected) in cases {
        let capture = RelativeReadbackDiagnostic(requestID: "guard", phase: "increment", registry: registry)
        capture.bind(snapshot: snapshot, target: record)
        let initial = try #require(try readbackPayload(capture)["initial_target"] as? [String: Any])
        #expect(initial["status"] as? String == "unavailable")
        #expect(initial["value"] == nil)
        #expect((initial["failed_checks"] as? [String])?.contains(expected) == true)
        let selected = try #require(initial["selected_record"] as? [String: Any])
        #expect((selected["value"] as? [String: Any])?["text"] as? String == "12")
    }
}

@Test
func relativeReadbackFinalAssociationRequiresScopePresenceAndEquality() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { CFHash($0) }, equal: { CFEqual($0, $1) }
    )
    let object = AXUIElementCreateApplication(getpid())
    let target = readbackRecord(1, object)
    let final = readbackRecord(8, AXUIElementCreateApplication(getpid()), value: "15")
    try bindMacProviderObjectIdentities(
        [target, final], scope: "mac:\(getpid()):test-generation", registry: registry
    )
    let replacement = readbackRecord(8, AXUIElementCreateSystemWide(), value: "99")
    replacement.bindProviderObjectPath(try #require(target.boundProviderObjectPath))
    let cases: [(AppSnapshot, String?)] = [
        (readbackSnapshot([final], serial: 4), nil),
        (readbackSnapshot([final], generation: "replacement-generation"), "process_scope_matches"),
        (readbackSnapshot([final], pid: getpid() + 1), "process_scope_matches"),
        (readbackSnapshot([final], window: "replacement-window"), "window_scope_matches"),
        (readbackSnapshot([final], generation: nil), "snapshot_process_generation_present"),
        (readbackSnapshot([final], window: nil), "snapshot_window_present"),
        (readbackSnapshot([]), "target_present"),
        (readbackSnapshot([replacement]), "candidate_equal_forward"),
    ]
    for (snapshot, failure) in cases {
        let capture = RelativeReadbackDiagnostic(requestID: "final", phase: "increment", registry: registry)
        capture.bind(snapshot: readbackSnapshot([target]), target: target)
        capture.bindFinal(snapshot: snapshot)
        let facts = try #require(try readbackPayload(capture)["final_target"] as? [String: Any])
        #expect(facts["status"] as? String == (failure == nil ? "associated" : "unavailable"))
        let checks = try #require(facts["checks"] as? [String: String])
        if let failure {
            #expect(checks[failure] == "fail")
            #expect(facts["value"] == nil)
            if checks["process_scope_matches"] == "fail" || checks["window_scope_matches"] == "fail" {
                #expect(checks["action_retained_match"] == "not_evaluated")
                #expect(checks["candidate_retained_match"] == "not_evaluated")
                let candidates = try #require(facts["candidates"] as? [[String: Any]])
                for candidate in candidates {
                    let value = try #require(candidate["value"] as? [String: Any])
                    #expect(value["reason"] as? String == "scope_not_matched")
                    #expect(value["text"] == nil)
                    #expect(candidate["association"] as? String == "not_claimed")
                }
            }
        } else {
            #expect((facts["value"] as? [String: Any])?["text"] as? String == "15")
            #expect(facts["snapshot_serial"] as? Int == 4)
        }
        #expect(facts["selected_record"] == nil)
    }
}

@Test
func relativeReadbackAssociationIsFrozenAtBindNotRecomputedAtEmit() throws {
    var permitEquality = true
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { CFHash($0) }, equal: { permitEquality && CFEqual($0, $1) }
    )
    let target = readbackRecord(1, AXUIElementCreateApplication(getpid()))
    try bindMacProviderObjectIdentities(
        [target], scope: "mac:\(getpid()):test-generation", registry: registry
    )
    let capture = RelativeReadbackDiagnostic(requestID: "bind-time", phase: "increment", registry: registry)
    capture.bind(snapshot: readbackSnapshot([target]), target: target)
    permitEquality = false
    capture.bindFinal(snapshot: readbackSnapshot([target]))
    let payload = try readbackPayload(capture)
    let initial = try #require(payload["initial_target"] as? [String: Any])
    let final = try #require(payload["final_target"] as? [String: Any])
    #expect(initial["status"] as? String == "associated")
    #expect(final["status"] as? String == "unavailable")
    #expect((final["checks"] as? [String: String])?["action_retained_match"] == "fail")
}

@Test
func relativeReadbackSnapshotCandidateAndScalarBoundsAreExplicit() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { CFHash($0) }, equal: { CFEqual($0, $1) }
    )
    let object = AXUIElementCreateApplication(getpid())
    let candidates = (1...6).map { readbackRecord($0, object, value: String($0)) }
    try bindMacProviderObjectIdentities(
        candidates, scope: "mac:\(getpid()):test-generation", registry: registry
    )
    let capture = RelativeReadbackDiagnostic(requestID: "candidate-bound", phase: "increment", registry: registry)
    capture.bind(snapshot: readbackSnapshot(candidates), target: candidates[5])
    capture.bindFinal(snapshot: readbackSnapshot(candidates))
    let payload = try readbackPayload(capture)
    #expect(payload["complete"] as? Bool == false)
    for key in ["initial_target", "final_target"] {
        let facts = try #require(payload[key] as? [String: Any])
        #expect(facts["status"] as? String == "unavailable")
        #expect(facts["token_candidate_count"] as? Int == 6)
        #expect(facts["omitted_candidate_count"] as? Int == 2)
        #expect(facts["candidates_complete"] as? Bool == false)
        #expect(facts["complete"] as? Bool == false)
        #expect((facts["candidates"] as? [[String: Any]])?.compactMap { $0["index"] as? Int } == [1, 2, 3, 4])
    }
    let initial = try #require(payload["initial_target"] as? [String: Any])
    #expect((initial["selected_record"] as? [String: Any])?["index"] as? Int == 6)
    for count in [512, 513] {
        let target = readbackRecord(1, object, value: String(repeating: "\u{00e9}", count: count))
        try bindMacProviderObjectIdentities(
            [target], scope: "mac:\(getpid()):test-generation", registry: registry
        )
        let scalar = RelativeReadbackDiagnostic(requestID: "scalar-bound", phase: "increment", registry: registry)
        scalar.bind(snapshot: readbackSnapshot([target]), target: target)
        scalar.bindFinal(snapshot: readbackSnapshot([target]))
        let payload = try readbackPayload(scalar)
        #expect(payload["complete"] as? Bool == (count == 512))
        for key in ["initial_target", "final_target"] {
            let facts = try #require(payload[key] as? [String: Any])
            #expect(facts["status"] as? String == "associated")
            let value = try #require(facts["value"] as? [String: Any])
            #expect(value["complete"] as? Bool == (count == 512))
            if count == 513 {
                #expect(value["reason"] as? String == "value_oversize")
                #expect(value["text"] == nil)
            }
        }
    }
}

@Test
func relativeReadbackFinalBindingFollowsSuccessfulSerialization() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { CFHash($0) }, equal: { CFEqual($0, $1) }
    )
    let target = readbackRecord(1, AXUIElementCreateApplication(getpid()))
    try bindMacProviderObjectIdentities(
        [target], scope: "mac:\(getpid()):test-generation", registry: registry
    )
    let invalid = readbackSnapshot([readbackRecord(2, AXUIElementCreateSystemWide())])
    for (snapshot, shouldFail) in [(readbackSnapshot([target]), false), (invalid, true)] {
        let capture = RelativeReadbackDiagnostic(requestID: "serialization", phase: "increment", registry: registry)
        capture.bind(snapshot: readbackSnapshot([target]), target: target)
        do {
            _ = try snapshot.providerDictionary()
            capture.bindFinal(snapshot: snapshot)
        } catch {
            capture.finalSnapshotStatus = "failed"
        }
        let final = try #require(try readbackPayload(capture)["final_target"] as? [String: Any])
        if shouldFail {
            #expect(final["reason"] as? String == "not_collected")
            #expect(final["snapshot_status"] as? String == "failed")
            #expect(final["binding_uptime_s"] == nil)
        } else {
            #expect(final["status"] as? String == "associated")
        }
    }
}

@Test
func relativeReadbackEscapedSnapshotDetailsNeverDisplaceActionScalars() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { CFHash($0) }, equal: { CFEqual($0, $1) }
    )
    let object = AXUIElementCreateApplication(getpid())
    let escaped = String(repeating: "\u{0001}", count: 1024)
    for count in [1, 4] {
        let records = (1...count).map { readbackRecord($0, object, value: escaped) }
        try bindMacProviderObjectIdentities(
            records, scope: "mac:\(getpid()):test-generation", registry: registry
        )
        let capture = RelativeReadbackDiagnostic(
            requestID: "snapshot-detail-budget", phase: "increment", registry: registry
        )
        capture.bind(snapshot: readbackSnapshot(records), target: records[0])
        capture.didReadBefore(.number("12"))
        capture.didReadAfter(.number("13"))
        capture.finish("changed", sampled: true, verified: true)
        capture.finalSnapshotStatus = "captured"
        capture.bindFinal(snapshot: readbackSnapshot(records))
        let payload = try readbackPayload(capture)
        #expect(payload["schema_version"] as? Int == 2)
        #expect(payload["reason"] == nil)
        #expect(payload["complete"] as? Bool == false)
        #expect(payload["readback_verified"] as? Bool == true)
        #expect(payload["outcome"] as? String == "changed")
        #expect((payload["before"] as? [String: Any])?["text"] as? String == "12")
        for key in ["decisive_after", "last_after"] {
            #expect((payload[key] as? [String: Any])?["text"] as? String == "13")
        }
        for key in ["initial_target", "final_target"] {
            let facts = try #require(payload[key] as? [String: Any])
            #expect(facts["reason"] as? String == "snapshot_detail_oversize")
            #expect(facts["complete"] as? Bool == false)
            #expect(facts["snapshot_serial"] as? Int == 3)
            #expect(facts["binding_uptime_s"] != nil)
            #expect(facts["value"] == nil && facts["candidates"] == nil)
        }
        #expect((payload["final_target"] as? [String: Any])?["snapshot_status"] as? String == "captured")
    }
}

@Test
func relativeReadbackSnapshotFactsDoNotPublishPrivateIdentityStrings() throws {
    let registry = ProviderObjectIdentityRegistry<AXUIElement>(
        hash: { CFHash($0) }, equal: { CFEqual($0, $1) }
    )
    let generation = "private-generation-must-not-appear"
    let window = "private-window-must-not-appear"
    let target = readbackRecord(1, AXUIElementCreateApplication(getpid()))
    try bindMacProviderObjectIdentities(
        [target], scope: "mac:\(getpid()):\(generation)", registry: registry
    )
    let snapshot = readbackSnapshot([target], generation: generation, window: window)
    let capture = RelativeReadbackDiagnostic(requestID: "private-identities", phase: "increment", registry: registry)
    capture.bind(snapshot: snapshot, target: target)
    capture.bindFinal(snapshot: snapshot)
    let payload = try readbackPayload(capture)
    let json = String(decoding: try JSONSerialization.data(withJSONObject: payload), as: UTF8.self)
    for forbidden in [generation, window, try #require(target.boundProviderObjectPath)] {
        #expect(!json.contains(forbidden))
    }
    for key in ["initial_target", "final_target"] {
        let facts = try #require(payload[key] as? [String: Any])
        #expect(facts["status"] as? String == "associated")
        #expect(facts["token_candidate_count"] as? Int == 1)
        #expect((facts["checks"] as? [String: String])?["process_scope_matches"] == "pass")
        #expect((facts["checks"] as? [String: String])?["window_scope_matches"] == "pass")
    }
}
