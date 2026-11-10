@testable import ComputerUseProviderKit
import Foundation
import Testing

private func predicateFixture() -> ([String: Any], [String: Any]) {
    let target: [String: Any] = [
        "providerObjectPath": "ax:predicate-test", "providerIdentityScope": "process_instance",
        "runtimeId": [0, 3], "runtimeAliases": [[0, 4]], "role": "spinbutton",
        "automationId": "private-id", "hasName": true, "name": "private-name",
        "providerWindowBusName": "mac:1", "providerWindowObjectPath": "window:one",
        "providerWindowProcessGeneration": "generation",
    ]
    var candidate = target
    candidate["controlType"] = "spinbutton"
    candidate["index"] = 7
    return (target, candidate)
}

private func mismatchFacts(
    target: [String: Any], candidates: [[String: Any]]
) throws -> [String: Any] {
    var facts: [String: Any]?
    var callbacks = 0
    var baselineError = ""
    do {
        _ = try resolveProviderElementIndex(
            elements: candidates, target: target, targetLabel: "target"
        )
        Issue.record("Expected unchanged baseline refusal")
    } catch {
        baselineError = String(describing: error)
    }
    do {
        _ = try resolveProviderElementIndex(
            elements: candidates, target: target, targetLabel: "target",
            onProviderMismatch: { makeFacts in
                callbacks += 1
                facts = makeFacts()
            }
        )
        Issue.record("Expected diagnostic refusal")
    } catch {
        #expect(String(describing: error) == baselineError)
    }
    #expect(callbacks == 1)
    return try #require(facts)
}

@Test
func providerPredicateDiagnosticIdentifiesEachEnforcedMismatch() throws {
    let (target, candidate) = predicateFixture()
    let changes: [(String, Any, String)] = [
        ("runtimeId", [0, 99], "runtime_matches"),
        ("controlType", "button", "role_matches"),
        ("automationId", "changed-private-id", "label_matches"),
        ("providerWindowBusName", "mac:2", "window_matches"),
        ("providerWindowObjectPath", "window:two", "window_matches"),
        ("providerWindowProcessGeneration", "changed", "window_matches"),
    ]
    for (key, value, failed) in changes {
        var changed = candidate
        changed[key] = value
        let facts = try mismatchFacts(target: target, candidates: [changed])
        #expect(facts["provider_match_count"] as? Int == 1)
        #expect(facts["exact_match_count"] as? Int == 0)
        let samples = try #require(facts["candidates"] as? [[String: Any]])
        for predicate in ["runtime_matches", "role_matches", "window_matches", "label_matches"] {
            #expect(samples[0][predicate] as? Bool == (predicate != failed))
        }
        let data = try JSONSerialization.data(withJSONObject: facts)
        let text = try #require(String(data: data, encoding: .utf8))
        #expect(!text.contains("private-"))
    }
}

@Test
func providerPredicateDiagnosticRespectsNameVersusAutomationID() throws {
    var (target, candidate) = predicateFixture()
    candidate["name"] = "changed-private-name"
    #expect(try resolveProviderElementIndex(
        elements: [candidate], target: target, targetLabel: "target",
        onProviderMismatch: { _ in Issue.record("Successful match emitted mismatch") }
    ) == 7)
    target["automationId"] = ""
    let facts = try mismatchFacts(target: target, candidates: [candidate])
    #expect(facts["name_enforced"] as? Bool == true)
    #expect(facts["automation_id_enforced"] as? Bool == false)
    let samples = try #require(facts["candidates"] as? [[String: Any]])
    #expect(samples[0]["name_equal"] as? Bool == false)
    #expect(samples[0]["label_matches"] as? Bool == false)
}

@Test
func providerPredicateDiagnosticPreservesAliasAndUnenforcedSemantics() throws {
    var (target, candidate) = predicateFixture()
    candidate["runtimeId"] = [0, 4]
    candidate["automationId"] = "changed"
    var facts = try mismatchFacts(target: target, candidates: [candidate])
    var samples = try #require(facts["candidates"] as? [[String: Any]])
    #expect(samples[0]["runtime_matches"] as? Bool == true)
    target.removeValue(forKey: "runtimeId")
    target.removeValue(forKey: "runtimeAliases")
    target.removeValue(forKey: "role")
    target.removeValue(forKey: "providerWindowObjectPath")
    candidate["runtimeId"] = [99]
    candidate["controlType"] = "button"
    candidate["providerWindowObjectPath"] = "window:other"
    facts = try mismatchFacts(target: target, candidates: [candidate])
    samples = try #require(facts["candidates"] as? [[String: Any]])
    #expect(facts["runtime_enforced"] as? Bool == false)
    #expect(facts["role_enforced"] as? Bool == false)
    #expect(samples[0]["runtime_matches"] as? Bool == true)
    #expect(samples[0]["role_matches"] as? Bool == true)
    #expect(samples[0]["window_matches"] as? Bool == true)
    #expect(samples[0]["providerWindowObjectPath_enforced"] as? Bool == false)
}

@Test
func providerPredicateDiagnosticBoundsPayloadWithoutChangingRefusal() throws {
    var (target, candidate) = predicateFixture()
    target["runtimeId"] = Array(repeating: 1, count: 65)
    target["runtimeAliases"] = Array(repeating: Array(repeating: 2, count: 65), count: 9)
    candidate["runtimeId"] = Array(repeating: 3, count: 65)
    let candidates = (0..<6).map { index -> [String: Any] in
        var value = candidate
        value["index"] = index
        return value
    }
    let facts = try mismatchFacts(target: target, candidates: candidates)
    #expect(facts["provider_match_count"] as? Int == 6)
    #expect(facts["exact_match_count"] as? Int == 0)
    #expect(facts["truncated"] as? Bool == true)
    #expect((facts["target_runtime"] as? [Int])?.count == 64)
    #expect((facts["target_aliases"] as? [[Int]])?.count == 8)
    let samples = try #require(facts["candidates"] as? [[String: Any]])
    #expect(samples.count == 4)
    #expect(samples.allSatisfy { ($0["runtime_path"] as? [Int])?.count == 64 })
    #expect(try JSONSerialization.data(withJSONObject: facts).count < 16_384)
}

@Test
func providerPredicateDiagnosticDoesNotReplaceMissingOrAmbiguousCases() {
    let (target, candidate) = predicateFixture()
    for candidates in [[], [candidate, candidate]] {
        #expect(throws: ComputerUseError.self) {
            _ = try resolveProviderElementIndex(
                elements: candidates, target: target, targetLabel: "target",
                onProviderMismatch: { _ in Issue.record("Wrong diagnostic branch") }
            )
        }
    }
}
