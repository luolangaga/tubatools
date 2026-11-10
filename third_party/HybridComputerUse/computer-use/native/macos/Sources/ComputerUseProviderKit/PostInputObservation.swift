import Foundation

struct PostInputObservationError:
    Error,
    LocalizedError,
    InputEmissionReportingError,
    ActionFailureEvidenceReportingError
{
    let underlyingError: Error
    let delivery: InputDeliveryError

    init(
        _ error: Error,
        route: String?,
        readbackSampled: Bool,
        readbackVerified: Bool?
    ) {
        underlyingError = error
        let message = (error as? LocalizedError)?.errorDescription
            ?? String(describing: error)
        delivery = InputDeliveryError(
            message: "Input was emitted, but the final AX observation was unavailable: \(message)",
            inputEventsEmitted: true,
            failureCode: "effect_unknown",
            partialInputPossible: false,
            readbackSampled: readbackSampled,
            readbackVerified: readbackVerified,
            actionRoute: route
        )
    }

    var errorDescription: String? { delivery.errorDescription }
    var inputEventsEmitted: Bool { delivery.inputEventsEmitted }
    var failureCode: String? { delivery.failureCode }
    var partialInputPossible: Bool? { delivery.partialInputPossible }
    var readbackSampled: Bool { delivery.readbackSampled }
    var readbackVerified: Bool? { delivery.readbackVerified }
    var actionRoute: String? { delivery.actionRoute }
}

func postInputObservationFailureResult(
    _ error: Error,
    evidence: [String: Any]? = nil
) -> [String: Any] {
    let failure = PostInputObservationError(
        error,
        route: evidence?["route"] as? String,
        readbackSampled: evidence?["readback_sampled"] as? Bool ?? false,
        readbackVerified: evidence?["readback_verified"] as? Bool
    )
    var result = runtimeFailureResult(failure)
    result["failurePhase"] = "observation"
    result["underlyingFailureCode"] = "post_action_snapshot_unavailable"
    result["failureFactsComplete"] = false
    if let evidence {
        result["actionEvidence"] = evidence
    }
    if requiresProviderReset(failure.underlyingError) {
        result["providerResetRequired"] = true
    }
    return result
}
