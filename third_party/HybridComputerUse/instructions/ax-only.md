# AX-Only Computer Use

The only Computer Use MCP tool available is AX. Shell, file editing, browser
automation, and documented application APIs remain available when exposed by
the agent. Do not request the full-desktop GUI tool or treat it as a recovery
option. Follow the current tool schema for argument shapes and actions.

## Choose A Channel

- Use structured file edits, CLI tools, or APIs for durable data. Inspect actual
  format and metadata before choosing a parser. Use an available browser runtime
  for web content and browser state.
- Discover application APIs from live process, configuration, help, or protocol
  information. Use one bounded probe and inspect relevant documented commands.
  Do not guess endpoints, bypass authentication, or expose remote access.
  Local-only automation must remain scoped to the user/session and preserve
  other instances' unsaved state.
- Use AX for named controls, semantic identity, values, states, and keyboard
  interaction. AX cannot prove pixels, hidden DOM, network activity, or durable
  file contents. Missing coverage does not prove an application or result absent.
- Once source, destination, and operation are known, act. Extra diagnostics must
  answer a concrete blocker; keep using a channel that provides reliable evidence.

## Observe And Act

- An incomplete `apps` inventory still provides confirmed refs. Missing
  windows remain unknown; they do not prove closure or a unique title match.
- List windows with `apps`, then observe or find in one window. Use content
  scope for one identifiable page or document root, and window scope for
  chrome, menus, dialogs, and settings. `app` accepts a name or PID;
  after the first read, pass the returned `wN` as `window_ref` to observe and
  find. `act` takes its window from the `rN` refs and needs no `window_ref`.
- Find a known role, name, text, value, or state directly. Find searches canonical
  records even when the compact tree omits containers. Partial coverage does not
  invalidate returned targets, and projection summaries are not action targets.
  Prefer focused or rooted observations to repeated full-window trees.
- Actions take current `rN` element refs or `wN` window refs, copied exactly.
  `activate` and `close` also take a window title substring. `keypress` and
  `type` without `ref` go to the window that has focus.
- Batch known actions within one window while later refs and focus should
  remain valid. End the batch at an action that can open, close, replace, or
  navigate a window; `close` is last.
- Semantic click may activate a selectable item, not merely select it. When
  a later action depends on a state that may arrive late, put a `wait` step
  between them. A `wait` cannot undo an action already sent.
- Every `act` result starts with an `effect` line per targeted element (the
  fields that changed, `no visible change`, or `unknown` with the reason) and a
  `windows` line (windows opened, closed, or focused, with their `wN`).
  `(only state flags changed in this window)` means no page, content, or
  window changed; if the control should have switched something, the
  application did not respond. When
  these lines show the result, do not read or wait again. Keep the default
  post-action diff and continue from it. Use `observe:"none"` only when readback
  is unnecessary; then the report says `unknown`. An `observe:"full"` image shows the
  full desktop; it is visual evidence, not a control target.
- A `wait` step holds until its condition is true, or fails the batch with
  `wait_timeout` and the current state. Forms: `window` (a window title on
  the whole desktop), `query` (an element in any window of the application,
  same fields as `find`), `ref` with `value`, `state`, or `text`, and `text`
  alone; `gone:true` waits for absence. An `act` with only `wait` steps
  sends no input. After a `window` wait, keys and text without `ref` go to
  that window. Prefer waiting for the next state to appear over waiting for
  an old node to vanish, and skip a wait the report already answers.
- Use `{"type":"wait","settle":true}` after an action when you know the UI
  will change but not what it becomes, such as the exact title of the next
  dialog. It waits until the UI changes and then stops changing, names what
  changed, and fails with "no change" only when nothing changed. Keys after it
  go to the window that took focus. Keep `window`, `query`, `ref`, and `text`
  waits for a state you know. Give a slow operation a longer `timeout_ms`.

## Recovery And Verification

- An explicit stop response overrides refresh or retry guidance. Stop desktop
  automation and report uncertainty; zero requested input does not prove an
  earlier OS focus request cannot still take effect.
- `not_executed` with `input_events_emitted=false` means the action caused no
  later UI change. Input emission alone does not prove its intended effect.
- Reuse current evidence and refs returned in a response. Errors return a
  `code` and a `hint` with a corrected call. On `stale_ref`, discard targets
  from the failed call and use fresh refs or one new find. Do not replay or
  reopen an obsolete dialog.
- An unchanged tree can coexist with changed formatting or file state. Inspect
  the appropriate artifact or runtime evidence instead of repeating the action.
  For a transient window absent from AX, try a suitable non-AX channel rather
  than repeatedly querying the tree.
- After two attempts with the same hypothesis and no new evidence, make one
  targeted read and change mechanism. Do not launch replacement instances
  just to escape ambiguous ownership, focus, or persistence.
- Combine related read-only shell checks. Prefer structured parsers or APIs
  over bulk clipboard transfer. If pasting is necessary, keep the clipboard
  owner alive; after one focus-verified paste failure, change mechanism.
- A `wait` proves only AX-visible state. Verify durable files and
  effective settings through their own state; use visual evidence when the
  outcome requires it and that evidence is available. The report, a `wait`,
  or the final observation can finish verification without another call when it directly
  covers the requirement. Report a hard blocker only after available alternative
  channels cannot meet the original instruction.
