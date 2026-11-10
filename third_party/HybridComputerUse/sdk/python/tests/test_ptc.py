"""PTC adapter tests without a paid API or a real desktop."""

import asyncio
import base64
import concurrent.futures
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch

import hcu_sdk as hcu
from hcu_sdk import ptc


PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6K9sAAAAASUVORK5CYII=")


def event(event_id="toolu_one", name="hcu_ax", args=None, caller=None):
    value = {"type": "tool_use", "id": event_id, "name": name,
             "input": args if args is not None else {"mode": "act", "actions": [{"type": "click", "ref": "r1"}]}}
    if caller:
        value["caller"] = {"type": caller, "tool_id": "srvtoolu_one"}
    return value


def record_from(result):
    content = result["content"]
    return json.loads(content if isinstance(content, str) else content[0]["text"])


class PTCAdapterTests(unittest.TestCase):
    def setUp(self):
        self.calls = []
        self.answer = {"ok": True, "input_emitted": True, "completed": 1, "requested": 2}
        def transport(method, params):
            self.calls.append((method, dict(params)))
            return self.answer
        self.desktop = hcu.Desktop(transport)

    def test_repeat_and_concurrent_events_dispatch_once(self):
        entered, release = threading.Event(), threading.Event()
        def transport(method, params):
            self.calls.append(params)
            entered.set()
            self.assertTrue(release.wait(5))
            return self.answer
        forwarder = ptc.Forwarder(hcu.Desktop(transport))
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            first = pool.submit(forwarder, event())
            self.assertTrue(entered.wait(5))
            second = pool.submit(forwarder, event())
            release.set()
            self.assertEqual(first.result(5), second.result(5))
        self.assertEqual(len(self.calls), 1)
        self.assertTrue(self.calls[0]["request_id"].startswith("ptc-"))

    def test_success_remains_cached_after_daemon_restart_or_eviction(self):
        forwarder = ptc.Forwarder(self.desktop)
        first = forwarder(event())
        def replacement(method, params):
            self.fail("a repeated event must not reach the replacement or evicted cache")
        self.desktop.transport = replacement
        self.assertEqual(forwarder(event()), first)

    def test_unsafe_reply_loss_is_terminal_and_preserves_unknown_input(self):
        def dropped(method, params):
            self.calls.append(params)
            raise hcu.TransportClosed("reply lost")
        forwarder = ptc.Forwarder(hcu.Desktop(dropped))
        first = forwarder(event())
        self.assertEqual(first, forwarder(event()))
        record = record_from(first)
        self.assertEqual(record["code"], "effect_unknown")
        self.assertIsNone(record["input_emitted"])
        self.assertEqual(len(self.calls), 1)

    def test_safe_retry_retains_generation_and_replay_only(self):
        def transport(method, params):
            self.calls.append(dict(params))
            if len(self.calls) == 1:
                params["daemon_instance"] = "generation-one"
                params["replay_only"] = True
                error = hcu.TransportClosed("lost guarded reply")
                error.retry_safe = True
                raise error
            self.assertEqual(params["daemon_instance"], "generation-one")
            self.assertTrue(params["replay_only"])
            raise hcu.TransportEffectUnknown("restarted or cache entry missing", params["request_id"])
        with patch("hcu_sdk.time.sleep"):
            forwarder = ptc.Forwarder(hcu.Desktop(transport))
            result = forwarder(event())
        self.assertEqual(record_from(result)["code"], "effect_unknown")
        forwarder(event())
        self.assertEqual(len(self.calls), 2)
        self.assertEqual(self.calls[0]["request_id"], self.calls[1]["request_id"])

    def test_argument_conflict_and_capacity_refuse_before_input(self):
        forwarder = ptc.Forwarder(self.desktop, max_events=1)
        forwarder(event())
        with self.assertRaises(ValueError):
            forwarder(event(args={"mode": "apps"}))
        with self.assertRaises(RuntimeError):
            forwarder(event("toolu_two"))
        self.assertEqual(len(self.calls), 1)
        forwarder(event())
        self.assertEqual(len(self.calls), 1)

    def test_interrupted_event_cannot_dispatch_again(self):
        def interrupted(method, params):
            self.calls.append(params)
            raise KeyboardInterrupt()
        forwarder = ptc.Forwarder(hcu.Desktop(interrupted))
        with self.assertRaises(KeyboardInterrupt):
            forwarder(event())
        with self.assertRaisesRegex(RuntimeError, "interrupted"):
            forwarder(event())
        self.assertEqual(len(self.calls), 1)

    def test_changed_caller_for_same_event_refuses_before_input(self):
        forwarder = ptc.Forwarder(self.desktop)
        forwarder(event())
        with self.assertRaises(ValueError):
            forwarder(event(caller=ptc.CODE_EXECUTION_CALLER))
        self.assertEqual(len(self.calls), 1)

    def test_malformed_caller_fails_before_input(self):
        forwarder = ptc.Forwarder(self.desktop)
        for caller in ("direct", {}, {"type": "unknown"}):
            block = event()
            block["caller"] = caller
            with self.assertRaises(ValueError):
                forwarder(block)
        self.assertEqual(len(self.calls), 0)
        forwarder(event())
        self.assertEqual(len(self.calls), 1)
        block = event()
        block["caller"] = None
        forwarder(block)
        self.assertEqual(len(self.calls), 1)

    def test_explicit_id_and_core_error_are_preserved(self):
        self.answer = {"ok": False, "code": "stale_ref", "input_emitted": False, "completed": 0}
        block = event(args={"mode": "act", "request_id": "explicit", "actions": []})
        result = ptc.forward(block, self.desktop)
        self.assertEqual(record_from(result), self.answer)
        self.assertFalse(result["is_error"])
        self.assertEqual(self.calls[0][1]["request_id"], "explicit")
        self.assertEqual(ptc.forward(block, self.desktop), result)
        self.assertEqual(len(self.calls), 1)

    def test_direct_images_and_code_json_have_distinct_formats(self):
        self.answer["screen"] = {"image": "/guest/screen.png", "width": 1, "height": 1}
        self.answer["images"] = ["/guest/screen.png"]
        reads = []
        def reader(path):
            reads.append(path)
            return "image/png", PNG
        forwarder = ptc.Forwarder(self.desktop, image_reader=reader)
        result = forwarder(event(name="hcu_gui", args={"actions": [{"type": "screenshot"}]}))
        self.assertEqual(result["content"][0]["type"], "text")
        image = result["content"][1]
        self.assertEqual(base64.b64decode(image["source"]["data"]), PNG)
        self.assertEqual(reads, ["/guest/screen.png"])
        coded = forwarder(event("toolu_code", name="hcu_gui", args={"actions": [{"type": "screenshot"}]}, caller=ptc.CODE_EXECUTION_CALLER))
        self.assertIsInstance(coded["content"], str)
        self.assertEqual(len(reads), 1)

    def test_image_failure_preserves_partial_input_and_does_not_repeat_action(self):
        self.answer["screen"] = {"image": "/guest/screen.png"}
        forwarder = ptc.Forwarder(self.desktop)
        result = forwarder(event())
        record = record_from(result)
        self.assertTrue(result["is_error"])
        self.assertTrue(record["input_emitted"])
        self.assertEqual((record["completed"], record["requested"]), (1, 2))
        self.assertEqual(record["delivery_error"]["code"], "image_unavailable")
        forwarder.image_reader = lambda path: ("image/png", b"x" * (5 * 1024 * 1024 + 1))
        self.assertTrue(forwarder(event())["is_error"])
        forwarder.image_reader = lambda path: ("image/png", PNG)
        self.assertEqual(forwarder(event())["content"][1]["type"], "image")
        self.assertEqual(len(self.calls), 1)

    def test_sdk_ptc_wrapper_still_receives_json_and_typed_errors(self):
        self.answer = {"ok": False, "code": "stale_ref", "input_emitted": False}
        forwarder = ptc.Forwarder(self.desktop)
        async def ax(args):
            return forwarder(event(args=args, caller=ptc.CODE_EXECUTION_CALLER))["content"]
        async def run():
            desktop = hcu.AsyncDesktop(hcu.ptc_transport(ax, ax))
            with self.assertRaises(hcu.StaleRef):
                await desktop.click("r1")
        asyncio.run(run())

    @unittest.skipUnless(os.name == "posix", "local reader is for a POSIX harness")
    def test_local_reader_bounds_format_size_and_symlinks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "output"
            root.mkdir()
            image = root / "screen.png"
            image.write_bytes(PNG)
            outside = Path(directory) / "outside.png"
            outside.write_bytes(PNG)
            (root / "link.png").symlink_to(outside)
            (root / "linkdir").symlink_to(Path(directory), target_is_directory=True)
            reader = ptc.local_image_reader(root)
            self.assertEqual(reader(str(image)), ("image/png", PNG))
            for path in (outside, root / ".." / "outside.png", root / "link.png", root / "linkdir" / "outside.png"):
                with self.assertRaises((ValueError, OSError)):
                    reader(str(path))
            with self.assertRaises(ValueError):
                ptc.local_image_reader(root, max_bytes=10)(str(image))
            image.write_bytes(b"not an image")
            with self.assertRaises(ValueError):
                reader(str(image))
            os.mkfifo(root / "pipe")
            with self.assertRaises(ValueError):
                reader(str(root / "pipe"))


class PTCMessagesTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = Path(__file__).resolve().parents[1] / "examples" / "ptc_messages.py"
        spec = importlib.util.spec_from_file_location("hcu_ptc_example", path)
        cls.example = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.example)

    def client(self, responses):
        class Client:
            def __init__(self):
                self.messages = self
                self.requests = []
            def create(self, **request):
                self.requests.append(json.loads(json.dumps(request)))
                return responses[len(self.requests) - 1]
        return Client()

    def test_preserves_blocks_all_results_and_container_across_pause(self):
        server = {"type": "server_tool_use", "id": "srvtoolu_one", "name": "code_execution", "input": {"code": "..."}}
        container = {"id": "container_one", "expires_at": "2099-01-01T00:00:00Z"}
        responses = [{"content": [server, event("one", caller=ptc.CODE_EXECUTION_CALLER), event("two")],
                      "stop_reason": "tool_use", "container": container},
                     {"content": [{"type": "text", "text": "continuing"}], "stop_reason": "pause_turn", "container": container},
                     {"content": [{"type": "text", "text": "done"}], "stop_reason": "end_turn"}]
        client = self.client(responses)
        calls = []
        def forward(block):
            calls.append(block)
            return {"type": "tool_result", "tool_use_id": block["id"], "content": "{}"}
        request = {"model": "test-only", "messages": [{"role": "user", "content": "test"}]}
        self.assertEqual(self.example.run_messages(client, request, forward), responses[-1])
        self.assertEqual(len(request["messages"]), 1)
        self.assertEqual(len(calls), 2)
        followup = client.requests[1]
        self.assertEqual(followup["container"], "container_one")
        self.assertEqual(followup["messages"][1]["content"][0], server)
        self.assertEqual(len(followup["messages"][2]["content"]), 2)
        self.assertEqual(client.requests[2]["container"], "container_one")

    def test_missing_expired_container_and_unknown_tools_fail_before_dispatch(self):
        for response in (
            {"content": [event(caller=ptc.CODE_EXECUTION_CALLER)], "stop_reason": "tool_use"},
            {"content": [event()], "stop_reason": "tool_use", "container": {"id": "old", "expires_at": "2000-01-01T00:00:00Z"}},
            {"content": [event(), event("other", name="other_tool")], "stop_reason": "tool_use"},
        ):
            with self.subTest(response=response):
                with self.assertRaises((RuntimeError, ValueError)):
                    self.example.run_messages(self.client([response]), {"messages": []}, lambda block: self.fail("no dispatch"))

    def test_sdk_response_requests_json_serialization(self):
        from datetime import datetime, timezone
        class Response:
            def model_dump(inner, mode="python", exclude_none=False):
                expires = datetime(2099, 1, 1, tzinfo=timezone.utc)
                if mode == "json":
                    expires = expires.isoformat()
                self.assertTrue(exclude_none)
                return {"content": [event(caller=ptc.CODE_EXECUTION_CALLER)], "stop_reason": "tool_use",
                        "container": {"id": "container_one", "expires_at": expires}}
        client = self.client([Response(), {"content": [], "stop_reason": "end_turn"}])
        self.example.run_messages(client, {"messages": []}, lambda block: {"type": "tool_result", "tool_use_id": block["id"], "content": "{}"})
        self.assertEqual(client.requests[1]["container"], "container_one")

    def test_round_limit_and_api_error_do_not_retry(self):
        client = self.client([{"content": [], "stop_reason": "pause_turn"}])
        with self.assertRaises(RuntimeError):
            self.example.run_messages(client, {"messages": []}, lambda block: None, max_rounds=1)
        self.assertEqual(len(client.requests), 1)
        class Broken:
            def create(self, **request):
                raise ConnectionError("API request outcome unknown")
        client.messages = Broken()
        with self.assertRaises(ConnectionError):
            self.example.run_messages(client, {"messages": []}, lambda block: None)

    def test_tool_results_survive_round_limit_and_resume(self):
        calls = []
        def transport(method, params):
            calls.append(params)
            return {"ok": True, "input_emitted": True}
        forwarder = ptc.Forwarder(hcu.Desktop(transport))
        response = {"content": [event(caller=ptc.CODE_EXECUTION_CALLER)], "stop_reason": "tool_use",
                    "container": {"id": "container_one"}}
        client = self.client([response, {"content": [], "stop_reason": "end_turn"}])
        with self.assertRaises(self.example.ContinuationLimit) as caught:
            self.example.run_messages(client, {"messages": []}, forwarder, max_rounds=1)
        pending = caught.exception.request
        self.assertEqual(pending["container"], "container_one")
        self.assertEqual(pending["messages"][-1]["content"][0]["tool_use_id"], "toolu_one")
        self.assertEqual(caught.exception.response, response)
        self.example.run_messages(client, pending, forwarder)
        self.assertEqual(client.requests[1], pending)
        self.assertEqual(len(calls), 1)


if __name__ == "__main__":
    unittest.main()
