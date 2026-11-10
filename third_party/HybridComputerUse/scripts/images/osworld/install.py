#!/usr/bin/env python3
"""Install ordinary desktop applications into a clean OSWorld guest."""

import argparse
import json
import os
from pathlib import Path
import platform
import pwd
import subprocess

import install_qt
import configure_apt


ROOT = Path(__file__).resolve().parent
PHASES = ("gtk", "qt", "joplin", "special", "preferences", "support")


def run(command):
    subprocess.run(command, check=True,
                   env={**os.environ, "DEBIAN_FRONTEND": "noninteractive"})


def required_assets(manifest, phases):
    keys = []
    if "qt" in phases:
        keys.extend(("freecad", "geogebra", "kicad"))
    if "joplin" in phases:
        keys.append("joplin")
    if "special" in phases:
        keys.append("slicer")
    return [manifest[key]["file"] for key in keys]


def preflight(manifest, assets, phases):
    release = platform.freedesktop_os_release()
    if (release.get("ID") != manifest["distribution"] or
            release.get("VERSION_ID") != manifest["ubuntu_version"]):
        raise RuntimeError("The selected manifest requires Ubuntu " + manifest["ubuntu_version"])
    arch = subprocess.check_output(["dpkg", "--print-architecture"], text=True).strip()
    if arch != manifest["architecture"]:
        raise RuntimeError("Guest architecture differs from the selected manifest")
    for name in required_assets(manifest, phases):
        if Path(name).name != name or not (assets / name).is_file():
            raise ValueError("Missing asset or invalid basename: " + name)


def install_special(manifest, assets, user):
    run(["apt-get", "install", "-y", "--no-upgrade", "--no-install-recommends",
         *manifest["special_packages"]])
    present = subprocess.run(["snap", "list", "openboard"], capture_output=True)
    if present.returncode:
        run(["snap", "install", "openboard", "--channel=" + manifest["openboard_channel"]])
    if not os.access("/snap/bin/openboard", os.X_OK):
        raise RuntimeError("OpenBoard launcher is missing")
    if not os.access("/opt/Slicer/Slicer", os.X_OK):
        destination = Path(user.pw_dir) / ".local/opt"
        run(["runuser", "-u", user.pw_name, "--", "mkdir", "-p", str(destination)])
        run(["runuser", "-u", user.pw_name, "--", "tar", "-xzf",
             str(assets / manifest["slicer"]["file"]), "-C", str(destination)])
        application = destination / manifest["slicer"]["directory"]
        if not os.access(application / "Slicer", os.X_OK):
            raise RuntimeError("Slicer archive did not provide its expected launcher")
        Path("/opt/Slicer").symlink_to(application)


def install_phase(phase, manifest, manifest_path, assets, user):
    if phase in ("gtk", "support"):
        run(["apt-get", "install", "-y", "--no-upgrade", "--no-install-recommends",
             *manifest[phase + "_packages"]])
    elif phase == "qt":
        print(json.dumps(install_qt.bootstrap(assets, manifest, user.pw_uid, user.pw_gid)))
    elif phase == "joplin":
        run(["bash", str(ROOT / "install_joplin.sh"), str(assets / manifest["joplin"]["file"])])
    elif phase == "special":
        install_special(manifest, assets, user)
    elif phase == "preferences":
        run(["python3", str(ROOT / "configure_thunderbird.py")])
        for script, arguments in (("configure_editor.py", []),
                                  ("configure_desktop.py", ["--manifest", str(manifest_path)]),
                                  ("configure_updates.py", [])):
            run(["runuser", "-u", user.pw_name, "--", "python3", str(ROOT / script),
                 "--home", user.pw_dir, *arguments])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, default=ROOT / "manifest.json")
    parser.add_argument("--user", required=True, help="Guest desktop account")
    parser.add_argument("--phase", choices=PHASES, action="append")
    parser.add_argument("--check", action="store_true", help="Check platform and assets without installing")
    args = parser.parse_args()
    phases = args.phase or PHASES
    manifest = json.loads(args.manifest.read_text())
    assets = args.assets.resolve()
    user = pwd.getpwnam(args.user)
    if user.pw_uid == 0:
        parser.error("Select a non-root desktop account")
    preflight(manifest, assets, phases)
    if args.check:
        print(json.dumps({"phases": phases, "assets": required_assets(manifest, phases)}))
        return
    if os.geteuid() != 0:
        parser.error("Run installation as root inside the guest")
    configure_apt.configure()
    for phase in phases:
        print("Installing phase: " + phase, flush=True)
        install_phase(phase, manifest, args.manifest.resolve(), assets, user)


if __name__ == "__main__":
    main()
