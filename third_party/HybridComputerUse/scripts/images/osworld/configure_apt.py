#!/usr/bin/env python3
"""Keep APT and PackageKit binary package caches local to each process."""

import os
from pathlib import Path


CONFIG = '''// APT and PackageKit can otherwise delete each other's temporary binary caches.
// Keep package indexes and archive downloads; build binary caches in memory.
Dir::Cache::pkgcache "";
Dir::Cache::srcpkgcache "";
'''
CONFIG_PATH = Path("/etc/apt/apt.conf.d/99hcu-apt-cache")


def configure(path=CONFIG_PATH):
    if path.is_symlink() or (path.exists() and (
            not path.is_file() or path.read_text() != CONFIG)):
        raise RuntimeError("APT cache configuration path is already occupied: " + str(path))
    path.write_text(CONFIG)
    path.chmod(0o644)


if __name__ == "__main__":
    if os.geteuid() != 0:
        raise RuntimeError("Run APT configuration as root inside the guest")
    configure()
    print("APT binary disk caches disabled; boot the exported image to apply this to all package readers.")
