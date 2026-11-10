# Hybrid Computer Use

Channels: the shell (commands, scripts, format-aware libraries), the running
browser's automation endpoint when it exposes one, and the `AX` and `GUI`
tools. The tool descriptions give call shapes. This guide says which channel
fits which state, and how to combine them.

## Route By Where The State Lives

| State to change | Change it through | Reason |
| --- | --- | --- |
| Files that no running application holds open: data, code, configuration, structured documents, archives, media | Shell with a format-aware library or CLI; check the real format first | Exact, scriptable, and verifiable by parsing |
| A document an application holds open | That application: AX for menus, dialogs, and fields; GUI for what AX lacks. For a property neither reaches, save and close it, then edit the file as above | Its copy is what gets saved |
| Application or system settings | The settings UI through AX; the configuration file or CLI only when the application is closed or rereads it | A running application can write its cached settings back |
| Web pages, forms, signed-in sites | The open browser session: its endpoint if exposed, otherwise AX or GUI | Login, cookies, and tabs exist only in that session |
| Canvases, images, drawings, layout | The editor: AX for menus and dialogs, GUI for pixels and dragging | Visual objects have no names to address |
| Messages, mail, calendar items, accounts | The client application through AX | The client owns sending and sync |

- A documented automation endpoint that a running application already exposes,
  such as a browser debugging port, acts inside that application. Probe for it
  once, early. After one failed probe, use AX or GUI. Do not rebuild a site
  with shell HTTP requests, and do not start another browser or profile:
  neither shares the user's session, so their changes do not appear there.
- When a setting, property, or command name is known, change it directly.
  Otherwise learn it from the application: its menus and dialogs, or one
  narrow query of its help or manual. Do not dump whole help texts or scan
  binaries and system directories. Search the web only for facts the machine
  cannot supply; web pages often describe another version.

## Work Efficiently

- `apps` may report an incomplete inventory when an application does not
  answer. Use confirmed refs from that listing. Missing windows remain unknown;
  do not infer that they closed or that a title match is unique.
- Once the source, target, and operation are known, act. Further checks must
  answer a concrete blocker.
- Plan from one read. After `observe` or `find`, put every action you can
  already determine for that window into one `act`, while later refs and
  focus remain valid. Split when the next action needs a new observation.
- Every `act` result starts with what happened, then a diff:

```text
effect: r58 checkbox "Interface" checked=false→true
windows: opened w5 "Insert Sheet" (dialog, focused)
```

  `effect` gives each target's changed fields, `no visible change`, or
  `unknown` with the reason. `(only state flags changed in this window)` means
  no page, content, or window changed; if the control should have switched
  something, the application did not respond. `windows` names windows that opened, closed, or
  took focus, with a `wN` you can use at once. When these lines already show
  the result, do not read or wait again. Continue from the diff for other
  changes; read again only when the next decision needs more.
- One `act` can mix actions and `wait` steps; the first failed step stops
  the batch. Add a `wait` only when a later step needs a state that may
  arrive after the action returns: a slow dialog, a menu item, a value
  computed later. A checkbox that shows `checked=false→true`, or a dialog
  listed as `opened`, needs no wait.
- Wait for a known state with `window`, `query`, `ref`, or `text`. When you
  know the UI will change but not what it becomes, such as the exact title of
  the next dialog, use `{"type":"wait","settle":true}`. It waits until the UI
  changes and then stops changing, names what changed, and fails with "no
  change" only when nothing changed. Keys after it go to the window that took
  focus. Give a slow operation a longer `timeout_ms`.

```json
{"mode":"act","actions":[{"type":"click","ref":"r12"},{"type":"wait","window":"Export"},{"type":"type","text":"q3.csv"},{"type":"keypress","keys":["ENTER"]},{"type":"wait","window":"Export","gone":true}]}
{"mode":"act","actions":[{"type":"click","ref":"r7"},{"type":"wait","query":{"role":"menu item","name_contains":"Paragraph"}},{"type":"click","ref":"r9"}]}
```

- Select text or cells with a `select` step before a command that applies to
  the selection: `{"type":"select","ref":"r4","to_ref":"r5"}` selects from
  paragraph `r4` through paragraph `r5`, or the cell range between two cells;
  `"text"` selects one substring of `ref`. The result reports what is now
  selected; check it before applying the command.
- Take a GUI screenshot when the question is visual or AX lacks a visible
  element. Do not alternate screenshots and AX reads of an unchanged state.
  An AX `observe:"full"` image is the same full-desktop GUI frame; plan GUI
  coordinates from it without another screenshot. A GUI `zoom`
  enlarges a frame region to read small text or icons; click with the frame
  pixels its mapping gives.
- Put GUI actions determined from the same screenshot into one `actions`
  array when focus and target positions remain valid. A call returns only a
  final screenshot. End the batch when a window or dialog opens, closes,
  moves, resizes, switches, or navigates; use the returned screenshot to plan
  subsequent coordinates. A screenshot action must stand alone.
- On Linux, start a GUI application from the shell with
  `setsid -f APP ARGS >/tmp/APP.log 2>&1 </dev/null`. Some agent shells kill
  the process group of each command when it returns; an application started
  there with `&`, `nohup`, or `disown` dies without showing a window.
- On macOS, use `open -a APP` to launch through LaunchServices. Reuse an
  existing application instance and verify its window before sending input.

## When Something Fails

Every AX and GUI error starts with a `code`, a recovery `hint`,
and whether input was sent. Read `failure_code`, `input_emitted`,
`completed`, and `retry_same_request` when present. If input was sent or
`completed` is nonzero, inspect state and send only what remains.
Never repeat the request when `retry_same_request=false`. Next step by code:

- `invalid_argument`: nothing was sent. Fix the field named in `path` as the
  hint shows. The control is not missing.
- `stale_ref`: drop every ref from that result. Use refs from a newer result,
  or one fresh `find`.
- `window_changed`: the batch stopped after `completed` actions. Read the
  returned state and send only what remains.
- `wait_timeout`: the batch stopped at that `wait`; the steps before it ran
  and none after it. The message says what was seen instead, or that the
  state could not be read. Read the returned state and revise the plan; do
  not resend the same batch.
- `effect_unknown`: input may have been sent. Inspect state before any retry,
  so that nothing is applied twice.
- `ambiguous_window`, `window_not_found`: choose a `wN` from the listed windows.
- `no_focused_window`: start the batch with `activate`.
- `app_unavailable`: if the application is still starting, wait once for
  its window with `{"mode":"act","actions":[{"type":"wait","window":"..."}]}`;
  otherwise use GUI for it.
- `permission_denied`: restore the provider's operating-system permissions
  and restart it before observing again. Repeating a screenshot cannot grant
  access. If earlier input was sent, inspect state before sending more.
- `runtime_error`: use the specific failure reason. For a temporary read
  failure, retry the read once unless the result forbids retrying. Inspect
  current state before retrying input. For `coordinate_target_unverified`,
  take a GUI screenshot and inspect covering windows and the target;
  refreshing refs alone does not fix screen-point ownership.
- A zero-result find or a timed-out wait may list nearby elements or window
  refs. Check those suggestions against the intended target before retrying.
- `gui_frame_stale`: the window under a GUI coordinate changed after its
  screenshot, so nothing was sent. Plan again from the returned screenshot.

A stop response ends desktop automation: report the uncertainty
and stop.
After two attempts on the same hypothesis without new evidence, change the
mechanism or the channel. Do not nudge coordinates, add Enter, add waits, or
relaunch applications to escape uncertainty.
