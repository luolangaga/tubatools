"""Disable Thunderbird's unsolicited startup pages without changing mail profiles."""

import argparse
import json
from pathlib import Path


PREFERENCES = '''// Computer Use desktop image: suppress unsolicited startup pages.
// The donation check compares the viewed campaign with this campaign number.
pref("app.donation.eoy.version", 0);
// Disabling data submission also prevents its first-run privacy page.
pref("datareporting.policy.dataSubmissionEnabled", false);
// Leave the message pane empty until a message is selected.
pref("mailnews.start_page.enabled", false);
'''


def configure(application):
    directory = application / "defaults/pref"
    if not (application / "omni.ja").is_file() or not directory.is_dir():
        raise ValueError("Expected an unpacked Thunderbird application with defaults/pref")
    path = directory / "hcu-startup.js"
    if path.is_symlink():
        raise ValueError("Refusing to follow a startup preferences symlink: " + str(path))
    if path.exists() and path.read_text() != PREFERENCES:
        raise ValueError("Refusing to overwrite different startup preferences: " + str(path))
    path.write_text(PREFERENCES)
    path.chmod(0o644)
    return {"preferences": str(path), "mail_profiles_changed": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--application", type=Path, default=Path("/usr/lib/thunderbird"))
    args = parser.parse_args()
    print(json.dumps(configure(args.application)))


if __name__ == "__main__":
    main()
