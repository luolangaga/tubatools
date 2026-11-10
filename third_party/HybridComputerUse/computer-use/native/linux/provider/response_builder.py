"""Typed provider failure responses for the Linux provider boundary."""

from . import action_evidence
from .input_primitives import InputEmissionError
from .recovery import bounded_recovery_context


def _bounded_recovery_response(operation):
    try:
        return bounded_recovery_context(operation)
    except Exception:
        return {
            "sourceAppStatus": "unknown",
            "sourceWindowStatus": "unknown",
            "inventoryComplete": False,
        }


def operation_failure_response(exc, recovery_operation=None):
    evidence = action_evidence.snapshot()
    emitted = bool(
        exc.input_events_emitted or evidence.get("input_emitted")
    )
    partial = bool(getattr(exc, "partial_input_possible", False))
    facts_complete = bool(exc.failure_facts_complete)
    if emitted != bool(exc.input_events_emitted):
        facts_complete = False
    failure_code = exc.failure_code
    failure_phase = exc.failure_phase
    error_text = str(exc)
    underlying_failure_code = ""
    # A known operation failure already describes its emitted input. Only
    # override a no-input claim contradicted by operation-scoped evidence or
    # by possible partial input.
    if (emitted or partial) and not exc.input_events_emitted:
        underlying_failure_code = failure_code
        failure_code = "effect_unknown"
        failure_phase = "post_action_observation"
        error_text = (
            "post_action_observation_failed; input was emitted; "
            "the final effect is unknown." if emitted else
            "post_action_observation_failed; input may have been emitted; "
            "the final effect is unknown."
        )
        facts_complete = False
    response = {
        "ok": False,
        "error": error_text,
        "failureCode": failure_code,
        "failurePhase": failure_phase,
        "input_events_emitted": emitted,
        "partial_input_possible": partial,
        "mutation_unknown": emitted or partial,
        "failureFactsComplete": facts_complete,
    }
    if underlying_failure_code:
        response["underlyingFailureCode"] = underlying_failure_code
    if exc.failure_subject:
        response["failureSubject"] = exc.failure_subject
    if exc.replacement_window:
        response["replacementWindow"] = exc.replacement_window
    read_only_stale_window = bool(
        not emitted and not partial
        and failure_code == "stale_window_ref"
        and failure_phase == "precondition"
        and facts_complete
    )
    observation_failure = bool(
        emitted and failure_code == "effect_unknown"
        and failure_phase == "post_action_observation"
    )
    if recovery_operation is not None and (observation_failure or read_only_stale_window):
        response["recoveryContext"] = _bounded_recovery_response(
            recovery_operation
        )
    if evidence:
        evidence["input_emitted"] = bool(
            evidence.get("input_emitted") or emitted
        )
        response["actionEvidence"] = evidence
    if exc.candidates:
        response["candidates"] = exc.candidates
        response["candidateCount"] = exc.candidate_count
        response["candidatesTruncated"] = (
            exc.candidate_count > len(exc.candidates)
        )
    return response


def unexpected_operation_failure_response(exc, recovery_operation=None):
    evidence = action_evidence.snapshot()
    emitted = bool(
        evidence.get("input_emitted")
        or getattr(exc, "events_emitted", False)
    )
    partial = bool(getattr(exc, "partial_input_possible", False))
    unknown = emitted or partial
    if evidence:
        evidence["input_emitted"] = emitted
    response = {
        "ok": False,
        "error": (
            "effect_unknown: an unexpected accessibility runtime failure "
            "occurred after input; inspect fresh state before retrying."
            if emitted
            else "effect_unknown: input may have been emitted before an "
            "accessibility runtime failure; inspect fresh state before retrying."
            if partial
            else "input_failed: the accessibility runtime failed before input."
        ),
        "failureCode": "effect_unknown" if unknown else "input_failed",
        "failurePhase": "post_action_observation" if unknown else "emit",
        "input_events_emitted": emitted,
        "partial_input_possible": bool(
            unknown or isinstance(exc, InputEmissionError)
        ),
        "mutation_unknown": unknown,
        "failureFactsComplete": False,
    }
    if emitted:
        if recovery_operation is not None:
            response["recoveryContext"] = _bounded_recovery_response(
                recovery_operation
            )
    if evidence:
        response["actionEvidence"] = evidence
    return response
