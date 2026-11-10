from __future__ import annotations

import os
from pathlib import Path
import tempfile


def isolated_default_cache_dir() -> str:
    """Return a unique cache root for callers without a result binding."""
    return tempfile.mkdtemp(prefix=f"osworld-cache-{os.getpid()}-")


def run_scoped_cache_dir(
    result_dir: str | os.PathLike[str],
    *scope: str,
) -> str:
    """Create a cache root owned by one evaluation run or worker."""
    path = Path(result_dir).resolve() / ".cache"
    for component in scope:
        if not component or component in {".", ".."}:
            raise ValueError(f"Invalid cache scope component: {component!r}")
        if Path(component).name != component:
            raise ValueError(f"Invalid cache scope component: {component!r}")
        path /= component

    path.mkdir(mode=0o700, parents=True, exist_ok=True)
    if not path.is_dir():
        raise NotADirectoryError(path)
    if not os.access(path, os.W_OK | os.X_OK):
        raise PermissionError(f"Run-scoped cache is not writable: {path}")
    return str(path)
