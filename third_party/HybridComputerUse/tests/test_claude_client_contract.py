"""Optional native Claude wire tests, using local scripted API and MCP peers."""

import json
import os
from pathlib import Path
import subprocess
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import pytest


ROOT = Path(__file__).resolve().parents[1]
PNG = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1WQAAAAASUVORK5CYII="


def _sse_message(block, model):
    usage = {"input_tokens": 10, "output_tokens": 5}
    if block["type"] == "tool_use":
        initial = dict(block, input={})
        delta = {"type": "input_json_delta", "partial_json": json.dumps(block["input"])}
    else:
        initial = dict(block, text="")
        delta = {"type": "text_delta", "text": block["text"]}
    events = [
        ("message_start", {"type": "message_start", "message": {
            "id": "msg_local_contract", "type": "message", "role": "assistant",
            "model": model, "content": [], "stop_reason": None,
            "stop_sequence": None, "usage": usage,
        }}),
        ("content_block_start", {"type": "content_block_start", "index": 0,
                                 "content_block": initial}),
        ("content_block_delta", {"type": "content_block_delta", "index": 0, "delta": delta}),
        ("content_block_stop", {"type": "content_block_stop", "index": 0}),
        ("message_delta", {"type": "message_delta", "delta": {
            "stop_reason": "tool_use" if block["type"] == "tool_use" else "end_turn",
            "stop_sequence": None,
        }, "usage": {"output_tokens": 5}}),
        ("message_stop", {"type": "message_stop"}),
    ]
    return "".join(f"event: {event}\ndata: {json.dumps(data)}\n\n"
                   for event, data in events).encode()


@pytest.mark.parametrize("is_error", [False, True], ids=["success-image", "error-image"])
@pytest.mark.parametrize("structured", [False, True], ids=["default-text", "opt-in-structured"])
def test_native_claude_mcp_model_wire_contract(tmp_path, is_error, structured):
    binary = Path(os.environ.get("CLAUDE_TEST_BINARY", ROOT / "artifacts/claude-code"))
    if not binary.is_file():
        pytest.skip("Claude artifact absent; set CLAUDE_TEST_BINARY for native wire test")
    contract = json.loads((ROOT / "computer-use/testdata/contract/public_contract.json").read_text())
    tools_path = tmp_path / "tools.json"
    tools_path.write_text(json.dumps(contract["tools_list"]))
    screenshot_call = {"actions": [{"type": "screenshot"}]}
    marker = ("GUI coordinate/window actions require a prior GUI screenshot. "
              "No actions were emitted; use the attached fresh screenshot." if is_error
              else "LOCAL_OBSERVATION_MARKER")
    # Match the default HCU GUI envelope: facts live in the text, followed by
    # the image. The stale-frame message and recovery call are from gui_service.go.
    facts = {"ok": not is_error, "input_emitted": False, "completed": 0, "requested": 1,
             "screen": {"width": 1, "height": 1}}
    if is_error:
        facts.update(code="gui_frame_stale", message=marker,
                     hint=json.dumps(screenshot_call, separators=(",", ":")))
    else:
        facts["message"] = marker
    response = {
        "isError": is_error,
        "content": [
            {"type": "text", "text": json.dumps(facts, separators=(",", ":"))},
            {"type": "image", "mimeType": "image/png", "data": PNG},
        ],
    }
    if structured:
        response["structuredContent"] = {
            "input_emitted": False, "diagnostic": "LOCAL_STRUCTURED_MARKER",
        }
    result_path = tmp_path / "result.json"
    result_path.write_text(json.dumps(response))
    calls_path = tmp_path / "calls.jsonl"
    fake_mcp = tmp_path / "fake_mcp.py"
    fake_mcp.write_text('''import json, pathlib, sys
tools = json.loads(pathlib.Path(sys.argv[1]).read_text())
result = json.loads(pathlib.Path(sys.argv[2]).read_text())
for line in sys.stdin:
    request = json.loads(line)
    if "id" not in request:
        continue
    method = request["method"]
    if method == "initialize":
        value = {"protocolVersion": request["params"]["protocolVersion"],
                 "capabilities": {"tools": {}},
                 "serverInfo": {"name": "local-diagnostic", "version": "1"}}
    elif method == "tools/list":
        value = tools["result"]
    elif method == "tools/call":
        with open(sys.argv[3], "a") as log:
            log.write(json.dumps(request["params"]) + "\\n")
        value = result
    else:
        value = {}
    print(json.dumps({"jsonrpc": "2.0", "id": request["id"], "result": value}), flush=True)
''')
    config = tmp_path / "mcp.json"
    config.write_text(json.dumps({"mcpServers": {"computer_use": {
        "command": sys.executable, "args": [
            str(ROOT / "hcu/osworld/guest_files/claude_mcp_schema_adapter.py"),
            sys.executable, str(fake_mcp), str(tools_path), str(result_path), str(calls_path),
        ],
    }}}))
    requests = []

    class Handler(BaseHTTPRequestHandler):
        def do_POST(self):
            body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            requests.append(body)
            results = [block for message in body.get("messages", [])
                       for block in message.get("content", [])
                       if isinstance(block, dict) and block.get("type") == "tool_result"]
            block = ({"type": "text", "text": "LOCAL_DONE"} if results else {
                "type": "tool_use", "id": "tool_local_gui", "name": "mcp__computer_use__GUI",
                "input": {"actions": [{"type": "screenshot"}]},
            })
            payload = _sse_message(block, body.get("model", "claude-sonnet-5"))
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)

        def log_message(self, *args):
            pass

    # Do not inherit provider credentials, proxies, or the user's client configuration.
    environment = {key: os.environ[key] for key in ("PATH", "LANG", "TMPDIR") if key in os.environ}
    environment.update({
        "HOME": str(tmp_path / "home"), "USERPROFILE": str(tmp_path / "home"),
        "CLAUDE_CONFIG_DIR": str(tmp_path / "claude-home"),
        "ANTHROPIC_API_KEY": "local-contract-test",
        "CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC": "1",
        "ENABLE_TOOL_SEARCH": "false",
    })
    with ThreadingHTTPServer(("127.0.0.1", 0), Handler) as endpoint:
        environment["ANTHROPIC_BASE_URL"] = f"http://127.0.0.1:{endpoint.server_port}"
        thread = threading.Thread(
            target=endpoint.serve_forever, kwargs={"poll_interval": 0.01}, daemon=True
        )
        thread.start()
        try:
            completed = subprocess.run([
                str(binary), "--bare", "-p", "--no-session-persistence",
                "--model", "claude-sonnet-5", "--strict-mcp-config", "--mcp-config", str(config),
                "--setting-sources", "", "--tools", "", "--allowedTools", "mcp__computer_use__GUI",
                "--system-prompt", "Use the diagnostic GUI tool once, then stop.",
                "--max-turns", "3", "--output-format", "stream-json", "--verbose",
                "Run the local diagnostic. No real desktop input is available.",
            ], cwd=tmp_path, env=environment, capture_output=True, text=True, timeout=45)
        finally:
            endpoint.shutdown()
            thread.join(timeout=5)
    assert completed.returncode == 0, completed.stderr + completed.stdout
    assert len(requests) >= 2, completed.stderr + completed.stdout
    tools = {tool["name"]: tool for tool in requests[0]["tools"]}
    assert {"mcp__computer_use__AX", "mcp__computer_use__GUI"} <= tools.keys()
    for name in ("AX", "GUI"):
        schema = tools[f"mcp__computer_use__{name}"]["input_schema"]
        assert schema["type"] == "object"
        assert "oneOf" not in schema
        assert schema["properties"]
    call = json.loads(calls_path.read_text())
    assert call["name"] == "GUI"
    assert call["arguments"] == {"actions": [{"type": "screenshot"}]}
    results = [block for message in requests[-1]["messages"]
               for block in message.get("content", [])
               if isinstance(block, dict) and block.get("type") == "tool_result"]
    result = next(block for block in results if block["tool_use_id"] == "tool_local_gui")
    content = result["content"]
    blocks = content if isinstance(content, list) else []
    images = [block for block in blocks if block.get("type") == "image"]
    observation = {
        "client_version": subprocess.check_output([str(binary), "--version"], text=True).strip(),
        "is_error": is_error, "structured": structured,
        "error_flag_preserved": bool(result.get("is_error")) is is_error,
        "recovery_text_preserved": marker in json.dumps(content),
        "structured_facts_preserved": "LOCAL_STRUCTURED_MARKER" in json.dumps(content),
        "images_forwarded": len(images), "requests": len(requests),
    }
    evidence = Path(os.environ.get("CLAUDE_TEST_EVIDENCE_DIR", tmp_path))
    evidence.mkdir(parents=True, exist_ok=True)
    evidence.joinpath(f"claude-error-{is_error}-structured-{structured}.json").write_text(
        json.dumps({"observation": observation, "requests": requests,
                    "stdout": completed.stdout, "stderr": completed.stderr}, indent=2)
    )
    print(json.dumps(observation))
    assert bool(result.get("is_error")) is is_error
    if not structured or is_error:
        assert observation["recovery_text_preserved"]
    else:
        assert observation["structured_facts_preserved"]
    if not is_error:
        assert len(images) == 1
    else:
        texts = [block["text"] for block in blocks if block.get("type") == "text"] if blocks else [content]
        restored = next(json.loads(text) for text in texts if marker in text)
        assert restored["code"] == "gui_frame_stale"
        assert restored["input_emitted"] is False
        assert json.loads(restored["hint"]) == screenshot_call
    # Claude 2.1.303 drops images on isError. Record the native behavior without
    # making that defect a required invariant or disguising the error as success.
    for image in images:
        assert image["source"]["media_type"] == "image/png"
        assert image["source"]["data"] == PNG
