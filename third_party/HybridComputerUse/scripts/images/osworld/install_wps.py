#!/usr/bin/env python3
"""Install an explicitly selected upstream WPS package without copying profiles."""

import argparse
import json
import os
from pathlib import Path
import pwd
import stat
import subprocess
import tempfile

import configure_wps_accessibility


def run(command, timeout=60):
    result = subprocess.run(command, capture_output=True, text=True, timeout=timeout,
                            env={**os.environ, "DEBIAN_FRONTEND": "noninteractive"})
    if result.returncode:
        raise RuntimeError(f"{command[0]} failed ({result.returncode}):\n{result.stdout}\n{result.stderr}")
    return result.stdout


def inspect_package(package, manifest):
    metadata = {field: run(["dpkg-deb", "-f", str(package), field]).strip()
                for field in ("Package", "Version", "Architecture")}
    if metadata != manifest["package"]:
        raise ValueError("WPS package metadata differs from the selected manifest: " + json.dumps(metadata))
    architecture = run(["dpkg", "--print-architecture"]).strip()
    if metadata["Architecture"] != architecture:
        raise ValueError("WPS package architecture differs from the guest")
    return metadata


def mime_paths():
    paths = [Path("/etc/xdg/mimeapps.list"), Path("/usr/share/applications/mimeapps.list"),
             Path("/root/.config/mimeapps.list")]
    for home in Path("/home").iterdir():
        if not home.is_dir():
            continue
        try:
            pwd.getpwuid(home.stat().st_uid)
        except KeyError:
            continue
        paths.append(home / ".config/mimeapps.list")
    return paths


def snapshot_mime_files():
    """Preserve every MIME preference file written by the package maintainer scripts."""
    snapshot = {}
    for path in mime_paths():
        if not path.exists() and not path.is_symlink():
            snapshot[path] = None
            continue
        info = path.stat()
        link_info = path.lstat()
        snapshot[path] = {"data": path.read_bytes(), "mode": stat.S_IMODE(info.st_mode),
                          "uid": info.st_uid, "gid": info.st_gid,
                          "link": os.readlink(path) if path.is_symlink() else None,
                          "link_uid": link_info.st_uid, "link_gid": link_info.st_gid}
    return snapshot


def restore_mime_files(snapshot):
    for path, previous in snapshot.items():
        if previous is None:
            path.unlink(missing_ok=True)
            continue
        path.parent.mkdir(parents=True, exist_ok=True)
        if previous["link"] is not None:
            if not path.is_symlink() or os.readlink(path) != previous["link"]:
                path.unlink(missing_ok=True)
                path.symlink_to(previous["link"])
            os.lchown(path, previous["link_uid"], previous["link_gid"])
            destination = path.resolve(strict=False)
        else:
            destination = path
        with tempfile.NamedTemporaryFile(dir=destination.parent, delete=False) as stream:
            temporary = Path(stream.name)
            try:
                stream.write(previous["data"])
                stream.flush()
                os.fchown(stream.fileno(), previous["uid"], previous["gid"])
                os.fchmod(stream.fileno(), previous["mode"])
                temporary.replace(destination)
            finally:
                temporary.unlink(missing_ok=True)


def install(package, manifest, check=False):
    package = package.resolve(strict=True)
    metadata = inspect_package(package, manifest)
    if run(["dpkg", "--audit"]).strip():
        raise RuntimeError("Resolve existing dpkg errors before installing WPS")
    plan = run(["apt-get", "--simulate", "--no-remove", "install", str(package)], timeout=180)
    if check:
        return {"package": metadata, "installation_performed": False, "dependency_plan": plan}
    if os.geteuid() != 0:
        raise RuntimeError("Run installation as root inside the guest")
    # WPS postinst prepends its own defaults and changes user file permissions.
    # Restore the full preference files, including on a failed package upgrade.
    mime_snapshot = snapshot_mime_files()
    try:
        run(["apt-get", "--no-remove", "-y", "install", str(package)], timeout=600)
    finally:
        restore_mime_files(mime_snapshot)
    if run(["dpkg", "--audit"]).strip():
        raise RuntimeError("dpkg reports incomplete packages after WPS installation")
    observed = run(["dpkg-query", "-W", "-f=${Version}\t${Status}", metadata["Package"]]).strip()
    if observed != metadata["Version"] + "\tinstall ok installed":
        raise RuntimeError("WPS package did not reach the selected installed state: " + observed)
    accessibility = configure_wps_accessibility.configure()
    return {"package": metadata, "installation_performed": True, "dependency_plan": plan,
            "accessibility": accessibility}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package", type=Path, required=True, help="Downloaded upstream WPS .deb")
    parser.add_argument("--manifest", type=Path, default=Path(__file__).with_name("wps.json"))
    parser.add_argument("--check", action="store_true", help="Validate metadata and simulate dependencies only")
    args = parser.parse_args()
    print(json.dumps(install(args.package, json.loads(args.manifest.read_text()), args.check), indent=2))
