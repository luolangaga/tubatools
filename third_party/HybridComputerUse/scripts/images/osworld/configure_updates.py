"""Keep Software update banners out of the image's desktop session."""
import argparse
import configparser
import json
import os
from pathlib import Path
import re
import subprocess


def set_false(command, schema, key, environment):
    def run(operation, *values):
        arguments = ["gsettings", operation, schema, key, *values]
        # The schema inside a confined Snap is unavailable to host gsettings.
        if command:
            arguments = command + [" ".join(arguments)]
        return subprocess.run(arguments, env=environment, check=True, text=True,
                              capture_output=True, timeout=30)

    run("set", "false")
    if run("get").stdout.strip() != "false":
        raise RuntimeError("Software update setting did not persist: " + key)


def configure(desktop_directory=Path("/var/lib/snapd/desktop/applications"), home=None):
    environment = {**os.environ, "XDG_RUNTIME_DIR": f"/run/user/{os.getuid()}",
                   "DBUS_SESSION_BUS_ADDRESS": f"unix:path=/run/user/{os.getuid()}/bus"}
    entries = sorted(desktop_directory.glob("snap-store_*.desktop"))
    if not entries:
        return {"notification_applications": [], "snap_store_present": False}

    # This Snap sends io.snapcraft.Store as its notification desktop-entry,
    # but exports only prefixed launchers. Without an alias GNOME falls back
    # to its generic policy and ignores the application's banner setting.
    alias = (home or Path.home()) / ".local/share/applications/io.snapcraft.Store.desktop"
    if not alias.exists():
        source = desktop_directory / "snap-store_ubuntu-software.desktop"
        parser = configparser.ConfigParser(interpolation=None)
        parser.optionxform = str
        parser.read(source)
        parser.set("Desktop Entry", "NoDisplay", "true")
        alias.parent.mkdir(parents=True, exist_ok=True)
        with alias.open("w") as stream:
            parser.write(stream, space_around_delimiters=False)

    applications = []
    for entry in [*entries, alias]:
        # GNOME can identify the same process through any exported launcher.
        # Its notification policy uses the desktop ID with punctuation replaced.
        application = re.sub(r"[^a-z0-9-]", "-", entry.stem.lower())
        schema = ("org.gnome.desktop.notifications.application:"
                  f"/org/gnome/desktop/notifications/application/{application}/")
        set_false([], schema, "show-banners", environment)
        applications.append(entry.name)

    prefix = ["snap", "run", "--shell", "snap-store.ubuntu-software", "-c"]
    for key in ("download-updates", "download-updates-notify"):
        set_false(prefix, "org.gnome.software", key, environment)
    return {"notification_applications": applications, "snap_store_present": True}


def main(argv=None):
    if os.getuid() == 0:
        raise SystemExit("Run as the logged-in guest desktop user")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--home", type=Path, default=Path.home())
    args = parser.parse_args(argv)
    print(json.dumps(configure(home=args.home)))


if __name__ == "__main__":
    main()
