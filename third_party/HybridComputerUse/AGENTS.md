# Agent Guide

Instructions for coding agents that work on this repository. Operators should
read [README.md](README.md) and [docs/OSWORLD.md](docs/OSWORLD.md) instead.

## Safety boundary

Never copy, mount, summarize or inject this file or any other repository
documentation into an evaluated OSWorld agent. The evaluated agent receives
only:

- the original OSWorld task instruction;
- `instructions/base.md`, the common safety and outcome policy;
- one selected interaction guide, normally `instructions/hybrid.md`, unless
  the run explicitly selects no operating profile;
- the runner's `TASK_COMPLETE`, `TASK_INFEASIBLE` and `ASK_USER` contract.

Product instructions are plain text injected at startup, not discoverable or
activated skills. Keep runner protocol markers out of those files.

Do not expose evaluator internals, task recipes, coordinates, credentials,
earlier traces or cross-task memory to an evaluated agent.

## Where things live

| Path | Contents |
| --- | --- |
| `computer-use/` | Go MCP server; `native/linux/provider/` is the embedded Python provider |
| `computer-use/testdata/contract/` | golden copy of the model-visible MCP contract |
| `hcu/osworld/` | OSWorld runner, batch and queue coordinators |
| `hcu/gateway/` | task-scoped OpenAI and Anthropic gateways |
| `osworld-overlay/` | files installed into the pinned OSWorld-V2 checkout |
| `instructions/` | model input: common base policy and selected interaction guide |
| `tests/` | runner and gateway tests |

## Where new files go

Put a file in the package that owns its responsibility. Go imports point in
one direction only: `computer-use/` (embedded Linux provider) ←
`internal/contract` ← `internal/core` ← `internal/provider` ←
`internal/transport` ← `cmd/computer-use`.

| Package | Owns |
| --- | --- |
| `internal/contract` | public tool schemas, published values, error codes and limits |
| `internal/core` | AX and GUI service logic, session state, results, and the provider port (`provider_types.go`, `provider_wire.go`) |
| `internal/provider` | platform backends, the Linux runtime bundle, macOS and Windows sidecar transports |
| `internal/transport` | MCP, daemon and CLI entry points |
| `cmd/computer-use` | `main` only |
| `native/linux/provider/` | the Linux AT-SPI and X11 provider in Python |
| `native/macos/`, `native/windows/` | the native macOS and Windows providers |
| `testdata/provider-wire/` | wire fixtures shared by the Go, Python, Swift and C# tests |

Naming:

- Name a file after what it does now. Do not put version markers (`_v2`,
  `-v1`, `V2`) or retired concept names in file, directory or identifier
  names. A version that is a value, such as the wire `schema_version`, stays
  a value. Names of external things keep their version (OSWorld-V2,
  `jsonschema/v6`).
- For platform-specific Go files, use Go's `_<goos>.go` suffix and a build
  tag, not a prefix.
- Keep Go tests next to the source as `<file>_test.go`. Linux provider tests
  go in `computer-use/runtime_tests/` and must be listed in `runtime_test.py`.
  Root `tests/` tests only `hcu/`, `configs/` and `instructions/`.

Register a new Linux provider module in three places:
`linuxProviderPaths` in `internal/provider/linux_runtime_bundle.go`,
`wantPaths` in `linux_runtime_bundle_test.go`, and the import list and
`PROVIDER_MODULES` in `runtime_test_support.py`.

`scripts/maint/check_layout.sh` (run in CI) checks the version-marker rule,
the Go import direction and the bundle list, and warns about non-test source
files over 800 lines and tests over 1,200 lines. Files in `osworld-overlay/`
are exempt: each one replaces the OSWorld-V2 file at the same path and keeps
its layout.

## Rules

- Keep `AX` and `GUI` as the only public MCP tools. Bash, file editing,
  application APIs and browser/CDP remain separate channels of the agent.
- Treat model-visible text as an interface: tool schemas, descriptions,
  server instructions, error texts, base policy and interaction guides. Do not
  change it unless the task asks for it. The contract golden test fails on
  MCP-visible changes; regenerate it only for an intended change.
- Editing product instructions changes model context and requires a new
  evaluation attempt. Do not mix instruction revisions within a run or claim
  that a text migration proves performance.
- Change the Go server and the Python provider together when you touch their
  private protocol, and update `computer-use/testdata/provider-wire/`.
- Real OSWorld runs spend model quota. Ask before starting one, and run a
  setup canary first.
- `VERSION` holds the release version; do not hard-code versions elsewhere.

## Writing docs, comments and changelogs

Docs, code comments and `CHANGELOG.md` describe the current behavior and why
it is so. Evidence for a change goes in the pull request description; git
history keeps it.

- Do not write commit SHAs, run ids, internal hostnames or paths, image
  tags, verification dates, costs or panel scores in them.
- Do not add "last verified" stamps. CI and git history show freshness. If a
  text needs a version, refer to `VERSION` or a release tag.
- Pin an external revision only where it is needed to reproduce a result,
  in one place (a lock file, a config or the one command that uses it), and
  refer to that place.
- A comment or test says what behavior it covers, not which task or run
  found it.
- Evaluation notes and investigation logs stay out of the repository.

`scripts/maint/check_layout.sh` fails on a commit-verification stamp or an
internal host path in tracked text, and warns on hex tokens that look like
commit SHAs in `README.md`, `CHANGELOG.md` and `docs/`.

## Checks

Run before reporting a change:

```bash
make test
```

For changes to the runner, also dry-run both example configs with the
OSWorld-V2 virtual environment:

```bash
external/OSWorld-V2/.venv/bin/python -m hcu.osworld.run_queue \
  configs/osworld-v1-codex-queue.example.json --dry-run
external/OSWorld-V2/.venv/bin/python -m hcu.osworld.run_queue \
  configs/osworld-v2-codex-queue.example.json --dry-run
```
