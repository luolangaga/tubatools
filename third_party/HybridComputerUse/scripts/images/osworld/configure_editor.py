"""Enable application accessibility without creating diagnostic documents."""

import argparse
import json
from pathlib import Path


def configure(home):
    settings = home / ".config/Code/User/settings.json"
    settings.parent.mkdir(parents=True, exist_ok=True)
    # Refuse unparseable settings rather than discarding an existing profile.
    data = json.loads(settings.read_text()) if settings.exists() else {}
    if not isinstance(data, dict):
        raise ValueError("VS Code user settings must be a JSON object")
    data["editor.accessibilitySupport"] = "on"
    settings.write_text(json.dumps(data, indent=4) + "\n")
    print(json.dumps({"settings": str(settings), "editor.accessibilitySupport": "on",
                      "requires_fresh_application_launch": True}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--home", type=Path, default=Path.home())
    configure(parser.parse_args().home)
