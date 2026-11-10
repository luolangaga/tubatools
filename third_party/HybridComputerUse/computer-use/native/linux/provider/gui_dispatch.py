"""Linux GUI capture, input, settling, and response helpers."""

import json
import time
from .foundation import (
    GUI_FRAME_GUARDED_ACTION_TYPES,
    GUI_INPUT_ACTION_TYPES,
    GUI_KEYBOARD_ACTION_INTERVAL_SECONDS,
    GUI_KEYBOARD_ACTION_TYPES,
    GUI_KEYBOARD_MICRO_WAIT_MAX_SECONDS,
    GUI_POINTER_ACTION_TYPES,
    GUI_POINTER_KEYBOARD_BARRIER_SECONDS,
    GUI_QUIESCENT_MIN_SAMPLES,
    GUI_QUIESCENT_MIN_SECONDS,
    GUI_SETTLE_POLL_SECONDS,
    PIXEL_DIFF_STRIDE,
    PIXEL_DIFF_THRESHOLD,
    OperationFailure,
    frame,
)

from .gui_capture import (
    capture_window_pixels,
    gui_model_dimensions,
    zoom_geometry,
    pixel_buffers_identical,
    pixel_change,
)
from .gui_input import (
    execute_gui_action,
    gui_actions_to_screen,
    gui_wait_metrics,
    validate_gui_actions,
)
from .input_primitives import InputEmissionError
from .snapshot import capture_gui_snapshot
from .desktop_layout import (
    desktop_bounds,
    desktop_layout_frame_id,
    desktop_layout_state,
)


def wait_for_gui_settle(bounds, settle_ms, clock=None, sleep=None):
    """Wait for a sustained run of pixel-identical full-desktop samples."""
    if clock is None:
        clock = time.monotonic
    if sleep is None:
        sleep = time.sleep
    if settle_ms == 0:
        return {
            "stable": False,
            "elapsed_ms": 0,
            "polls": 0,
            "skipped": True,
            "reason": "settle_ms_zero",
        }

    previous = capture_window_pixels(bounds)
    if previous is None:
        return {"stable": False, "elapsed_ms": 0, "polls": 0, "unavailable": True}
    start = clock()
    stable_started_at = start
    stable_samples = 1
    polls = 0
    while True:
        now = clock()
        elapsed_seconds = max(0.0, now - start)
        elapsed_ms = int(round(elapsed_seconds * 1000))
        if elapsed_seconds >= settle_ms / 1000.0:
            return {
                "stable": False,
                "elapsed_ms": elapsed_ms,
                "polls": polls,
                "timed_out": True,
                "pixels": previous,
                "stable_samples": stable_samples,
                "stable_for_ms": int(round(
                    max(0.0, now - stable_started_at) * 1000
                )) if stable_samples else 0,
            }
        remaining = max(0.0, settle_ms / 1000.0 - elapsed_seconds)
        sleep(min(GUI_SETTLE_POLL_SECONDS, remaining))
        current = capture_window_pixels(bounds)
        sampled_at = clock()
        polls += 1
        identical = pixel_buffers_identical(previous, current)
        if identical is True:
            stable_samples += 1
        elif current is not None:
            stable_started_at = sampled_at
            stable_samples = 1
        else:
            stable_started_at = sampled_at
            stable_samples = 0
        stable_for = (
            sampled_at - stable_started_at if stable_samples else 0.0
        )
        if (
            stable_samples >= GUI_QUIESCENT_MIN_SAMPLES
            and stable_for + 1e-9 >= GUI_QUIESCENT_MIN_SECONDS
        ):
            return {
                "stable": True,
                "elapsed_ms": int(round((sampled_at - start) * 1000)),
                "polls": polls,
                "pixels": current,
                "stable_samples": stable_samples,
                "stable_for_ms": int(round(stable_for * 1000)),
            }
        if current is not None:
            previous = current


def gui_settle_note(result):
    if result.get("skipped"):
        if result.get("reason") == "observation_only":
            return (
                "This read-only batch returned one current screenshot without "
                "quiescence polling or a before/after visual comparison."
            )
        if result.get("reason") == "no_input_actions":
            return (
                "No quiescence comparison was needed because the batch emitted no "
                "input events."
            )
        return (
            "No quiescence comparison was made because settle_ms=0; this says "
            "nothing about the action effect."
        )
    if result.get("unavailable"):
        return (
            "Quiescence was not established because desktop pixels could not be "
            "sampled; the final screenshot is still attached."
        )
    if result.get("invalidated_by_final_capture"):
        return (
            "A stable run was observed, but the desktop changed again while the final "
            "response screenshot was captured; quiescent was cleared so the verdict "
            "and attached image do not describe different states."
        )
    if result.get("stable"):
        return (
            "{} consecutive full-desktop screenshots were pixel-identical over "
            "{}ms (settled after {}ms and {} poll(s)); quiescent=true describes "
            "only full-frame byte equality and does not verify any action effect."
        ).format(
            result["stable_samples"], result["stable_for_ms"],
            result["elapsed_ms"], result["polls"])
    return (
        "No run of at least {} consecutive pixel-identical full-desktop screenshots "
        "covering at least {}ms was observed within {}ms ({} poll(s)); quiescence "
        "was not established and the final screenshot is only the latest sample."
    ).format(
        GUI_QUIESCENT_MIN_SAMPLES,
        int(GUI_QUIESCENT_MIN_SECONDS * 1000),
        result["elapsed_ms"],
        result["polls"],
    )


def gui_visual_change(before, after):
    identical = pixel_buffers_identical(before, after)
    if identical is None:
        return {
            "changed_since_before": None,
            "visual_change_percent": None,
            "visual_change_box": None,
            "visual_change_method": None,
        }
    if identical:
        return {
            "changed_since_before": False,
            "visual_change_percent": 0.0,
            "visual_change_box": None,
            "visual_change_method": "exact_full_frame_bytes",
        }
    change = pixel_change(before, after)
    method = (
        "exact_full_frame_boolean_with_sampled_rgb_stride_{}_threshold_{}_region"
        .format(PIXEL_DIFF_STRIDE, PIXEL_DIFF_THRESHOLD)
    )
    if change is None:
        return {
            "changed_since_before": True,
            "visual_change_percent": None,
            "visual_change_box": None,
            "visual_change_method": "exact_full_frame_bytes",
        }
    if change.get("resized"):
        return {
            "changed_since_before": True,
            "visual_change_percent": None,
            "visual_change_box": None,
            "visual_change_method": "frame_geometry_changed",
        }
    return {
        "changed_since_before": True,
        "visual_change_percent": change.get("percent"),
        "visual_change_box": change.get("box"),
        "visual_change_method": method,
    }


def finalize_gui_response(response):
    """Attach a stable structured GUI result schema on success and failure."""
    if not isinstance(response, dict):
        return response
    defaults = {
        "dispatched": 0,
        "input_events_emitted": False,
        "input_actions_dispatched": 0,
        "mutation_unknown": False,
        "partial_input_possible": False,
        "effect_unknown": False,
        "quiescent": None,
        "changed_since_before": None,
        "visual_change_percent": None,
        "visual_change_box": None,
        "visual_change_method": None,
        "visual_change_observed": None,
    }
    for field, value in defaults.items():
        response.setdefault(field, value)
    gui_result = response.get("guiResult")
    if not isinstance(gui_result, dict):
        gui_result = {}
    for field, value in defaults.items():
        gui_result.setdefault(field, response.get(field, value))
    response["guiResult"] = gui_result
    return response


# Layouts of the screenshots this provider returned, by frame id. A
# coordinate batch names the frame its coordinates came from; its layout
# says which window was under each point. A restarted provider has none and
# then rejects any layout change, as before.
GUI_PUBLISHED_LAYOUT_LIMIT = 32
_published_layouts = {}


def remember_published_layout(snapshot):
    if not isinstance(snapshot, dict):
        return
    frame_id = snapshot.get("frameId")
    layout = snapshot.get("desktopLayout")
    if not frame_id or not isinstance(layout, dict):
        return
    _published_layouts.pop(frame_id, None)
    _published_layouts[frame_id] = layout
    while len(_published_layouts) > GUI_PUBLISHED_LAYOUT_LIMIT:
        _published_layouts.pop(next(iter(_published_layouts)))


def capture_layout(capture):
    """The layout sampled around a capture, without encoding the image."""
    metadata = getattr(capture, "_metadata", None)
    if not isinstance(metadata, dict):
        return None
    return metadata.get("desktopLayout")


def action_points(action):
    if "path" in action:
        return list(action["path"])
    if "x" in action and "y" in action:
        return [(action["x"], action["y"])]
    return []


def layout_hit(layout, x, y):
    """The topmost visible managed window containing an X root point.

    Windows are in stacking order, bottom first, with desktop-relative
    geometry. The result is the window id and geometry, or None when no
    window contains the point.
    """
    desktop = layout.get("desktop") or {}
    px = x - desktop.get("screenX", 0)
    py = y - desktop.get("screenY", 0)
    for window in reversed(layout.get("windows") or []):
        if (window["x"] <= px < window["x"] + window["width"]
                and window["y"] <= py < window["y"] + window["height"]):
            return (window.get("id"), window["x"], window["y"],
                    window["width"], window["height"])
    return None


def coordinates_still_valid(reference, current, actions):
    """Whether every coordinate of actions still lands where it did.

    The screenshot's coordinates stay valid when, for each point, the same
    window with the same geometry is topmost there now. A window moving or
    stacking elsewhere on the desktop does not change that.
    """
    if not isinstance(reference, dict) or not isinstance(current, dict):
        return False
    if reference.get("desktop") != current.get("desktop"):
        return False
    for action in actions:
        if action["type"] not in GUI_FRAME_GUARDED_ACTION_TYPES:
            continue
        for x, y in action_points(action):
            if layout_hit(reference, x, y) != layout_hit(current, x, y):
                return False
    return True


def windows_over(layout, rect):
    """The visible managed windows that overlap rect, bottom first.

    rect is (x, y, width, height) in desktop-relative native pixels, the
    space of the layout's window geometry.
    """
    x, y, width, height = rect
    over = []
    for window in layout.get("windows") or []:
        if (window["x"] < x + width and x < window["x"] + window["width"]
                and window["y"] < y + height and y < window["y"] + window["height"]):
            over.append((window.get("id"), window["x"], window["y"],
                         window["width"], window["height"]))
    return over


def region_still_valid(reference, current, rect):
    """The area form of coordinates_still_valid: every window over rect,
    and their stacking order, is unchanged since the reference frame."""
    if not isinstance(reference, dict) or not isinstance(current, dict):
        return False
    if reference.get("desktop") != current.get("desktop"):
        return False
    return windows_over(reference, rect) == windows_over(current, rect)


def gui_zoom(action, bounds, frame_id):
    """Return an enlarged region of the GUI frame named by frame_id.

    The zoom is look-only: it emits no input, publishes no frame, and its
    pixels come from a fresh grab only when the windows over the region are
    the same as in that frame, so the region shows the frame's geometry. A
    frame this provider did not publish, or a layout change over the region,
    is rejected with a fresh full-desktop screenshot, as a stale coordinate
    action is.
    """
    def stale(message, capture):
        snapshot = {}
        try:
            snapshot = capture.encode() if capture is not None else {}
        except Exception:
            snapshot = {}
        return {
            "ok": False,
            "error": message + " Nothing was captured for the zoom and no input "
                     "was emitted.",
            "failureCode": "stale_gui_frame",
            "failurePhase": "precondition",
            "dispatched": 0,
            "input_events_emitted": False,
            "input_actions_dispatched": 0,
            "effect_unknown": False,
            "quiescent": None,
            "snapshot": snapshot,
        }

    try:
        geometry = zoom_geometry(action["region"], bounds)
    except ValueError as exc:
        return {
            "ok": False,
            "error": "gui zoom failed region pre-validation; nothing was captured",
            "validation_errors": [{"index": 0, "type": "zoom", "error": str(exc)}],
            "preflight_only": True,
            "dispatched": 0,
            "input_events_emitted": False,
            "input_actions_dispatched": 0,
        }
    try:
        current = capture_gui_snapshot(bounds)
    except Exception as exc:
        return {
            "ok": False,
            "error": "full-desktop capture failed before zoom: {}".format(exc),
            "dispatched": 0,
            "input_events_emitted": False,
            "input_actions_dispatched": 0,
        }
    if not current.has_image or not current.frame_id:
        return stale(
            "The desktop could not be captured with a consistent layout sample.",
            current)
    if not frame_id:
        return stale("Zoom regions are frame pixels, and no GUI frame was named.", current)
    if current.frame_id != frame_id:
        left, top, width, height = geometry["native"]
        if not region_still_valid(
                _published_layouts.get(frame_id), capture_layout(current),
                (left, top, width, height)):
            return stale(
                "The windows over the zoom region changed since the frame its "
                "coordinates came from.", current)
    image = current.zoom(geometry)
    if not image or not image.get("data"):
        return {
            "ok": False,
            "error": "gui zoom capture failed; no input was sent",
            "dispatched": 0,
            "input_events_emitted": False,
            "input_actions_dispatched": 0,
        }
    return {
        "ok": True,
        "text": "GUI zoom; no input was sent.",
        "dispatched": 0,
        "input_events_emitted": False,
        "input_actions_dispatched": 0,
        "guiResult": {"zoom": {
            "region": geometry["region"],
            "width": geometry["width"],
            "height": geometry["height"],
        }},
        # The zoom image is not a frame: no frameId, no layout, so it is
        # never remembered as coordinate provenance.
        "snapshot": {
            "screenshotPngBase64": image["data"],
            "screenshotMimeType": image.get("mimeType"),
            "screenshotEncoding": image.get("encoding"),
            "coordinateSpace": "zoom_image_px",
            "width": geometry["width"],
            "height": geometry["height"],
        },
    }


def perform_gui(actions, settle_ms=800, frame_id=None):
    """Run one validated GUI batch and remember the layout it returns."""
    response = _perform_gui(actions, settle_ms, frame_id)
    if isinstance(response, dict):
        remember_published_layout(response.get("snapshot"))
    return response


def _perform_gui(actions, settle_ms=800, frame_id=None):
    """Run one validated GUI batch.

    The wire validator has already checked that actions is a non-empty array
    and settle_ms an integer in range; the core's published schema bounds the
    batch size, keeps a screenshot alone, and its wait budget bounds the
    total wait. validate_gui_actions is the one provider check of each
    action's values.
    """
    static_actions, static_errors = validate_gui_actions(actions)
    if static_errors:
        return {
            "ok": False,
            "error": "gui batch failed static pre-validation; no actions were "
                     "dispatched and no screenshot was captured",
            "validation_errors": static_errors,
            "preflight_only": True,
            "dispatched": 0,
            "input_events_emitted": False,
            "input_actions_dispatched": 0,
        }

    zooms = sum(1 for action in static_actions if action["type"] == "zoom")
    if zooms and len(static_actions) != 1:
        return {
            "ok": False,
            "error": "gui zoom must be the only action in the call; nothing was "
                     "dispatched or captured",
            "preflight_only": True,
            "dispatched": 0,
            "input_events_emitted": False,
            "input_actions_dispatched": 0,
        }
    bounds = desktop_bounds()
    if bounds is not None and zooms:
        return gui_zoom(static_actions[0], bounds, frame_id)
    if bounds is None:
        return {
            "ok": False,
            "error": "full desktop bounds are unavailable; gui fails closed because "
                     "it cannot validate coordinates or capture its required screenshot",
            "dispatched": 0,
            "input_events_emitted": False,
            "input_actions_dispatched": 0,
            "preflight_only": True,
        }
    model_dimensions = gui_model_dimensions(bounds)
    model_bounds = frame(
        0,
        0,
        model_dimensions["width"],
        model_dimensions["height"],
    )
    validated, coordinate_errors = gui_actions_to_screen(
        static_actions,
        model_bounds,
        bounds,
    )
    if coordinate_errors:
        return {
            "ok": False,
            "error": "gui batch failed coordinate pre-validation; no actions were "
                     "dispatched and no screenshot was captured",
            "validation_errors": coordinate_errors,
            "preflight_only": True,
            "dispatched": 0,
            "input_events_emitted": False,
            "input_actions_dispatched": 0,
        }
    try:
        current_capture = capture_gui_snapshot(bounds)
    except Exception as exc:
        return {
            "ok": False,
            "error": "full-desktop screenshot capture failed before input: {}".format(exc),
            "dispatched": 0,
            "input_events_emitted": False,
            "input_actions_dispatched": 0,
            "effect_unknown": False,
            "quiescent": None,
        }
    def encode_evidence(captured):
        try:
            return captured.encode()
        except Exception:
            return {}

    def failure_evidence(fresh=True):
        if fresh:
            try:
                latest = encode_evidence(capture_gui_snapshot())
                if latest.get("screenshotPngBase64"):
                    return latest, "A fresh full-desktop screenshot is attached."
            except Exception:
                pass
        fallback = encode_evidence(current_capture)
        if fallback.get("screenshotPngBase64"):
            return fallback, (
                "The pre-action screenshot is attached because final observation "
                "failed; it does not show the effects of any emitted input.")
        return fallback, "No screenshot is available; visual state is unknown."

    if not current_capture.has_image:
        return {
            "ok": False,
            "error": "full-desktop screenshot capture is unavailable; gui failed closed "
                     "before emitting input because it could not return or validate the "
                     "visual state used for coordinates",
            "dispatched": 0,
            "input_events_emitted": False,
            "input_actions_dispatched": 0,
            "effect_unknown": False,
            "quiescent": None,
            "snapshot": encode_evidence(current_capture),
        }
    wait_metrics = gui_wait_metrics(validated)
    frame_guarded_actions = any(
        action["type"] in GUI_FRAME_GUARDED_ACTION_TYPES for action in validated)
    if frame_guarded_actions and not current_capture.frame_id:
        return {
            "ok": False,
            "error": "desktop layout could not be sampled consistently around the "
                     "full-desktop screenshot; gui failed closed before coordinate input",
            "dispatched": 0,
            "input_events_emitted": False,
            "input_actions_dispatched": 0,
            "effect_unknown": False,
            "quiescent": None,
            "snapshot": encode_evidence(current_capture),
        }
    reference_layout = _published_layouts.get(frame_id) if frame_id else None
    if frame_id and frame_guarded_actions:
        if (current_capture.frame_id != frame_id
                and not coordinates_still_valid(
                    reference_layout, capture_layout(current_capture), validated)):
            return {
                "ok": False,
                "error": "The desktop layout changed since the screenshot used "
                         "for coordinates. No actions were dispatched and no input "
                         "events were emitted.",
                "failureCode": "stale_gui_frame",
                "failurePhase": "precondition",
                "text": "GUI rejected the coordinate batch against a fresh desktop layout sample.",
                "dispatched": 0,
                "input_events_emitted": False,
                "input_actions_dispatched": 0,
                "effect_unknown": False,
                "quiescent": None,
                "snapshot": encode_evidence(current_capture),
            }

    mutating = any(
        action["type"] not in {"screenshot", "wait"}
        for action in validated
    )
    observation_only = (
        len(validated) == 1 and validated[0]["type"] == "screenshot")
    # The pre-action screenshot is the "before" frame: same grab, no extra sample.
    before_pixels = current_capture.pixels if mutating else None
    dispatched = 0
    input_actions_dispatched = 0
    pointer_settle_credit = None
    for index, action in enumerate(validated):
        action_type = action["type"]
        try:
            # The initial frame check sampled the layout just before this loop;
            # re-sample only once an earlier action or wait could have moved it,
            # and reject only when the window under this action's point moved.
            if (frame_id and dispatched
                    and action_type in GUI_FRAME_GUARDED_ACTION_TYPES):
                live_bounds = desktop_bounds()
                live_layout = desktop_layout_state(live_bounds)
                live_frame_id = desktop_layout_frame_id(live_layout)
                if (live_frame_id != frame_id
                        and not coordinates_still_valid(
                            reference_layout, live_layout, [action])):
                    error_snapshot, evidence_note = failure_evidence()
                    return {
                        "ok": False,
                        "error": "The desktop layout changed during the batch before "
                                 "action {} ({}), so this coordinate action was not "
                                 "emitted.".format(index + 1, action_type),
                        "failureCode": "stale_gui_frame",
                        "failurePhase": "precondition",
                        "text": "GUI stopped before emitting the stale coordinate action. "
                                + evidence_note,
                        "dispatched": dispatched,
                        "input_events_emitted": input_actions_dispatched > 0,
                        "input_actions_dispatched": input_actions_dispatched,
                        "partial_input_possible": False,
                        "effect_unknown": bool(input_actions_dispatched),
                        "mutation_unknown": bool(input_actions_dispatched),
                        "quiescent": None,
                        "snapshot": error_snapshot,
                    }
            if (pointer_settle_credit is not None
                    and action_type in GUI_KEYBOARD_ACTION_TYPES):
                remaining = max(
                    0.0,
                    GUI_POINTER_KEYBOARD_BARRIER_SECONDS
                    - pointer_settle_credit,
                )
                if remaining:
                    time.sleep(remaining)
            execute_gui_action(action)
            dispatched += 1
            if action_type in GUI_INPUT_ACTION_TYPES:
                input_actions_dispatched += 1
            if action_type in GUI_POINTER_ACTION_TYPES:
                pointer_settle_credit = 0.0
            elif action_type == "wait" and pointer_settle_credit is not None:
                pointer_settle_credit += action["seconds"]
            elif action_type in GUI_KEYBOARD_ACTION_TYPES:
                pointer_settle_credit = None
            if (
                action_type in GUI_KEYBOARD_ACTION_TYPES
                and index + 1 < len(validated)
                and validated[index + 1]["type"] in GUI_INPUT_ACTION_TYPES
            ):
                time.sleep(GUI_KEYBOARD_ACTION_INTERVAL_SECONDS)
        except Exception as exc:
            explicit_partial = (
                getattr(exc, "events_emitted", False)
                or isinstance(exc, OperationFailure) and exc.input_events_emitted
            )
            input_events_emitted = bool(
                input_actions_dispatched or explicit_partial)
            partial_input_possible = bool(
                explicit_partial
                or (
                    action_type in GUI_INPUT_ACTION_TYPES
                    and not isinstance(exc, (InputEmissionError, OperationFailure))
                )
            )
            error_snapshot, evidence_note = failure_evidence()
            return {
                "ok": False,
                "error": "gui action {} ({}) failed after {} dispatched action(s): {}".format(
                    index + 1, action_type or "unknown", dispatched, exc),
                "text": "GUI stopped at the first failed action. " + evidence_note
                        + " The failing action may have emitted a partial input "
                        "sequence; no application effect is inferred.",
                "dispatched": dispatched,
                "input_events_emitted": input_events_emitted,
                "input_actions_dispatched": input_actions_dispatched,
                "partial_input_possible": partial_input_possible,
                "effect_unknown": bool(
                    input_events_emitted
                    or partial_input_possible),
                "mutation_unknown": bool(
                    input_events_emitted
                    or partial_input_possible),
                "quiescent": None,
                "snapshot": error_snapshot,
            }
    try:
        settle_result = (
            wait_for_gui_settle(bounds, settle_ms)
            if not observation_only
            else {
                "stable": False,
                "elapsed_ms": 0,
                "polls": 0,
                "skipped": True,
                "reason": "observation_only",
            }
        )
        final_capture = (
            current_capture if observation_only else capture_gui_snapshot())
        if not final_capture.has_image:
            raise RuntimeError("full-desktop screenshot capture is unavailable")
        final_snapshot = final_capture.encode()
        if not final_snapshot.get("screenshotPngBase64"):
            raise RuntimeError("full-desktop screenshot encoding is unavailable")
    except Exception as exc:
        error_snapshot, evidence_note = failure_evidence(fresh=False)
        return {
            "ok": False,
            "error": "gui post-dispatch observation failed after {} dispatched "
                     "action(s): {}".format(dispatched, exc),
            "text": "GUI final observation failed. " + evidence_note,
            "dispatched": dispatched,
            "input_events_emitted": input_actions_dispatched > 0,
            "input_actions_dispatched": input_actions_dispatched,
            "partial_input_possible": False,
            "effect_unknown": bool(input_actions_dispatched),
            "mutation_unknown": bool(input_actions_dispatched),
            "quiescent": None,
            "snapshot": error_snapshot,
        }
    # Every pixel verdict describes the delivered image itself. A stable run
    # counts only when the delivered frame is byte-identical to its last
    # sample; otherwise the desktop moved on and quiescent would describe a
    # different state than the attached image.
    after_pixels = (
        final_capture.pixels
        if not observation_only and final_capture.matches_bounds(bounds)
        else None
    )
    if settle_result.get("stable") and pixel_buffers_identical(
            settle_result.get("pixels"), after_pixels) is not True:
        settle_result = dict(settle_result)
        settle_result["stable"] = False
        settle_result["invalidated_by_final_capture"] = True
    visual = (
        gui_visual_change(before_pixels, after_pixels)
        if mutating else gui_visual_change(None, None)
    )
    gui_result = {
        "dispatched": dispatched,
        "input_events_emitted": input_actions_dispatched > 0,
        "input_actions_dispatched": input_actions_dispatched,
        "effect_unknown": mutating,
        "quiescent": True if settle_result.get("stable") else None,
        "changed_since_before": visual["changed_since_before"],
        "visual_change_percent": visual["visual_change_percent"],
        "visual_change_box": visual["visual_change_box"],
        "visual_change_method": visual["visual_change_method"],
        "visual_change_observed": visual["changed_since_before"] if mutating else None,
        **wait_metrics,
    }
    size = "{}x{}".format(final_snapshot.get("width"), final_snapshot.get("height"))
    focus_note = ""
    if any(action["type"] in {"type", "keypress"} for action in validated):
        focus_note = (
            " GUI type/keypress input was emitted toward whichever control held input "
            "focus; gui did not identify or verify a target, delivery, or effect."
        )
    wait_note = ""
    if wait_metrics["keyboard_micro_wait_actions"]:
        wait_note = (
            " {} explicit wait action(s) of at most {:.1f}s were placed between "
            "keyboard/text actions. wait produced no intermediate observation; "
            "consecutive keyboard/text actions already receive a short processing "
            "barrier when no wait action separates them."
        ).format(
            wait_metrics["keyboard_micro_wait_actions"],
            GUI_KEYBOARD_MICRO_WAIT_MAX_SECONDS,
        )
    return {
        "ok": True,
        "text": "GUI dispatched {}/{} action(s); {} input-producing action(s) "
                "completed event emission. {} visual_changed={} is a non-assertive "
                "full-frame pixel observation; it does not verify delivery, target, "
                "or semantic effect. effect_unknown={}. The attached "
                "full-desktop screenshot is {}; coordinateSpace=screen_image_px.{}{}"
                .format(
                    dispatched, len(actions), input_actions_dispatched,
                    gui_settle_note(settle_result),
                    json.dumps(visual["changed_since_before"]),
                    str(mutating).lower(), size,
                    focus_note,
                    wait_note),
        **gui_result,
        "guiResult": gui_result,
        "snapshot": final_snapshot,
    }
