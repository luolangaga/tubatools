"""Filesystem locations shared by the harness entry points."""

from __future__ import annotations

import os
from pathlib import Path
import sys

REPO_ROOT = Path(__file__).resolve().parents[1]


def _osworld_root() -> Path:
    """The OSWorld-V2 checkout whose ``desktop_env`` the harness imports.

    Entry points are meant to run with that checkout's ``.venv/bin/python``,
    so the checkout is the interpreter's parent directory. ``OSWORLD_V2_ROOT``
    overrides this; ``external/OSWorld-V2`` is the fallback.
    """
    if os.environ.get("OSWORLD_V2_ROOT"):
        return Path(os.environ["OSWORLD_V2_ROOT"]).expanduser().resolve()
    interpreter_checkout = Path(sys.prefix).resolve().parent
    if (interpreter_checkout / "desktop_env").is_dir():
        return interpreter_checkout
    return REPO_ROOT / "external" / "OSWorld-V2"


OSWORLD_ROOT = _osworld_root()
DEFAULT_ARTIFACT_DIR = REPO_ROOT / "artifacts"
DEFAULT_RESULTS_DIR = REPO_ROOT / "runs"
DEFAULT_ASSET_CACHE = REPO_ROOT / "assets"
DEFAULT_INSTRUCTIONS_FILE = REPO_ROOT / "instructions/hybrid.md"
# Code mode appends this guide to the selected profile.
CODE_MODE_INSTRUCTIONS_FILE = REPO_ROOT / "instructions/hybrid-code.md"
DEFAULT_BASE_INSTRUCTIONS_FILE = REPO_ROOT / "instructions/base.md"


def anchor_path_args(args, *names: str) -> None:
    """Make path arguments absolute before an entry point changes directory.

    Entry points switch into the OSWorld checkout because upstream code uses
    checkout-relative paths; user-supplied relative paths must keep meaning
    "relative to where the command was started".
    """
    for name in names:
        value = getattr(args, name)
        if not value:
            continue
        absolute = os.path.abspath(os.path.expanduser(os.fspath(value)))
        setattr(args, name, type(value)(absolute) if isinstance(value, Path) else absolute)
