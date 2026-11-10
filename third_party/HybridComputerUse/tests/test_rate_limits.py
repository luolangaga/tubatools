"""Rate limits are absorbed by the gateway and never fail a task.

Fake upstreams only: no model service, no OSWorld checkout.
"""

import json

import pytest

from hcu.gateway import common, openai_proxy
from hcu.osworld import traces
from hcu.osworld.usage import collect_task_proxy_usage, read_task_log
from test_gateways import FakeResponse, StaticToken, log_rows, post, serve
from test_generation_budget import PATHS, gateway_server, success_response


def sse(*events):
    return FakeResponse(
        200, b"".join(b"data: " + json.dumps(event).encode() + b"\n\n" for event in events),
        "text/event-stream",
    )


# What Azure sends when the token rate limit trips mid-conversation: HTTP 200
# and a stream whose only event is an error.
AZURE_STREAM_RATE_LIMIT = sse({
    "type": "error",
    "error": {
        "code": "rate_limit_exceeded",
        "message": "Your requests to gpt-5.4 for gpt-5.4 in swedencentral have exceeded "
                   "token rate limit. Please retry after 7 seconds.",
    },
})


# A stream that starts normally and then fails on the rate limit.
STARTED_THEN_RATE_LIMITED = sse(
    {"type": "response.created", "response": {"id": "resp_x", "status": "in_progress"}},
    {"type": "response.in_progress", "response": {"id": "resp_x", "status": "in_progress"}},
    {"type": "error", "error": {"code": "rate_limit_exceeded",
                                "message": "Rate limit reached for gpt-5.4."}},
)


def http_429(retry_after=None):
    response = FakeResponse(429, b'{"error":{"code":"429","message":"Too Many Requests"}}')
    if retry_after is not None:
        response.headers["retry-after"] = retry_after
    return response


@pytest.fixture
def sleeps(monkeypatch):
    waits = []
    monkeypatch.setattr(common, "sleep", waits.append)
    return waits


@pytest.mark.parametrize("body, status, expected", [
    (AZURE_STREAM_RATE_LIMIT, 200, 7.0),
    (STARTED_THEN_RATE_LIMITED, 200, 0.0),
    (http_429("3"), 429, 3.0),
    (http_429(), 429, 0.0),
    (sse({"type": "response.failed", "response": {"error": {"code": "rate_limit_exceeded"}}}),
     200, 0.0),
    (sse({"type": "error", "error": {"code": "server_error", "message": "boom"}}), 200, None),
    (success_response("azure", True), 200, None),
    (FakeResponse(503, b'{"error":"unavailable"}'), 503, None),
])
def test_rate_limit_detection(body, status, expected):
    assert common.rate_limit_delay(status, body.headers, body.data) == expected


@pytest.mark.parametrize("value, seconds", [
    ("12", 12.0), ("1.5", 1.5), ("250ms", 0.25), ("1m30s", 90.0), ("soon", None),
])
def test_reset_header_durations(value, seconds):
    assert common.parse_duration(value) == seconds


@pytest.mark.parametrize("kind", PATHS)
@pytest.mark.parametrize("rate_limit", ["stream", "started-stream", "http"])
def test_rate_limit_is_retried_before_the_agent_sees_it(tmp_path, sleeps, kind, rate_limit):
    limited = {
        "stream": AZURE_STREAM_RATE_LIMIT,
        "started-stream": STARTED_THEN_RATE_LIMITED,
        "http": http_429("2"),
    }[rate_limit]
    responses = [limited, limited, success_response(kind, True)]
    with gateway_server(tmp_path, kind, responses, limit=1) as (gateway, upstream, base, path):
        assert post(base, path, {}) == 200
        assert post(base, path, {}) == 429  # the budget still applies
    assert len(upstream.calls) == 3
    assert len(sleeps) == 2 and all(wait >= 1 for wait in sleeps)
    rows = log_rows(gateway.token_log.path)
    waits = [row for row in rows if row.get("rate_limit_wait")]
    assert len(waits) == 2
    request = next(row for row in rows if row.get("model_step_completed"))
    assert request["rate_limit_retries"] == 2
    assert not request.get("rate_limited")
    state = read_task_log(gateway.token_log.path, "task-test")
    assert state.completed_responses == 1 and state.budget_rejected
    assert state.rate_limit_waits == 2
    assert state.rate_limit_wait_sec == pytest.approx(sum(sleeps), abs=0.01)
    usage, _ = collect_task_proxy_usage(gateway.token_log.path, "task-test")
    assert usage["model_requests"] == 1 and usage["rate_limit_waits"] == 2


@pytest.mark.parametrize("kind", PATHS)
def test_persistent_rate_limit_is_relayed_after_bounded_wait_without_tripping_breaker(
    tmp_path, sleeps, kind,
):
    limit = common.MAX_CONSECUTIVE_GENERATION_FAILURES
    responses = [http_429()] * (common.RATE_LIMIT_MAX_ATTEMPTS * (limit + 1))
    with gateway_server(tmp_path, kind, responses, limit=10) as (gateway, upstream, base, path):
        gateway.rate_limit_max_wait = 20
        statuses = [post(base, path, {}) for _ in range(limit + 1)]
    # Every request reaches the upstream again: rate limits never open the
    # consecutive-failure breaker that would end the task.
    assert statuses == [429] * (limit + 1)
    assert not read_task_log(gateway.token_log.path, "task-test").retry_exhausted
    assert 0 < sum(sleeps) <= 20 * (limit + 1) + 1e-6
    gateway.restore()
    assert gateway.budget.admit({"task": "task-test"}, True) is None


def openai_pools(tmp_path, upstreams, script, pin_resources=()):
    """Gateway whose upstreams answer from per-route response lists.

    A route is ``host`` or ``host@deployment``; the deployment shows in the
    forwarded model name, so the fake tells deployments of one host apart.
    """
    calls = []

    class Pool:
        def __init__(self, host):
            self.host = host

        def urlopen(self, method, path, body=None, headers=None, **_kwargs):
            model = json.loads(body).get("model")
            route = next(
                (r for r in script if r.partition("@")[0] == self.host and r.partition("@")[2] == model),
                self.host,
            )
            calls.append((route, json.loads(body), headers.get("X-HCU-Resource")))
            return script[route].pop(0)

    pools = {}
    gateway = openai_proxy.OpenAIGateway(
        upstreams=upstreams, model_upstreams={},
        token_log=common.TokenLog(tmp_path / "tokens.jsonl"),
        max_requests_per_task=0, token=StaticToken(),
    )
    gateway.pin_resources = list(pin_resources)
    gateway.pool = lambda host: pools.setdefault(host, Pool(host))
    server, base = serve(openai_proxy.make_handler(gateway))
    return gateway, server, base, calls


def run_requests(gateway, server, base, payloads):
    try:
        return [post(base, "/t/task-a/v1/responses", payload) for payload in payloads]
    finally:
        server.shutdown()
        gateway.token_log.close()


def test_stateless_request_moves_to_another_deployment_of_the_same_resource(tmp_path, sleeps):
    script = {
        "east.example": [AZURE_STREAM_RATE_LIMIT],
        "east.example@gpt-5.4-b": [success_response("azure", True), success_response("azure", True)],
        "west.example": [],
    }
    gateway, server, base, calls = openai_pools(
        tmp_path, ["east.example", "west.example", "east.example@gpt-5.4-b"], script,
    )
    payload = {"model": "gpt-5.4", "input": []}
    assert run_requests(gateway, server, base, [payload, payload]) == [200, 200]
    assert sleeps == []
    # The task stays on the deployment that served it.
    assert [route for route, _, _ in calls] == [
        "east.example", "east.example@gpt-5.4-b", "east.example@gpt-5.4-b",
    ]
    # The deployment name replaces the model; the rest of the body is unchanged.
    assert calls[1][1] == {"model": "gpt-5.4-b", "input": []}
    row = next(row for row in log_rows(gateway.token_log.path) if row.get("model_step_completed"))
    assert row["spilled_from"] == ["east.example"]


@pytest.mark.parametrize("payload", [
    {"model": "gpt-5.4", "input": []},
    {"model": "gpt-5.4", "previous_response_id": "resp_1", "input": []},
])
def test_rate_limited_task_never_changes_resource(tmp_path, sleeps, payload):
    # Encrypted reasoning only decrypts on the resource that produced it, so a
    # free deployment on another resource must not take the request.
    script = {
        "east.example": [http_429("4"), AZURE_STREAM_RATE_LIMIT, success_response("azure", True)],
        "west.example": [success_response("azure", True)] * 3,
    }
    gateway, server, base, calls = openai_pools(tmp_path, ["east.example", "west.example"], script)
    assert run_requests(gateway, server, base, [payload]) == [200]
    assert [route for route, _, _ in calls] == ["east.example"] * 3
    assert len(sleeps) == 2 and 4 <= sleeps[0] <= 5


def test_relay_receives_the_pinned_resource_on_every_retry(tmp_path, sleeps):
    script = {"relay:8541": [AZURE_STREAM_RATE_LIMIT, http_429(), success_response("azure", True)]}
    gateway, server, base, calls = openai_pools(
        tmp_path, ["relay:8541"], script,
        pin_resources=["east.openai.azure.com", "west.openai.azure.com"],
    )
    assert run_requests(gateway, server, base, [{"model": "gpt-5.4", "input": []}]) == [200]
    assert [resource for _, _, resource in calls] == ["east.openai.azure.com"] * 3
    rows = log_rows(gateway.token_log.path)
    assert next(row for row in rows if row.get("model_step_completed"))["resource"] == "east.openai.azure.com"


def test_new_tasks_go_to_the_deployment_with_most_token_headroom(tmp_path):
    gateway = openai_proxy.OpenAIGateway(
        upstreams=["a.example", "b.example", "c.example"], model_upstreams={},
        token_log=common.TokenLog(tmp_path / "tokens.jsonl"),
        max_requests_per_task=0, token=StaticToken(),
    )
    gateway.observe("a.example", {"x-ratelimit-remaining-tokens": "1000"}, None)
    gateway.observe("b.example", {"x-ratelimit-remaining-tokens": "40000"}, None)
    gateway.observe("c.example", {"x-ratelimit-remaining-tokens": "90000"}, 30.0)
    assert gateway.host_for_task("task-1", "m") == "b.example"
    gateway.observe("b.example", {"x-ratelimit-remaining-tokens": "500"}, None)
    assert gateway.host_for_task("task-2", "m") == "a.example"
    assert gateway.host_for_task("task-1", "m") == "b.example"
    gateway.token_log.close()


@pytest.mark.parametrize("message", [
    "Reconnecting... 5/5 (rate limit exceeded: Your requests to gpt-5.4 for gpt-5.4 in "
    "swedencentral have exceeded token rate limit.)",
    "exceeded retry limit, last status: 429 Too Many Requests",
    "RESOURCE_EXHAUSTED: quota",
])
def test_rate_limit_failures_are_infrastructure(message):
    classification = traces.classify_turn_failure(message)
    assert classification == "upstream_rate_limit"
    assert classification in traces.INFRASTRUCTURE_FAILURES


def test_agent_errors_are_not_infrastructure():
    assert traces.classify_turn_failure("tool failed") not in traces.INFRASTRUCTURE_FAILURES


def test_relay_reported_wait_is_logged_for_the_runner(tmp_path):
    from hcu.gateway import common
    log = common.TokenLog(tmp_path / "t.jsonl")

    class Response:
        status = 200
        headers = {"X-HCU-Rate-Limit-Wait": "42.5", "content-type": "application/json"}
        data = b"{}"

    row = {"task": "task-x", "endpoint": "relay"}
    common.forward_with_rate_limit_retry(lambda: Response(), row, log, max_wait=300)
    rows = [json.loads(l) for l in (tmp_path / "t.jsonl").read_text().splitlines()]
    waits = [r for r in rows if r.get("rate_limit_wait")]
    assert waits and waits[0]["wait_sec"] == 42.5 and row["rate_limit_wait_sec"] == 42.5
    assert common.relay_rate_limit_wait({"X-HCU-Rate-Limit-Wait": "nan"}) == 0.0


def test_relay_wait_spends_the_request_budget(tmp_path, sleeps):
    # A relay that waited out the whole budget and still answers with a rate
    # limit is relayed at once instead of being resent for another relay wait.
    log = common.TokenLog(tmp_path / "t.jsonl")

    class Response:
        status = 429
        headers = {"X-HCU-Rate-Limit-Wait": "300", "content-type": "application/json"}
        data = b'{"error":{"code":"429","message":"Too Many Requests"}}'

    calls = []
    row = {"task": "task-x", "endpoint": "relay"}
    response = common.forward_with_rate_limit_retry(
        lambda: calls.append(1) or Response(), row, log, max_wait=300)
    assert response.status == 429 and row["rate_limited"]
    assert len(calls) == 1 and sleeps == []
    assert row["rate_limit_wait_sec"] == 300
