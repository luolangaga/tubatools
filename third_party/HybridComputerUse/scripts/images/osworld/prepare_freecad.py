#!/usr/bin/env python3
"""Download Ubuntu packages into a private FreeCAD/OCCT installation bundle."""

import argparse
import json
from pathlib import Path
import platform
import subprocess
import tarfile
import tempfile


def prepare(manifest_path, output):
    manifest = json.loads(manifest_path.read_text())
    release = platform.freedesktop_os_release()
    if release.get("ID") != "ubuntu" or release.get("VERSION_ID") != "22.04":
        raise RuntimeError("Prepare FreeCAD packages in Ubuntu 22.04 with its apt repositories")
    arch = subprocess.check_output(["dpkg", "--print-architecture"], text=True).strip()
    if arch != manifest["architecture"]:
        raise RuntimeError("Package manifest architecture differs from this environment")
    if output.exists():
        raise FileExistsError(output)
    with tempfile.TemporaryDirectory(prefix="freecad-packages-") as directory:
        staging = Path(directory)
        for item in manifest["packages"]:
            name = item["file"]
            if Path(name).name != name or not name.endswith(".deb"):
                raise ValueError("Package filename must be a .deb basename")
            subprocess.run(["apt-get", "download",
                            item["Package"] + "=" + item["Version"]],
                           cwd=staging, check=True)
            package = staging / name
            for field in ("Package", "Version", "Architecture"):
                actual = subprocess.check_output(
                    ["dpkg-deb", "-f", str(package), field], text=True).strip()
                if actual != item[field]:
                    raise ValueError("Downloaded package metadata differs: " + name)
        output.parent.mkdir(parents=True, exist_ok=True)
        with tarfile.open(output, "x") as archive:
            archive.add(manifest_path, arcname="manifest.json")
            for item in manifest["packages"]:
                archive.add(staging / item["file"], arcname=item["file"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path,
                        default=Path(__file__).with_name("freecad-packages.json"))
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    prepare(args.manifest, args.output)
