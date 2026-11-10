# Hybrid Computer Use From Code

Add-on for agents that drive the desktop from code. It extends `hybrid.md`;
channel routing, batching and error recovery are the same. The calls are the
`AX` and `GUI` calls, and each call returns the same result in every entry
point.

## Pick One Entry Point

- If your code can call `tools.mcp__computer_use__AX` and
  `tools.mcp__computer_use__GUI`, use them. They can be callable even when a
  tool description omits them. Check in the code of your first desktop call,
  not in a call of its own: `tools.mcp__computer_use__AX` is `undefined` when
  the tools are absent.
- Otherwise run the `computer-use` command line in the shell, or Python with
  `import hcu_sdk as hcu`.
- Use one entry point for the whole task. An MCP server refuses input while
  a command-line or SDK daemon serves the same desktop.
- Do not print help, the schema or source code before the first call. The
  calls below cover normal work. Read `computer-use help cli` only after a
  command fails on its syntax.

## Known Steps In One Code Call

- Put every step you already know into one piece of code: the batch, the
  check of its result, and the image you need next. Return only what the
  next decision needs.
- Read the first result line or field: `ok`, `code`, `hint`, `completed`.
  Stop the code when a step fails, and print that result.
- After an input step, use its diff (`effect`, `windows`). Do not request
  `tree` or `observe` again for a window that the diff already describes.
- Open an image in the same code that took it. You see an image only when
  the code returns it; you do not see a file path's image.

```js
// AX and GUI tools inside code: the first text item is the JSON evidence line.
const r = await tools.mcp__computer_use__AX({mode: "act", actions: [
  {type: "click", ref: "r12"}, {type: "wait", window: "Export"},
  {type: "type", text: "q3.csv"}, {type: "keypress", keys: ["ENTER"]}]});
const lines = r.content.filter(c => c.type === "text").map(c => c.text);
text(lines.join("\n"));                       // evidence, effect, windows, diff
if (!JSON.parse(lines[0]).ok) exit();
const g = await tools.mcp__computer_use__GUI({actions: [{type: "screenshot"}]});
for (const c of g.content) if (c.type === "image") image(c);
```

```js
// Command line inside code: --records prints one JSON line; images[0] is the
// image path. --json - reads the JSON body from standard input.
const sh = async cmd => (await tools.exec_command({cmd})).output.trim();
const body = {actions: [{type: "click", ref: "r12"}, {type: "wait", window: "Export"}]};
const act = JSON.parse(await sh("computer-use act --records --as diff --json - <<'EOF'\n" + JSON.stringify(body) + "\nEOF"));
text(JSON.stringify({ok: act.ok, code: act.code, hint: act.hint, text: act.text}));
const shot = JSON.parse(await sh("computer-use gui screenshot --records"));
image((await tools.view_image({path: shot.images[0]})).image_url);
```

## Command Line

```text
computer-use apps
computer-use observe <app|wN> [--as tree|diff|full|screenshot]
computer-use find <app|wN> [--role R] [--name-has S] [--text-has S] [--limit N]
computer-use read rN [rN ...]
computer-use click|set|select|scroll rN ...   computer-use type|key [rN] TEXT|CHORD
computer-use act --json '{"actions":[...]}'|-|@FILE [--window wN] [--wait STEP_JSON]
computer-use wait [wN] STEP_JSON
computer-use gui screenshot | gui zoom X0 Y0 X1 Y1 | gui click X Y
computer-use gui --json '{"actions":[...]}'|-|@FILE
```

- Output is the tool text by default; `--json` (`--records` for `act` and
  `gui`) prints one JSON record. Input commands return the diff by default.
- Exit status: 0 success, 2 bad argument, 3 a wait step timed out, 4 stale
  ref or stale screenshot, 5 window changed or effect unknown, 6 window or
  application not found, 10 runtime error, 64 bad command line. Codes 2, 4
  and 6 sent no input.

## Python SDK

```python
import hcu_sdk as hcu
d = hcu.connect()
w = next(x.ref for x in d.apps().windows if "Report" in x.title)
for el in d.find_all(w, role="button"):        # follows next_cursor
    print(el.ref, el.name)
d.act([{"type": "click", "ref": "r12"}, {"type": "wait", "window": "Export"}], observe="diff")
z = d.zoom([600, 60, 680, 100])                # frame pixels of the current GUI frame
d.click_zoom(z, 212, 96)                       # clicks the frame pixel under zoom pixel (212, 96)
print(d.screenshot().images[0])                # open this path in the same code
```

- Helpers accept the schema's field names (`window_ref`, `name_contains`).
  Errors are exceptions named after the code, such as `hcu.StaleRef`, with
  `code`, `hint`, `input_emitted` and `completed`.
- Print the fields you need, not whole records or trees.

## Argument Shapes

Code declarations of the tools can show some fields as `unknown`. Their
shapes are:

```js
{mode: "observe", window_ref: "w2"}           // or app: "name"; reads never take window
{mode: "find", window_ref: "w2", query: {role: "button", name_contains: "Save"}}
{mode: "act", actions: [{type: "set_value", ref: "r7", value: "42"}, {type: "activate", ref: "w3"}]}
// act step types: click, type, keypress, set_value, select, scroll, activate, close, wait
// activate and close take ref wN or title; one act works in one window
// query fields: role, name_equals, name_contains, text_equals, text_contains, state
{actions: [{type: "scroll", x: 640, y: 412, scroll_y: -3}]}   // GUI; scroll_x or scroll_y, non-zero
```

## Images And Coordinates

- A record's `screen` is the current GUI frame: a GUI screenshot, or an AX
  `observe` with `full` or `screenshot`. GUI `x`/`y` are pixels of that
  image; GUI input may follow either one without another screenshot.
- To read small text or icons, zoom a frame region. Zoom only looks.
  Convert a zoom pixel with the mapping in its result, or use `click_zoom`.

## Retries And Limits

- Refs (`aN`, `wN`, `rN`) stay valid across calls of one entry point. Copy
  them from a result; never build them.
- An act that the connection dropped may have run. Resend it only with the
  same `request_id`; the answer is then the first result, with
  `replayed: true`.
- Never script blind input: after each step that should change state, add a
  `wait` step or read the state before the next step. When you do not know
  what the next state is, `{type: "wait", settle: true}` waits until the UI
  changes and stops changing; it fails with "no change" when nothing changed.
- Do not send desktop input through other shell utilities or scripted
  accessibility calls. After two attempts on one hypothesis, change the
  mechanism or the channel.
