@testable import ComputerUseProviderKit
import Foundation
import Testing

private func sharedProviderFixtureData(_ name: String) throws -> Data {
    var root = URL(fileURLWithPath: #filePath)
    for _ in 0..<5 {
        root.deleteLastPathComponent()
    }
    return try Data(contentsOf: root.appendingPathComponent("testdata/provider-wire/\(name)"))
}

private func sharedProviderFixture(_ name: String) throws -> [String: Any] {
    try #require(
        JSONSerialization.jsonObject(with: sharedProviderFixtureData(name)) as? [String: Any]
    )
}

@Test
func sharedProviderRequestsPassNativeValidation() throws {
    for manifestName in ["manifest.json", "native-manifest.json"] {
        let manifest = try sharedProviderFixture(manifestName)
        #expect(manifest["protocol"] as? String == "computer-use-provider")
        #expect(manifest["schema_version"] as? Int == 2)
        if manifestName == "native-manifest.json" {
            let platforms = try #require(manifest["ordered_input_platforms"] as? [String: Bool])
            #expect(platforms["darwin"] == true)
        }
        let fixtures = try #require(manifest["fixtures"] as? [[String: Any]])
        for fixture in fixtures where fixture["direction"] as? String == "request" {
            let file = try #require(fixture["file"] as? String)
            try ProviderRequestValidator.validate(sharedProviderFixture(file))
        }
    }
}

@Test
func sharedOptionalResponsesPreserveNativeWireContract() async throws {
    let manifest = try sharedProviderFixture("native-manifest.json")
    let fixtures = try #require(manifest["fixtures"] as? [[String: Any]])
    var responseCount = 0
    for fixture in fixtures where fixture["direction"] as? String == "response" {
        let file = try #require(fixture["file"] as? String)
        let requestFile = try #require(fixture["request_file"] as? String)
        var expected = try sharedProviderFixture(file)
        var request = try sharedProviderFixture(requestFile)
        if expected["request_rejection"] != nil {
            // The Go core owns the contract rule this fixture breaks. Break a
            // rule this provider owns instead; the rejection must carry the
            // same typed facts.
            var orderedInput = try #require(request["ordered_input"] as? [String: Any])
            orderedInput["steps"] = [["action_index": 2, "key": "NOT_A_REAL_KEY"]]
            request["ordered_input"] = orderedInput
        }
        let payload = try #require(request["ordered_input"] as? [String: Any])
        var runtimeResult = expected["ordered_input"] as? [String: Any] ?? [:]
        if let reset = expected["provider_reset_required"] {
            runtimeResult["providerResetRequired"] = reset
        }
        var dispatched: [[String: Any]] = []
        let server = ProviderServer(
            transport: ProviderLineTransport(inputDescriptor: -1, outputDescriptor: -1),
            executeRuntime: { operation in
                dispatched.append(operation)
                return runtimeResult
            }
        )
        let line = String(decoding: try JSONSerialization.data(withJSONObject: request), as: UTF8.self)
        let response = await server.handle(line: line)
        if var rejection = expected["request_rejection"] as? [String: Any] {
            #expect(dispatched.isEmpty)
            do {
                try ProviderRequestValidator.validate(request)
                Issue.record("Expected shared invalid request to fail validation")
            } catch {
                let message = (error as? LocalizedError)?.errorDescription ?? String(describing: error)
                #expect(message.contains("unsupported"))
                // Error wording is platform-specific; all typed facts must agree.
                rejection["message"] = message
                expected["request_rejection"] = rejection
            }
        } else {
            try #require(dispatched.count == 1)
            let operation = dispatched[0]
            #expect(operation["tool"] as? String == "ordered_keypress")
            #expect(operation["_diagnostic_request_id"] as? String == request["request_id"] as? String)
            let adapted = try #require(operation["orderedInput"] as? [String: Any])
            #expect(NSDictionary(dictionary: adapted).isEqual(to: payload))
        }
        #expect(NSDictionary(dictionary: response).isEqual(to: expected))
        responseCount += 1
    }
    #expect(responseCount == 4)
}

@Test
func sharedInvalidRequestsGetOneClearAnswer() async throws {
    // The Go core rejects each of these requests before it sends one. If one
    // reaches this provider anyway, the provider must answer it without
    // trapping: with a request rejection when the request breaks an envelope,
    // shape or platform check of this provider, otherwise with one dispatch.
    let corpus = try #require(
        JSONSerialization.jsonObject(
            with: sharedProviderFixtureData("invalid-requests.json")
        ) as? [[String: Any]]
    )
    #expect(!corpus.isEmpty)
    var rejected: [String] = []
    for fixture in corpus {
        let name = try #require(fixture["name"] as? String)
        let request = try #require(fixture["request"] as? [String: Any])
        var dispatches = 0
        let server = ProviderServer(
            transport: ProviderLineTransport(inputDescriptor: -1, outputDescriptor: -1),
            executeRuntime: { _ in
                dispatches += 1
                return ["ok": false, "error": "runtime rejected the request"]
            }
        )
        let line = String(decoding: try JSONSerialization.data(withJSONObject: request), as: UTF8.self)
        let response = await server.handle(line: line)
        #expect(response["request_id"] as? String == request["request_id"] as? String, "\(name)")
        if let rejection = response["request_rejection"] as? [String: Any] {
            #expect(dispatches == 0, "\(name)")
            #expect(rejection["phase"] as? String == "request_validation", "\(name)")
            rejected.append(name)
        } else {
            #expect(dispatches == 1, "\(name)")
        }
    }
    // Dispatch reads a window action's target without a fallback.
    #expect(rejected.contains("window action rejects missing target"))
}
