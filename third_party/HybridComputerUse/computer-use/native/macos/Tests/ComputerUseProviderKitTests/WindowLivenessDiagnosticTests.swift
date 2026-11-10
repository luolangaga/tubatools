import Foundation
@testable import ComputerUseProviderKit
import Testing

@Test
func windowLivenessDiagnosticKeepsOnlyBoundedMetadata() throws {
    let json = try #require(windowLivenessDiagnosticJSON(
        event: "inventory_result", requestID: "req-17", phase: "ordered_final",
        facts: [
            "ax_count": 0, "cg_count": 1, "native_comparable": false,
            "result": "unknown", "title": "private document",
            "text": "private input", "window_id": 117,
            "tree": ["private"], "handle": "private identity",
        ]
    ))
    let record = try #require(
        JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any]
    )
    #expect(record["ax_count"] as? Int == 0)
    #expect(record["cg_count"] as? Int == 1)
    #expect(record["native_comparable"] as? Bool == false)
    #expect(record["result"] as? String == "unknown")
    #expect(record["request_id"] as? String == "req-17")
    for key in ["title", "text", "window_id", "tree", "handle"] {
        #expect(record[key] == nil)
    }
    #expect(!json.contains("private"))
}

@Test
func windowLivenessDiagnosticBoundsLabelsAndRejectsWrongFactTypes() throws {
    let long = String(repeating: "x", count: 5000)
    let json = try #require(windowLivenessDiagnosticJSON(
        event: long, requestID: long, phase: long,
        facts: ["ax_count": "private", "result": long]
    ))
    let record = try #require(
        JSONSerialization.jsonObject(with: Data(json.utf8)) as? [String: Any]
    )
    #expect((record["event"] as? String)?.count == 64)
    #expect((record["request_id"] as? String)?.count == 128)
    #expect((record["phase"] as? String)?.count == 64)
    #expect(record["ax_count"] == nil)
    #expect(json.utf8.count <= 2048)
}

@Test
func windowLivenessDiagnosticErrorKindDoesNotLeakErrorText() {
    #expect(windowLivenessErrorKind(
        ComputerUseError.stateUnavailable("private document title")
    ) == "state_unavailable")
    #expect(windowLivenessErrorKind(
        ComputerUseError.providerResetRequired("private path")
    ) == "provider_reset_required")
    #expect(windowLivenessErrorKind(
        ComputerUseError.staleWindowRef("private handle")
    ) == "stale_window_ref")
}
