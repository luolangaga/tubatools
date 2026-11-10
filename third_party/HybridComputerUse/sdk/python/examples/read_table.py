"""Read every cell of the visible sheet and report the negative numbers.

One script, many core calls, one line of output for the model. Usage:
    python3 read_table.py WINDOW_TITLE_SUBSTRING
"""
import json
import sys

import hcu_sdk as hcu

d = hcu.connect()
window = next(w for w in d.apps().windows if sys.argv[1].lower() in w.title.lower())

cells = list(d.find_all(window.ref, page_size=50, role="cell"))
values = {}
for cell in cells:
    if cell.name:
        values[cell.name] = cell.value or ""

negative = []
for name, value in values.items():
    try:
        if float(value) < 0:
            negative.append((name, value))
    except ValueError:
        pass
print(json.dumps({"window": window.ref, "cells": len(cells), "negative": negative[:20]}))
