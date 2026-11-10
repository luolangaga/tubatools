"""HCU calls and explicit output for an existing Python code executor.

The host owns execution, cancellation, and image delivery. This module neither
executes model code nor creates a desktop. Successful calls remain silent;
failed calls publish their evidence even when user code catches the exception.
"""
import copy
import inspect
import json

from . import AsyncDesktop, CallCancelled, HcuError, TransportClosed, TransportEffectUnknown


class OutputError(RuntimeError):
    """Output failed after a call; record retains any preceding input facts."""

    def __init__(self, message, record=None):
        self.record = record
        super().__init__(message + "; record=" + json.dumps(record, ensure_ascii=False))


class CodeDesktop(AsyncDesktop):
    """AsyncDesktop with an operator-supplied output sink and image reader.

    output(event) must be synchronous. Text/record/error events
    contain JSON values. Image events contain PNG/JPEG bytes, media_type, and
    the original screen metadata; the host must deliver these as actual images.
    image_reader(path) returns (media_type, bytes). No files are read by default.
    The ptc.local_image_reader helper can restrict reads to a local output root.
    """

    def __init__(self, transport=None, *, output, image_reader=None, retries=2):
        if not callable(output):
            raise TypeError("output must be callable")
        if image_reader is not None and not callable(image_reader):
            raise TypeError("image_reader must be callable")
        super().__init__(transport, retries=retries)
        self.output = output
        self.image_reader = image_reader

    def _output(self, event, record=None):
        try:
            result = self.output(copy.deepcopy(event))
            if inspect.isawaitable(result):
                if inspect.iscoroutine(result):
                    result.close()
                raise TypeError("output must be synchronous")
        except Exception as error:
            raise OutputError("output sink failed: " + str(error), record) from error

    def _report_error(self, error, tool, request_id):
        record = getattr(error, "record", None)
        event = {"type": "error", "tool": tool,
                 "request_id": getattr(error, "request_id", None) or request_id,
                 "code": getattr(error, "code", "transport_error"),
                 "message": str(error), "record": copy.deepcopy(record)}
        if isinstance(error, CallCancelled):
            event["input_emitted"] = error.input_emitted
            if error.transport_error is not None:
                event["transport_error"] = str(error.transport_error)
        elif record is None:
            event["input_emitted"] = getattr(error, "input_emitted", None)
        # No await between receiving call evidence and publishing it. A host
        # that cancels all async tasks cannot interrupt this handoff.
        self._output(event, record)

    async def call(self, tool, args, request_id=None, since=None):
        try:
            return await super().call(tool, args, request_id=request_id, since=since)
        except (HcuError, TransportEffectUnknown, TransportClosed, CallCancelled) as error:
            try:
                self._report_error(error, tool, request_id)
            except OutputError as output_error:
                # Delivery cannot change whether the call failed, was cancelled,
                # or has an unknown effect. Preserve that exception for callers.
                error.output_error = output_error
            raise

    async def emit(self, value):
        """Publish existing text or JSON data without querying the desktop."""
        if isinstance(value, str):
            self._output({"type": "text", "text": value})
        else:
            # Reject opaque handles and invalid JSON before invoking the sink.
            json.dumps(value, allow_nan=False)
            self._output({"type": "record", "record": value}, value)

    async def emit_image(self, record):
        """Publish a call's existing image without repeating its actions.

        A GUI screenshot or AX full/screenshot record carries the GUI frame
        in screen; its pixels are GUI coordinates (image_pixels). A GUI zoom
        record carries its image in images[0] and the mapping back to frame
        pixels in evidence.zoom (zoom_pixels); GUI x/y never take zoom pixels.

        On image failure, emit an output_error event with the original record
        before raising OutputError. Screen dimensions are provider metadata;
        the image sink must verify them if it decodes or transforms the image.
        """
        try:
            screen = record.get("screen") or {}
            zoom = (record.get("evidence") or {}).get("zoom")
            path = screen.get("image")
            if zoom and not path:
                path = (record.get("images") or [None])[0]
                screen = {"width": zoom.get("width"), "height": zoom.get("height"), "image": path}
            if not isinstance(path, str) or not path:
                raise ValueError("record has no screen.image path")
            if self.image_reader is None:
                raise ValueError("configure image_reader for this desktop's output files")
            result = self.image_reader(path)
            if inspect.isawaitable(result):
                if inspect.iscoroutine(result):
                    result.close()
                raise TypeError("image_reader must be synchronous")
            media_type, data = result
            if not isinstance(data, bytes) or not 0 < len(data) <= 5 * 1024 * 1024:
                raise ValueError("image_reader must return image bytes within 5 MiB")
            valid = ((media_type == "image/png" and data.startswith(b"\x89PNG\r\n\x1a\n"))
                     or (media_type == "image/jpeg" and data.startswith(b"\xff\xd8\xff")))
            if not valid:
                raise ValueError("image_reader must return matching PNG/JPEG media_type and bytes")
            event = {"type": "image", "media_type": media_type, "data": data, "screen": screen,
                     "coordinate_space": "zoom_pixels" if zoom else "image_pixels"}
            if zoom:
                event["zoom"] = zoom
            self._output(event, record)
        except Exception as error:
            if isinstance(error, OutputError):
                raise
            self._output({"type": "output_error", "code": "image_unavailable",
                          "message": str(error), "record": record}, record)
            raise OutputError("image delivery failed: " + str(error), record) from error
