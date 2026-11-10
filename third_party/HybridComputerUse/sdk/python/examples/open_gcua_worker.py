"""Inject HCU into the existing OpenGCUA persistent Python worker.

Operator example tested against OpenGCUA revision
ae34bba0643598638232103c32dbfa017f3b84c8, using private bootstrap and
cleanup hooks, not a public plugin API. Set OPEN_GCUA_RUNTIME_DIR to that
checkout's packages/harness/runtime and pass this file as CodeExecutor's
runtime_path. Set HCU_SOCKET, HCU_SESSION, and HCU_OUTPUT_ROOT explicitly.
The host needs Python 3.11+ and hcu_sdk/Pillow on its import path.

Model code gets d (CodeDesktop), emit, emit_image, hcu, and asyncio. Use one
executor per isolated task. The host must bind presentation screenshot_index
to the emitted file and send actual image content, without resizing. Do not
use the upstream Mac/normalized-coordinate prompt with this HCU bootstrap.
This is trusted Python, not a security sandbox. No model or daemon is started.
"""
import asyncio
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import threading
import uuid

import hcu_sdk as hcu
from hcu_sdk.code import CodeDesktop
from hcu_sdk.ptc import local_image_reader


def install(runtime, *, transport, image_reader):
    """Install the small HCU bootstrap into an operator-selected upstream module."""
    if sys.version_info < (3, 11):
        raise RuntimeError("OpenGCUA CodeExecutor requires Python 3.11 or newer")
    for name in ("_bootstrap", "_close_sdk_session", "_reset_sdk_session",
                 "_check_active_request", "_emit", "_worker", "main"):
        if not callable(getattr(runtime, name, None)):
            raise RuntimeError("unsupported OpenGCUA runtime: missing " + name)
    desktop = None

    def output(event):
        if event["type"] in ("error", "output_error"):
            # Settled cancellation evidence is emitted while the old cell drains,
            # after its input permit has been revoked but before its request ends.
            token = runtime._ACTIVE_REQUEST.get()
            if (threading.current_thread() is not threading.main_thread()
                    or token is None or token["request_id"] !=
                    getattr(runtime._REQUEST_CONTEXT, "request_id", None)):
                raise RuntimeError("error output does not belong to the current cell")
        else:
            runtime._check_active_request()
        if event["type"] == "image":
            from PIL import Image
            data, screen = event["data"], event["screen"]
            with Image.open(io.BytesIO(data)) as image:
                image.load()
                width, height = image.size
            if (screen.get("width"), screen.get("height")) != (width, height):
                raise ValueError("image dimensions differ from HCU screen metadata")
            directory = Path(runtime._CONFIG.get("image_dir") or runtime._CONFIG["work_dir"])
            directory.mkdir(parents=True, exist_ok=True)
            path = directory / ("hcu-" + uuid.uuid4().hex + (".png" if event["media_type"] == "image/png" else ".jpg"))
            path.write_bytes(data)
            # A zoom image is look-only: coordinate_space zoom_pixels tells the
            # host that GUI x/y come from the mapping, not from these pixels.
            runtime._emit({"type": "screenshot", "path": str(path.resolve()),
                           "width": width, "height": height,
                           "coordinate_space": event["coordinate_space"]})
        else:
            runtime._emit({"type": "display", "kind": "text",
                           "text": json.dumps(event, ensure_ascii=False),
                           "value_type": "hcu_" + event["type"]})

    class ActiveDesktop(CodeDesktop):
        async def call(self, *args, **kwargs):
            runtime._check_active_request()
            return await super().call(*args, **kwargs)

    class Session:
        # HCU core owns ref and GUI-frame validation; no mai-ui state is created.
        def begin_turn(self):
            pass

        def end_turn(self):
            pass

        def reset_output_context(self):
            raise RuntimeError("create a fresh HCU executor/session after output-context reset")

    def bootstrap(loop):
        nonlocal desktop
        desktop = ActiveDesktop(transport, output=output, image_reader=image_reader)
        runtime._MAI_SESSION = Session()
        namespace = {"__name__": "__main__", "__builtins__": __builtins__,
                     "d": desktop, "emit": desktop.emit, "emit_image": desktop.emit_image,
                     "hcu": hcu, "asyncio": asyncio, "todo": runtime.todo}
        runtime._EXEC_NAMESPACE = namespace
        return namespace

    async def close_session(*, close_runtime):
        close = getattr(transport, "close", None)
        if close is not None:
            await asyncio.get_running_loop().run_in_executor(None, close)

    async def reset_session():
        raise RuntimeError("create one fresh CodeExecutor per isolated HCU task")

    runtime._bootstrap = bootstrap
    runtime._close_sdk_session = close_session
    runtime._reset_sdk_session = reset_session


def main(argv):
    if sys.version_info < (3, 11):
        raise RuntimeError("OpenGCUA CodeExecutor requires Python 3.11 or newer")
    required = ("OPEN_GCUA_RUNTIME_DIR", "HCU_SOCKET", "HCU_SESSION", "HCU_OUTPUT_ROOT")
    missing = [name for name in required if not os.environ.get(name)]
    if missing:
        raise RuntimeError("missing configuration: " + ", ".join(missing))
    directory = Path(os.environ["OPEN_GCUA_RUNTIME_DIR"]).resolve(strict=True)
    path = directory / "code_runtime.py"
    if not path.is_file():
        raise RuntimeError("OPEN_GCUA_RUNTIME_DIR must contain code_runtime.py")
    sys.path.insert(0, str(directory))
    spec = importlib.util.spec_from_file_location("hcu_opengcua_runtime", path)
    runtime = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(runtime)
    install(runtime, transport=hcu.SocketTransport(path=os.environ["HCU_SOCKET"],
                                                  session=os.environ["HCU_SESSION"], start=False),
            image_reader=local_image_reader(os.environ["HCU_OUTPUT_ROOT"]))
    return runtime.main(argv)


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
