"""Screen capture, pixel comparison and screenshot encoding."""

import base64
import math
from .foundation import (
    Gdk,
    GdkPixbuf,
    GUI_MODEL_MAX_HEIGHT,
    GUI_MODEL_MAX_WIDTH,
    GUI_ZOOM_MAX_SCALE,
    GUI_ZOOM_MIN_SIDE,
    PIXEL_DIFF_STRIDE,
    PIXEL_DIFF_THRESHOLD,
    SCREENSHOT_MAX_BASE64_CHARS,
)


def capture_window_pixels(bounds):
    """Capture raw pixels of a screen region for before/after comparison.

    The result is not an image for the model. The accessibility tree misses
    whole classes of effects; saving a file in LibreOffice Impress, for
    example, can leave it byte-for-byte unchanged. Pixels are evidence
    independent of the tree: an unchanged tree with a changed screen means
    the effect is real but invisible to the tree, and only an unchanged tree
    together with an unchanged screen is strong evidence that nothing
    happened.

    Use ``read_pixel_bytes()`` rather than ``get_pixels()``: under PyGObject
    the latter returns truncated data.
    """
    if Gdk is None or bounds is None:
        return None
    try:
        screen = Gdk.Screen.get_default()
        if screen is None:
            return None
        pixbuf = Gdk.pixbuf_get_from_window(
            screen.get_root_window(),
            int(round(bounds["x"])), int(round(bounds["y"])),
            max(1, int(round(bounds["width"]))), max(1, int(round(bounds["height"]))))
        if pixbuf is None:
            return None
        return {
            "data": pixbuf.read_pixel_bytes().get_data(),
            "stride": pixbuf.get_rowstride(),
            "channels": pixbuf.get_n_channels(),
            "width": pixbuf.get_width(),
            "height": pixbuf.get_height(),
        }
    except Exception:
        return None


def pixel_change(before, after):
    """Return the changed fraction and region between two captures.

    Returns None when the captures cannot be compared, rather than reporting
    a comparison that did not happen.
    """
    if not before or not after:
        return None
    if (before["width"] != after["width"] or before["height"] != after["height"]
            or before["stride"] != after["stride"]):
        # A resize is itself a change but rules out a pixel-by-pixel
        # comparison; report it as such.
        return {"resized": True}
    a, b = before["data"], after["data"]
    stride, channels = before["stride"], before["channels"]
    width, height = before["width"], before["height"]
    changed = total = 0
    min_x = min_y = 1 << 30
    max_x = max_y = -1
    try:
        for y in range(0, height, PIXEL_DIFF_STRIDE):
            row = y * stride
            for x in range(0, width, PIXEL_DIFF_STRIDE):
                offset = row + x * channels
                total += 1
                delta = (abs(a[offset] - b[offset])
                         + abs(a[offset + 1] - b[offset + 1])
                         + abs(a[offset + 2] - b[offset + 2]))
                if delta > PIXEL_DIFF_THRESHOLD:
                    changed += 1
                    min_x = min(min_x, x)
                    min_y = min(min_y, y)
                    max_x = max(max_x, x)
                    max_y = max(max_y, y)
    except Exception:
        return None
    if total == 0:
        return None
    result = {"resized": False, "changed": changed, "total": total,
              "percent": 100.0 * changed / total}
    if max_x >= 0:
        result["box"] = (min_x, min_y, max_x - min_x + PIXEL_DIFF_STRIDE,
                         max_y - min_y + PIXEL_DIFF_STRIDE)
    return result


def pixel_buffers_identical(before, after):
    """Compare complete captured frames without sampling or colour tolerance."""
    if not before or not after:
        return None
    geometry = ("width", "height", "stride", "channels")
    if any(before.get(key) != after.get(key) for key in geometry):
        return False
    try:
        return before["data"] == after["data"]
    except Exception:
        return None


def persistent_pixel_change(before, after_one, after_two):
    """Count only changes present in both post-action captures.

    Two pre-action captures
    cannot measure noise: they are milliseconds apart, so a 1 Hz text caret
    never blinks between them, the noise floor reads as zero, and a no-op
    keystroke is reported as a screen change located exactly at the caret. A
    blinking element differs between the two post-action captures (on in one,
    off in the other), so requiring both to differ from the pre-action capture
    filters it out, while a real UI change appears in both.

    Building the snapshot fills the gap between the two captures, so the
    extra capture adds almost no latency.
    """
    if not before or not after_one or not after_two:
        return None
    frames = (before, after_one, after_two)
    if len({(f["width"], f["height"], f["stride"]) for f in frames}) != 1:
        return {"resized": True}
    a, b, c = before["data"], after_one["data"], after_two["data"]
    stride, channels = before["stride"], before["channels"]
    width, height = before["width"], before["height"]
    changed = total = 0
    min_x = min_y = 1 << 30
    max_x = max_y = -1
    try:
        for y in range(0, height, PIXEL_DIFF_STRIDE):
            row = y * stride
            for x in range(0, width, PIXEL_DIFF_STRIDE):
                offset = row + x * channels
                total += 1
                base = (a[offset], a[offset + 1], a[offset + 2])
                one = (abs(base[0] - b[offset]) + abs(base[1] - b[offset + 1])
                       + abs(base[2] - b[offset + 2])) > PIXEL_DIFF_THRESHOLD
                if not one:
                    continue
                two = (abs(base[0] - c[offset]) + abs(base[1] - c[offset + 1])
                       + abs(base[2] - c[offset + 2])) > PIXEL_DIFF_THRESHOLD
                if not two:
                    # Changed in only one capture: flicker, not an effect.
                    continue
                changed += 1
                min_x = min(min_x, x)
                min_y = min(min_y, y)
                max_x = max(max_x, x)
                max_y = max(max_y, y)
    except Exception:
        return None
    if total == 0:
        return None
    result = {"resized": False, "changed": changed, "total": total,
              "percent": 100.0 * changed / total}
    if max_x >= 0:
        result["box"] = (min_x, min_y, max_x - min_x + PIXEL_DIFF_STRIDE,
                         max_y - min_y + PIXEL_DIFF_STRIDE)
    return result


def pixel_change_note(change):
    """Describe an AX window pixel comparison, prefixed with `[pixels]`.

    The note states no coordinates: the comparison samples the target
    window, while every image the model can click is the full-desktop frame.
    """
    if change is None:
        return None
    subject = "window"
    if change.get("resized"):
        return (
            "[pixels] geometry_changed=true: The {} dimensions changed between "
            "captures, so a pixel-by-pixel comparison was not possible. Cause, input "
            "delivery, target identity, and semantic effect are unknown."
        ).format(subject)
    if change["changed"] == 0:
        return (
            "[pixels] sampled_pixel_change=none: No sampled pixel difference persisted "
            "between the pre-action {} and both post-action captures. This does not "
            "prove that no application or file state changed."
        ).format(subject)
    strength = "tiny" if change["percent"] <= 0.1 else "material"
    return (
        "[pixels] sampled_pixel_change={}: A sampled pixel difference persisted between "
        "the pre-action {} and both post-action captures ({:.2f}% of sample points; "
        "{} changed). This is visual evidence only; input delivery, target identity, "
        "and task effect are unverified."
    ).format(strength, subject, change["percent"], change["changed"])


def encoded_screenshot(pixbuf):
    smallest = None
    formats = [
        ("png", ["compression"], ["3"], "image/png"),
        ("jpeg", ["quality"], ["85"], "image/jpeg"),
        ("jpeg", ["quality"], ["70"], "image/jpeg"),
        ("jpeg", ["quality"], ["55"], "image/jpeg"),
        ("jpeg", ["quality"], ["40"], "image/jpeg"),
    ]
    for image_format, keys, values, mime_type in formats:
        try:
            ok, data = pixbuf.save_to_bufferv(image_format, keys, values)
        except Exception:
            continue
        if not ok:
            continue
        data = bytes(data)
        candidate = (data, mime_type, image_format + (
            "-quality-" + values[0] if image_format == "jpeg"
            else "-compression-" + values[0]
        ))
        if smallest is None or len(data) < len(smallest[0]):
            smallest = candidate
        # Base64 uses four characters per three bytes, including padding.
        if 4 * ((len(data) + 2) // 3) <= SCREENSHOT_MAX_BASE64_CHARS:
            smallest = candidate
            break
    if smallest is None:
        return None
    data, mime_type, encoding = smallest
    return {
        "data": base64.b64encode(data).decode("ascii"),
        "mimeType": mime_type,
        "encoding": encoding,
    }


def gui_model_dimensions(bounds):
    """Return the exact pixel dimensions delivered to the model."""
    if bounds is None:
        return None
    native_width = max(1, int(round(bounds["width"])))
    native_height = max(1, int(round(bounds["height"])))
    scale = min(
        1.0,
        float(GUI_MODEL_MAX_WIDTH) / native_width,
        float(GUI_MODEL_MAX_HEIGHT) / native_height,
    )
    width = max(1, int(round(native_width * scale)))
    height = max(1, int(round(native_height * scale)))
    # Rounding makes the two axes differ slightly; coordinates map back per
    # axis with these exact ratios.
    return {
        "width": width,
        "height": height,
        "scale_x": float(width) / native_width,
        "scale_y": float(height) / native_height,
    }


def zoom_geometry(region, bounds):
    """Map a zoom region of the GUI frame to a native crop and an image size.

    region is [x0, y0, x1, y1] in pixels of the delivered full-desktop frame.
    The crop is snapped outward to whole native pixels, and the returned
    region is that snapped crop in frame pixels, unrounded. The image fits
    the model box and enlarges native pixels at most GUI_ZOOM_MAX_SCALE
    times. Each axis maps back by its own exact ratio, width / (x1 - x0)
    and height / (y1 - y0); one shared scale would misplace points on a
    narrow crop. Raises ValueError for a region outside the frame or with a
    side shorter than GUI_ZOOM_MIN_SIDE frame pixels.
    """
    frame_size = gui_model_dimensions(bounds)
    frame_width, frame_height = frame_size["width"], frame_size["height"]
    x0, y0, x1, y1 = (float(value) for value in region)
    if not (0 <= x0 < x1 <= frame_width and 0 <= y0 < y1 <= frame_height):
        raise ValueError(
            "zoom region [{:g}, {:g}, {:g}, {:g}] must lie inside the {}x{} "
            "frame with x0 < x1 and y0 < y1".format(
                x0, y0, x1, y1, frame_width, frame_height))
    if x1 - x0 < GUI_ZOOM_MIN_SIDE or y1 - y0 < GUI_ZOOM_MIN_SIDE:
        raise ValueError(
            "zoom region sides must be at least {} frame pixels".format(
                GUI_ZOOM_MIN_SIDE))
    native_width = max(1, int(round(bounds["width"])))
    native_height = max(1, int(round(bounds["height"])))
    per_x = float(native_width) / frame_width
    per_y = float(native_height) / frame_height
    left = max(0, int(math.floor(x0 * per_x + 1e-9)))
    top = max(0, int(math.floor(y0 * per_y + 1e-9)))
    right = min(native_width, int(math.ceil(x1 * per_x - 1e-9)))
    bottom = min(native_height, int(math.ceil(y1 * per_y - 1e-9)))
    crop_width, crop_height = right - left, bottom - top
    enlarge = min(
        GUI_ZOOM_MAX_SCALE,
        float(GUI_MODEL_MAX_WIDTH) / crop_width,
        float(GUI_MODEL_MAX_HEIGHT) / crop_height,
    )
    width = max(1, int(round(crop_width * enlarge)))
    height = max(1, int(round(crop_height * enlarge)))
    return {
        "native": (left, top, crop_width, crop_height),
        "region": [left / per_x, top / per_y, right / per_x, bottom / per_y],
        "width": width,
        "height": height,
    }


def zoom_pixels(pixbuf, geometry):
    """Encode the zoom image of a full-desktop native grab."""
    left, top, crop_width, crop_height = geometry["native"]
    crop = pixbuf.new_subpixbuf(left, top, crop_width, crop_height)
    if crop is None:
        raise RuntimeError("zoom crop failed")
    if (geometry["width"], geometry["height"]) != (crop_width, crop_height):
        crop = crop.scale_simple(
            geometry["width"], geometry["height"], GdkPixbuf.InterpType.BILINEAR)
        if crop is None:
            raise RuntimeError("zoom scaling failed")
    return encoded_screenshot(crop)


def capture_gui_pixels(bounds):
    """Grab the full desktop once, at native resolution and unencoded.

    The same grab serves as the delivered image (scaled lazily by
    scale_to_model) and as the exact comparison sample (pixbuf_sample), so a
    pixel verdict always describes the image the model receives.
    """
    if Gdk is None or GdkPixbuf is None or bounds is None:
        return None
    try:
        screen = Gdk.Screen.get_default()
        if screen is None:
            return None
        pixbuf = Gdk.pixbuf_get_from_window(
            screen.get_root_window(),
            int(round(bounds["x"])),
            int(round(bounds["y"])),
            max(1, int(round(bounds["width"]))),
            max(1, int(round(bounds["height"]))),
        )
        if pixbuf is None or pixbuf_looks_black(pixbuf):
            return None
        return pixbuf
    except Exception:
        return None


def pixbuf_sample(pixbuf):
    """Return the comparison sample of a grab, shaped like capture_window_pixels."""
    if pixbuf is None:
        return None
    try:
        return {
            "data": pixbuf.read_pixel_bytes().get_data(),
            "stride": pixbuf.get_rowstride(),
            "channels": pixbuf.get_n_channels(),
            "width": pixbuf.get_width(),
            "height": pixbuf.get_height(),
        }
    except Exception:
        return None


def scale_to_model(pixbuf, bounds):
    """Return the delivered image for a native grab of ``bounds``."""
    dimensions = gui_model_dimensions(bounds)
    if (
        dimensions["width"] == max(1, int(round(bounds["width"])))
        and dimensions["height"] == max(1, int(round(bounds["height"])))
    ):
        return pixbuf
    scaled = pixbuf.scale_simple(
        dimensions["width"],
        dimensions["height"],
        GdkPixbuf.InterpType.BILINEAR,
    )
    if scaled is None:
        raise RuntimeError("full-desktop screenshot scaling failed")
    return scaled


def pixbuf_looks_black(pixbuf):
    try:
        pixels = pixbuf.get_pixels()
        channels = pixbuf.get_n_channels()
        rowstride = pixbuf.get_rowstride()
        width = pixbuf.get_width()
        height = pixbuf.get_height()
        if width <= 0 or height <= 0 or channels < 3:
            return True
        step_x = max(1, width // 16)
        step_y = max(1, height // 16)
        checked = 0
        for y in range(0, height, step_y):
            row = y * rowstride
            for x in range(0, width, step_x):
                offset = row + (x * channels)
                if (
                    pixels[offset] > 3
                    or pixels[offset + 1] > 3
                    or pixels[offset + 2] > 3
                ):
                    return False
                checked += 1
        return checked > 0
    except Exception:
        return False
