#!/usr/bin/env python3
"""Linux provider GUI capture, screenshot encoding, pixel evidence, and settle wait tests."""

from runtime_test_support import *  # noqa: F401,F403
from runtime_tests.gui_test_helpers import patch_gui_capture
import base64
import random


class GUICapturePipelineTests(unittest.TestCase):
    def patch(self, name, **kwargs):
        patcher = mock.patch.object(runtime, name, **kwargs)
        value = patcher.start()
        self.addCleanup(patcher.stop)
        return value

    def setUp(self):
        self.bounds = runtime.frame(0, 0, 100, 100)
        self.layout = {"desktop": self.bounds, "activeWindow": "a", "windows": []}
        published = mock.patch.dict(gui_dispatch._published_layouts, clear=True)
        published.start()
        self.addCleanup(published.stop)
        self.frame_id = runtime.desktop_layout_frame_id(self.layout)
        self.events = []
        self.frame = {"data": b"abc", "stride": 3, "channels": 3,
                      "width": 1, "height": 1}
        self.bounds_mock = self.patch("desktop_bounds", return_value=self.bounds)
        self.layout_mock = self.patch(
            "desktop_layout_state", side_effect=self.sample_layout)
        self.capture = self.patch("capture_gui_pixels", side_effect=self.capture_pixels)
        self.encoder = self.patch("encoded_screenshot", side_effect=self.encode_pixels)
        self.execute = self.patch(
            "execute_gui_action", side_effect=lambda action: self.events.append("action"))
        self.pixels = self.patch("pixbuf_sample", side_effect=self.sample_pixels)
        self.patch("scale_to_model", side_effect=lambda pixbuf, _bounds: pixbuf)
        self.wait = self.patch("wait_for_gui_settle", return_value={
            "stable": True, "elapsed_ms": 300, "polls": 3,
            "stable_samples": 4, "stable_for_ms": 300, "pixels": self.frame})

    def sample_layout(self, _bounds):
        self.events.append("layout")
        return self.layout

    def capture_pixels(self, _bounds):
        self.events.append("capture")
        return mock.sentinel.before if self.capture.call_count == 1 else mock.sentinel.after

    def sample_pixels(self, _bounds):
        self.events.append("pixels")
        return self.frame

    def encode_pixels(self, pixbuf):
        self.events.append("encode")
        return {"data": str(pixbuf), "mimeType": "image/png",
                "encoding": "png-compression-3"}

    def click(self):
        return runtime.perform_gui([{"type": "click", "x": 1, "y": 2}],
                                   frame_id=self.frame_id)

    def test_screenshot_captures_and_encodes_once_after_layout_bracket(self):
        response = runtime.perform_gui([{"type": "screenshot"}])
        self.assertTrue(response["ok"])
        self.assertEqual(self.events, ["layout", "capture", "layout", "action", "encode"])
        self.capture.assert_called_once()
        self.encoder.assert_called_once_with(mock.sentinel.before)
        self.pixels.assert_not_called()
        self.wait.assert_not_called()
        self.assertIsNone(response["quiescent"])
        self.assertIsNone(response["changed_since_before"])
        self.assertIn("without quiescence polling or a before/after visual comparison", response["text"])

    def test_success_encodes_only_final_frame_after_pixel_bracket(self):
        response = self.click()
        self.assertTrue(response["ok"])
        self.assertTrue(response["quiescent"])
        self.assertEqual(self.capture.call_count, 2)
        self.encoder.assert_called_once_with(mock.sentinel.after)
        # The final grab is both the delivered image and the "after" sample;
        # no raw desktop sample is taken between it and its encoding.
        self.assertEqual(self.events[-6:],
                         ["action", "layout", "capture", "layout", "encode", "pixels"])

    def test_cache_is_call_local(self):
        first = runtime.perform_gui([{"type": "screenshot"}])
        second = runtime.perform_gui([{"type": "screenshot"}])
        self.assertIsNot(first["snapshot"], second["snapshot"])
        self.assertEqual(self.capture.call_count, 2)
        self.assertEqual(self.encoder.call_count, 2)

    def test_capture_owns_metadata_and_caches_encoding(self):
        captured = runtime.capture_gui_snapshot(self.bounds)
        self.encoder.assert_not_called()
        self.layout["activeWindow"] = "changed"
        self.bounds["width"] = 200
        first = captured.encode()
        first["desktopLayout"]["windows"].append("changed")
        second = captured.encode()
        self.assertEqual(second["desktopLayout"]["activeWindow"], "a")
        self.assertEqual(second["desktopLayout"]["windows"], [])
        self.assertEqual(second["width"], 100)
        self.assertFalse(captured.matches_bounds(self.bounds))
        self.encoder.assert_called_once()

    def test_layout_retry_never_encodes_discarded_capture(self):
        changed = dict(self.layout, activeWindow="b")
        self.layout_mock.side_effect = [self.layout, changed, changed, changed]
        response = runtime.perform_gui([{"type": "screenshot"}])
        self.assertTrue(response["ok"])
        self.assertEqual(self.capture.call_count, 2)
        self.encoder.assert_called_once_with(mock.sentinel.after)
        self.assertEqual(response["snapshot"]["frameId"],
                         runtime.desktop_layout_frame_id(changed))

    def test_stale_initial_layout_returns_coherent_frame_before_input(self):
        response = runtime.perform_gui([{"type": "click", "x": 1, "y": 2}],
                                       frame_id="old-layout")
        self.assertFalse(response["ok"])
        self.execute.assert_not_called()
        self.encoder.assert_called_once_with(mock.sentinel.before)
        self.assertEqual(response["snapshot"]["frameId"], self.frame_id)

    def test_first_coordinate_uses_the_pre_action_bracket_without_resampling(self):
        response = self.click()
        self.assertTrue(response["ok"])
        self.execute.assert_called_once()
        # Two layout samples bracket the pre-action grab; the first coordinate
        # action is emitted right after them. Two more bracket the final grab.
        self.assertEqual(self.layout_mock.call_count, 4)
        self.assertEqual(self.events[:5],
                         ["layout", "capture", "layout", "pixels", "action"])

    def test_live_layout_rejects_later_coordinate_with_fresh_evidence(self):
        # A window that opens over the second click's point after the first.
        changed = dict(self.layout, activeWindow="b", windows=[
            {"id": "0xb", "x": 0, "y": 0, "width": 10, "height": 10}])
        self.layout_mock.side_effect = [self.layout, self.layout,
                                        changed, changed, changed]
        response = runtime.perform_gui([
            {"type": "click", "x": 1, "y": 2},
            {"type": "click", "x": 3, "y": 4}], frame_id=self.frame_id)
        self.assertFalse(response["ok"])
        self.execute.assert_called_once()
        self.assertTrue(response["mutation_unknown"])
        self.encoder.assert_called_once_with(mock.sentinel.after)
        self.assertEqual(response["snapshot"]["frameId"],
                         runtime.desktop_layout_frame_id(changed))

    # Point-based staleness: a coordinate still lands where it did when the
    # same window, with the same geometry, is topmost under it.
    def layouts(self, *window_lists):
        return [dict(self.layout, windows=[
            dict(zip(("id", "x", "y", "width", "height"), window))
            for window in windows]) for windows in window_lists]

    def publish(self, layout):
        """Return the frame id of a screenshot GUI returned with layout."""
        self.layout_mock.side_effect = [layout, layout]
        response = runtime.perform_gui([{"type": "screenshot"}])
        self.assertTrue(response["ok"])
        self.events.clear()
        self.execute.reset_mock()
        return response["snapshot"]["frameId"]

    def click_after(self, published, now, *points):
        frame_id = self.publish(published)
        self.layout_mock.side_effect = [now] * 8
        return runtime.perform_gui(
            [{"type": "click", "x": x, "y": y} for x, y in points],
            frame_id=frame_id)

    def test_unrelated_window_change_does_not_reject(self):
        before, after = self.layouts(
            [("0xa", 0, 0, 50, 50), ("0xb", 60, 60, 20, 20)],
            [("0xa", 0, 0, 50, 50), ("0xb", 70, 55, 25, 20), ("0xc", 80, 0, 10, 10)])
        self.assertNotEqual(runtime.desktop_layout_frame_id(before),
                            runtime.desktop_layout_frame_id(after))
        response = self.click_after(before, after, (10, 10))
        self.assertTrue(response["ok"], response.get("error"))
        self.execute.assert_called_once()

    def test_window_under_the_point_changing_rejects_before_input(self):
        before = self.layouts([("0xa", 0, 0, 50, 50)])[0]
        for name, windows in [
            ("moved", [("0xa", 5, 0, 50, 50)]),
            ("resized", [("0xa", 0, 0, 40, 50)]),
            ("covered", [("0xa", 0, 0, 50, 50), ("0xc", 5, 5, 10, 10)]),
            ("closed", []),
            ("raised other", [("0xa", 0, 0, 50, 50), ("0xb", 0, 0, 30, 30)]),
        ]:
            with self.subTest(name):
                response = self.click_after(before, self.layouts(windows)[0], (10, 10))
                self.assertFalse(response["ok"])
                self.assertEqual(response["failureCode"], "stale_gui_frame")
                self.execute.assert_not_called()

    def test_point_outside_any_window_stays_valid_until_one_covers_it(self):
        before, unrelated, covering = self.layouts(
            [("0xa", 0, 0, 20, 20)],
            [("0xa", 0, 0, 30, 30)],
            [("0xa", 0, 0, 30, 30), ("0xd", 50, 50, 30, 30)])
        self.assertTrue(self.click_after(before, unrelated, (60, 60))["ok"])
        self.assertFalse(self.click_after(before, covering, (60, 60))["ok"])

    def test_every_point_of_a_drag_is_checked(self):
        before, after = self.layouts(
            [("0xa", 0, 0, 50, 50)],
            [("0xa", 0, 0, 50, 50), ("0xe", 70, 70, 20, 20)])
        frame_id = self.publish(before)
        self.layout_mock.side_effect = [after] * 8
        response = runtime.perform_gui([{"type": "drag", "path": [
            {"x": 10, "y": 10}, {"x": 75, "y": 75}]}], frame_id=frame_id)
        self.assertFalse(response["ok"])
        self.execute.assert_not_called()

    def test_frame_this_provider_did_not_publish_rejects_any_layout_change(self):
        # After a provider restart the core still holds its old frame id.
        before, after = self.layouts(
            [("0xa", 0, 0, 50, 50), ("0xb", 60, 60, 20, 20)],
            [("0xa", 0, 0, 50, 50), ("0xb", 70, 60, 20, 20)])
        frame_id = self.publish(before)
        gui_dispatch._published_layouts.clear()
        self.layout_mock.side_effect = [after] * 8
        response = runtime.perform_gui([{"type": "click", "x": 10, "y": 10}],
                                       frame_id=frame_id)
        self.assertFalse(response["ok"])
        self.execute.assert_not_called()

    def test_desktop_resize_rejects(self):
        before = self.layouts([("0xa", 0, 0, 50, 50)])[0]
        after = dict(before, desktop=dict(self.bounds, width=90))
        self.assertFalse(self.click_after(before, after, (10, 10))["ok"])

    def test_mid_batch_unrelated_change_continues_and_target_change_stops(self):
        before, unrelated, covering = self.layouts(
            [("0xa", 0, 0, 50, 50), ("0xb", 60, 60, 20, 20)],
            [("0xa", 0, 0, 50, 50), ("0xb", 65, 60, 20, 20)],
            [("0xa", 0, 0, 50, 50), ("0xb", 0, 0, 20, 20)])
        for later, ok, calls in [(unrelated, True, 2), (covering, False, 1)]:
            with self.subTest(ok=ok):
                frame_id = self.publish(before)
                # Bracket of the pre-action grab, then the re-sample before
                # the second click, then the final bracket.
                self.layout_mock.side_effect = [before, before] + [later] * 6
                response = runtime.perform_gui([
                    {"type": "click", "x": 10, "y": 10},
                    {"type": "click", "x": 12, "y": 12}], frame_id=frame_id)
                self.assertEqual(response["ok"], ok)
                self.assertEqual(self.execute.call_count, calls)
                if not ok:
                    self.assertIn("changed during the batch", response["error"])
                    self.assertTrue(response["mutation_unknown"])

    # Zoom is look-only and bound to the frame its region came from.
    def zoom_after(self, published, now, region, frame_id=None):
        frame_id = self.publish(published) if frame_id is None else frame_id
        self.layout_mock.side_effect = [now] * 8
        self.zoomed = []
        def zoom(_capture, geometry):
            self.zoomed.append(geometry)
            return {"data": "zoom-png", "mimeType": "image/png", "encoding": "png"}
        with mock.patch.object(runtime.CapturedGUISnapshot, "zoom", zoom):
            return runtime.perform_gui([{"type": "zoom", "region": region}],
                                       frame_id=frame_id)

    def test_zoom_returns_a_region_of_the_named_frame_without_input(self):
        layout = self.layouts([("0xa", 0, 0, 50, 50)])[0]
        response = self.zoom_after(layout, layout, [10, 10, 40, 30])
        self.assertTrue(response["ok"], response.get("error"))
        self.execute.assert_not_called()
        self.assertFalse(response["input_events_emitted"])
        zoom = response["guiResult"]["zoom"]
        self.assertEqual(zoom["region"], [10.0, 10.0, 40.0, 30.0])
        self.assertEqual((zoom["width"], zoom["height"]), (120, 80))
        # The zoom image is not a frame and is never remembered as one.
        self.assertNotIn("frameId", response["snapshot"])
        self.assertEqual(response["snapshot"]["coordinateSpace"], "zoom_image_px")
        published = dict(gui_dispatch._published_layouts)
        self.assertEqual(dict(gui_dispatch._published_layouts), published)

    def test_zoom_rejects_an_unpublished_frame(self):
        before, after = self.layouts(
            [("0xa", 0, 0, 50, 50)], [("0xa", 0, 0, 50, 50), ("0xb", 60, 60, 20, 20)])
        response = self.zoom_after(before, after, [10, 10, 40, 30],
                                   frame_id="not-a-published-frame")
        self.assertFalse(response["ok"])
        self.assertEqual(response["failureCode"], "stale_gui_frame")
        self.assertEqual(self.zoomed, [])
        self.assertTrue(response["snapshot"]["frameId"])

    def test_zoom_without_a_frame_is_stale(self):
        layout = self.layouts([("0xa", 0, 0, 50, 50)])[0]
        self.layout_mock.side_effect = [layout] * 4
        response = runtime.perform_gui([{"type": "zoom", "region": [0, 0, 20, 20]}])
        self.assertFalse(response["ok"])
        self.assertEqual(response["failureCode"], "stale_gui_frame")

    def test_zoom_checks_every_window_over_the_region(self):
        before = self.layouts([("0xa", 0, 0, 50, 50), ("0xb", 60, 60, 30, 30)])[0]
        for name, windows, ok in [
            ("unrelated window moved", [("0xa", 0, 0, 50, 50), ("0xb", 65, 60, 30, 30)], True),
            ("window over region moved", [("0xa", 2, 0, 50, 50), ("0xb", 60, 60, 30, 30)], False),
            ("new window covers a corner", [("0xa", 0, 0, 50, 50), ("0xb", 60, 60, 30, 30),
                                             ("0xc", 38, 28, 5, 5)], False),
            ("stacking changed", [("0xb", 60, 60, 30, 30), ("0xa", 0, 0, 50, 50)], True),
        ]:
            with self.subTest(name):
                response = self.zoom_after(before, self.layouts(windows)[0], [10, 10, 40, 30])
                self.assertEqual(response["ok"], ok, response.get("error"))
                self.execute.assert_not_called()
                if not ok:
                    self.assertEqual(response["failureCode"], "stale_gui_frame")
                    self.assertEqual(self.zoomed, [])

    def test_zoom_region_is_validated_before_capture(self):
        for region in ([0, 0, 7, 50], [0, 0, 50, 7.9], [90, 90, 101, 99], [20, 0, 10, 10]):
            with self.subTest(region=region):
                self.capture.reset_mock()
                response = runtime.perform_gui([{"type": "zoom", "region": region}],
                                               frame_id=self.frame_id)
                self.assertFalse(response["ok"])
                self.assertTrue(response["preflight_only"])
                self.capture.assert_not_called()

    def test_zoom_must_be_alone(self):
        response = runtime.perform_gui([
            {"type": "zoom", "region": [0, 0, 20, 20]},
            {"type": "click", "x": 1, "y": 1}], frame_id=self.frame_id)
        self.assertFalse(response["ok"])
        self.execute.assert_not_called()

    def test_absent_initial_capture_rejects_keyboard_input_too(self):
        self.capture.side_effect = None
        self.capture.return_value = None
        response = runtime.perform_gui([{"type": "type", "text": "hello"}])
        self.assertFalse(response["ok"])
        self.execute.assert_not_called()
        self.encoder.assert_not_called()

    def test_initial_capture_exception_is_classified_before_input(self):
        self.capture.side_effect = RuntimeError("capture failed")
        response = self.click()
        self.assertFalse(response["ok"])
        self.assertFalse(response["input_events_emitted"])
        self.execute.assert_not_called()

    def test_partial_emission_uses_only_fresh_frame(self):
        self.execute.side_effect = runtime.InputEmissionError("release failed", True)
        response = self.click()
        self.assertFalse(response["ok"])
        self.assertTrue(response["effect_unknown"])
        self.assertTrue(response["partial_input_possible"])
        self.encoder.assert_called_once_with(mock.sentinel.after)
        self.assertIn("fresh", response["text"])

    def test_operation_failure_preserves_known_keyboard_emission(self):
        self.execute.side_effect = runtime.OperationFailure(
            "input method acknowledgement failed", "input_emission_failed",
            "emission", input_events_emitted=True)
        response = runtime.perform_gui([{"type": "type", "text": "中"}])
        self.assertFalse(response["ok"])
        self.assertTrue(response["input_events_emitted"])
        self.assertTrue(response["partial_input_possible"])

    def test_input_method_precondition_failure_does_not_claim_partial_input(self):
        self.execute.side_effect = runtime.OperationFailure(
            "input method unavailable", "unsupported_action", "precondition")
        response = runtime.perform_gui([{"type": "type", "text": "中"}])
        self.assertFalse(response["ok"])
        self.assertFalse(response["input_events_emitted"])
        self.assertFalse(response["partial_input_possible"])

    def test_partial_emission_with_absent_capture_labels_fallback(self):
        self.capture.side_effect = [mock.sentinel.before, None]
        self.execute.side_effect = runtime.InputEmissionError("release failed", True)
        response = self.click()
        self.assertFalse(response["ok"])
        self.assertTrue(response["partial_input_possible"])
        self.assertTrue(response["mutation_unknown"])
        self.encoder.assert_called_once_with(mock.sentinel.before)
        self.assertIn("pre-action screenshot", response["text"])
        self.assertIn("does not show", response["text"])

    def test_absent_final_capture_is_not_success(self):
        self.capture.side_effect = [mock.sentinel.before, None]
        response = self.click()
        self.assertFalse(response["ok"])
        self.assertTrue(response["effect_unknown"])
        self.assertTrue(response["mutation_unknown"])
        self.assertEqual(response["input_actions_dispatched"], 1)
        self.encoder.assert_called_once_with(mock.sentinel.before)
        self.assertIn("pre-action screenshot", response["text"])

    def test_final_encoder_exception_preserves_mutation_and_fallback(self):
        self.encoder.side_effect = [RuntimeError("encoder failed"),
                                   self.encode_pixels(mock.sentinel.before)]
        response = self.click()
        self.assertFalse(response["ok"])
        self.assertTrue(response["effect_unknown"])
        self.assertTrue(response["mutation_unknown"])
        self.assertEqual(response["input_actions_dispatched"], 1)
        self.assertEqual(self.encoder.call_args_list,
                         [mock.call(mock.sentinel.after), mock.call(mock.sentinel.before)])
        self.assertIn("pre-action screenshot", response["text"])

    def test_partial_emission_with_encoder_failure_preserves_partial_evidence(self):
        self.execute.side_effect = runtime.InputEmissionError("release failed", True)
        self.encoder.side_effect = [RuntimeError("encoder failed"),
                                   self.encode_pixels(mock.sentinel.before)]
        response = self.click()
        self.assertFalse(response["ok"])
        self.assertTrue(response["partial_input_possible"])
        self.assertTrue(response["effect_unknown"])
        self.assertTrue(response["mutation_unknown"])
        self.assertIn("pre-action screenshot", response["text"])

    def test_no_encoder_candidate_is_classified_as_failure(self):
        self.encoder.side_effect = None
        self.encoder.return_value = None
        response = self.click()
        self.assertFalse(response["ok"])
        self.assertTrue(response["effect_unknown"])
        self.assertTrue(response["mutation_unknown"])
        self.assertFalse(response["snapshot"].get("screenshotPngBase64"))
        self.assertIn("No screenshot", response["text"])

    def test_screenshot_encoder_exception_is_attempted_once(self):
        self.encoder.side_effect = RuntimeError("encoder failed")
        response = runtime.perform_gui([{"type": "screenshot"}])
        self.assertFalse(response["ok"])
        self.assertFalse(response["effect_unknown"])
        self.assertFalse(response["mutation_unknown"])
        self.capture.assert_called_once()
        self.encoder.assert_called_once()
        self.assertIn("No screenshot", response["text"])

    def test_encoding_failure_cache_reraises_bounded_description(self):
        captured = runtime.capture_gui_snapshot(self.bounds)
        self.encoder.side_effect = ValueError("encoder failure " + "x" * 2048)
        with self.assertRaises(ValueError):
            captured.encode()
        with self.assertRaises(RuntimeError) as repeated:
            captured.encode()
        self.assertIn("encoder failure", str(repeated.exception))
        self.assertLessEqual(len(str(repeated.exception)), 512)
        self.encoder.assert_called_once()

    def test_changed_native_geometry_invalidates_final_pixel_coherence(self):
        # Both native sizes scale to 1365x768; delivered dimensions are insufficient.
        initial = runtime.frame(0, 0, 2560, 1440)
        final = runtime.frame(0, 0, 3840, 2160)
        self.bounds_mock.side_effect = [initial, final]
        response = runtime.perform_gui([{"type": "type", "text": "hello"}])
        self.assertTrue(response["ok"])
        self.assertIsNone(response["quiescent"])
        self.assertIsNone(response["changed_since_before"])
        self.assertEqual(response["snapshot"]["width"], 1365)
        self.assertEqual(response["snapshot"]["height"], 768)
        self.assertIn("changed again", response["text"])


class GUIEncodingTests(unittest.TestCase):
    def test_base64_budget_padding_boundary_encodes_only_selected_bytes(self):
        for length, budget, expected in [(3, 4, "png"), (4, 7, "jpeg"),
                                          (4, 8, "png"), (6, 8, "png"),
                                          (7, 8, "jpeg")]:
            with self.subTest(length=length, budget=budget):
                pixbuf = mock.Mock()
                pixbuf.save_to_bufferv.side_effect = [(True, b"p" * length), (True, b"j")]
                with mock.patch.object(runtime, "SCREENSHOT_MAX_BASE64_CHARS", budget), \
                        mock.patch.object(base64, "b64encode", wraps=base64.b64encode) as encode:
                    image = runtime.encoded_screenshot(pixbuf)
                self.assertEqual(image["mimeType"], "image/" + expected)
                self.assertLessEqual(len(image["data"]), budget)
                encode.assert_called_once_with(b"p" * length if expected == "png" else b"j")
                self.assertEqual(pixbuf.save_to_bufferv.call_args_list[0],
                                 mock.call("png", ["compression"], ["3"]))

    def test_all_overbudget_returns_smallest_raw_candidate(self):
        pixbuf = mock.Mock()
        pixbuf.save_to_bufferv.side_effect = [(True, b"p" * 13), (True, b"a" * 11),
                                             (True, b"b" * 8), (True, b"c" * 9),
                                             (True, b"d" * 10)]
        with mock.patch.object(runtime, "SCREENSHOT_MAX_BASE64_CHARS", 4), \
                mock.patch.object(base64, "b64encode", wraps=base64.b64encode) as encode:
            image = runtime.encoded_screenshot(pixbuf)
        self.assertEqual(image["encoding"], "jpeg-quality-70")
        self.assertGreater(len(image["data"]), 4)
        encode.assert_called_once_with(b"b" * 8)
        self.assertEqual([call.args[2] for call in pixbuf.save_to_bufferv.call_args_list],
                         [["3"], ["85"], ["70"], ["55"], ["40"]])

    def test_failed_formats_continue_and_total_failure_has_no_base64(self):
        pixbuf = mock.Mock()
        pixbuf.save_to_bufferv.side_effect = [RuntimeError("png unavailable"),
                                             (False, b""), (True, b"jpeg")]
        image = runtime.encoded_screenshot(pixbuf)
        self.assertEqual(image["encoding"], "jpeg-quality-70")
        pixbuf.save_to_bufferv.side_effect = RuntimeError("codec unavailable")
        with mock.patch.object(base64, "b64encode", wraps=base64.b64encode) as encode:
            self.assertIsNone(runtime.encoded_screenshot(pixbuf))
        encode.assert_not_called()

    @unittest.skipIf(runtime.GdkPixbuf is None, "GdkPixbuf unavailable")
    def test_real_png_and_jpeg_mime_decode_dimensions_and_lossless_pixels(self):
        from gi.repository import GLib

        width, height = 128, 96
        rng = random.Random(17)
        data = bytes(rng.getrandbits(8) for _ in range(width * height * 3))
        pixbuf = runtime.GdkPixbuf.Pixbuf.new_from_bytes(
            GLib.Bytes.new(data), runtime.GdkPixbuf.Colorspace.RGB,
            False, 8, width, height, width * 3)
        png_size = len(pixbuf.save_to_bufferv("png", ["compression"], ["3"])[1])
        candidates = [("png", "png-compression-3", png_size)]
        for quality in (85, 70, 55, 40):
            jpeg_size = len(pixbuf.save_to_bufferv("jpeg", ["quality"], [str(quality)])[1])
            self.assertGreater(candidates[-1][2], jpeg_size)
            candidates.append(("jpeg", "jpeg-quality-{}".format(quality), jpeg_size))
        for kind, encoding, size in candidates:
            with self.subTest(encoding=encoding):
                budget = 4 * ((size + 2) // 3)
                with mock.patch.object(runtime, "SCREENSHOT_MAX_BASE64_CHARS", budget):
                    image = runtime.encoded_screenshot(pixbuf)
                self.assertEqual(image["mimeType"], "image/" + kind)
                self.assertEqual(image["encoding"], encoding)
                self.assertLessEqual(len(image["data"]), budget)
                loader = runtime.GdkPixbuf.PixbufLoader.new_with_type(kind)
                loader.write(base64.b64decode(image["data"], validate=True))
                loader.close()
                decoded = loader.get_pixbuf()
                self.assertIn(image["mimeType"], loader.get_format().get_mime_types())
                self.assertEqual((decoded.get_width(), decoded.get_height()), (width, height))
                if kind == "png":
                    self.assertEqual(decoded.read_pixel_bytes().get_data(), data)


class GUICoordinateProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.bounds = runtime.frame(0, 0, 100, 100)

    def test_coordinate_input_without_layout_provenance_is_rejected(self):
        snapshot = {
            "frameId": None,
            "screenshotPngBase64": "png",
        }
        with mock.patch.object(
            runtime, "desktop_bounds", return_value=self.bounds
        ), patch_gui_capture( return_value=snapshot
        ), mock.patch.object(runtime, "execute_gui_action") as execute:
            response = runtime.perform_gui(
                [{"type": "click", "x": 10, "y": 10}],
            )

        self.assertFalse(response["ok"])
        execute.assert_not_called()


class PixelEvidenceTests(unittest.TestCase):
    """Pixel comparison is a third effect check, independent of the tree.

    The tree misses whole classes of effects such as formatting, file state
    and canvas pixels; after ctrl+s it can stay byte-identical although the
    file was saved. An unchanged tree does not prove an action had no effect.
    """

    def _frame(self, fill, size=(80, 60)):
        width, height = size
        stride = width * 3
        return {"data": bytes([fill]) * (stride * height), "stride": stride,
                "channels": 3, "width": width, "height": height}

    def test_identical_frames_report_no_change(self):
        frame = self._frame(10)
        note = runtime.pixel_change_note(
            runtime.persistent_pixel_change(frame, self._frame(10), self._frame(10)))
        self.assertIn("sampled_pixel_change=none", note)

    def test_resize_is_reported_without_causal_inference(self):
        grown = self._frame(10, size=(90, 60))
        note = runtime.pixel_change_note(
            runtime.persistent_pixel_change(self._frame(10), grown, grown))
        self.assertIn("dimensions changed", note)
        self.assertIn("semantic effect are unknown", note)
        self.assertNotIn("something happened", note)

    def test_substantial_change_is_reported_with_a_region(self):
        after = self._frame(10)
        data = bytearray(after["data"])
        for y in range(0, 60):
            for x in range(20, 60):
                offset = y * after["stride"] + x * 3
                data[offset:offset + 3] = b"\xff\xff\xff"
        after["data"] = bytes(data)
        note = runtime.pixel_change_note(
            runtime.persistent_pixel_change(self._frame(10), after, after))
        self.assertIn("sampled_pixel_change=material", note)
        self.assertIn("% of sample points", note)
        # The window comparison states no coordinates: the only clickable
        # image is the full-desktop frame.
        self.assertNotIn("concentrated", note)
        self.assertNotIn("window-relative", note)

    def _dots(self, count, size, offset_rows=1):
        frame = self._frame(10, size=size)
        data = bytearray(frame["data"])
        for i in range(count):
            offset = (8 * (i + offset_rows)) * frame["stride"] + 8 * 3
            data[offset:offset + 3] = b"\xff\xff\xff"
        frame["data"] = bytes(data)
        return frame

    def test_flicker_is_filtered_by_requiring_the_change_to_persist(self):
        """A change present in only one post-action capture is flicker.

        Two pre-action captures taken milliseconds apart cannot measure the
        noise of a 1 Hz text caret, so a no-op would report the 8x16 caret as
        a change. Only changes present in both post-action captures count.
        """
        size = (1600, 1000)
        before = self._frame(10, size=size)
        blink_on = self._dots(1, size)          # caret shown
        blink_off = self._frame(10, size=size)  # caret hidden, as before

        change = runtime.persistent_pixel_change(before, blink_on, blink_off)
        self.assertEqual(change["changed"], 0, "a flicker must not count as a change")
        self.assertIn(
            "sampled_pixel_change=none", runtime.pixel_change_note(change))

    def test_a_real_change_persists_across_both_captures(self):
        size = (1600, 1000)
        before = self._frame(10, size=size)
        after = self._dots(30, size)

        change = runtime.persistent_pixel_change(before, after, after)
        self.assertEqual(change["changed"], 30)
        note = runtime.pixel_change_note(change)
        self.assertIn("sampled_pixel_change=material", note)
        self.assertNotIn("{", note)

    def test_resize_between_captures_is_reported_not_guessed(self):
        size = (1600, 1000)
        before = self._frame(10, size=size)
        grown = self._frame(10, size=(1608, 1000))
        change = runtime.persistent_pixel_change(before, grown, grown)
        self.assertTrue(change["resized"])
        note = runtime.pixel_change_note(change)
        self.assertIn("geometry_changed=true", note)
        self.assertIn("semantic effect are unknown", note)

    def test_missing_capture_never_pretends_to_have_compared(self):
        """A missing capture returns None instead of pretending to compare."""
        frame = self._frame(10)
        self.assertIsNone(runtime.persistent_pixel_change(None, frame, frame))
        self.assertIsNone(runtime.persistent_pixel_change(frame, None, frame))
        self.assertIsNone(runtime.persistent_pixel_change(frame, frame, None))
        self.assertIsNone(runtime.pixel_change_note(None))


class SettleWaitTests(AtspiPatchedTestCase):
    """Post-action settle wait.

    These tests cover a race that silently targets the wrong window: for a
    few tens of milliseconds a new window is in the tree while ACTIVE is
    still on the old one. A snapshot taken then shows the old window, and
    the tree is fully self-consistent, so nothing in it reveals the problem.
    """

    def _wait(self, app, before, clock, **kwargs):
        return runtime.wait_for_ui_to_settle(
            app,
            before,
            clock=clock.monotonic,
            sleep=clock.sleep,
            **kwargs
        )

    def test_returns_right_after_the_minimum_wait_when_nothing_opened(self):
        """The common path stays fast: no window change means only the minimum wait."""
        clock = _FakeClock()
        main = _WindowNode("frame", "Inbox", active=True)
        app = _ScriptedApp(clock, [(0.0, [main])])
        before = runtime.window_identity_set(app)

        self.assertIsNone(self._wait(app, before, clock))
        self.assertAlmostEqual(clock.now, runtime.SETTLE_MIN_SECONDS, places=6)

    def test_waits_until_the_newly_opened_window_takes_focus(self):
        """A synthetic window becomes active after it first appears."""
        clock = _FakeClock()
        main_active = _WindowNode("frame", "Inbox", active=True)
        main_idle = _WindowNode("frame", "Inbox")
        filters_idle = _WindowNode("frame", "Message Filters")
        filters_active = _WindowNode("frame", "Message Filters", active=True)
        app = _ScriptedApp(
            clock,
            [
                (0.0, [main_active]),
                (0.070, [main_active, filters_idle]),
                (0.123, [main_idle, filters_active]),
            ],
        )
        before = runtime.window_identity_set(app)

        self.assertIsNone(self._wait(app, before, clock))
        self.assertGreaterEqual(clock.now, 0.123)
        # Beyond waiting long enough, main_window() must then pick the new window.
        _, chosen = runtime.main_window(app)
        self.assertEqual(runtime.node_name(chosen), "Message Filters")

    def test_appearance_without_focus_transfer_keeps_the_active_window(self):
        """An inactive synthetic window does not replace the active target."""
        clock = _FakeClock()
        main_active = _WindowNode("frame", "Inbox", active=True)
        filters_idle = _WindowNode("frame", "Message Filters")
        app = _ScriptedApp(
            clock,
            [(0.0, [main_active]), (0.070, [main_active, filters_idle])],
        )

        clock.sleep(0.12)  # fixed post-action delay before the snapshot
        _, chosen = runtime.main_window(app)
        self.assertEqual(runtime.node_name(chosen), "Inbox")

    def test_modal_window_counts_as_settled(self):
        """A modal window may report MODAL without ACTIVE; either counts as settled."""
        clock = _FakeClock()
        main = _WindowNode("frame", "Writer", active=True)
        alert = _WindowNode("alert", "Question", modal=True)
        app = _ScriptedApp(clock, [(0.0, [main]), (0.045, [main, alert])])
        before = runtime.window_identity_set(app)

        self.assertIsNone(self._wait(app, before, clock))
        _, chosen = runtime.main_window(app)
        self.assertEqual(runtime.node_name(chosen), "Question")

    def test_waits_for_focus_to_come_back_after_a_window_closes(self):
        """After a dialog closes, focus takes a while to return to the main window."""
        clock = _FakeClock()
        main_idle = _WindowNode("frame", "Writer")
        main_active = _WindowNode("frame", "Writer", active=True)
        dialog = _WindowNode("dialog", "Find and Replace", active=True)
        app = _ScriptedApp(
            clock,
            [
                (0.0, [main_idle, dialog]),
                (0.05, [main_idle]),
                (0.30, [main_active]),
            ],
        )
        before = runtime.window_identity_set(app)

        self.assertIsNone(self._wait(app, before, clock))
        self.assertGreaterEqual(clock.now, 0.30)

    def test_window_opening_after_the_minimum_wait_is_out_of_scope(self):
        """An unchanged window set ends settling at the minimum wait.

        A window that appears later is not covered by this wait. The caller
        must observe again to detect that asynchronous transition.
        """
        clock = _FakeClock()
        main = _WindowNode("frame", "Inbox", active=True)
        slow = _WindowNode("frame", "Slow Dialog")
        app = _ScriptedApp(
            clock,
            [(0.0, [main]), (runtime.SETTLE_MIN_SECONDS + 0.05, [main, slow])],
        )
        before = runtime.window_identity_set(app)

        self.assertIsNone(self._wait(app, before, clock))
        self.assertAlmostEqual(clock.now, runtime.SETTLE_MIN_SECONDS, places=6)
        self.assertEqual(runtime.window_identity_set(app), before)
        clock.sleep(0.05)
        self.assertNotEqual(runtime.window_identity_set(app), before)

    def test_reports_when_the_new_window_never_takes_focus(self):
        """A timeout is reported, since the snapshot may show an unexpected window."""
        clock = _FakeClock()
        main = _WindowNode("frame", "Inbox", active=True)
        tooltip = _WindowNode("window", "")
        app = _ScriptedApp(clock, [(0.0, [main]), (0.05, [main, tooltip])])
        before = runtime.window_identity_set(app)

        note = self._wait(app, before, clock, timeout_seconds=0.4)
        self.assertIsNotNone(note)
        self.assertIn("target ref", note)
        # The wait is bounded and never hangs the tool.
        self.assertLess(clock.now, runtime.SETTLE_MIN_SECONDS + 0.4 + 0.1)

    def test_missing_baseline_skips_the_wait(self):
        """Without a pre-action window set, the wait stops at the minimum settle time."""
        clock = _FakeClock()
        app = _ScriptedApp(clock, [(0.0, [_WindowNode("frame", "Inbox")])])

        self.assertIsNone(self._wait(app, None, clock))
        self.assertAlmostEqual(clock.now, runtime.SETTLE_MIN_SECONDS, places=6)


class ScreenshotPolicyTests(unittest.TestCase):
    """The provider captures no accessibility window image.

    AX images are full-desktop GUI frames that the Go core captures with a
    desktop batch, so they share the GUI coordinate frame. A request for a
    provider window image is rejected before any read.
    """

    def _observe_request(self, observation):
        return {
            "protocol": "computer-use-provider",
            "schema_version": 2,
            "request_id": "req-observe",
            "operation": "observe",
            "observe": {
                "protocol_version": 2,
                "app": "Editor",
                "observation": observation,
            },
        }

    def test_observation_rejects_a_window_image_request(self):
        with self.assertRaises(runtime.ProviderRequestError) as caught:
            runtime.validate_request(
                self._observe_request({"include_screenshot": True}))
        self.assertIn("include_screenshot", str(caught.exception))
        with self.assertRaises(runtime.ProviderRequestError):
            runtime.validate_request(
                self._observe_request({"screenshot_scope": "screen"}))
        runtime.validate_request(
            self._observe_request({"include_screenshot": False}))

    def test_window_image_capture_was_removed(self):
        for name in ("capture_window_image", "a11y_screenshots_enabled"):
            self.assertFalse(hasattr(runtime, name), name)

    def test_gui_screenshot_is_unaffected(self):
        desktop = runtime.frame(0, 0, 1600, 900)
        with mock.patch.object(runtime, "desktop_bounds", return_value=desktop), \
                patch_gui_capture(
                return_value={"frameId": "layout", "screenshotPngBase64": "png"}):
            response = runtime.perform_gui([{"type": "screenshot"}])
        self.assertTrue(response["ok"])
        self.assertEqual(response["snapshot"]["screenshotPngBase64"], "png")
        self.assertFalse(response["input_events_emitted"])
