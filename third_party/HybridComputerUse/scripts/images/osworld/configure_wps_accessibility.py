#!/usr/bin/env python3
"""Enable the bundled Qt accessibility bridge at each WPS launcher."""

import os
from pathlib import Path


VARIABLE = b"QT_LINUX_ACCESSIBILITY_ALWAYS_ON"
EXPORT = b"export " + VARIABLE + b"=1\n"
APPLICATIONS = ("wps", "wpp", "et")


def configure(bin_directory=Path("/usr/bin")):
    updates = []
    # Validate every entry before changing any launcher in the suite.
    for name in APPLICATIONS:
        path = bin_directory / name
        if path.is_symlink() or not path.is_file():
            raise RuntimeError("Expected a regular WPS launcher: " + str(path))
        original = path.read_bytes()
        shebang, separator, body = original.partition(b"\n")
        if shebang != b"#!/bin/bash" or not separator or not os.access(path, os.X_OK):
            raise RuntimeError("Unrecognized WPS launcher: " + str(path))
        if body.startswith(EXPORT) and VARIABLE not in body[len(EXPORT):]:
            continue
        if VARIABLE in body:
            raise RuntimeError("Conflicting Qt accessibility setting: " + str(path))
        if not body.startswith(b"\n"):
            raise RuntimeError("Expected a blank line after the WPS shebang: " + str(path))
        # Replace that blank line to preserve line numbers in shell diagnostics.
        updates.append((path, shebang + separator + EXPORT + body[1:]))
    for path, contents in updates:
        # Keep the vendor's argument parsing, process routing and exit behavior.
        path.write_bytes(contents)
    return {"launchers": [str(bin_directory / name) for name in APPLICATIONS],
            "updated": len(updates)}


if __name__ == "__main__":
    if os.geteuid() != 0:
        raise RuntimeError("Run WPS accessibility configuration as root inside the guest")
    print(configure())
