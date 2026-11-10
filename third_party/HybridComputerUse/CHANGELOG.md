# Changelog

## 1.0.0

First open-source release of Hybrid Computer Use.

### Desktop MCP Server, CLI, and SDK

- `computer-use` exposes two public MCP tools over stdio: `AX` (`apps`,
  `observe`, `find`, `act`) and `GUI` (`screenshot`, `zoom`, and batched
  pointer/keyboard input).
- `AX` binds short session references (`aN -> wN -> rN`), projects compact
  working trees while keeping all canonical elements searchable through `find`
  (with `next_cursor` paging), executes up to 64 ordered steps with in-call
  `wait` (including `settle: true`) and `select` steps, and returns
  per-target `effect:` lines, `windows:` lines, and compact AX diffs.
- `GUI` scales desktop screenshots to fit `1366x768`, supports `zoom` crops on
  the current frame, executes up to 64 ordered input actions, and checks live
  window layout at target points before each coordinate action
  (`gui_frame_stale`).
- Every `AX` and `GUI` response begins with a single-line JSON status record
  carrying one of 12 public error codes, `input_emitted`, `completed`, and a
  corrected-call `hint`.
- A per-desktop daemon (`computer-use serve`) shares live desktop state and
  reference continuity across the CLI, the Python SDK (`hcu_sdk`), attached
  MCP sessions (`--attach`), and the programmatic tool calling (PTC) adapter.

### Native Platform Backends

- **Linux** (primary): embedded Python provider using AT-SPI2, X11, GdkPixbuf,
  XTest, and optional IBus Simple composition; automatically enables session
  accessibility on first use.
- **macOS 14+**: native Swift sidecar app bundle using `AXUIElement`,
  CoreGraphics, and ScreenCaptureKit.
- **Windows 10/11**: native C# / .NET 8 sidecar using FlaUI UIA3 and Win32,
  with AF_UNIX socket support for the CLI and Python SDK.

### Instructions and OSWorld Evaluation Harness

- Plain-text model instructions in `instructions/` separate common computer-use
  policy (`base.md`) from channel interaction guides (`hybrid.md`,
  `ax-only.md`, `gui-only.md`, and `hybrid-code.md`).
- `hcu.osworld` runs OSWorld 1.0 and 2.0 tasks in isolated Docker/QEMU guests
  with Codex, Claude Code, Gemini CLI, or Grok Build, paired with task-scoped
  OpenAI Responses, Anthropic, and Gemini gateways (`hcu.gateway`) for
  credential isolation, rate-limit handling, and response-budget accounting.
