#!/usr/bin/env python3
"""Install the official LibreOffice candidate beside the distribution version.

Run this operator script as root inside the guest. The desktop user must exist.
The default leaves both unversioned commands and the old user profile intact.
Use --switch-default only when the candidate is selected for the image.
"""

import argparse
import json
import os
from pathlib import Path
import pwd
import re
import shutil
import subprocess
import tarfile
import tempfile
import xml.etree.ElementTree as ET


OOR = "http://openoffice.org/2001/registry"
XS = "http://www.w3.org/2001/XMLSchema"
XSI = "http://www.w3.org/2001/XMLSchema-instance"


def command(args, timeout=60):
    result = subprocess.run(args, capture_output=True, text=True, timeout=timeout)
    if result.returncode:
        raise RuntimeError(f"{args[0]} failed: {result.stdout}\n{result.stderr}")
    return result.stdout


def require_stopped(prefix):
    for proc in Path("/proc").iterdir():
        if not proc.name.isdecimal():
            continue
        try:
            executable = (proc / "exe").resolve(strict=True)
        except (FileNotFoundError, PermissionError, ProcessLookupError):
            continue
        if executable == prefix / "program/soffice.bin":
            raise RuntimeError("Close the candidate's desktop windows before installation")


def initialize_navigator(root):
    """Seed the complete docking record only when no window preference exists."""
    windows_path = "/org.openoffice.Office.Views/Windows"
    items = [item for item in root if item.get(f"{{{OOR}}}path") == windows_path]
    if any(node.tag == "node" and node.get(f"{{{OOR}}}name") == "simpress/10366"
           for item in items for node in item):
        return
    item = items[0] if items else ET.SubElement(root, "item", {f"{{{OOR}}}path": windows_path})
    node = ET.SubElement(item, "node", {f"{{{OOR}}}name": "simpress/10366", f"{{{OOR}}}op": "replace"})
    visible = ET.SubElement(node, "prop", {f"{{{OOR}}}name": "Visible", f"{{{OOR}}}op": "fuse"})
    ET.SubElement(visible, "value", {f"{{{XSI}}}nil": "true"})
    user_data = ET.SubElement(node, "node", {f"{{{OOR}}}name": "UserData"})
    data = ET.SubElement(user_data, "prop", {
        f"{{{OOR}}}name": "Data", f"{{{OOR}}}op": "fuse", f"{{{OOR}}}type": "xs:string"})
    ET.SubElement(data, "value").text = "V2,H,128,AL:(16,4,0/0/270/240,270;240)"
    state = ET.SubElement(node, "prop", {f"{{{OOR}}}name": "WindowState", f"{{{OOR}}}op": "fuse"})
    ET.SubElement(state, "value").text = "80,109,301,401;1;,,,;"


def prepare_preferences(path, uid, gid):
    """Set ordinary first-start preferences without copying diagnostic documents."""
    ET.register_namespace("oor", OOR)
    ET.register_namespace("xsi", XSI)
    root = ET.parse(path).getroot() if path.exists() else ET.Element(f"{{{OOR}}}items")
    initialize_navigator(root)
    # ElementTree cannot infer namespaces used only in attribute values.
    root.set("xmlns:xs", XS)
    settings = [
        # FirstRun controls initialization jobs. WhatsNew controls the welcome UI.
        ("/org.openoffice.Setup/Product", "WhatsNew"),
        ("/org.openoffice.Office.Common/Misc", "ShowTipOfTheDay"),
        ("/org.openoffice.Office.Impress/Misc/NewDoc", "AutoPilot"),
    ]
    for section, name in settings:
        for item in list(root):
            if item.get(f"{{{OOR}}}path") != section:
                continue
            for prop in list(item):
                if prop.tag == "prop" and prop.get(f"{{{OOR}}}name") == name:
                    item.remove(prop)
            if not len(item):
                root.remove(item)
        item = ET.SubElement(root, "item", {f"{{{OOR}}}path": section})
        prop = ET.SubElement(item, "prop", {
            f"{{{OOR}}}name": name, f"{{{OOR}}}op": "fuse",
        })
        ET.SubElement(prop, "value").text = "false"
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".xcu.tmp")
    ET.ElementTree(root).write(temporary, encoding="utf-8", xml_declaration=True)
    os.chown(temporary, uid, gid)
    os.chmod(temporary, 0o600)
    temporary.replace(path)
    for directory in [path.parent, path.parent.parent]:
        os.chown(directory, uid, gid)


def extract_packages(archive_path, directory, manifest):
    packages = {}
    expected = set(manifest["packages"])
    with tarfile.open(archive_path) as archive:
        for member in archive:
            if not member.isfile() or not member.name.endswith(".deb"):
                continue
            if "debian-menus" in Path(member.name).name:
                continue
            destination = directory / Path(member.name).name
            if destination.exists():
                raise ValueError("Duplicate package filename in archive")
            with archive.extractfile(member) as source, destination.open("wb") as target:
                shutil.copyfileobj(source, target)
            package = command(["dpkg-deb", "-f", str(destination), "Package"]).strip()
            version = command(["dpkg-deb", "-f", str(destination), "Version"]).strip()
            architecture = command(["dpkg-deb", "-f", str(destination), "Architecture"]).strip()
            if package not in expected or package in packages:
                raise ValueError(f"Unexpected or duplicate package: {package}")
            if version != manifest["package_version"] or architecture != manifest["architecture"]:
                raise ValueError(f"Unexpected package metadata: {package} {version} {architecture}")
            packages[package] = destination
    if set(packages) != expected:
        raise ValueError("Missing candidate packages: " + ", ".join(sorted(expected - packages.keys())))
    return sorted(packages.values())


def install(args):
    manifest = json.loads(args.manifest.read_text())
    prefix = Path(manifest["prefix"])
    profile_name = args.profile_name or manifest["profile_name"]
    if os.geteuid() != 0:
        raise RuntimeError("Run this script as root inside the guest")
    user = pwd.getpwnam(args.desktop_user)
    if user.pw_uid == 0:
        raise ValueError("The desktop user must not be root")
    if not re.fullmatch(re.escape(manifest["profile_prefix"]) + r"(?:-[A-Za-z0-9_-]+)?", profile_name):
        raise ValueError("Use an isolated profile name beginning with " + manifest["profile_prefix"])
    if args.archive.name != manifest["file"]:
        raise ValueError("Archive filename differs from the selected manifest")
    require_stopped(prefix)
    audit = command(["dpkg", "--audit"])
    if audit.strip():
        raise RuntimeError("Resolve existing package errors before installation: " + audit)
    executable = prefix / "program/soffice"
    defaults = {name: str(Path("/usr/bin", name).resolve())
                for name in ("libreoffice", "soffice")}
    active_commands = {name: shutil.which(name) for name in defaults}
    links = [Path("/usr/local/bin", name) for name in defaults]
    if args.switch_default:
        for link in links:
            if os.path.lexists(link) and not (link.is_symlink() and link.resolve() == executable):
                raise RuntimeError(f"Preserve the existing command before switching: {link}")

    with tempfile.TemporaryDirectory(prefix="libreoffice-candidate-") as directory:
        packages = extract_packages(args.archive, Path(directory), manifest)
        command(["dpkg", "-i", *map(str, packages)], timeout=240)

    bootstrap = prefix / "program/bootstraprc"
    lines = bootstrap.read_text().splitlines()
    if sum(line.startswith("UserInstallation=") for line in lines) != 1:
        raise RuntimeError("Candidate bootstrap must contain exactly one user profile setting")
    lines = [f"UserInstallation=$SYSUSERCONFIG/libreoffice/{profile_name}"
             if line.startswith("UserInstallation=") else line for line in lines]
    bootstrap.write_text("\n".join(lines) + "\n")
    profile = Path(user.pw_dir, ".config/libreoffice", profile_name)
    prepare_preferences(profile / "user/registrymodifications.xcu", user.pw_uid, user.pw_gid)
    if args.switch_default:
        for link in links:
            if not os.path.lexists(link):
                link.parent.mkdir(parents=True, exist_ok=True)
                link.symlink_to(executable)
    for name, previous in defaults.items():
        if str(Path("/usr/bin", name).resolve()) != previous:
            raise RuntimeError(f"The distribution's {name} command changed unexpectedly")
        if not args.switch_default and shutil.which(name) != active_commands[name]:
            raise RuntimeError(f"The default {name} command changed unexpectedly")
    return {
        "release": manifest["release"],
        "package_version": manifest["package_version"],
        "executable": str(executable),
        "profile": str(profile),
        "desktop_user": args.desktop_user,
        "default_switched": args.switch_default,
        "distribution_commands": defaults,
        "active_commands": {name: shutil.which(name) for name in defaults},
        "first_start_dialogs": "disabled in the candidate profile",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", required=True, type=Path,
                        help="Official LibreOffice deb tar.gz selected by the manifest")
    parser.add_argument("--manifest", type=Path,
                        default=Path(__file__).with_name("libreoffice.json"))
    parser.add_argument("--desktop-user", required=True)
    parser.add_argument("--profile-name", help="Override the manifest's isolated profile name")
    parser.add_argument("--switch-default", action="store_true",
                        help="Select the candidate through /usr/local/bin/libreoffice and soffice")
    args = parser.parse_args()
    print(json.dumps(install(args), indent=2))


if __name__ == "__main__":
    main()
