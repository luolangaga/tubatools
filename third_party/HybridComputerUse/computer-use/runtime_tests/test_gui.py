#!/usr/bin/env python3
"""Linux provider GUI screen coordinate, layout frame, batch execution, and schema tests."""

from runtime_test_support import *  # noqa: F401,F403
from runtime_tests.test_gui_settle_frames import GUISettleAndFrameGuardCases
from runtime_tests.gui_test_helpers import patch_gui_capture


class GUIScreenCoordinateTests(GUISettleAndFrameGuardCases, unittest.TestCase):
    def setUp(self):
        self.desktop = runtime.frame(0, 0, 1920, 1080)

    def test_absolute_point_is_not_offset_or_clamped(self):
        self.assertEqual(
            runtime.screen_absolute_point(125, 250, self.desktop), (125.0, 250.0)
        )

    def test_image_point_is_translated_for_a_negative_virtual_desktop_origin(self):
        desktop = runtime.frame(-1920, -100, 3840, 1180)

        self.assertEqual(
            runtime.screen_absolute_point(100, 50, desktop),
            (-1820.0, -50.0),
        )

    def test_delivered_image_point_maps_back_by_pixel_center(self):
        dimensions = runtime.gui_model_dimensions(self.desktop)
        model = runtime.frame(
            0, 0, dimensions["width"], dimensions["height"])
        x, y = runtime.screen_absolute_point(
            model["width"] / 2 - 0.5,
            model["height"] / 2 - 0.5,
            model,
            self.desktop,
        )

        self.assertAlmostEqual(x, 959.5, places=3)
        self.assertAlmostEqual(y, 539.5, places=3)

    def test_ax_synthesis_points_are_stated_in_frame_pixels(self):
        """AX notes state a synthesized point in GUI frame pixels, the
        inverse of the GUI mapping, so the point can be reused as GUI x/y."""
        dimensions = runtime.gui_model_dimensions(self.desktop)
        model = runtime.frame(0, 0, dimensions["width"], dimensions["height"])
        for native in ((717.0, 414.0), (959.5, 539.5), (3.0, 1070.0)):
            frame_x, frame_y = runtime.desktop_frame_point(*native, self.desktop)
            back = runtime.screen_absolute_point(frame_x, frame_y, model, self.desktop)
            self.assertAlmostEqual(back[0], native[0], places=6)
            self.assertAlmostEqual(back[1], native[1], places=6)
        # Edge pixels clamp to the frame, within one native pixel.
        for native in ((0.0, 0.0), (1919.0, 1079.0)):
            back = runtime.screen_absolute_point(
                *runtime.desktop_frame_point(*native, self.desktop), model, self.desktop)
            self.assertLessEqual(abs(back[0] - native[0]), 1.0)
            self.assertLessEqual(abs(back[1] - native[1]), 1.0)
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop):
            self.assertEqual(
                runtime.frame_point_text(768.0, 425.0),
                "at (546, 302) in full-desktop frame pixels")
        with mock.patch.object(runtime, "desktop_bounds", return_value=None):
            self.assertEqual(runtime.frame_point_text(1, 2), "at an unreported point")

    def test_zoom_geometry_maps_back_within_two_frame_pixels(self):
        """Every zoom image pixel maps to the frame point the crop holds,
        per axis, for narrow, wide, tall, edge and off-grid regions."""
        desktop = self.desktop
        frame_size = runtime.gui_model_dimensions(desktop)
        per_x = desktop["width"] / frame_size["width"]
        per_y = desktop["height"] / frame_size["height"]
        regions = [
            [100, 0, 108, 8], [100, 0, 108, 768], [0, 300, 1365, 308],
            [0, 30, 700, 70], [612.3, 61.7, 640.9, 83.2], [0, 0, 1365, 768],
            [1357, 760, 1365, 768], [333.33, 444.44, 345.67, 455.55],
        ]
        for region in regions:
            with self.subTest(region=region):
                g = runtime.zoom_geometry(region, desktop)
                x0, y0, x1, y1 = g["region"]
                left, top, crop_w, crop_h = g["native"]
                self.assertLessEqual(x0, region[0]); self.assertLessEqual(y0, region[1])
                self.assertGreaterEqual(x1, region[2]); self.assertGreaterEqual(y1, region[3])
                self.assertLessEqual(g["width"], runtime.GUI_MODEL_MAX_WIDTH)
                self.assertLessEqual(g["height"], runtime.GUI_MODEL_MAX_HEIGHT)
                scale_x = g["width"] / (x1 - x0)
                scale_y = g["height"] / (y1 - y0)
                for u in (0, g["width"] / 2, g["width"] - 0.5):
                    for v in (0, g["height"] / 2, g["height"] - 0.5):
                        # The native pixel the scaled crop shows at (u, v).
                        native_x = left + u * crop_w / g["width"]
                        native_y = top + v * crop_h / g["height"]
                        error = max(abs(x0 + u / scale_x - native_x / per_x),
                                    abs(y0 + v / scale_y - native_y / per_y))
                        self.assertLessEqual(error, 2.0)

    def test_zoom_geometry_matches_the_shared_fixture(self):
        """macOS and Windows replay the same fixture; it pins one geometry."""
        path = Path(__file__).resolve().parent.parent / "testdata" / "contract" / "zoom_geometry.json"
        cases = json.loads(path.read_text())["cases"]
        self.assertGreater(len(cases), 50)
        for case in cases:
            with self.subTest(case["name"]):
                desktop = runtime.frame(0, 0, *case["native"])
                size = runtime.gui_model_dimensions(desktop)
                self.assertEqual([size["width"], size["height"]], case["frame"])
                if "expect" not in case:
                    with self.assertRaises(ValueError):
                        runtime.zoom_geometry(case["region"], desktop)
                    continue
                g = runtime.zoom_geometry(case["region"], desktop)
                self.assertEqual(list(g["native"]), case["expect"]["native"])
                self.assertEqual((g["width"], g["height"]),
                                 (case["expect"]["width"], case["expect"]["height"]))
                for got, want in zip(g["region"], case["expect"]["region"]):
                    self.assertAlmostEqual(got, want, places=9)

    def test_zoom_geometry_rejects_small_or_outside_regions(self):
        for region in ([100, 0, 107.9, 768], [0, 0, 1366, 100], [-1, 0, 50, 50],
                       [50, 50, 40, 60]):
            with self.subTest(region=region):
                with self.assertRaises(ValueError):
                    runtime.zoom_geometry(region, self.desktop)

    def test_1080p_desktop_is_delivered_at_the_size_the_model_sees(self):
        # GPT-5.4 at detail high shrinks a 1920x1080 image to 1365x768 before
        # the model sees it, so coordinates read from that image were 0.71x
        # the delivered pixels. Delivering 1365x768 makes them one space.
        dimensions = runtime.gui_model_dimensions(self.desktop)

        self.assertEqual((dimensions["width"], dimensions["height"]), (1365, 768))
        self.assertAlmostEqual(dimensions["scale_x"], 1365 / 1920)
        self.assertAlmostEqual(dimensions["scale_y"], 768 / 1080)

    def test_delivered_image_stays_within_the_model_image_box(self):
        for native, delivered in [
            ((1280, 720), (1280, 720)),
            ((1366, 768), (1366, 768)),
            ((2560, 1440), (1365, 768)),
            ((1920, 1200), (1229, 768)),
            ((3440, 1440), (1366, 572)),
            ((1080, 1920), (432, 768)),
        ]:
            with self.subTest(native=native):
                dimensions = runtime.gui_model_dimensions(
                    runtime.frame(0, 0, *native))
                self.assertEqual(
                    (dimensions["width"], dimensions["height"]), delivered)
                self.assertAlmostEqual(
                    dimensions["scale_x"], delivered[0] / native[0])
                self.assertAlmostEqual(
                    dimensions["scale_y"], delivered[1] / native[1])

    def test_last_delivered_pixel_maps_inside_the_native_desktop(self):
        dimensions = runtime.gui_model_dimensions(self.desktop)
        model = runtime.frame(0, 0, dimensions["width"], dimensions["height"])
        x, y = runtime.screen_absolute_point(
            model["width"] - 1, model["height"] - 1, model, self.desktop)

        self.assertLessEqual(x, 1919)
        self.assertLessEqual(y, 1079)
        self.assertGreater(x, 1917)
        self.assertGreater(y, 1077)

    def test_out_of_bounds_point_is_rejected_instead_of_moved(self):
        with self.assertRaises(RuntimeError) as ctx:
            runtime.screen_absolute_point(1920, 20, self.desktop)

        self.assertIn("outside the full desktop image bounds", str(ctx.exception))
        self.assertIn("gui", str(ctx.exception))

    def test_coordinate_validation_fails_closed_without_desktop_bounds(self):
        with mock.patch.object(runtime, "desktop_bounds", return_value=None):
            with self.assertRaises(RuntimeError) as ctx:
                runtime.screen_absolute_point(10, 20)

        self.assertIn("bounds are unavailable", str(ctx.exception))
        self.assertIn("refusing", str(ctx.exception))

    def test_gui_snapshot_captures_the_desktop_without_rendering_the_tree(self):
        layout = {
            "desktop": {"x": 0, "y": 0, "width": 1920, "height": 1080},
            "activeWindow": "0x20",
            "windows": [
                {"id": "0x20", "x": 10, "y": 20, "width": 800, "height": 600},
            ],
        }
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "capture_gui_pixels",
                                  return_value=mock.sentinel.pixels) as capture, \
                mock.patch.object(runtime, "scale_to_model",
                                  return_value=mock.sentinel.scaled) as scale, \
                mock.patch.object(runtime, "encoded_screenshot",
                                  return_value={"data": "png", "mimeType": "image/png",
                                                "encoding": "png-compression-3"}) as encode, \
                mock.patch.object(runtime, "desktop_layout_state",
                                  return_value=layout), \
                mock.patch.object(
                    runtime, "resolve_app",
                    side_effect=AssertionError("app-free gui resolved an app")), \
                mock.patch.object(runtime, "render_tree",
                                  side_effect=AssertionError("GUI screenshot walked tree")):
            snapshot = runtime.capture_gui_snapshot().encode()

        capture.assert_called_once_with(self.desktop)
        scale.assert_called_once_with(mock.sentinel.pixels, self.desktop)
        encode.assert_called_once_with(mock.sentinel.scaled)
        self.assertEqual(snapshot["screenshotPngBase64"], "png")
        self.assertEqual(snapshot["coordinateSpace"], "screen_image_px")
        self.assertEqual(snapshot["width"], 1365)
        self.assertEqual(snapshot["height"], 768)
        self.assertEqual(
            snapshot["frameId"],
            runtime.desktop_layout_frame_id(layout),
        )
        self.assertEqual(snapshot["desktopLayout"], layout)
        self.assertNotIn("app", snapshot)
        self.assertNotIn("elements", snapshot)
        self.assertNotIn("treeLines", snapshot)

    def test_screenshot_encoding_falls_back_to_same_size_jpeg_under_budget(self):
        class Pixbuf:
            def save_to_bufferv(self, image_format, _keys, values):
                if image_format == "png":
                    return True, b"p" * 500000
                quality = int(values[0])
                sizes = {85: 400000, 70: 200000, 55: 150000, 40: 100000}
                return True, b"j" * sizes[quality]

        image = runtime.encoded_screenshot(Pixbuf())

        self.assertEqual(image["mimeType"], "image/jpeg")
        self.assertEqual(image["encoding"], "jpeg-quality-70")
        self.assertLessEqual(
            len(image["data"]), runtime.SCREENSHOT_MAX_BASE64_CHARS)

    def test_screenshot_encoding_keeps_png_when_it_fits(self):
        class Pixbuf:
            def save_to_bufferv(self, image_format, _keys, _values):
                if image_format == "png":
                    return True, b"p" * 100
                raise AssertionError("JPEG fallback should not run")

        image = runtime.encoded_screenshot(Pixbuf())

        self.assertEqual(image["mimeType"], "image/png")
        self.assertEqual(image["encoding"], "png-compression-3")

    def test_gui_snapshot_retries_until_layout_brackets_the_screenshot(self):
        first = {
            "desktop": {"x": 0, "y": 0, "width": 1920, "height": 1080},
            "activeWindow": "0x10",
            "windows": [
                {"id": "0x10", "x": 0, "y": 0, "width": 800, "height": 600},
            ],
        }
        second = dict(first, activeWindow="0x20")
        with mock.patch.object(runtime, "scale_to_model", side_effect=lambda pixbuf, bounds: pixbuf), \
                mock.patch.object(
                runtime, "desktop_layout_state",
                side_effect=[first, second, second, second]), \
                mock.patch.object(
                    runtime, "capture_gui_pixels",
                    side_effect=[mock.sentinel.raced, mock.sentinel.coherent]) as capture, \
                mock.patch.object(
                    runtime, "encoded_screenshot",
                    return_value={"data": "coherent", "mimeType": "image/png",
                                  "encoding": "png-compression-3"}) as encode:
            snapshot = runtime.capture_gui_snapshot(self.desktop).encode()

        self.assertEqual(capture.call_count, 2)
        encode.assert_called_once_with(mock.sentinel.coherent)
        self.assertEqual(snapshot["screenshotPngBase64"], "coherent")
        self.assertEqual(
            snapshot["frameId"], runtime.desktop_layout_frame_id(second))

    def test_gui_snapshot_with_perpetual_layout_race_has_no_frame_id(self):
        layouts = []
        for active in range(runtime.GUI_SNAPSHOT_LAYOUT_ATTEMPTS * 2):
            layouts.append({
                "desktop": {"x": 0, "y": 0, "width": 1920, "height": 1080},
                "activeWindow": hex(active + 1),
                "windows": [],
            })
        with mock.patch.object(runtime, "scale_to_model", side_effect=lambda pixbuf, bounds: pixbuf), \
                mock.patch.object(
                runtime, "desktop_layout_state", side_effect=layouts), \
                mock.patch.object(
                    runtime, "capture_gui_pixels", return_value=mock.sentinel.pixels), \
                mock.patch.object(
                    runtime, "encoded_screenshot",
                    return_value={"data": "png", "mimeType": "image/png",
                                  "encoding": "png-compression-3"}):
            snapshot = runtime.capture_gui_snapshot(self.desktop).encode()

        self.assertIsNone(snapshot["frameId"])
        self.assertEqual(snapshot["screenshotPngBase64"], "png")

    def test_gui_snapshot_reuses_each_closing_layout_sample(self):
        layouts = [
            {"desktop": {"x": 0, "y": 0, "width": 1920, "height": 1080},
             "activeWindow": hex(index + 1), "windows": []}
            for index in range(runtime.GUI_SNAPSHOT_LAYOUT_ATTEMPTS + 1)
        ]
        with mock.patch.object(
                runtime, "desktop_layout_state", side_effect=layouts) as layout, \
                mock.patch.object(
                    runtime, "capture_gui_pixels", return_value=mock.sentinel.pixels):
            snapshot = runtime.capture_gui_snapshot(self.desktop)

        self.assertIsNone(snapshot.frame_id)
        self.assertEqual(
            layout.call_count, runtime.GUI_SNAPSHOT_LAYOUT_ATTEMPTS + 1)

    def test_gui_snapshot_title_change_during_capture_keeps_frame(self):
        first = {
            "desktop": {"x": 0, "y": 0, "width": 1920, "height": 1080},
            "activeWindow": "0x20",
            "windows": [{"id": "0x20", "x": 10, "y": 20, "width": 800,
                         "height": 600, "title": "Saving...",
                         "overrideRedirect": False}],
        }
        retitled = json.loads(json.dumps(first))
        retitled["windows"][0]["title"] = "Saved"
        with mock.patch.object(
                runtime, "desktop_layout_state",
                side_effect=[first, retitled]) as layout, \
                mock.patch.object(
                    runtime, "capture_gui_pixels",
                    return_value=mock.sentinel.pixels) as capture:
            snapshot = runtime.capture_gui_snapshot(self.desktop)

        capture.assert_called_once()
        self.assertEqual(layout.call_count, 2)
        self.assertEqual(snapshot.frame_id, runtime.desktop_layout_frame_id(first))

    def test_gui_layout_frame_ignores_pixel_only_changes(self):
        layout = {
            "desktop": {"x": 0, "y": 0, "width": 1920, "height": 1080},
            "activeWindow": "0x20",
            "windows": [
                {"id": "0x20", "x": 10, "y": 20, "width": 800, "height": 600},
            ],
        }
        with mock.patch.object(runtime, "scale_to_model", side_effect=lambda pixbuf, bounds: pixbuf), \
                mock.patch.object(runtime, "desktop_layout_state",
                               return_value=layout), \
                mock.patch.object(
                    runtime, "capture_gui_pixels",
                    side_effect=[mock.sentinel.first, mock.sentinel.second]), \
                mock.patch.object(
                    runtime, "encoded_screenshot",
                    side_effect=[
                        {"data": "png-a", "mimeType": "image/png", "encoding": "png-compression-3"},
                        {"data": "png-b", "mimeType": "image/png", "encoding": "png-compression-3"},
                    ]):
            first = runtime.capture_gui_snapshot(self.desktop).encode()
            second = runtime.capture_gui_snapshot(self.desktop).encode()

        self.assertEqual(first["frameId"], second["frameId"])
        self.assertNotEqual(first["screenshotPngBase64"], second["screenshotPngBase64"])

    def test_gui_layout_frame_changes_for_coordinate_relevant_layout(self):
        base = {
            "desktop": {"x": 0, "y": 0, "width": 1920, "height": 1080},
            "activeWindow": "0x20",
            "windows": [
                {"id": "0x10", "x": 0, "y": 0, "width": 400, "height": 300},
                {"id": "0x20", "x": 10, "y": 20, "width": 800, "height": 600},
            ],
        }
        variants = [
            dict(base, activeWindow="0x10"),
            dict(base, windows=[
                base["windows"][0],
                dict(base["windows"][1], x=11),
            ]),
            dict(base, windows=base["windows"][:1]),
            dict(base, windows=list(reversed(base["windows"]))),
        ]

        original = runtime.desktop_layout_frame_id(base)
        for layout in variants:
            with self.subTest(layout=layout):
                self.assertNotEqual(
                    original,
                    runtime.desktop_layout_frame_id(layout),
                )

    def test_desktop_layout_state_uses_visible_x11_toplevels_only(self):
        root_props = (
            "_NET_CLIENT_LIST_STACKING(WINDOW): window id # "
            "0x10, 0x20, 0x30\n"
            "_NET_ACTIVE_WINDOW(WINDOW): window id # 0x20\n"
        )
        window_info = {
            "0x10": (
                'xwininfo: Window id: 0x10 "Tip of the Day"\n'
                "Absolute upper-left X:  10\n"
                "Absolute upper-left Y:  20\n"
                "Width: 800\nHeight: 600\nMap State: IsViewable\n"
                "Override Redirect State: no\n"
            ),
            "0x20": (
                'xwininfo: Window id: 0x20 "Document"\n'
                "Absolute upper-left X:  100\n"
                "Absolute upper-left Y:  200\n"
                "Width: 900\nHeight: 700\nMap State: IsViewable\n"
                "Override Redirect State: no\n"
            ),
            "0x30": (
                "Absolute upper-left X:  1\n"
                "Absolute upper-left Y:  2\n"
                "Width: 3\nHeight: 4\nMap State: IsUnMapped\n"
            ),
        }

        def run(command, **_kwargs):
            if command[:2] == ["xprop", "-root"]:
                return mock.Mock(returncode=0, stdout=root_props)
            return mock.Mock(returncode=0, stdout=window_info[command[-1]])

        with mock.patch.object(runtime.subprocess, "run", side_effect=run):
            layout = runtime.desktop_layout_state(self.desktop)

        self.assertEqual(layout["activeWindow"], "0x20")
        self.assertEqual(layout["windows"], [
            {"id": "0x10", "x": 10, "y": 20, "width": 800, "height": 600,
             "title": "Tip of the Day", "overrideRedirect": False},
            {"id": "0x20", "x": 100, "y": 200, "width": 900, "height": 700,
             "title": "Document", "overrideRedirect": False},
        ])

    def test_layout_frame_ignores_title_only_changes(self):
        first = {
            "desktop": {"x": 0, "y": 0, "width": 1920, "height": 1080},
            "activeWindow": "0x20",
            "windows": [{
                "id": "0x20", "x": 10, "y": 20, "width": 800, "height": 600,
                "title": "Document A", "overrideRedirect": False,
            }],
        }
        second = json.loads(json.dumps(first))
        second["windows"][0]["title"] = "Document B"

        self.assertEqual(
            runtime.desktop_layout_frame_id(first),
            runtime.desktop_layout_frame_id(second),
        )

    def test_desktop_layout_state_rejects_unbounded_window_lists(self):
        ids = ", ".join(
            "0x{:x}".format(index + 1)
            for index in range(runtime.GUI_MAX_LAYOUT_WINDOWS + 1)
        )
        root_props = (
            "_NET_CLIENT_LIST_STACKING(WINDOW): window id # " + ids + "\n"
            "_NET_ACTIVE_WINDOW(WINDOW): window id # 0x1\n"
        )
        with mock.patch.object(
                runtime.subprocess, "run",
                return_value=mock.Mock(returncode=0, stdout=root_props)) as run:
            layout = runtime.desktop_layout_state(self.desktop)

        self.assertIsNone(layout)
        self.assertEqual(run.call_count, 1)

    def test_root_window_state_uses_one_bounded_query(self):
        root_props = (
            "_NET_CLIENT_LIST_STACKING(WINDOW): window id # 0x10, 0x20\n"
            "_NET_ACTIVE_WINDOW(WINDOW): window id # 0x20\n"
        )
        with mock.patch.object(
                runtime.subprocess, "run",
                return_value=mock.Mock(returncode=0, stdout=root_props)) as run:
            state = runtime.x11_root_window_state()

        self.assertEqual(state, {
            "windowIds": {"0x10", "0x20"},
            "activeWindow": "0x20",
        })
        self.assertEqual(run.call_count, 1)
        self.assertEqual(run.call_args.kwargs["timeout"], 0.25)

    def test_gui_executes_actions_in_order_and_returns_one_final_screenshot(self):
        executed = []
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(
                runtime, "execute_gui_action",
                side_effect=lambda action: executed.append(action["type"])), \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png"}):
            response = runtime.perform_gui([
                {"type": "move", "x": 1, "y": 2},
                {"type": "click", "x": 1, "y": 2},
            ])
        self.assertTrue(response["ok"])
        self.assertEqual(executed, ["move", "click"])
        self.assertEqual(response["dispatched"], 2)
        self.assertTrue(response["input_events_emitted"])
        self.assertEqual(response["input_actions_dispatched"], 2)
        self.assertTrue(response["effect_unknown"])
        self.assertNotIn("executed", response["text"].lower())
        self.assertEqual(response["snapshot"]["screenshotPngBase64"], "png")

    def test_gui_stops_at_first_failure_and_still_returns_screenshot(self):
        executed = []

        def execute(action):
            executed.append(action["type"])
            if action["type"] == "click":
                raise RuntimeError("blocked")

        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "execute_gui_action", side_effect=execute), \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png"}):
            response = runtime.perform_gui([
                {"type": "move", "x": 1, "y": 2},
                {"type": "click", "x": 1, "y": 2},
                {"type": "type", "text": "must not run"},
            ])
        self.assertFalse(response["ok"])
        self.assertEqual(executed, ["move", "click"])
        self.assertIn("action 2", response["error"])
        self.assertEqual(response["snapshot"]["screenshotPngBase64"], "png")

    def test_gui_does_not_claim_emission_when_atspi_returns_false(self):
        snapshot = {"frameId": "layout", "screenshotPngBase64": "png"}
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                patch_gui_capture( return_value=snapshot), \
                mock.patch.object(
                    runtime, "capture_window_pixels", return_value=None), \
                mock.patch.object(
                    runtime.Atspi, "generate_mouse_event",
                    return_value=False, create=True):
            response = runtime.perform_gui([
                {"type": "click", "x": 1, "y": 2},
            ])

        self.assertFalse(response["ok"])
        self.assertFalse(response["input_events_emitted"])
        self.assertEqual(response["input_actions_dispatched"], 0)
        self.assertIn("input emission failed", response["error"])

    def test_gui_reports_partial_emission_when_release_fails(self):
        snapshot = {"frameId": "layout", "screenshotPngBase64": "png"}
        results = iter([True, True, False, True])
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                patch_gui_capture( return_value=snapshot), \
                mock.patch.object(
                    runtime, "capture_window_pixels", return_value=None), \
                mock.patch.object(
                    runtime.Atspi, "generate_mouse_event",
                    side_effect=lambda *_args: next(results), create=True):
            response = runtime.perform_gui([
                {"type": "click", "x": 1, "y": 2},
            ])

        self.assertFalse(response["ok"])
        self.assertTrue(response["input_events_emitted"])
        self.assertTrue(response["partial_input_possible"])
        self.assertEqual(response["input_actions_dispatched"], 0)

    def test_later_invalid_action_rejects_the_whole_batch_before_side_effects(self):
        execute = mock.Mock()
        actions = [
            {"type": "click", "x": 10, "y": 20},
            {
                "type": "scroll",
                "x": 10,
                "y": 20,
                "scroll_y": runtime.GUI_MAX_SCROLL_DELTA + 1,
            },
        ]
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "execute_gui_action", execute), \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png"}):
            response = runtime.perform_gui(actions)

        self.assertFalse(response["ok"])
        self.assertIn("static pre-validation", response["error"])
        self.assertIn("no actions were dispatched", response["error"])
        self.assertEqual(response["dispatched"], 0)
        self.assertFalse(response["input_events_emitted"])
        self.assertEqual(response["input_actions_dispatched"], 0)
        execute.assert_not_called()
        self.assertNotIn("snapshot", response)

    def test_out_of_image_point_is_rejected_once_with_batch_index(self):
        execute = mock.Mock()
        validate = mock.Mock(wraps=runtime.validate_gui_action)
        actions = [
            {"type": "type", "text": "hello"},
            {"type": "click", "x": self.desktop["width"] + 5, "y": 20},
        ]
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "execute_gui_action", execute), \
                mock.patch.object(runtime, "validate_gui_action", validate), \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png"}):
            response = runtime.perform_gui(actions)

        self.assertFalse(response["ok"])
        self.assertIn("coordinate pre-validation", response["error"])
        self.assertEqual(
            [(item["index"], item["type"]) for item in response["validation_errors"]],
            [(1, "click")],
        )
        self.assertIn("outside the full desktop image bounds",
                      response["validation_errors"][0]["error"])
        # Each action is validated once; the second pass only maps coordinates.
        self.assertEqual(validate.call_count, len(actions))
        execute.assert_not_called()

    def test_gui_limits_are_enforced_by_runtime_validation(self):
        cases = [
            (
                {
                    "type": "scroll",
                    "x": 1,
                    "y": 1,
                    "scroll_y": runtime.GUI_MAX_SCROLL_DELTA + 1,
                },
                "between",
            ),
            (
                {
                    "type": "drag",
                    "path": [
                        {"x": 1, "y": 1}
                        for _ in range(runtime.GUI_MAX_DRAG_POINTS + 1)
                    ],
                },
                "at most",
            ),
            (
                {
                    "type": "keypress",
                    "keys": ["CTRL" for _ in range(runtime.GUI_MAX_KEYS + 1)],
                },
                "at most",
            ),
            (
                {
                    "type": "keypress",
                    "keys": ["x" * (runtime.GUI_MAX_KEY_LENGTH + 1)],
                },
                "at most",
            ),
            (
                {
                    "type": "type",
                    "text": "x" * (runtime.GUI_MAX_TEXT_LENGTH + 1),
                },
                "at most",
            ),
        ]
        for action, expected in cases:
            with self.subTest(action=action["type"]):
                with self.assertRaises(RuntimeError) as ctx:
                    runtime.validate_gui_action(action, self.desktop)
                self.assertIn(expected, str(ctx.exception))

    def test_gui_scroll_uses_only_computer_contract_field_names(self):
        normalized = runtime.validate_gui_action({
            "type": "scroll",
            "x": 10,
            "y": 20,
            "scroll_x": -2,
            "scroll_y": 3,
        }, self.desktop)
        self.assertEqual(normalized["scroll_x"], -2)
        self.assertEqual(normalized["scroll_y"], 3)
        with self.assertRaisesRegex(RuntimeError, "unsupported field"):
            runtime.validate_gui_action({
                "type": "scroll",
                "x": 10,
                "y": 20,
                "delta_y": 3,
            }, self.desktop)

    def test_gui_type_and_keypress_state_current_focus_boundary(self):
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "parse_key", return_value=([], "S")), \
                mock.patch.object(runtime, "execute_gui_action"), \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png"}):
            response = runtime.perform_gui([
                {"type": "type", "text": "hello"},
                {"type": "keypress", "keys": ["CTRL", "S"]},
            ])

        self.assertTrue(response["ok"])
        self.assertIn("whichever control held input focus", response["text"])
        self.assertIn("did not identify or verify a target", response["text"])
        self.assertTrue(response["effect_unknown"])

    def test_gui_fails_closed_when_desktop_bounds_are_unavailable(self):
        with mock.patch.object(runtime, "desktop_bounds", return_value=None):
            response = runtime.perform_gui([{"type": "click", "x": 1, "y": 2}])

        self.assertFalse(response["ok"])
        self.assertIn("fails closed", response["error"])

    def test_gui_fails_closed_before_input_when_screenshot_is_unavailable(self):
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                patch_gui_capture(
                    return_value={
                        "frameId": "layout",
                        "screenshotPngBase64": None,
                    }), \
                mock.patch.object(runtime, "execute_gui_action") as execute:
            response = runtime.perform_gui([
                {"type": "click", "x": 1, "y": 2},
            ])

        self.assertFalse(response["ok"])
        self.assertIn("failed closed", response["error"])
        self.assertFalse(response["input_events_emitted"])
        execute.assert_not_called()

    def test_gui_fails_closed_before_input_when_layout_is_incoherent(self):
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                patch_gui_capture(
                    return_value={
                        "frameId": None,
                        "screenshotPngBase64": "png",
                    }), \
                mock.patch.object(runtime, "execute_gui_action") as execute:
            response = runtime.perform_gui([
                {"type": "click", "x": 1, "y": 2},
            ])

        self.assertFalse(response["ok"])
        self.assertIn("layout could not be sampled consistently", response["error"])
        self.assertFalse(response["input_events_emitted"])
        execute.assert_not_called()

    def test_pure_observation_survives_missing_layout_provenance(self):
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                patch_gui_capture(
                    return_value={
                        "frameId": None,
                        "screenshotPngBase64": "png",
                    }):
            response = runtime.perform_gui([{"type": "screenshot"}])

        self.assertTrue(response["ok"])
        self.assertIsNone(response["snapshot"]["frameId"])

    def test_pointer_to_keyboard_batch_inserts_focus_barrier(self):
        events = []

        def execute(action):
            events.append(("dispatch", action["type"]))

        def sleep(seconds):
            events.append(("sleep", seconds))

        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "execute_gui_action", side_effect=execute), \
                mock.patch.object(runtime.time, "sleep", side_effect=sleep), \
                mock.patch.object(
                    runtime, "wait_for_gui_settle",
                    return_value={"stable": False, "elapsed_ms": 0, "polls": 0,
                                  "skipped": True}), \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png"}):
            response = runtime.perform_gui([
                {"type": "click", "x": 10, "y": 20},
                {"type": "type", "text": "hello"},
            ])

        self.assertTrue(response["ok"])
        self.assertEqual(events, [
            ("dispatch", "click"),
            ("sleep", runtime.GUI_POINTER_KEYBOARD_BARRIER_SECONDS),
            ("dispatch", "type"),
        ])

    def test_mid_batch_screenshot_is_rejected_before_input(self):
        events = []

        def execute(action):
            events.append(("dispatch", action["type"]))

        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "execute_gui_action", side_effect=execute), \
                mock.patch.object(
                    runtime.time, "sleep",
                    side_effect=lambda seconds: events.append(("sleep", seconds))), \
                mock.patch.object(
                    runtime, "wait_for_gui_settle",
                    return_value={"stable": False, "elapsed_ms": 0, "polls": 0,
                                  "skipped": True}), \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png"}):
            response = runtime.perform_gui([
                {"type": "click", "x": 10, "y": 20},
                {"type": "screenshot"},
                {"type": "keypress", "keys": ["ENTER"]},
            ])

        self.assertFalse(response["ok"])
        self.assertTrue(response["preflight_only"])
        self.assertFalse(response["input_events_emitted"])
        self.assertEqual(events, [])

    def test_explicit_wait_already_breaks_pointer_to_keyboard_transition(self):
        sleeps = []

        def validate(actions, *_args):
            return [
                dict(action, seconds=1.0)
                if action["type"] == "wait"
                else action
                for action in actions
            ], []

        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(
                    runtime, "validate_gui_actions",
                    side_effect=validate), \
                mock.patch.object(runtime, "execute_gui_action"), \
                mock.patch.object(
                    runtime.time, "sleep", side_effect=lambda seconds: sleeps.append(seconds)), \
                mock.patch.object(
                    runtime, "wait_for_gui_settle",
                    return_value={"stable": False, "elapsed_ms": 0, "polls": 0,
                                  "skipped": True}), \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png"}):
            response = runtime.perform_gui([
                {"type": "click", "x": 10, "y": 20},
                {"type": "wait"},
                {"type": "keypress", "keys": ["ENTER"]},
            ])

        self.assertTrue(response["ok"])
        self.assertEqual(sleeps, [])

    def test_fixed_wait_fully_covers_pointer_to_keyboard_barrier(self):
        sleeps = []
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(runtime, "execute_gui_action"), \
                mock.patch.object(
                    runtime.time, "sleep", side_effect=lambda seconds: sleeps.append(seconds)), \
                mock.patch.object(
                    runtime, "wait_for_gui_settle",
                    return_value={"stable": False, "elapsed_ms": 0, "polls": 0,
                                  "skipped": True}), \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png"}):
            response = runtime.perform_gui([
                {"type": "click", "x": 10, "y": 20},
                {"type": "wait"},
                {"type": "type", "text": "hello"},
            ])

        self.assertTrue(response["ok"])
        self.assertEqual(sleeps, [])

    def test_consecutive_keyboard_actions_have_a_processing_barrier(self):
        events = []
        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(
                    runtime, "validate_gui_actions",
                    side_effect=lambda actions, *_args: (actions, [])), \
                mock.patch.object(
                    runtime, "execute_gui_action",
                    side_effect=lambda action: events.append(
                        ("dispatch", action["type"]))), \
                mock.patch.object(
                    runtime.time, "sleep",
                    side_effect=lambda seconds: events.append(("sleep", seconds))), \
                mock.patch.object(
                    runtime, "wait_for_gui_settle",
                    return_value={"stable": False, "elapsed_ms": 0, "polls": 0,
                                  "skipped": True}), \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png"}):
            response = runtime.perform_gui([
                {"type": "type", "text": "one"},
                {"type": "keypress", "keys": ["ENTER"]},
                {"type": "type", "text": "two"},
            ], settle_ms=0)

        self.assertTrue(response["ok"])
        self.assertEqual(events, [
            ("dispatch", "type"),
            ("sleep", runtime.GUI_KEYBOARD_ACTION_INTERVAL_SECONDS),
            ("dispatch", "keypress"),
            ("sleep", runtime.GUI_KEYBOARD_ACTION_INTERVAL_SECONDS),
            ("dispatch", "type"),
        ])

    def test_gui_reports_fixed_waits_without_micro_wait_classification(self):
        def validate(actions, *_args):
            return [
                dict(action, seconds=1.0)
                if action["type"] == "wait"
                else action
                for action in actions
            ], []

        with mock.patch.object(runtime, "desktop_bounds", return_value=self.desktop), \
                mock.patch.object(
                    runtime, "validate_gui_actions",
                    side_effect=validate), \
                mock.patch.object(runtime, "execute_gui_action"), \
                mock.patch.object(
                    runtime, "wait_for_gui_settle",
                    return_value={"stable": False, "elapsed_ms": 0, "polls": 0,
                                  "skipped": True}), \
                patch_gui_capture(
                    return_value={"frameId": "layout", "screenshotPngBase64": "png"}):
            response = runtime.perform_gui([
                {"type": "keypress", "keys": ["ARROWDOWN"]},
                {"type": "wait"},
                {"type": "keypress", "keys": ["ARROWDOWN"]},
                {"type": "wait"},
            ], settle_ms=0)

        self.assertTrue(response["ok"])
        self.assertEqual(response["wait_actions_requested"], 2)
        self.assertEqual(response["wait_seconds_requested"], 2.0)
        self.assertEqual(response["keyboard_micro_wait_actions"], 0)

    def test_execute_gui_action_uses_only_prevalidated_payload(self):
        events = []
        with mock.patch.object(
                runtime, "send_mouse_click",
                side_effect=lambda *args: events.append(("click", args))), \
                mock.patch.object(
                    runtime, "send_drag_path",
                    side_effect=lambda path: events.append(("drag", path))), \
                mock.patch.object(
                    runtime, "send_gui_scroll",
                    side_effect=lambda *args: events.append(("scroll", args))), \
                mock.patch.object(
                    runtime, "send_text",
                    side_effect=lambda text: events.append(("type", text))), \
                mock.patch.object(
                    runtime, "send_key",
                    side_effect=lambda key: events.append(("key", key))), \
                mock.patch.object(
                    runtime, "require_window_focus",
                    side_effect=AssertionError("app-free gui must not focus a window")):
            runtime.execute_gui_action({
                "type": "click",
                "x": 3.0,
                "y": 4.0,
                "button": "left",
            })
            runtime.execute_gui_action({
                "type": "drag",
                "path": [(1.0, 2.0), (3.0, 4.0)],
            })
            runtime.execute_gui_action({
                "type": "scroll",
                "x": 5.0,
                "y": 6.0,
                "scroll_x": 0.0,
                "scroll_y": 2.0,
            })
            runtime.execute_gui_action({"type": "type", "text": "hello"})
            runtime.execute_gui_action({
                "type": "keypress",
                "keys": ["CTRL", "S"],
            })

        self.assertEqual(
            [event[0] for event in events],
            ["click", "drag", "scroll", "type", "key"],
        )


class GUISchemaContractTests(unittest.TestCase):
    """The Linux GUI validator accepts exactly what the published GUI schema
    accepts. The Go test TestGUIActionSamplesMatchSchema writes the samples
    and their schema verdicts; key-name resolution needs a display and is
    stubbed, because the schema leaves key names open."""

    def test_validate_gui_action_matches_published_schema(self):
        path = Path(__file__).resolve().parent.parent / "testdata" / "contract" / "gui_action_samples.json"
        samples = json.loads(path.read_text())
        self.assertGreater(len(samples), 100)
        mismatches = []
        # Whether a key chord names real keys (a modifier before the main
        # key, a resolvable keysym) needs the keyboard map; the schema leaves
        # key names open, so parse_key is stubbed.
        with mock.patch.object(runtime, "keycode", return_value=1), \
                mock.patch("native.linux.provider.ax_actions.keycode", return_value=1), \
                mock.patch("native.linux.provider.ax_actions.parse_key", return_value=([], "a")):
            for sample in samples:
                try:
                    runtime.validate_gui_action(sample["action"])
                    accepted = True
                except RuntimeError:
                    accepted = False
                if accepted != sample["accepted"]:
                    mismatches.append((sample["origin"], sample["action"], sample["accepted"]))
        self.maxDiff = None
        self.assertEqual(mismatches[:20], [], "{} GUI schema/provider mismatches".format(len(mismatches)))
