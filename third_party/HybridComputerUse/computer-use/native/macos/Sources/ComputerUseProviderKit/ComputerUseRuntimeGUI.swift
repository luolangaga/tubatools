import CoreGraphics
import Foundation

extension ComputerUseRuntimeBridge {
    func executeGUI(_ operation: [String: Any]) throws -> [String: Any] {
        let actions = operation["actions"] as? [[String: Any]] ?? []
        if actions.count == 1, string(actions[0]["type"]).lowercased() == "zoom" {
            return executeDesktopZoom(actions[0], frameID: string(operation["frame_id"]))
        }
        let nativeBounds = desktopBounds()
        let deliveredBounds = desktopDeliveredBounds(nativeBounds)
        do {
            try actions.forEach {
                try validateGUIAction($0, desktopBounds: deliveredBounds)
            }
        } catch {
            let message = (error as? LocalizedError)?.errorDescription
                ?? String(describing: error)
            return [
                "ok": false,
                "error": "GUI BATCH REJECTED BEFORE INPUT: \(message). No input was emitted.",
                "failureCode": "invalid_gui_action",
                "failurePhase": "preflight",
                "input_events_emitted": false,
                "partial_input_possible": false,
                "failureFactsComplete": true,
            ]
        }
        if actions.contains(where: {
            !["screenshot", "wait"].contains(
                string($0["type"]).lowercased()
            )
        }),
        !PermissionDiagnostics.current().eventPostingGranted
        {
            return [
                "ok": false,
                "error": "GUI BATCH REJECTED BEFORE INPUT: event-posting permission is required. No input was emitted.",
                "failureCode": "permission_denied",
                "failurePhase": "preflight",
                "input_events_emitted": false,
                "partial_input_possible": false,
                "mutation_unknown": false,
                "failureFactsComplete": true,
            ]
        }
        let expectedFrameID = string(operation["frame_id"])
        var emitted = false
        var inputActionsDispatched = 0
        var previousActionType = ""

        for (index, action) in actions.enumerated() {
            let type = string(action["type"]).lowercased()
            if type == "screenshot" {
                continue
            }
            if type == "wait" {
                Thread.sleep(forTimeInterval: 1)
                previousActionType = type
                continue
            }
            if guiNeedsProcessingBarrier(
                previousActionType: previousActionType,
                currentActionType: type
            ) {
                Thread.sleep(forTimeInterval: 0.12)
            }
            if guiActionUsesCoordinates(type) {
                let live = desktopLayout()
                if !expectedFrameID.isEmpty,
                   desktopLayoutFrameID(live) != expectedFrameID
                {
                    var snapshot: [String: Any]?
                    var providerResetRequired = false
                    do {
                        snapshot = try desktopSnapshot()
                    } catch {
                        providerResetRequired = requiresProviderReset(error)
                    }
                    let emissionSummary = emitted
                        ? "Earlier input action(s) were emitted; the stale coordinate action was not emitted."
                        : "No input was emitted."
                    var failure = guiExecutionFailure(
                        error: "STALE GUI FRAME before action \(index) (\(type)): the desktop layout changed since the most recent GUI screenshot. \(emissionSummary)",
                        text: "GUI stopped before emitting the stale coordinate action. Earlier actions, if any, are reflected in the attached full-desktop screenshot.",
                        code: "stale_gui_frame",
                        phase: "precondition",
                        failedActionIndex: index,
                        inputActionsDispatched: inputActionsDispatched,
                        inputEventsEmitted: emitted,
                        partialInputPossible: emitted,
                        mutationUnknown: emitted,
                        snapshot: snapshot
                    )
                    if providerResetRequired {
                        failure["providerResetRequired"] = true
                    }
                    return failure
                }
            }
            do {
                try executeGUIAction(
                    action,
                    deliveredBounds: deliveredBounds,
                    nativeBounds: nativeBounds
                )
            } catch {
                let message = (error as? LocalizedError)?.errorDescription
                    ?? String(describing: error)
                let currentActionEmitted =
                    (error as? InputEmissionReportingError)?
                        .inputEventsEmitted
                    ?? false
                let anyInputEmitted = emitted || currentActionEmitted
                var snapshot: [String: Any]?
                var providerResetRequired = requiresProviderReset(error)
                do {
                    snapshot = try desktopSnapshot()
                } catch {
                    providerResetRequired =
                        providerResetRequired || requiresProviderReset(error)
                }
                var failure = guiExecutionFailure(
                    error: "GUI action \(index) (\(type)) failed: \(message)",
                    text: "GUI stopped at the first action error. Earlier actions, if any, are reflected in the attached full-desktop screenshot.",
                    code: "gui_action_failed",
                    phase: anyInputEmitted ? "execution" : "precondition",
                    failedActionIndex: index,
                    inputActionsDispatched: inputActionsDispatched,
                    inputEventsEmitted: anyInputEmitted,
                    partialInputPossible: true,
                    mutationUnknown: true,
                    snapshot: snapshot
                )
                if providerResetRequired {
                    failure["providerResetRequired"] = true
                }
                return failure
            }
            emitted = true
            inputActionsDispatched += 1
            previousActionType = type
        }

        return guiFinalObservationResult(
            actionCount: actions.count, inputActionsDispatched: inputActionsDispatched,
            emitted: emitted
        ) {
            try sampleGUIStillness(
                settleMS: int(operation["settle_ms"]) ?? 0,
                capture: desktopVisualSample,
                identical: { $0.hasSamePixelsAndLayout(as: $1) }
            )
        }
    }
}

func guiFinalObservationResult(
    actionCount: Int, inputActionsDispatched: Int, emitted: Bool,
    capture: () throws -> GUIStillnessResult<DesktopVisualSample>
) -> [String: Any] {
        do {
            let observation = try capture()
            DesktopPublishedLayouts.shared.remember(observation.sample.snapshot)
            return [
                "ok": true,
                "snapshot": observation.sample.snapshot,
                "guiResult": [
                    "dispatched": actionCount,
                    "input_events_emitted": emitted,
                    "input_actions_dispatched": inputActionsDispatched,
                    "desktop_state_mutated": emitted,
                    "partial_input_possible": false,
                    "effect_unknown": emitted,
                    "mutation_unknown": false,
                    "quiescent": observation.quiescent,
                ],
                "input_events_emitted": emitted,
                "desktop_state_mutated": emitted,
            ]
        } catch {
            let error = normalizedScreenCaptureError(error)
            let message = (error as? LocalizedError)?.errorDescription
                ?? String(describing: error)
            let failureCode: String
            if let computerError = error as? ComputerUseError,
               case .permissionDenied = computerError {
                failureCode = "permission_denied"
            } else {
                failureCode = "gui_snapshot_failed"
            }
            var failure = guiExecutionFailure(
                error: "GUI final desktop observation failed: \(message)",
                text: "GUI actions completed, but the required final full-desktop screenshot could not be captured.",
                code: failureCode,
                phase: emitted ? "observation" : "precondition",
                failedActionIndex: actionCount,
                inputActionsDispatched: inputActionsDispatched,
                inputEventsEmitted: emitted,
                partialInputPossible: emitted,
                mutationUnknown: emitted,
                snapshot: nil
            )
            if requiresProviderReset(error) {
                failure["providerResetRequired"] = true
            }
            return failure
        }
}
