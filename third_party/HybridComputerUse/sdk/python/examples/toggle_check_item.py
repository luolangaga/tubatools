"""Open a menu, make sure a check item is off, and confirm it by reading it.

    python3 toggle_check_item.py WINDOW_TITLE MENU ITEM

Every click has a wait step for the next menu level, so the script never
acts on a menu that has not opened. A ref that went stale is never
retried blindly: find runs again.
"""
import json
import sys

import hcu_sdk as hcu

title, menu, item = sys.argv[1:4]
d = hcu.connect()
window = next(w for w in d.apps().windows if title.lower() in w.title.lower())


def open_item():
    top = d.find(window.ref, role="menu", name=menu).matches[0]
    d.click(top.ref, wait={"query": {"name_equals": item}})
    return d.find(window.ref, name=item).matches[0]


entry = open_item()
was_checked = (entry.states or {}).get("checked") == "true"
if was_checked:
    d.click(entry.ref)
else:
    d.key("escape", window_ref=window.ref)

entry = open_item()
now = d.read([entry.ref]).read[0].element
d.key("escape", window_ref=window.ref)
print(json.dumps({"item": item, "was_checked": was_checked,
                  "checked_now": (now.states or {}).get("checked") == "true"}))
