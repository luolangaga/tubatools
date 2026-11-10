"""Save the document under a new name and confirm by the window title.

    python3 save_as.py WINDOW_TITLE NEW_NAME [SCREENSHOT_PATH]

Errors are classes named after the public codes: WaitTimeout means the
title never changed, so the script looks for a dialog that asks about the
file format and answers it, then waits again.
"""
import json
import sys

import hcu_sdk as hcu

title, new_name = sys.argv[1:3]
d = hcu.connect()
window = next(w for w in d.apps().windows if title.lower() in w.title.lower())

# File choosers are not always exposed with the dialog role, so wait for
# the dialog's title. A wait that does not hold raises WaitTimeout.
try:
    d.key("ctrl+shift+s", window_ref=window.ref, wait={"window": "Save", "timeout_ms": 10000})
except hcu.WaitTimeout:
    raise SystemExit(json.dumps({"error": "the save dialog did not open"}))
dialog = next(w for w in d.apps().windows if w.active)
# The file-name field is the text field that has focus when the dialog
# opens; a label next to it can carry the same name.
fields = d.find(dialog.ref, role="text", limit=50).matches
name_field = next(f for f in fields if (f.states or {}).get("active") == "true")
d.set_value(name_field.ref, new_name)
saved = {"window": new_name, "timeout_ms": 8000}
try:
    d.key("enter", window_ref=dialog.ref, wait=saved)
except hcu.WaitTimeout:
    # A follow-up question (for example about the file format) is open.
    question = next(w for w in d.apps().windows if w.active)
    keep = d.find(question.ref, role="button", name_has="Keep", limit=1).matches
    button = keep[0] if keep else d.find(question.ref, role="button", limit=1).matches[0]
    d.click(button.ref, wait=saved)

shot = d.screenshot() if len(sys.argv) > 3 else None
print(json.dumps({"title_has_name": True, "screenshot": shot.screen.image if shot else None}))
