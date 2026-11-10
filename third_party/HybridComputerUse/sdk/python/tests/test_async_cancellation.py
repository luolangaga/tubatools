"""Cancellation barriers with controlled transports; no desktop input."""

import asyncio
import json
import os
import socket
import sys
import threading
import unittest
from unittest import mock

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
import hcu_sdk as hcu


async def turn():
    ready = asyncio.get_running_loop().create_future()
    asyncio.get_running_loop().call_soon(ready.set_result, None)
    await ready


def action(name):
    return [{"type": "type", "text": name}]


class AsyncCancellationTests(unittest.IsolatedAsyncioTestCase):
    async def test_sync_running_and_queued_cancellation(self):
        loop = asyncio.get_running_loop()
        started, release = asyncio.Event(), threading.Event()
        inputs = []

        def transport(method, params):
            name = params["args"]["actions"][0]["text"]
            if name == "first":
                loop.call_soon_threadsafe(started.set)
                if not release.wait(5):
                    raise TimeoutError("test did not release the transport")
            inputs.append(name)
            return {"ok": True, "input_emitted": True, "completed": 1, "requested": 1}

        desktop = hcu.AsyncDesktop(transport)
        first = asyncio.create_task(desktop.gui(action("first"), request_id="first-id"))
        try:
            await asyncio.wait_for(started.wait(), 5)
            queued = asyncio.create_task(desktop.gui(action("cancelled"), request_id="queued-id"))
            await turn()
            queued.cancel()
            with self.assertRaises(hcu.CallCancelled) as caught:
                await queued
            self.assertEqual(caught.exception.request_id, "queued-id")
            self.assertIs(caught.exception.input_emitted, False)
            self.assertIsNone(caught.exception.record)
            first.cancel()
            await turn()
            first.cancel()
            await turn()
            self.assertFalse(first.done(), "running input must settle before cancellation returns")
            third = asyncio.create_task(desktop.gui(action("third")))
            await turn()
            self.assertEqual(inputs, [])
            release.set()
            with self.assertRaises(hcu.CallCancelled) as caught:
                await first
            self.assertEqual(caught.exception.request_id, "first-id")
            self.assertIs(caught.exception.input_emitted, True)
            self.assertEqual(caught.exception.record.completed, 1)
            self.assertTrue((await third).ok)
            self.assertEqual(inputs, ["first", "third"])
        finally:
            release.set()

    async def test_async_transport_settles_and_retains_partial_record(self):
        started, release = asyncio.Event(), asyncio.Event()
        transport_cancelled = False

        async def transport(method, params):
            nonlocal transport_cancelled
            started.set()
            try:
                await release.wait()
            except asyncio.CancelledError:
                transport_cancelled = True
                raise
            return {"ok": False, "code": "runtime_error", "input_emitted": True,
                    "completed": 1, "requested": 2, "message": "second action failed"}

        desktop = hcu.AsyncDesktop(transport)
        task = asyncio.create_task(desktop.gui(action("first"), request_id="partial-id"))
        await started.wait()
        task.cancel()
        await turn()
        task.cancel()
        await turn()
        self.assertFalse(task.done())
        release.set()
        with self.assertRaises(hcu.CallCancelled) as caught:
            await task
        self.assertFalse(transport_cancelled)
        self.assertEqual(caught.exception.request_id, "partial-id")
        self.assertIs(caught.exception.input_emitted, True)
        self.assertEqual(caught.exception.record.completed, 1)
        self.assertEqual(caught.exception.record.requested, 2)
        self.assertFalse(caught.exception.record.ok)

    async def test_returned_future_is_drained_on_cancellation(self):
        result = asyncio.get_running_loop().create_future()
        started = asyncio.Event()

        async def transport(method, params):
            started.set()
            return result

        desktop = hcu.AsyncDesktop(transport)
        task = asyncio.create_task(desktop.gui(action("future"), request_id="future-id"))
        await started.wait()
        task.cancel()
        await turn()
        task.cancel()
        await turn()
        self.assertFalse(task.done())
        self.assertFalse(result.cancelled())
        result.set_result({"ok": True, "input_emitted": True})
        with self.assertRaises(hcu.CallCancelled) as caught:
            await task
        self.assertEqual(caught.exception.request_id, "future-id")
        self.assertIs(caught.exception.input_emitted, True)

    async def test_cancel_during_retry_backoff_does_not_send_again(self):
        backoff = asyncio.Event()
        attempts = []

        async def transport(method, params):
            attempts.append(params)
            raise hcu.TransportClosed("reply dropped", retry_safe=True)

        async def pause(delay):
            backoff.set()
            await asyncio.Event().wait()

        desktop = hcu.AsyncDesktop(transport, retries=2)
        with mock.patch("hcu_sdk.asyncio.sleep", pause):
            task = asyncio.create_task(desktop.gui(action("retry"), request_id="retry-id"))
            await backoff.wait()
            task.cancel()
            with self.assertRaises(hcu.CallCancelled) as caught:
                await task
        self.assertEqual(len(attempts), 1)
        self.assertIsNone(caught.exception.input_emitted)
        self.assertEqual(caught.exception.request_id, "retry-id")

    async def test_cancelled_queued_retry_keeps_previous_effect_unknown(self):
        backoff, retry_ready = asyncio.Event(), asyncio.Event()
        blocker_started, blocker_release = asyncio.Event(), asyncio.Event()
        attempts = []

        async def transport(method, params):
            name = params["args"]["actions"][0]["text"]
            attempts.append(name)
            if name == "first":
                raise hcu.TransportClosed("reply dropped", retry_safe=True)
            blocker_started.set()
            await blocker_release.wait()
            return {"ok": True, "input_emitted": True}

        async def pause(delay):
            backoff.set()
            await retry_ready.wait()

        desktop = hcu.AsyncDesktop(transport, retries=2)
        with mock.patch("hcu_sdk.asyncio.sleep", pause):
            task = asyncio.create_task(desktop.gui(action("first"), request_id="queued-retry"))
            await backoff.wait()
            blocker = asyncio.create_task(desktop.gui(action("blocker")))
            await blocker_started.wait()
            retry_ready.set()
            await turn()
            task.cancel()
            with self.assertRaises(hcu.CallCancelled) as caught:
                await task
            blocker_release.set()
            await blocker
        self.assertIsNone(caught.exception.input_emitted)
        self.assertEqual(caught.exception.request_id, "queued-retry")
        self.assertEqual(attempts, ["first", "blocker"])

    async def test_cancelled_dropped_reply_is_unknown_and_not_retried(self):
        loop = asyncio.get_running_loop()
        started, release = asyncio.Event(), threading.Event()
        calls = []

        def transport(method, params):
            calls.append(params)
            loop.call_soon_threadsafe(started.set)
            if not release.wait(5):
                raise TimeoutError("test did not release the transport")
            raise hcu.TransportClosed("reply dropped", retry_safe=True)

        desktop = hcu.AsyncDesktop(transport, retries=2)
        task = asyncio.create_task(desktop.gui(action("first"), request_id="unknown-id"))
        try:
            await asyncio.wait_for(started.wait(), 5)
            task.cancel()
            await turn()
            self.assertFalse(task.done())
            release.set()
            with self.assertRaises(hcu.CallCancelled) as caught:
                await task
            self.assertIsNone(caught.exception.input_emitted)
            self.assertEqual(caught.exception.request_id, "unknown-id")
            self.assertIsInstance(caught.exception.transport_error, hcu.TransportClosed)
            self.assertEqual(len(calls), 1)
        finally:
            release.set()

    async def test_cell_cleanup_cancelling_all_tasks_still_drains_thread(self):
        loop = asyncio.get_running_loop()
        started, release = asyncio.Event(), threading.Event()
        inputs = []

        def transport(method, params):
            loop.call_soon_threadsafe(started.set)
            if not release.wait(5):
                raise TimeoutError("test did not release the transport")
            inputs.append("settled")
            return {"ok": True, "input_emitted": True}

        desktop = hcu.AsyncDesktop(transport)
        task = asyncio.create_task(desktop.gui(action("first"), request_id="cell-id"))
        try:
            await asyncio.wait_for(started.wait(), 5)
            owner = asyncio.current_task()
            for _ in range(2):
                for pending in asyncio.all_tasks():
                    if pending is not owner:
                        pending.cancel()
                await turn()
                self.assertFalse(task.done(), "cell cleanup must not abandon an executor thread")
            self.assertEqual(inputs, [])
            release.set()
            with self.assertRaises(hcu.CallCancelled) as caught:
                await task
            self.assertEqual(inputs, ["settled"])
            self.assertIs(caught.exception.input_emitted, True)
            self.assertEqual(caught.exception.request_id, "cell-id")
        finally:
            release.set()

    async def test_socket_queued_cancellation_never_reaches_wire(self):
        loop = asyncio.get_running_loop()
        started, release = asyncio.Event(), threading.Event()
        client, server = socket.socketpair()
        client.settimeout(5)
        server.settimeout(5)
        requests, errors = [], []
        transport = hcu.SocketTransport(start=False)
        transport._open = lambda: setattr(transport, "_sock", client)
        blocked = asyncio.Event()

        class TrackedRLock:
            def __init__(self):
                self.lock = threading.RLock()

            def __enter__(self):
                if not self.lock.acquire(blocking=False):
                    loop.call_soon_threadsafe(blocked.set)
                    self.lock.acquire()
                return self

            def __exit__(self, *args):
                self.lock.release()

        transport._lock = TrackedRLock()

        def peer():
            try:
                with server.makefile("rb") as stream:
                    for line in stream:
                        request = json.loads(line)
                        if request["method"] == "hello":
                            result = {"protocol": hcu.PROTOCOL, "session": "default",
                                      "instance_id": "cancellation-fixture"}
                        else:
                            name = request["params"]["args"]["actions"][0]["text"]
                            requests.append(name)
                            if name == "first":
                                loop.call_soon_threadsafe(started.set)
                                if not release.wait(5):
                                    raise TimeoutError("test did not release peer")
                            result = {"ok": True, "input_emitted": True}
                        server.sendall((json.dumps({"jsonrpc": "2.0", "id": request["id"],
                                                    "result": result}) + "\n").encode())
            except BaseException as error:
                errors.append(error)
            finally:
                server.close()

        worker = threading.Thread(target=peer, daemon=True)
        worker.start()
        desktop = hcu.AsyncDesktop(transport)
        first = asyncio.create_task(desktop.gui(action("first")))
        try:
            await asyncio.wait_for(started.wait(), 5)
            other_desktop = hcu.AsyncDesktop(transport)
            queued = asyncio.create_task(other_desktop.gui(action("cancelled")))
            await asyncio.wait_for(blocked.wait(), 5)
            queued.cancel()
            first.cancel()
            await turn()
            self.assertFalse(first.done())
            release.set()
            with self.assertRaises(hcu.CallCancelled) as caught:
                await queued
            self.assertIs(caught.exception.input_emitted, False)
            self.assertIsNone(caught.exception.record)
            with self.assertRaises(hcu.CallCancelled):
                await first
            self.assertTrue((await desktop.gui(action("last"))).ok)
            self.assertEqual(requests, ["first", "last"])
        finally:
            release.set()
            transport.close()
            worker.join(5)
        self.assertFalse(worker.is_alive())
        self.assertEqual(errors, [])


class EventLoopTests(unittest.TestCase):
    def test_instance_can_be_reused_after_previous_loop_closes(self):
        desktop = hcu.AsyncDesktop(lambda method, params: {"ok": True})
        self.assertTrue(asyncio.run(desktop.apps()).ok)
        self.assertTrue(asyncio.run(desktop.apps()).ok)


if __name__ == "__main__":
    unittest.main()
