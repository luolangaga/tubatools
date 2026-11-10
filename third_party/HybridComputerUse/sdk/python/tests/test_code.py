import asyncio
import base64
import os
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import hcu_sdk as hcu
from hcu_sdk.code import CodeDesktop, OutputError


PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aXioAAAAASUVORK5CYII=")


class CodeTests(unittest.IsolatedAsyncioTestCase):
    def setUp(self):
        self.events = []
        self.calls = []
        self.record = {"ok": True, "input_emitted": True, "completed": 1, "requested": 1}

    async def transport(self, method, params):
        self.calls.append(params)
        return self.record

    def desktop(self, **kwargs):
        return CodeDesktop(self.transport, output=self.events.append, **kwargs)

    async def test_success_silent_then_explicit_record_without_second_call(self):
        d = self.desktop()
        result = await d.click("r1")
        self.assertEqual(self.events, [])
        await d.emit(result)
        self.assertEqual(self.events[0]["record"], self.record)
        self.assertEqual(len(self.calls), 1)

    async def test_caught_hcu_error_still_emits_full_partial_record(self):
        self.record = {"ok": False, "code": "effect_unknown", "message": "partial",
                       "input_emitted": True, "completed": 1, "requested": 3,
                       "evidence": {"effect_unknown": True}, "hint": "inspect"}
        with self.assertRaises(hcu.HcuError):
            await self.desktop().click("r1", request_id="call-one")
        self.assertEqual(self.events[0]["record"], self.record)
        self.assertEqual(self.events[0]["request_id"], "call-one")

    async def test_unknown_transport_retains_request_and_unknown_input(self):
        async def transport(method, params):
            raise hcu.TransportEffectUnknown("daemon changed", "original")
        d = CodeDesktop(transport, output=self.events.append)
        with self.assertRaises(hcu.TransportEffectUnknown):
            await d.click("r1")
        self.assertEqual(self.events[0]["request_id"], "original")
        self.assertIsNone(self.events[0]["input_emitted"])
        self.assertIsNone(self.events[0]["record"])

    async def test_image_reader_required_and_input_facts_preserved(self):
        self.record["screen"] = {"image": "/not-read.png"}
        d = self.desktop()
        result = await d.click("r1")
        with self.assertRaises(OutputError) as caught:
            await d.emit_image(result)
        self.assertTrue(caught.exception.record["input_emitted"])
        self.assertEqual(self.events[-1]["type"], "output_error")
        self.assertEqual(len(self.calls), 1)

    async def test_image_bytes_and_pixel_metadata_reuse_existing_capture(self):
        with tempfile.TemporaryDirectory() as root:
            path = os.path.join(root, "capture.png")
            with open(path, "wb") as stream:
                stream.write(PNG)
            self.record["screen"] = {"image": path, "width": 1, "height": 1}
            # 此处验证图片输出；POSIX 本地 reader 的安全约束由 test_ptc 覆盖。
            reader = mock.Mock(side_effect=lambda path: ("image/png", Path(path).read_bytes()))
            d = self.desktop(image_reader=reader)
            result = await d.screenshot()
            await d.emit_image(result)
            reader.assert_called_once_with(path)
            self.assertEqual(self.events[0]["data"], PNG)
            self.assertEqual(self.events[0]["coordinate_space"], "image_pixels")
            self.assertEqual(len(self.calls), 1)

    async def test_zoom_image_is_labelled_with_its_mapping(self):
        mapping = {"frame": "89abcdef", "region": [600, 60, 680, 100], "width": 1, "height": 1,
                   "scale_x": 0.0125, "scale_y": 0.025}
        with tempfile.TemporaryDirectory() as root:
            path = os.path.join(root, "zoom.png")
            with open(path, "wb") as stream:
                stream.write(PNG)
            self.record.update(images=[path], evidence={"zoom": mapping})
            # 此处验证图片输出；POSIX 本地 reader 的安全约束由 test_ptc 覆盖。
            reader = mock.Mock(side_effect=lambda path: ("image/png", Path(path).read_bytes()))
            d = self.desktop(image_reader=reader)
            result = await d.zoom([600, 60, 680, 100])
            await d.emit_image(result)
            reader.assert_called_once_with(path)
            event = self.events[0]
            self.assertEqual((event["coordinate_space"], event["zoom"]), ("zoom_pixels", mapping))
            self.assertEqual((event["screen"]["width"], event["screen"]["height"]), (1, 1))
            self.assertEqual(self.calls[0]["args"], {"actions": [{"type": "zoom", "region": [600, 60, 680, 100]}]})
            self.assertNotIn("request_id", self.calls[0])
            self.assertEqual(hcu.zoom_point(result, 40, 20), (600 + 40 / 0.0125, 60 + 20 / 0.025))

    async def test_mismatched_image_type_reports_failure(self):
        d = self.desktop(image_reader=lambda path: ("image/jpeg", PNG))
        with self.assertRaises(OutputError):
            await d.emit_image({"screen": {"image": "fixture"}})
        self.assertEqual(self.events[0]["code"], "image_unavailable")

    async def test_async_sink_is_rejected(self):
        async def sink(event):
            self.events.append(event)
        d = CodeDesktop(self.transport, output=sink)
        with self.assertRaisesRegex(OutputError, "output must be synchronous"):
            await d.emit("text")
        self.assertEqual(self.events, [])

    async def test_async_reader_is_rejected(self):
        async def reader(path):
            return "image/png", PNG
        d = self.desktop(image_reader=reader)
        with self.assertRaisesRegex(OutputError, "image_reader must be synchronous"):
            await d.emit_image({"screen": {"image": "fixture"}})
        self.assertEqual(self.events[0]["type"], "output_error")

    async def test_failed_sink_keeps_full_error_record(self):
        self.record = {"ok": False, "code": "effect_unknown", "input_emitted": True,
                       "completed": 1, "requested": 2}
        def sink(event):
            raise RuntimeError("sink offline")
        d = CodeDesktop(self.transport, output=sink)
        with self.assertRaises(hcu.HcuError) as caught:
            await d.click("r1")
        self.assertEqual(caught.exception.record, self.record)
        self.assertEqual(caught.exception.output_error.record, self.record)
        self.assertIn('"input_emitted": true', str(caught.exception.output_error))

    async def test_sink_failure_preserves_transport_unknown_exception(self):
        failure = hcu.TransportEffectUnknown("daemon changed", "original")
        async def transport(method, params):
            raise failure
        def sink(event):
            raise RuntimeError("sink offline")
        d = CodeDesktop(transport, output=sink)
        with self.assertRaises(hcu.TransportEffectUnknown) as caught:
            await d.click("r1")
        self.assertIs(caught.exception, failure)
        self.assertEqual(caught.exception.request_id, "original")
        self.assertIsNone(caught.exception.input_emitted)
        self.assertIsInstance(caught.exception.output_error, OutputError)

    async def test_sink_failure_preserves_cancelled_exception_and_facts(self):
        def sink(event):
            raise RuntimeError("sink offline")
        for record in (None, {"ok": True, "input_emitted": True, "completed": 1, "requested": 2}):
            failure = hcu.CallCancelled("original", None if record is None else True, record)
            async def transport(method, params):
                raise failure
            d = CodeDesktop(transport, output=sink)
            with self.assertRaises(hcu.CallCancelled) as caught:
                await d.click("r1", request_id="original")
            self.assertEqual(caught.exception.request_id, "original")
            self.assertEqual(caught.exception.record, record)
            self.assertEqual(caught.exception.input_emitted, None if record is None else True)
            self.assertIsInstance(caught.exception.output_error, OutputError)

    def test_example_rejects_python310_before_runtime_setup(self):
        path = Path(__file__).resolve().parents[1] / "examples/open_gcua_worker.py"
        spec = importlib.util.spec_from_file_location("hcu_example", path)
        example = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(example)
        with mock.patch.object(example.sys, "version_info", (3, 10, 0)):
            with self.assertRaisesRegex(RuntimeError, "Python 3.11"):
                example.install(object(), transport=None, image_reader=None)
            with self.assertRaisesRegex(RuntimeError, "Python 3.11"):
                example.main([])

    async def test_cancelled_call_drains_and_emits_record_before_raising(self):
        entered, release = asyncio.Event(), asyncio.Event()
        async def transport(method, params):
            entered.set()
            await release.wait()
            return self.record
        d = CodeDesktop(transport, output=self.events.append)
        task = asyncio.create_task(d.click("r1", request_id="cancel-one"))
        await entered.wait()
        task.cancel()
        await asyncio.sleep(0)
        self.assertFalse(task.done())
        release.set()
        with self.assertRaises(hcu.CallCancelled):
            await task
        self.assertEqual(self.events[0]["code"], "cancelled")
        self.assertEqual(self.events[0]["record"], self.record)
        self.assertTrue(self.events[0]["input_emitted"])

if __name__ == "__main__":
    unittest.main()
