"""Strict provider process boundary for the Linux implementation."""

import json
import sys
import traceback

from . import phase_timing
from . import ui_events
from .a11y_enable import enable_session_accessibility
from .ax_dispatch import perform_provider_request
from .ax_transition_evidence import public_element_record
from .foundation import (
    Atspi,  # pyright: ignore[reportAttributeAccessIssue]
    OperationFailure,
    require_desktop_session,
)
from .response_builder import (
    operation_failure_response,
    unexpected_operation_failure_response,
)
from .wire_request import (
    PROTOCOL,
    ProviderRequestError,
    SCHEMA_VERSION,
    validate_request,
)


# Upper bound for one AT-SPI call; also libatspi's default method timeout.
# An application that does not answer within it is reported as unreadable.
ATSPI_CALL_TIMEOUT_MS = 800
_provider_initialized = False


def _failure(response):
    code = response.get("failureCode")
    underlying = response.get("underlyingFailureCode")
    phase = response.get("failurePhase")
    subject = response.get("failureSubject")
    replacement = response.get("replacementWindow")
    facts_complete = response.get("failureFactsComplete")
    if not any((code, underlying, phase, subject, replacement, facts_complete)):
        return None
    result = {
        "code": code or "",
        "underlying_code": underlying or "",
        "phase": phase or "",
        "facts_complete": bool(facts_complete),
        "subject": subject or "",
    }
    if replacement is not None:
        result["replacement_window"] = replacement
    return result


def _mutation(response):
    result = {
        "input_events_emitted": bool(
            response.get("input_events_emitted")
        ),
        "partial_input_possible": bool(
            response.get("partial_input_possible")
        ),
        "desktop_state_mutated": bool(
            response.get("desktop_state_mutated")
        ),
        "mutation_unknown": bool(response.get("mutation_unknown")),
    }
    return result if any(result.values()) else None


def _common_result(response):
    result: dict[str, object] = {
        "ok": bool(response.get("ok")),
    }
    for source, destination in (
        ("text", "text"),
        ("error", "error"),
        ("notes", "notes"),
        ("snapshot", "snapshot"),
    ):
        if response.get(source) not in (None, "", []):
            result[destination] = response[source]
    failure = _failure(response)
    if failure is not None:
        result["failure"] = failure
    mutation = _mutation(response)
    if mutation is not None:
        result["mutation"] = mutation
    return result


def _accessibility_result(response):
    result = _common_result(response)
    mappings = (
        ("actionEvidence", "action_evidence"),
        ("recoveryContext", "recovery_context"),
        ("apps", "apps"),
        ("inventory_issues", "inventory_issues"),
        ("resolvedElement", "resolved_element"),
        ("candidates", "candidates"),
        ("candidateCount", "candidate_count"),
        ("candidatesTruncated", "candidates_truncated"),
        ("externalDesktopTransition", "external_desktop_transition"),
        ("observationSkipped", "observation_skipped"),
    )
    for source, destination in mappings:
        value = response.get(source)
        if value not in (None, "", [], False, 0):
            result[destination] = value
    if "resolved_element" in result:
        # Resolver records carry private bookkeeping such as
        # _providerIdentityConflict. The host decodes the wire strictly, so
        # one private key fails the whole response.
        result["resolved_element"] = public_element_record(
            result["resolved_element"]
        )
    return result


def _desktop_result(response):
    result = _common_result(response)
    if response.get("actionEvidence"):
        result["action_evidence"] = response["actionEvidence"]
    gui_result = response.get("guiResult")
    if isinstance(gui_result, dict) and gui_result:
        result["gui_result"] = gui_result
    if response.get("validation_errors"):
        result["validation_errors"] = response["validation_errors"]
    return result


def _window_result(response):
    result = _common_result(response)
    mappings = (
        ("appName", "app_name"),
        ("appPid", "app_pid"),
        ("windowTitles", "window_titles"),
        ("actionEvidence", "action_evidence"),
        ("recoveryContext", "recovery_context"),
        ("externalDesktopTransition", "external_desktop_transition"),
    )
    for source, destination in mappings:
        value = response.get(source)
        if value not in (None, "", [], False, 0):
            result[destination] = value
    return result


def provider_response(request, response, provider_reset_required=False):
    operation = request["operation"]
    envelope = {
        "protocol": PROTOCOL,
        "schema_version": SCHEMA_VERSION,
        "request_id": request["request_id"],
        "operation": operation,
    }
    if provider_reset_required:
        envelope["provider_reset_required"] = True
    if operation in {"observe_apps", "observe", "resolve_and_act"}:
        envelope["accessibility"] = _accessibility_result(response)
    elif operation == "desktop_execute_batch":
        envelope["desktop"] = _desktop_result(response)
    elif operation in {"window_list", "window_action"}:
        envelope["window"] = _window_result(response)
    else:
        raise ValueError("unsupported provider operation {!r}".format(operation))
    return envelope


def provider_boundary_failure(
    request, code, message, phase, provider_reset_required=False
):
    operation = (
        request.get("operation")
        if isinstance(request, dict)
        else "observe_apps"
    )
    if operation not in {
        "observe_apps",
        "observe",
        "resolve_and_act",
        "desktop_execute_batch",
        "window_list",
        "window_action",
    }:
        operation = "observe_apps"
    response = {
        "ok": False,
        "error": message,
        "failureCode": code,
        "failurePhase": phase,
        "failureFactsComplete": True,
        "input_events_emitted": False,
        "partial_input_possible": False,
        "mutation_unknown": False,
    }
    request_id = (
        request.get("request_id")
        if isinstance(request, dict)
        else None
    ) or "startup"
    envelope_request = {
        "operation": operation,
        "request_id": request_id,
    }
    return provider_response(
        envelope_request,
        response,
        provider_reset_required=provider_reset_required,
    )


def _write_operator_exception(request, stage):
    diagnostic = {
        "kind": "linux_provider_diagnostic",
        "request_id": (
            request.get("request_id")
            if isinstance(request, dict)
            else "startup"
        ) or "startup",
        "operation": (
            request.get("operation")
            if isinstance(request, dict)
            else "unknown"
        ) or "unknown",
        "stage": stage,
        "diagnostic": traceback.format_exc(),
    }
    print(
        json.dumps(diagnostic, separators=(",", ":")),
        file=sys.stderr,
        flush=True,
    )


def _initialize_provider(request):
    global _provider_initialized
    if _provider_initialized:
        return
    try:
        require_desktop_session()
        Atspi.init()
        # libatspi waits up to app_startup_time (15 s by default) instead of
        # the method timeout for any application it first saw less than that
        # long ago. The clock starts when this process first sees the
        # application, not when the application started, so after every
        # provider restart one call to a busy application blocked for 15 s.
        Atspi.set_timeout(ATSPI_CALL_TIMEOUT_MS, ATSPI_CALL_TIMEOUT_MS)
    except Exception:
        _write_operator_exception(request, "startup")
        raise
    # Users start applications without accessibility flags, and most
    # toolkits decide at startup whether to export a tree. Switching the
    # session on here makes every application started from now on visible;
    # a session that already has it on sees only reads. A failure leaves
    # GTK 3 applications and GUI working, so it is recorded, not raised.
    try:
        facts = enable_session_accessibility()
    except Exception:
        _write_operator_exception(request, "accessibility_enable")
    else:
        if facts["changed"] or facts["errors"]:
            print(
                json.dumps({
                    "kind": "linux_provider_diagnostic",
                    "request_id": "startup",
                    "operation": "accessibility_enable",
                    "stage": "startup",
                    "diagnostic": facts,
                }, separators=(",", ":")),
                file=sys.stderr,
                flush=True,
            )
    # Without the event monitor the provider still works: every observe
    # walks the tree and the settle wait uses its fixed floor.
    ui_events.start()
    _provider_initialized = True


def execute(request):
    validate_request(request)
    return _execute_validated(request)


def _execute_validated(request):
    phase_timing.start()
    try:
        return _execute(request)
    finally:
        phase_timing.finish(request["operation"], request["request_id"])


def _execute(request):
    operation = request["operation"]
    payload_key = {
        "observe_apps": "observe_apps",
        "observe": "observe",
        "resolve_and_act": "action",
        "desktop_execute_batch": "desktop",
        "window_list": "window_list",
        "window_action": "window_action",
    }[operation]
    try:
        response = perform_provider_request(operation, request[payload_key])
    except OperationFailure as exc:
        response = operation_failure_response(
            exc,
            recovery_operation=(
                request[payload_key]
                if operation in {"observe", "resolve_and_act", "window_action"}
                else None
            ),
        )
        return provider_response(
            request,
            response,
            provider_reset_required=exc.provider_reset_required,
        )
    except Exception as exc:
        _write_operator_exception(request, "dispatch")
        response = unexpected_operation_failure_response(
            exc,
            recovery_operation=(
                request[payload_key]
                if operation in {"resolve_and_act", "window_action"}
                else None
            ),
        )
        return provider_response(
            request,
            response,
            provider_reset_required=True,
        )
    return provider_response(request, response)


def _execute_serialized_request(raw):
    request = None
    try:
        request = json.loads(raw)
        validate_request(request)
    except ProviderRequestError as exc:
        _write_operator_exception(request, "precondition")
        return provider_boundary_failure(
            request,
            exc.code,
            str(exc),
            "precondition",
        )
    except (json.JSONDecodeError, TypeError, ValueError):
        _write_operator_exception(request, "precondition")
        return provider_boundary_failure(
            request,
            "invalid_provider_request",
            "invalid_provider_request",
            "precondition",
        )
    try:
        _initialize_provider(request)
    except Exception:
        return provider_boundary_failure(
            request,
            "runtime_startup_failed",
            "provider_startup_failed",
            "startup",
            provider_reset_required=True,
        )
    try:
        return _execute_validated(request)
    except Exception:
        _write_operator_exception(request, "dispatch")
        return provider_boundary_failure(
            request,
            "input_failed",
            "provider_dispatch_failed",
            "dispatch",
            provider_reset_required=True,
        )


def serve_stdio(stdin=None, stdout=None):
    stdin = stdin or sys.stdin
    stdout = stdout or sys.stdout
    for raw in stdin:
        if not raw.strip():
            continue
        response = _execute_serialized_request(raw)
        stdout.write(json.dumps(response, separators=(",", ":")) + "\n")
        stdout.flush()


def main():
    if len(sys.argv) == 2 and sys.argv[1] == "--stdio":
        serve_stdio()
        return
    if len(sys.argv) != 2:
        try:
            raise RuntimeError(
                "runtime.py requires a provider request path or --stdio"
            )
        except RuntimeError:
            request = None
            _write_operator_exception(request, "startup")
        response = provider_boundary_failure(
            request,
            "runtime_startup_failed",
            "provider_startup_failed",
            "startup",
        )
    else:
        try:
            with open(sys.argv[1], "r", encoding="utf-8") as file:
                raw = file.read()
        except OSError:
            request = None
            _write_operator_exception(request, "startup")
            response = provider_boundary_failure(
                request,
                "runtime_startup_failed",
                "provider_startup_failed",
                "startup",
            )
        else:
            response = _execute_serialized_request(raw)
    print(json.dumps(response, separators=(",", ":")))


if __name__ == "__main__":
    main()
