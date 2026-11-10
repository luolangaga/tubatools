"""Install desktop applications and repair Qt startup compatibility."""

from pathlib import Path
import argparse
import json
import os
import platform
import subprocess
import tarfile
import zipfile


def configure_freecad_resources(destination, resource_path=Path("/usr/share/freecad")):
    resources = destination / "usr/share/freecad"
    if not (resources / "Mod/Start/StartPage/StartPage.html").is_file():
        raise RuntimeError("FreeCAD package resources are incomplete")
    # The Ubuntu binary compiles this absolute resource path into getResourceDir().
    # Keep native libraries private while restoring the package's resource layout.
    if resource_path.is_symlink() and resource_path.resolve() == resources.resolve():
        return
    if resource_path.exists() or resource_path.is_symlink():
        raise RuntimeError("FreeCAD resource path is already occupied: " + str(resource_path))
    resource_path.symlink_to(resources, target_is_directory=True)


def install_freecad(bundle_archive, expected_manifest):
    bundle = bundle_archive.parent / "freecad-expanded"
    bundle.mkdir(exist_ok=True)
    with tarfile.open(bundle_archive) as archive:
        archive.extractall(bundle)
    manifest = json.loads((bundle / "manifest.json").read_text())
    if manifest != expected_manifest:
        raise RuntimeError("FreeCAD bundle metadata differs from the selected package manifest")
    if platform.freedesktop_os_release().get("VERSION_ID") != "22.04":
        raise RuntimeError("FreeCAD bundle requires Ubuntu 22.04")
    missing = []
    for package in manifest["dependencies"]:
        status = subprocess.run(["dpkg-query", "-W", "-f=${db:Status-Status}", package],
                                capture_output=True, text=True)
        if status.returncode or status.stdout != "installed":
            missing.append(package)
    if missing:
        subprocess.run(["apt-get", "install", "-y", "--no-install-recommends", *missing],
                       env={**os.environ,"DEBIAN_FRONTEND":"noninteractive"}, check=True)
    destination = Path("/opt/hcu-apps/freecad-0.19")
    destination.mkdir(parents=True, exist_ok=True)
    for item in manifest["packages"]:
        package = bundle / item["file"]
        if package.parent != bundle or package.suffix != ".deb":
            raise RuntimeError("Invalid private FreeCAD package path")
        for field in ["Package", "Version", "Architecture"]:
            actual = subprocess.check_output(["dpkg-deb", "-f", str(package), field], text=True).strip()
            if actual != item[field]:
                raise RuntimeError("FreeCAD package metadata mismatch")
        subprocess.run(["dpkg-deb", "-x", str(package), str(destination)], check=True)
    liblink = destination / "usr/lib/freecad/lib"
    if not liblink.exists():
        liblink.symlink_to("../freecad-python3/lib")
    configure_freecad_resources(destination)
    launcher = Path("/usr/local/bin/freecad")
    launcher.write_text("#!/bin/sh\n"
        "export QT_LINUX_ACCESSIBILITY_ALWAYS_ON=1\n"
        "export LD_LIBRARY_PATH=/opt/hcu-apps/freecad-0.19/usr/lib/x86_64-linux-gnu:/opt/hcu-apps/freecad-0.19/usr/lib/freecad-python3/lib\n"
        'exec /opt/hcu-apps/freecad-0.19/usr/lib/freecad/bin/freecad-python3 "$@"\n')
    launcher.chmod(0o755)
    desktop = Path("/usr/local/share/applications/freecad.desktop")
    desktop.parent.mkdir(parents=True, exist_ok=True)
    desktop.write_text("[Desktop Entry]\nType=Application\nName=FreeCAD\nExec=/usr/local/bin/freecad %F\nTerminal=false\nCategories=Graphics;Engineering;\n")
    return {"version":manifest["freecad_version"], "occt":manifest["occt_version"],
            "source":"Ubuntu packages with private OCCT libraries", "private_prefix":str(destination)}


def install_kicad(bundle, expected_version):
    manifest = json.loads((bundle / "manifest.json").read_text())
    release = platform.freedesktop_os_release()
    arch = subprocess.check_output(["dpkg", "--print-architecture"], text=True).strip()
    if release["ID"] != "ubuntu" or release["VERSION_ID"] != manifest["ubuntu_version"] or arch != manifest["architecture"]:
        raise RuntimeError("KiCad offline bundle OS/architecture mismatch")
    packages = []
    for item in manifest["packages"]:
        package = (bundle / item["file"]).resolve()
        if package.parent != (bundle / "debs").resolve() or package.suffix != ".deb":
            raise RuntimeError("Invalid package path")
        for field in ["Package", "Version", "Architecture"]:
            actual = subprocess.check_output(["dpkg-deb", "-f", str(package), field], text=True).strip()
            if actual != item[field]:
                raise RuntimeError("Package metadata mismatch: " + str(package))
        packages.append(str(package))
    subprocess.run(["dpkg", "--install", *packages], check=True,
                   env={**os.environ, "DEBIAN_FRONTEND":"noninteractive"})
    version = subprocess.check_output(["kicad-cli", "version"], text=True).strip()
    if version != expected_version:
        raise RuntimeError("Unexpected KiCad version: " + version)
    if subprocess.check_output(["dpkg", "--audit"], text=True).strip():
        raise RuntimeError("dpkg reports incomplete packages")
    return {"kicad_version":version,"packages":len(packages)}


def bootstrap(assets, manifest, uid, gid):
    if os.geteuid() != 0:
        raise RuntimeError("Run the image bootstrap as root")
    subprocess.run(["apt-get", "install", "-y", "--no-install-recommends", *manifest["qt_packages"],
                    "libatk-wrapper-java", "libatk-wrapper-java-jni"],
        check=True, env={**os.environ,"DEBIAN_FRONTEND":"noninteractive"})
    result = {"spyder":repair_spyder_integer_pixmap_dimensions(),
              "freecad":install_freecad(assets / manifest["freecad"]["file"],
                  json.loads(Path(__file__).with_name(manifest["freecad"]["package_manifest"]).read_text()))}
    geogebra = Path("/opt/hcu-apps")
    geogebra.mkdir(exist_ok=True)
    with tarfile.open(assets / manifest["geogebra"]["file"]) as archive:
        archive.extractall(geogebra)
    portable = geogebra / manifest["geogebra"]["directory"]
    java = portable / manifest["geogebra"]["java_directory"]
    for path in [portable / "geogebra-portable", portable / "geogebra/geogebra",
                 java / "bin/java", java / "lib/jspawnhelper"]:
        path.chmod(path.stat().st_mode | 0o111)
    # The official portable launcher stores settings beside its executable.
    subprocess.run(["chown", "-R", f"{uid}:{gid}", str(portable)], check=True)
    launcher = Path("/usr/local/bin/geogebra")
    launcher.write_text("#!/bin/sh\nexec " + str(portable / "geogebra-portable") + ' --versionCheckAllow=false "$@"\n')
    launcher.chmod(0o755)
    result["geogebra_portable"] = portable.name
    kicad = assets / "kicad-expanded"
    kicad.mkdir(exist_ok=True)
    with zipfile.ZipFile(assets / manifest["kicad"]["file"]) as archive:
        archive.extractall(kicad)
    result["kicad"] = install_kicad(kicad, manifest["kicad"]["version"])
    return result


def repair_spyder_file(path):
    if not path.exists():
        return {"spyder": "not installed"}
    text = path.read_text()
    replacements = {
        ".scaled(image_width, image_height,": ".scaled(int(image_width), int(image_height),",
        "QSize(image_width, image_height)": "QSize(int(image_width), int(image_height))",
    }
    updated = text
    changes = 0
    for before, after in replacements.items():
        changes += updated.count(before)
        updated = updated.replace(before, after)
    if not changes:
        if any(after in text for after in replacements.values()):
            return {"spyder": "already repaired"}
        raise RuntimeError("Unrecognized Spyder pixmap sizing source; inspect before changing")
    backup = path.with_suffix(".py.hcu-infra-original")
    if not backup.exists():
        backup.write_text(text)
    path.write_text(updated)
    return {"spyder": "integer pixmap sizes", "calls_changed": changes}


def repair_spyder_integer_pixmap_dimensions():
    root = Path("/usr/lib/python3/dist-packages/spyder")
    return {str(path): repair_spyder_file(root / path) for path in (
        "plugins/completion/kite/widgets/install.py", "app/tour.py")}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, default=Path(__file__).with_name("manifest.json"))
    parser.add_argument("--uid", type=int, required=True)
    parser.add_argument("--gid", type=int, required=True)
    args = parser.parse_args()
    print(json.dumps(bootstrap(args.assets, json.loads(args.manifest.read_text()), args.uid, args.gid)))
