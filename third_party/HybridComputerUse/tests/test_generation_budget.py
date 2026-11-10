"""Local fake-upstream tests. No model services or OSWorld installation."""

from concurrent.futures import ThreadPoolExecutor
from contextlib import contextmanager
import json
import threading
import urllib.request

import pytest

from hcu.gateway import anthropic_proxy, common, gemini_proxy, openai_proxy
from hcu.osworld.usage import collect_task_proxy_usage, read_task_log
from test_gateways import FakeResponse, StaticToken, log_rows, post, serve


PATHS = {
    "azure": "/v1/responses",
    "grok": "/v1/responses",
    "anthropic": "/v1/messages",
    "gemini": "/v1beta/models/gemini:streamGenerateContent",
}


def success_response(kind, stream=False):
    if kind in {"azure", "grok"}:
        payload = {"id": "r", "status": "completed", "output": []}
        event = {"type": "response.completed", "response": payload}
    elif kind == "anthropic":
        payload = {"id": "m", "type": "message", "stop_reason": "tool_use", "content": []}
        event = {"type": "message_stop"}
    else:
        payload = {"candidates": [{"finishReason": "STOP", "content": {"parts": []}}]}
        event = payload
    if stream:
        return FakeResponse(200, b"data: " + json.dumps(event).encode() + b"\n\n",
                            "text/event-stream")
    return FakeResponse(200, json.dumps(payload).encode())


class SequenceUpstream:
    def __init__(self, responses):
        self.responses = iter(responses)
        self.calls = []

    def request(self, *args, **kwargs):
        self.calls.append((args, kwargs))
        response = next(self.responses)
        if isinstance(response, Exception):
            raise response
        return response

    urlopen = request


@contextmanager
def gateway_server(tmp_path, kind, responses, limit=2):
    token_log = common.TokenLog(tmp_path / "tokens.jsonl")
    upstream = SequenceUpstream(responses)
    if kind in {"azure", "grok"}:
        gateway = openai_proxy.OpenAIGateway(
            upstreams=["fake.invalid"], model_upstreams={}, token_log=token_log,
            max_requests_per_task=limit, token=StaticToken(),
            upstream_kind="openai" if kind == "grok" else "azure",
        )
        gateway.pool = lambda _host: upstream
        handler = openai_proxy.make_handler(gateway)
    else:
        cls = anthropic_proxy.AnthropicGateway if kind == "anthropic" else gemini_proxy.GeminiGateway
        gateway = cls(
            upstream_base="https://fake.invalid", upstream_token="fake",
            token_log=token_log, max_requests_per_task=limit, pool=upstream,
        )
        handler = common.make_passthrough_handler(gateway)
    server, base = serve(handler)
    try:
        yield gateway, upstream, base, "/t/task-test" + PATHS[kind]
    finally:
        server.shutdown()
        server.server_close()
        token_log.close()


@pytest.mark.parametrize("kind", PATHS)
@pytest.mark.parametrize("stream", [False, True])
def test_errors_retry_without_consuming_generation_budget(tmp_path, kind, stream):
    responses = [
        FakeResponse(503, b'{"error":"unavailable"}'),
        RuntimeError("connection failed"),
        success_response(kind, stream),
        success_response(kind, stream),
    ]
    with gateway_server(tmp_path, kind, responses) as (gateway, upstream, base, path):
        assert [post(base, path, {}) for _ in range(5)] == [503, 502, 200, 200, 429]
    rows = log_rows(gateway.token_log.path)
    assert len(upstream.calls) == 4
    assert [row.get("model_step_completed", False) for row in rows] == [
        False, False, True, True, False,
    ]
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 2
    assert not read_task_log(gateway.token_log.path, "task-test").retry_exhausted
    # The fake records byte-for-byte bodies, including retries.
    assert all(call[1]["body"] == b"{}" for call in upstream.calls)


@pytest.mark.parametrize("kind", PATHS)
def test_failed_or_unfinished_200_stream_does_not_count(tmp_path, kind):
    unfinished = {
        "azure": {"type": "response.output_text.delta", "delta": "partial"},
        "grok": {"type": "response.failed", "response": {"status": "failed"}},
        "anthropic": {"type": "message_delta", "delta": {"stop_reason": "end_turn"}},
        "gemini": {"candidates": [{"content": {"parts": [{"text": "partial"}]}}]},
    }[kind]
    responses = [
        FakeResponse(200, b"data: " + json.dumps(unfinished).encode() + b"\n\n",
                     "text/event-stream"),
        success_response(kind, True),
    ]
    with gateway_server(tmp_path, kind, responses, limit=1) as (gateway, upstream, base, path):
        assert [post(base, path, {}) for _ in range(3)] == [200, 200, 429]
    rows = log_rows(gateway.token_log.path)
    assert "model_step_completed" not in rows[0]
    assert rows[0]["generation_error"]
    assert rows[1]["model_step_completed"]
    assert len(upstream.calls) == 2


@pytest.mark.parametrize("kind", PATHS)
def test_non_generation_and_get_do_not_consume_budget(tmp_path, kind):
    auxiliary = {
        "azure": "/v1/responses/compact", "grok": "/v1/responses/compact",
        "anthropic": "/v1/messages/count_tokens",
        "gemini": "/v1beta/models/gemini:countTokens",
    }[kind]
    with gateway_server(tmp_path, kind, [success_response(kind)] * 4, limit=1) as (
        gateway, upstream, base, path,
    ):
        assert post(base, "/t/task-test" + auxiliary, {}) == 200
        with urllib.request.urlopen(base + path, timeout=10) as response:
            assert response.status == 200
        assert post(base, path, {}) == 200
        assert post(base, path, {}) == 429
        assert post(base, "/t/task-test" + auxiliary, {}) == 200
    rows = log_rows(gateway.token_log.path)
    assert [row.get("model_step_completed", False) for row in rows] == [
        False, False, True, False, False,
    ]
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 1
    assert len(upstream.calls) == 4
    summary, _ = collect_task_proxy_usage(gateway.token_log.path, "task-test")
    assert summary["request_attempts"] == 5
    assert summary["model_requests"] == 1
    assert summary["non_generation_requests"] == 3
    assert summary["gateway_rejected_requests"] == 1
    assert summary["unclassified_requests"] == 0
    assert summary["completed_model_responses"] == 1


@pytest.mark.parametrize("kind", PATHS)
def test_transport_disconnect_releases_slot(tmp_path, monkeypatch, kind):
    module = openai_proxy if kind in {"azure", "grok"} else common
    original = module.relay_response
    deliveries = iter([False, True])

    def relay(*args):
        original(*args)
        return next(deliveries)

    monkeypatch.setattr(module, "relay_response", relay)
    with gateway_server(tmp_path, kind, [success_response(kind)] * 2, limit=1) as (
        gateway, _upstream, base, path,
    ):
        assert [post(base, path, {}) for _ in range(3)] == [200, 200, 429]
    rows = log_rows(gateway.token_log.path)
    assert rows[0]["client_disconnected"]
    assert "model_step_completed" not in rows[0]
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 1


@pytest.mark.parametrize("kind", PATHS)
def test_failed_retries_have_a_separate_persistent_circuit_breaker(tmp_path, kind):
    limit = common.MAX_CONSECUTIVE_GENERATION_FAILURES
    responses = [FakeResponse(503, b'{"error":"unavailable"}')] * limit
    with gateway_server(tmp_path, kind, responses, limit=10) as (
        gateway, upstream, base, path,
    ):
        assert [post(base, path, {}) for _ in range(limit)] == [503] * limit
        assert post(base, path, {}) == 429
    gateway.restore()
    assert gateway.budget.admit({"task": "task-test"}, True) == "generation_retry_exhausted"
    assert len(upstream.calls) == limit
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 0
    assert read_task_log(gateway.token_log.path, "task-test").retry_exhausted
    assert not log_rows(gateway.token_log.path)[-1].get("budget_rejected")


@pytest.mark.parametrize("kind", PATHS)
def test_success_resets_failure_streak_and_survives_restart(tmp_path, kind):
    failures = [FakeResponse(503, b'{"error":"unavailable"}')] * 4
    responses = failures + [success_response(kind)] + failures + [success_response(kind)]
    with gateway_server(tmp_path, kind, responses) as (gateway, upstream, base, path):
        for _ in range(2):
            assert [post(base, path, {}) for _ in range(5)] == [503] * 4 + [200]
        assert post(base, path, {}) == 429
    gateway.restore()
    assert gateway.budget.admit({"task": "task-test"}, True) == "budget_rejected"
    assert len(upstream.calls) == 10
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 2
    assert not read_task_log(gateway.token_log.path, "task-test").retry_exhausted


@pytest.mark.parametrize("kind", PATHS)
def test_next_requests_wait_until_previous_response_is_finished(tmp_path, monkeypatch, kind):
    # The agent can read a response and send its next requests before the
    # gateway thread that relayed it has committed the outcome. Hold that
    # thread after the first relay until the next generation admission has
    # decided. A correct gateway defers later requests of the task until the
    # outcome is committed, so the hold can only end at its bound. The old
    # order logged the GET first and rejected the generation as in flight.
    module = openai_proxy if kind in {"azure", "grok"} else common
    original = module.relay_response
    next_generation_decided = threading.Event()
    held = []

    def relay(*args):
        delivered = original(*args)
        if not held:
            held.append(next_generation_decided.wait(1))
        return delivered

    monkeypatch.setattr(module, "relay_response", relay)
    responses = [
        FakeResponse(503, b'{"error":"unavailable"}'),
        success_response(kind),
        success_response(kind),
    ]
    with gateway_server(tmp_path, kind, responses, limit=1) as (
        gateway, upstream, base, path,
    ):
        admit = gateway.budget.admit
        generations = []

        def observed_admit(row, generation):
            try:
                return admit(row, generation)
            finally:
                generations.append(generation)
                if generations.count(True) == 2:
                    next_generation_decided.set()

        gateway.budget.admit = observed_admit
        assert post(base, path, {}) == 503
        with urllib.request.urlopen(base + path, timeout=10) as response:
            assert response.status == 200
        assert [post(base, path, {}) for _ in range(2)] == [200, 429]
    assert held == [False]
    assert len(upstream.calls) == 3
    rows = log_rows(gateway.token_log.path)
    assert [(row["method"], row.get("http_status")) for row in rows] == [
        ("POST", 503), ("GET", 200), ("POST", 200), ("POST", 429),
    ]
    assert not any(row.get("budget_inflight") for row in rows)
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 1


@pytest.mark.parametrize("kind", PATHS)
def test_concurrent_last_slot_is_reserved_without_terminal_rejection(tmp_path, kind):
    entered = threading.Event()
    release = threading.Event()
    with gateway_server(tmp_path, kind, [success_response(kind)], limit=1) as (
        gateway, upstream, base, path,
    ):
        original = upstream.request

        def blocked(*args, **kwargs):
            entered.set()
            assert release.wait(5)
            return original(*args, **kwargs)

        upstream.request = upstream.urlopen = blocked
        with ThreadPoolExecutor(max_workers=1) as pool:
            pending = pool.submit(post, base, path, {})
            try:
                assert entered.wait(5)
                assert post(base, path, {}) == 429
                row = log_rows(gateway.token_log.path)[-1]
                assert row["budget_inflight"] and not row.get("budget_rejected")
            finally:
                release.set()
            assert pending.result(timeout=5) == 200
        assert post(base, path, {}) == 429
    assert len(upstream.calls) == 1
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 1


def test_legacy_rows_and_non_object_logs_are_not_delivery_evidence(tmp_path):
    path = tmp_path / "tokens.jsonl"
    path.write_text(
        'null\n[]\n{"task":"task-test","path":"/v1/responses","http_status":200}\n'
        '{"task":"task-other","model_step_completed":true}\n'
        '{"task":"task-test","model_step_completed":true}\n{"partial":'
    )
    assert read_task_log(path, "task-test").completed_responses == 1


@pytest.mark.parametrize("kind", PATHS)
def test_uncapped_gateway_still_marks_successful_generations(tmp_path, kind):
    with gateway_server(tmp_path, kind, [success_response(kind)] * 3, limit=0) as (
        gateway, _upstream, base, path,
    ):
        assert [post(base, path, {}) for _ in range(3)] == [200, 200, 200]
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 3


@pytest.mark.parametrize("kind", PATHS)
def test_malformed_success_body_is_not_a_generation(tmp_path, kind):
    with gateway_server(tmp_path, kind, [
        FakeResponse(200, b'{"broken":'), success_response(kind),
    ], limit=1) as (gateway, _upstream, base, path):
        assert [post(base, path, {}) for _ in range(3)] == [200, 200, 429]
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 1
    assert log_rows(gateway.token_log.path)[0]["generation_error"]


@pytest.mark.parametrize("kind", ["azure", "grok"])
@pytest.mark.parametrize("stream", [False, True])
def test_output_token_limit_is_a_delivered_response(tmp_path, kind, stream):
    payload = {
        "status": "incomplete", "incomplete_details": {"reason": "max_output_tokens"},
        "output": [{"type": "message"}],
    }
    if stream:
        response = FakeResponse(200, b"data: " + json.dumps({
            "type": "response.incomplete", "response": payload,
        }).encode() + b"\n\n", "text/event-stream")
    else:
        response = FakeResponse(200, json.dumps(payload).encode())
    with gateway_server(tmp_path, kind, [response], limit=1) as (
        gateway, _upstream, base, path,
    ):
        assert [post(base, path, {}) for _ in range(2)] == [200, 429]
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 1


def test_openai_auth_failure_releases_reservation(tmp_path):
    with gateway_server(tmp_path, "azure", [success_response("azure")], limit=1) as (
        gateway, upstream, base, path,
    ):
        class FailingToken:
            def get(self):
                raise RuntimeError("expired credential")

        gateway.token = FailingToken()
        assert post(base, path, {}) == 502
        gateway.token = StaticToken()
        assert post(base, path, {}) == 200
        assert post(base, path, {}) == 429
    assert len(upstream.calls) == 1
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 1


def test_real_relay_write_failure_is_not_delivery():
    class BrokenConnection:
        def send_response(self, _status):
            pass

        def send_header(self, *_args):
            pass

        def end_headers(self):
            pass

        @property
        def wfile(self):
            return self

        def write(self, _body):
            raise BrokenPipeError("peer closed")

    assert not common.relay_response(BrokenConnection(), 200, {}, b"response")


def test_retry_circuit_stays_open_after_late_inflight_success(tmp_path):
    budget = common.GenerationBudget(10)
    token_log = common.TokenLog(tmp_path / "tokens.jsonl")
    late = {"task": "task-test"}
    assert budget.admit(late, True) is None
    for _ in range(common.MAX_CONSECUTIVE_GENERATION_FAILURES):
        row = {"task": "task-test"}
        assert budget.admit(row, True) is None
        budget.finish(row, token_log)
    budget.finish(late, token_log, True)
    token_log.close()
    assert budget.admit({"task": "task-test"}, True) == "generation_retry_exhausted"
    budget.restore(common.read_log_rows(token_log.path))
    assert budget.admit({"task": "task-test"}, True) == "generation_retry_exhausted"


def test_accounting_keeps_attempts_separate_from_successful_budget_units(tmp_path):
    rows = [
        {"task": "task-test", "generation_request": True, "http_status": 503},
        {"task": "task-test", "generation_request": True, "model_step_completed": True,
         "input_tokens": 3, "output_tokens": 2, "total_tokens": 5},
        {"task": "task-test", "budget_inflight": True},
        {"task": "task-test", "budget_rejected": True},
        {"task": "task-test", "generation_retry_exhausted": True},
    ]
    path = tmp_path / "tokens.jsonl"
    path.write_text("".join(json.dumps(row) + "\n" for row in rows))
    summary, _ = collect_task_proxy_usage(path, "task-test")
    assert summary["request_attempts"] == 5
    assert summary["model_requests"] == 2
    assert summary["completed_model_responses"] == 1
    assert summary["gateway_rejected_requests"] == 3
    assert summary["total_tokens"] == 5


def test_auxiliary_calls_keep_usage_without_counting_as_model_requests(tmp_path):
    rows = [
        {"task": "task-test", "generation_request": False, "path": "/v1/messages/count_tokens",
         "http_status": 200},
        {"task": "task-test", "generation_request": False, "path": "/v1/responses/compact",
         "input_tokens": 10, "output_tokens": 3, "total_tokens": 13},
        {"task": "task-test", "generation_request": True, "model_step_completed": True,
         "input_tokens": 20, "output_tokens": 5, "total_tokens": 25},
        {"task": "task-other", "generation_request": True, "model_step_completed": True},
    ]
    path = tmp_path / "tokens.jsonl"
    path.write_text("".join(json.dumps(row) + "\n" for row in rows))
    summary, frozen = collect_task_proxy_usage(path, "task-test")
    assert summary["status"] == "completed"
    assert summary["request_attempts"] == 3
    assert summary["model_requests"] == 1
    assert summary["non_generation_requests"] == 2
    assert summary["accounted_responses"] == 2
    assert summary["accounted_model_responses"] == 1
    assert summary["unaccounted_requests"] == 0
    assert summary["completed_model_responses"] == 1
    assert summary["total_tokens"] == 38
    assert len(frozen.splitlines()) == 3


def test_unclassified_records_do_not_become_model_requests_or_complete_coverage(tmp_path):
    path = tmp_path / "tokens.jsonl"
    path.write_text(json.dumps({
        "task": "task-test", "input_tokens": 10, "output_tokens": 2, "total_tokens": 12,
    }) + "\n")
    summary, frozen = collect_task_proxy_usage(path, "task-test")
    assert summary["status"] == "incomplete"
    assert summary["unclassified_requests"] == 1
    assert summary["model_requests"] == summary["non_generation_requests"] == 0
    assert summary["accounted_responses"] == 1
    assert summary["accounted_model_responses"] == 0
    assert summary["total_tokens"] == 12
    assert frozen


def test_gemini_json_array_stream_counts_delivery_and_final_cumulative_usage(tmp_path):
    chunks = [
        {"responseId": "g-array", "candidates": [{"content": {"parts": [{"text": "OK"}]}}],
         "usageMetadata": {"promptTokenCount": 3, "candidatesTokenCount": 1, "totalTokenCount": 4}},
        {"candidates": [{"finishReason": "STOP"}],
         "usageMetadata": {"promptTokenCount": 3, "candidatesTokenCount": 2, "totalTokenCount": 5}},
    ]
    response = FakeResponse(200, json.dumps(chunks).encode())
    with gateway_server(tmp_path, "gemini", [response], limit=1) as (
        gateway, upstream, base, path,
    ):
        assert post(base, path, {}) == 200
        assert post(base, path, {}) == 429
    assert len(upstream.calls) == 1
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 1
    assert not read_task_log(gateway.token_log.path, "task-test").retry_exhausted
    summary, _ = collect_task_proxy_usage(gateway.token_log.path, "task-test")
    assert summary["accounted_model_responses"] == 1
    assert summary["total_tokens"] == 5
    assert log_rows(gateway.token_log.path)[0]["response_id"] == "g-array"


@pytest.mark.parametrize("body", [
    b'[]',
    b'[{"candidates":[{"content":{"parts":[{"text":"partial"}]}}]}]',
    b'[{"candidates":[{"finishReason":"STOP"}]},{"error":{"message":"failed"}}]',
    b'[{"candidates":[{"finishReason":"STOP"}]},null]',
    b'[{"candidates":[{"finishReason":"STOP"}]}',
])
def test_gemini_unsuccessful_arrays_release_the_reservation(tmp_path, body):
    with gateway_server(tmp_path, "gemini", [
        FakeResponse(200, body), success_response("gemini"),
    ], limit=1) as (gateway, upstream, base, path):
        assert [post(base, path, {}) for _ in range(3)] == [200, 200, 429]
    assert len(upstream.calls) == 2
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 1
    assert log_rows(gateway.token_log.path)[0]["generation_error"]


@pytest.mark.parametrize("kind", ["azure", "grok", "anthropic"])
def test_non_gemini_terminal_arrays_are_not_successful_generations(tmp_path, kind):
    terminal = json.loads(success_response(kind).data)
    with gateway_server(tmp_path, kind, [
        FakeResponse(200, json.dumps([terminal]).encode()), success_response(kind),
    ], limit=1) as (gateway, upstream, base, path):
        assert [post(base, path, {}) for _ in range(3)] == [200, 200, 429]
    assert len(upstream.calls) == 2
    assert read_task_log(gateway.token_log.path, "task-test").completed_responses == 1
    rows = log_rows(gateway.token_log.path)
    assert rows[0]["generation_error"]
    assert not rows[0].get("model_step_completed")
