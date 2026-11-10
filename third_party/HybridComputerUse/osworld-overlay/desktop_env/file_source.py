# SPDX-License-Identifier: Apache-2.0
# Modified from xlang-ai/OSWorld-V2 file_source.py; see THIRD_PARTY_NOTICES.md.
"""Task setup assets: local files first, then a download cache keyed by URL.

Setup steps resolve every source through ``require_setup_source``. A local
path or ``file://`` URI is used as is. An HTTP(S) source is looked up in the
cache at ``<cache>/<host>/<url path>``; a missing file is downloaded into the
cache once, unless offline mode forbids it, in which case setup fails.
Browser and application network access during the task is not affected.
"""

from __future__ import annotations

import hashlib
import os
from pathlib import Path
import tempfile
from urllib.parse import unquote, urlparse

import requests

DEFAULT_BASE_URL = "https://huggingface.co/datasets/xlangai/osworld_v2_assets/resolve/main"
HISTORY_DB_URL = (
    "https://huggingface.co/datasets/xlangai/ubuntu_osworld_file_cache/resolve/main/"
    "chrome/44ee5668-ecd5-4366-a6ce-c1c9b8d4e938/history_empty.sqlite?download=true"
)

_cache_dir: Path | None = None
_offline = False


def configure(cache_dir: str | os.PathLike | None, *, offline: bool = False) -> None:
    """Select the download cache and whether setup may download into it."""
    global _cache_dir, _offline
    _cache_dir = Path(cache_dir).expanduser().resolve() if cache_dir else None
    _offline = offline


def cache_path(url: str) -> Path | None:
    """Where an HTTP(S) source lives in the cache; None for other sources."""
    parsed = urlparse(url)
    if parsed.scheme not in ("http", "https") or _cache_dir is None:
        return None
    parts = [part for part in unquote(parsed.path).split("/") if part]
    if not parts or any(part in {".", ".."} for part in parts):
        raise ValueError(f"Setup asset URL has no usable path: {url}")
    if parsed.query:
        digest = hashlib.sha256(parsed.query.encode()).hexdigest()[:12]
        parts[-1] = f"{parts[-1]}.{digest}"
    return _cache_dir.joinpath(parsed.hostname or "_", *parts)


def resolve_local_source(url: str) -> str | None:
    """Return an existing local file for ``url`` without any network access.

    Upstream evaluators also call this helper. Plain paths try the literal
    name first, then the percent-decoded name.
    """
    if not url:
        return None
    if url.startswith("file://"):
        parsed = urlparse(url)
        if parsed.netloc not in ("", "localhost"):
            raise ValueError("Asset file URI must be local")
        path = Path(unquote(parsed.path))
        return str(path.resolve()) if path.exists() else None
    if "://" not in url:
        for raw in (url, unquote(url)):
            path = Path(raw).expanduser()
            if path.exists():
                return str(path.resolve())
        return None
    cached = cache_path(url)
    return str(cached) if cached is not None and cached.is_file() else None


def download(url: str, destination: Path) -> None:
    """Publish ``destination`` only after the complete body has arrived."""
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with requests.get(url, stream=True, timeout=(15, 120)) as response:
            response.raise_for_status()
            with tempfile.NamedTemporaryFile(dir=destination.parent, delete=False) as stream:
                temporary = Path(stream.name)
                size = 0
                for chunk in response.iter_content(chunk_size=1024 * 1024):
                    stream.write(chunk)
                    size += len(chunk)
            length = response.headers.get("Content-Length")
            # requests transparently decompresses Content-Encoding bodies.
            if length is not None and not response.headers.get("Content-Encoding") and size != int(length):
                raise ValueError(f"Incomplete setup asset response: {url}")
            os.replace(temporary, destination)
            temporary = None
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def require_setup_source(source: str) -> str:
    """Return a readable local file for a setup source, downloading if allowed."""
    local = resolve_local_source(source)
    if local is not None and Path(local).is_file():
        return local
    destination = cache_path(source)
    if destination is None:
        raise FileNotFoundError(f"Setup asset is not a local file: {source}")
    if _offline:
        raise FileNotFoundError(
            f"Setup asset is not in the cache and offline mode forbids downloads: {source} "
            f"(expected {destination})"
        )
    download(source, destination)
    return str(destination)


def get_base_url() -> str:
    return os.environ.get("OSWORLD_FILE_BASE_URL", DEFAULT_BASE_URL).rstrip("/")


def asset(path: str) -> str:
    if not isinstance(path, str) or not path or path.startswith("/"):
        raise ValueError("asset() requires a nonempty relative path")
    if ".." in Path(unquote(path)).parts:
        raise ValueError("asset() path must not escape its root")
    base = get_base_url()
    if "://" not in base:
        return os.path.join(base, path)
    return f"{base}/{path}"
