import json
import threading
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer

import pytest

from hcu.gateway import anthropic_proxy, gemini_proxy, openai_proxy
from hcu.gateway.common import TokenLog, split_task_path


class FakeResponse:
    def __init__(self, status, body, content_type="application/json"):
        self.status = status
        self.data = body
        self.headers = {"content-type": content_type}


class FakeUpstream:
    """Records forwarded requests and answers with fixed usage."""

    def __init__(self, host=None):
        self.host = host
        self.calls = []

    def urlopen(self, method, path, body=None, headers=None, **_kwargs):
        self.calls.append((self.host, method, path, headers["Authorization"]))
        return FakeResponse(200, json.dumps({
            "id": "resp", "status": "completed",
            "usage": {"input_tokens": 10, "output_tokens": 2, "total_tokens": 12,
                      "input_tokens_details": {"cached_tokens": 4},
                      "output_tokens_details": {"reasoning_tokens": 1}},
        }).encode())

    def request(self, method, url, body=None, headers=None, **_kwargs):
        self.calls.append((method, url, headers["x-api-key"]))
        return FakeResponse(200, json.dumps({
            "id": "msg", "type": "message", "stop_reason": "end_turn",
            "usage": {"input_tokens": 5, "output_tokens": 3},
        }).encode())


class StaticToken:
    def get(self):
        return "aad"


def serve(handler):
    server = ThreadingHTTPServer(("127.0.0.1", 0), handler)
    threading.Thread(target=server.serve_forever, kwargs={"poll_interval": 0.01}, daemon=True).start()
    return server, f"http://127.0.0.1:{server.server_address[1]}"


def post(base, path, payload):
    request = urllib.request.Request(
        base + path, data=json.dumps(payload).encode(), method="POST"
    )
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            response.read()
            return response.status
    except urllib.error.HTTPError as error:
        error.read()
        return error.code


def log_rows(path):
    return [json.loads(line) for line in path.read_text().splitlines()]


@pytest.fixture
def openai_gateway(tmp_path):
    pools = {}
    gateway = openai_proxy.OpenAIGateway(
        upstreams=["east.example", "west.example"],
        model_upstreams={"other-model": ["other.example"]},
        token_log=TokenLog(tmp_path / "tokens.jsonl"),
        max_requests_per_task=2,
        token=StaticToken(),
    )
    gateway.pool = lambda host: pools.setdefault(host, FakeUpstream(host))
    server, base = serve(openai_proxy.make_handler(gateway))
    yield gateway, base, pools
    server.shutdown()


def test_split_task_path():
    assert split_task_path("/t/a%2Db/v1/responses") == ("a-b", "/v1/responses")
    assert split_task_path("/t/only") == ("only", "/")
    assert split_task_path("/v1/responses") == (None, "/v1/responses")


def test_openai_budget_caps_task_agents(openai_gateway):
    gateway, base, _pools = openai_gateway
    path = "/t/task-abc/v1/responses"
    statuses = [post(base, path, {"model": "m"}) for _ in range(3)]
    assert statuses == [200, 200, 429]
    rows = log_rows(gateway.token_log.path)
    assert [row.get("request_ordinal") for row in rows] == [1, 2, 3]
    assert rows[-1]["budget_rejected"] is True
    assert rows[0]["input_tokens"] == 10 and rows[0]["cached_tokens"] == 4


def test_openai_non_task_traffic_is_not_capped(openai_gateway):
    _gateway, base, pools = openai_gateway
    statuses = [
        post(base, "/t/other-run/v1/responses", {"model": "other-model"})
        for _ in range(3)
    ]
    assert statuses == [200, 200, 200]
    assert len(pools["other.example"].calls) == 3


def test_openai_pins_each_task_to_one_upstream(openai_gateway):
    _gateway, base, pools = openai_gateway
    for task in ("one", "two", "one", "two"):
        post(base, f"/t/task-{task}/v1/responses", {"model": "m"})
    assert [call[0] for call in pools["east.example"].calls] == ["east.example"] * 2
    assert [call[0] for call in pools["west.example"].calls] == ["west.example"] * 2
    assert pools["east.example"].calls[0][2] == "/openai/v1/responses?api-version=preview"
    assert pools["east.example"].calls[0][3] == "Bearer aad"


def test_openai_restores_affinity_and_budget_from_the_log(tmp_path):
    log = tmp_path / "tokens.jsonl"
    log.write_text(
        json.dumps({"task": "task-t", "model": "m",
                    "endpoint": "west.example", "path": "/v1/responses",
                    "model_step_completed": True}) + "\n"
    )
    gateway = openai_proxy.OpenAIGateway(
        upstreams=["east.example", "west.example"],
        model_upstreams={},
        token_log=TokenLog(log),
        max_requests_per_task=2,
        token=StaticToken(),
    )
    gateway.restore()
    assert gateway.host_for_task("task-t", "m") == "west.example"
    row = {"task": "task-t"}
    assert gateway.budget.admit(row, True) is None
    assert row["request_ordinal"] == 2
    gateway.budget.finish(row, gateway.token_log, True)
    assert gateway.budget.admit({"task": "task-t"}, True) == "budget_rejected"
    gateway.token_log.close()


def test_anthropic_budget_counts_delivered_responses(tmp_path):
    log = tmp_path / "tokens.jsonl"
    log.write_text(json.dumps({
        "task": "task-t", "path": "/v1/messages",
        "model_step_completed": True,
    }) + "\n")
    upstream = FakeUpstream()
    gateway = anthropic_proxy.AnthropicGateway(
        upstream_base="https://upstream.example/api",
        upstream_token="secret",
        token_log=TokenLog(log),
        max_requests_per_task=2,
        pool=upstream,
    )
    gateway.restore()
    server, base = serve(anthropic_proxy.make_handler(gateway))
    try:
        statuses = [
            post(base, "/t/task-t/v1/messages?beta=true", {"model": "c"})
            for _ in range(2)
        ]
    finally:
        server.shutdown()
    assert statuses == [200, 429]
    assert upstream.calls == [
        ("POST", "https://upstream.example/api/v1/messages?beta=true", "secret")
    ]
    rows = log_rows(log)[1:]
    assert rows[0]["model_step_completed"] is True
    assert rows[0]["request_ordinal"] == 2
    assert rows[0]["total_tokens"] == 8
    assert rows[1]["budget_rejected"] is True


def test_anthropic_sse_usage_takes_the_maximum_per_field():
    raw = (
        b'data: {"type":"message_start","message":{"id":"m1",'
        b'"usage":{"input_tokens":20,"cache_read_input_tokens":4,"output_tokens":1}}}\n'
        b'data: {"type":"message_delta","usage":{"output_tokens":9}}\n'
    )
    usage, response_id = anthropic_proxy.parse_sse_usage(raw)
    assert response_id == "m1"
    assert usage["input_tokens"] == 20
    assert usage["output_tokens"] == 9
    assert usage["cached_tokens"] == 4


def test_openai_compatible_upstream_keeps_the_path_and_bearer_token(tmp_path):
    pools = {}
    gateway = openai_proxy.OpenAIGateway(
        upstreams=["api.x.ai"],
        model_upstreams={},
        token_log=TokenLog(tmp_path / "tokens.jsonl"),
        max_requests_per_task=0,
        token=openai_proxy.StaticToken("xai-secret"),
        upstream_kind="openai",
    )
    gateway.pool = lambda host: pools.setdefault(host, FakeUpstream(host))
    server, base = serve(openai_proxy.make_handler(gateway))
    try:
        assert post(base, "/t/task-g/v1/responses", {"model": "grok-4.7"}) == 200
    finally:
        server.shutdown()
    assert pools["api.x.ai"].calls == [
        ("api.x.ai", "POST", "/v1/responses", "Bearer xai-secret")
    ]


class FakeGeminiUpstream:
    def __init__(self):
        self.calls = []

    def request(self, method, url, body=None, headers=None, **_kwargs):
        self.calls.append((method, url, headers["x-goog-api-key"]))
        events = (
            b'data: {"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"OK"}]}}],'
            b'"usageMetadata":{"promptTokenCount":30,"candidatesTokenCount":2,'
            b'"cachedContentTokenCount":5,"thoughtsTokenCount":1,"totalTokenCount":33},'
            b'"responseId":"g1"}\n\n'
        )
        return FakeResponse(200, events, "text/event-stream")


def test_gemini_gateway_budgets_generate_calls_and_records_usage(tmp_path):
    log = tmp_path / "tokens.jsonl"
    upstream = FakeGeminiUpstream()
    gateway = gemini_proxy.GeminiGateway(
        upstream_base="https://generativelanguage.googleapis.com",
        upstream_token="google-secret",
        token_log=TokenLog(log),
        max_requests_per_task=1,
        pool=upstream,
    )
    server, base = serve(gemini_proxy.make_handler(gateway))
    path = "/t/task-g/v1beta/models/gemini-3.8-flash:streamGenerateContent?alt=sse"
    try:
        statuses = [post(base, path, {"contents": []}) for _ in range(2)]
        count_status = post(base, "/t/task-g/v1beta/models/gemini-3.8-flash:countTokens", {})
    finally:
        server.shutdown()
    assert statuses == [200, 429]
    assert count_status == 200
    assert upstream.calls[0] == (
        "POST",
        "https://generativelanguage.googleapis.com/v1beta/models/"
        "gemini-3.8-flash:streamGenerateContent?alt=sse",
        "google-secret",
    )
    rows = log_rows(log)
    assert rows[0]["model"] == "gemini-3.8-flash"
    assert rows[0]["input_tokens"] == 30 and rows[0]["cached_tokens"] == 5
    assert rows[0]["reasoning_tokens"] == 1 and rows[0]["response_id"] == "g1"
    assert rows[1]["budget_rejected"] is True
    assert "request_ordinal" not in rows[2]


def test_openai_deployment_route_rewrites_model_and_pins_task(tmp_path):
    gateway = openai_proxy.OpenAIGateway(
        upstreams=["east.example", "east.example@m-extra"],
        model_upstreams={},
        token_log=TokenLog(tmp_path / "tokens.jsonl"),
        max_requests_per_task=0,
        token=StaticToken(),
    )
    assert gateway.host_for_task("task-a", "m") == "east.example"
    assert gateway.host_for_task("task-b", "m") == "east.example@m-extra"
    assert gateway.host_for_task("task-b", "m") == "east.example@m-extra"
    assert openai_proxy.split_host("east.example@m-extra") == ("east.example", 443)


def test_openai_pins_each_task_to_one_resource(tmp_path):
    gateway = openai_proxy.OpenAIGateway(
        upstreams=["relay.local:8541"],
        model_upstreams={},
        token_log=TokenLog(tmp_path / "tokens.jsonl"),
        max_requests_per_task=0,
        token=StaticToken(),
    )
    gateway.pin_resources = ["a.example", "b.example"]
    assert gateway.resource_for_task("task-1") == "a.example"
    assert gateway.resource_for_task("task-2") == "b.example"
    assert gateway.resource_for_task("task-1") == "a.example"
    assert gateway.resource_for_task("task-3") == "a.example"
