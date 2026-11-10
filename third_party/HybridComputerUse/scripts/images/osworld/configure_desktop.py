"""Set desktop preferences without creating documents or task state."""
from pathlib import Path
import configparser
import argparse
import json
import os


SETTINGS = {
    ".config/Kingsoft/Office.conf": {
        "6.0": {
            "common\\do_not_detect_file_association_while_startup": "true",
        },
    },
    ".config/MuseScore/MuseScore4.ini": {
        "application": {
            "welcomeDialogShowOnStartup": "false",
        },
    },
    ".config/slicer.org/Slicer.ini": {
        "MainWindow": {"DontShowDisclaimerMessage": "1024"},
        "ApplicationUpdate": {"AutoUpdateCheck": "false"},
    },
}


def configure(home, manifest):
    settings = {**SETTINGS,
        ".config/REAPER/reaper.ini": {
            "reaper": {"linux_audio_mode": "3", "verchk": "0"}},
    }
    for relative, sections in settings.items():
        path = home / relative
        parser = configparser.ConfigParser(interpolation=None, strict=False)
        parser.optionxform = str
        if path.exists():
            parser.read(path)
        for section, values in sections.items():
            if not parser.has_section(section):
                parser.add_section(section)
            for key, value in values.items():
                parser.set(section, key, value)
        if relative.endswith("MuseScore4.ini"):
            parser.set("application", "skippedVersion", manifest["musescore_skipped_version"])
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("w") as stream:
            parser.write(stream, space_around_delimiters=False)
        print(path)


if __name__ == "__main__":
    if os.getuid() == 0:
        raise SystemExit("Run as the guest desktop user")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--home", type=Path, default=Path.home())
    parser.add_argument("--manifest", type=Path, required=True)
    args = parser.parse_args()
    configure(args.home, json.loads(args.manifest.read_text()))
