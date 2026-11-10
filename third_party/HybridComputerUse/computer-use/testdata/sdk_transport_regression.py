"""SDK wire regressions, run by go test with a real daemon/fake desktop.

The socketpair cases exercise framing and caller ownership without desktop
input. The daemon cases independently count provider input in the Go test.
"""

import asyncio
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import socket
import sys
import threading
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "sdk" / "python"))
import hcu_sdk as hcu


class WirePeer:
    """A real byte stream with a deterministic, request-driven peer."""

    def __init__(self, respond, hello_result=None):
        self.client, self.server = socket.socketpair()
        self.client.settimeout(5)
        self.server.settimeout(5)
        self.requests = []
        self.errors = []
        self.respond = respond
        self.hello_result = hello_result
        self.transport = hcu.SocketTransport(start=False, timeout=5)
        self.transport._open = self.open
        self.worker = threading.Thread(target=self.serve, daemon=True)
        self.worker.start()

    def open(self):
        self.transport._sock = self.client
        self.transport._buffer = b""

    def serve(self):
        try:
            with self.server.makefile("rb") as reader:
                for line in reader:
                    request = json.loads(line)
                    self.requests.append(request)
                    if request["method"] == "hello":
                        result = self.hello_result or {
                            "protocol": hcu.PROTOCOL, "session": self.transport.session,
                            "pid": 100,
                        }
                        response = {"jsonrpc": "2.0", "id": request["id"], "result": result}
                    else:
                        response = self.respond(request)
                    if response is None:
                        break
                    data = response if isinstance(response, bytes) else (
                        json.dumps(response) + "\n").encode()
                    self.server.sendall(data)
        except (BrokenPipeError, ConnectionResetError):
            pass  # A rejected frame closes the client before another response.
        except Exception as error:
            self.errors.append(error)
        finally:
            self.server.close()

    def close(self):
        self.transport.close()
        self.worker.join(6)
        if self.worker.is_alive():
            raise AssertionError("wire peer did not finish")
        if self.errors:
            raise AssertionError(self.errors)


class WireTests(unittest.TestCase):
    def test_custom_transport_retry_requires_explicit_guarantee_for_input(self):
        for asynchronous in (False, True):
            for kind in ("input", "read", "wait", "safe-input"):
                with self.subTest(asynchronous=asynchronous, kind=kind):
                    calls = []

                    def transport(method, params):
                        calls.append(dict(params))
                        if len(calls) == 1:
                            raise hcu.TransportClosed("lost reply", retry_safe=kind == "safe-input")
                        return {"ok": True}

                    async def async_transport(method, params):
                        return transport(method, params)

                    args = ({"mode": "apps"} if kind == "read" else {
                        "mode": "act", "actions": [{"type": "wait" if kind == "wait" else "click", "ref": "r1"}]})
                    desktop = (hcu.AsyncDesktop(async_transport) if asynchronous else hcu.Desktop(transport))
                    try:
                        result = desktop.call("AX", args)
                        if asynchronous:
                            result = asyncio.run(result)
                    except hcu.TransportEffectUnknown as error:
                        self.assertEqual(kind, "input")
                        self.assertIsNone(error.input_emitted)
                        self.assertEqual(len(calls), 1)
                    else:
                        self.assertNotEqual(kind, "input")
                        self.assertTrue(result.ok)
                        self.assertEqual(len(calls), 2)
                        self.assertEqual(calls[0], calls[1])
                    self.assertTrue(all("daemon_instance" not in call for call in calls))

    def test_hello_identity_mismatch_closes_before_business_call(self):
        for hello in ({"protocol": 2, "session": "default"},
                      {"protocol": hcu.PROTOCOL, "session": "wrong-session"},
                      {"protocol": hcu.PROTOCOL, "session": "default", "instance_id": 1},
                      {"protocol": hcu.PROTOCOL, "session": "default", "instance_id": ""}):
            with self.subTest(hello=hello):
                peer = WirePeer(lambda request: {"id": request["id"],
                                                "result": {"ok": True}}, hello_result=hello)
                try:
                    with self.assertRaises(RuntimeError):
                        hcu.Desktop(peer.transport).click("r1")
                    self.assertIsNone(peer.transport._sock)
                    self.assertIsNone(peer.transport.hello)
                    self.assertEqual([r["method"] for r in peer.requests], ["hello"])
                finally:
                    peer.close()

    def test_async_schema_allows_event_loop_to_make_progress(self):
        release = threading.Event()

        def transport(method, params):
            self.assertEqual(method, "schema")
            if not release.wait(timeout=5):
                raise AssertionError("schema transport blocked the event loop callback")
            return {"protocol": hcu.PROTOCOL}

        async def run():
            asyncio.get_running_loop().call_soon(release.set)
            return await hcu.AsyncDesktop(transport).schema()

        self.assertEqual(asyncio.run(run()).protocol, hcu.PROTOCOL)

    def test_concurrent_async_calls_on_new_connection(self):
        self.check_concurrent_calls(connected=False)

    def test_concurrent_async_calls_on_existing_connection(self):
        self.check_concurrent_calls(connected=True)

    def check_concurrent_calls(self, connected):
        def respond(request):
            return {"jsonrpc": "2.0", "id": request["id"], "result": {
                "ok": True, "tag": request["params"]["args"]["tag"],
            }}

        peer = WirePeer(respond)
        if connected:
            peer.transport("call", {"args": {"tag": "warmup"}})
        count = 8
        ready = threading.Barrier(count)

        def simultaneous_call(method, params):
            ready.wait(timeout=5)
            return peer.transport(method, params)

        async def run():
            asyncio.get_running_loop().set_default_executor(ThreadPoolExecutor(max_workers=count))
            # Each AsyncDesktop queues its own calls before worker submission.
            # Separate clients retain eight simultaneous workers against the
            # same SocketTransport, which owns framing and response IDs.
            desktops = [hcu.AsyncDesktop(simultaneous_call, retries=0) for _ in range(count)]
            records = await asyncio.gather(*(
                desktop.call("AX", {"tag": str(index)})
                for index, desktop in enumerate(desktops)))
            return [record.tag for record in records]

        try:
            self.assertEqual(asyncio.run(run()), [str(index) for index in range(count)])
            self.assertEqual(sum(r["method"] == "hello" for r in peer.requests), 1)
            ids = [r["id"] for r in peer.requests]
            self.assertEqual(len(ids), len(set(ids)))
        finally:
            peer.close()

    def test_eof_discards_partial_frame_and_connection(self):
        def respond(request):
            peer.server.sendall(b'{"jsonrpc":"2.0",')
            return None

        peer = WirePeer(respond)
        try:
            with self.assertRaises(hcu.TransportClosed):
                peer.transport("schema", {})
            self.assertIsNone(peer.transport._sock)
            self.assertEqual(peer.transport._buffer, b"")
            self.assertIsNone(peer.transport.hello)
        finally:
            peer.close()

    def test_wrong_response_ids_fail_without_retrying_input(self):
        for wrong_id in (999, "2", True, None):
            with self.subTest(response_id=wrong_id):
                peer = WirePeer(lambda request: {
                    "jsonrpc": "2.0", "id": wrong_id,
                    "result": {"ok": True, "input_emitted": True},
                })
                try:
                    with self.assertRaises(RuntimeError):
                        hcu.Desktop(peer.transport).click("r1")
                    self.assertIsNone(peer.transport._sock)
                    self.assertEqual(sum(r["method"] == "call" for r in peer.requests), 1)
                finally:
                    peer.close()

    def test_invalid_frames_fail_without_retrying_input(self):
        for frame in (b"not-json\n", b"[]\n", b'{"jsonrpc":"2.0","id":2}\n'):
            with self.subTest(frame=frame):
                peer = WirePeer(lambda request: frame)
                try:
                    with self.assertRaises(RuntimeError):
                        hcu.Desktop(peer.transport).click("r1")
                    self.assertIsNone(peer.transport._sock)
                    self.assertEqual(sum(r["method"] == "call" for r in peer.requests), 1)
                finally:
                    peer.close()


class RejectedDisplayTests(unittest.TestCase):
    def test_rejected_hello_cannot_be_bypassed_by_next_input(self):
        with hcu.Desktop(hcu.SocketTransport(path=os.environ["HCU_SOCKET"],
                                              start=False)) as setup:
            tree = setup.observe(setup.apps().windows[0].ref)
            button = next(e for e in tree.elements if e.role == "button" and e.ref)
        os.environ["DISPLAY"] = ":sdk-transport-wrong-display"
        transport = hcu.SocketTransport(path=os.environ["HCU_SOCKET"], start=False)
        desktop = hcu.Desktop(transport, retries=0)
        try:
            outcomes = []
            for call in (desktop.apps, desktop.apps, lambda: desktop.click(button.ref)):
                try:
                    outcomes.append(call())
                except RuntimeError as error:
                    outcomes.append(str(error))
            for outcome in outcomes:
                self.assertIsInstance(outcome, str)
                self.assertIn("this daemon serves display", outcome)
            self.assertIsNone(transport._sock)
            self.assertIsNone(transport.hello)
        finally:
            desktop.close()


class ReplayTests(unittest.TestCase):
    def test_lost_reply_replays_same_daemon_result_without_second_input(self):
        transport = hcu.SocketTransport(path=os.environ["HCU_SOCKET"],
                                        session="sdk-transport-replay", start=False)
        desktop = hcu.Desktop(transport)
        try:
            window = desktop.apps().windows[0].ref
            tree = desktop.observe(window)
            button = next(e for e in tree.elements if e.role == "button" and e.ref)
            requests = []

            def discard_first_reply(method, params):
                result = transport(method, params)
                requests.append(params.get("request_id"))
                if len(requests) == 1:
                    transport.close()
                    # SocketTransport retains the instance binding in params
                    # and refuses replay if reconnection reaches another daemon.
                    params["replay_only"] = True
                    raise hcu.TransportClosed("test discards reply after completed input", retry_safe=True)
                return result

            result = hcu.Desktop(discard_first_reply).click(button.ref)
            self.assertEqual(len(requests), 2)
            self.assertTrue(requests[0])
            self.assertEqual(requests[0], requests[1])
            self.assertTrue(result.replayed)
            self.assertTrue(result.input_emitted)
        finally:
            desktop.close()


if __name__ == "__main__":
    suite = {"wire": WireTests, "reject-display": RejectedDisplayTests,
             "replay": ReplayTests}[sys.argv.pop(1)]
    unittest.main(defaultTest=suite.__name__, verbosity=2)
