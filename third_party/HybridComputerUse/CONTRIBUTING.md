# Contributing

## Development setup

- Go 1.22 or later for the MCP server.
- Python 3.10 or later. The provider tests also need PyGObject and the
  AT-SPI, GTK, GdkPixbuf and IBus typelibs (`python3-gi gir1.2-atspi-2.0
  gir1.2-gtk-3.0 gir1.2-gdkpixbuf-2.0 gir1.2-ibus-1.0` on Debian or Ubuntu).
  Live checks of input-method typing for Unicode, line feeds and tabs also
  require `ibus ibus-gtk ibus-gtk3` and an active IBus Simple desktop input
  source. Without one, the provider types that text as key events; see
  [Linux setup](docs/MCP.md#desktop-runtime).
- `pip install -e '.[test]'` for the runner and gateway tests.
- Docker, KVM and an OSWorld-V2 checkout only for OSWorld runs; see
  [Running OSWorld](docs/OSWORLD.md).
- Swift 6.2+ and the macOS SDK for the macOS provider; .NET 8 SDK on Windows
  for the Windows provider. See [native checks](docs/PLATFORMS.md).

## Checks

```bash
make test
```

runs gofmt, `go vet` and the Go tests in `computer-use/`, the provider tests
(`computer-use/runtime_test.py`) and the Python tests in `tests/`. Tests that
need an OSWorld-V2 checkout are skipped unless `OSWORLD_V2_ROOT` points at one
with the overlay installed. CI runs the same checks, the native-transport
tests with the race detector, and `go vet` for macOS and Windows.
`make test-transports` exercises both native worker transports with subprocess fixtures on Linux. Native CI also builds
and tests Swift/C# code; it does not prove interactive desktop permissions.

`scripts/maint/check_all.sh` runs the full check list: gofmt, builds and
`go vet` for Linux, macOS and Windows, the Go tests with and without the
native-transport tags, the provider tests and pytest. With a previous binary
as its argument it also compares the MCP `initialize`, `tools/list`, `help`
and `schema` bytes.

Put Go tests in the `_test.go` file of the source file they exercise; a test
that needs a higher Go package goes to the lowest package that can see
everything it uses (see [Architecture](docs/ARCHITECTURE.md#tests-across-packages)). Put
provider tests under `computer-use/runtime_tests/`, and runner and gateway
tests under `tests/`. Prefer a test that runs the code over one that searches
source text.

## Rules that protect evaluations

- The tool schemas, descriptions, server instructions, error texts, base policy
  and selected interaction guide are model input. A change to any of them
  changes the benchmark condition. The contract golden test
  (`computer-use/internal/transport/contract_golden_test.go`) fails on MCP-visible changes;
  regenerate it with `go test ./internal/transport -run TestPublicContractMatchesGolden
  -update-contract` and explain the change in the pull request.
- Product instruction edits require a new evaluation attempt; keep instruction
  revisions separate and do not infer performance from a wording change.
  `instructions/base.md` owns common safety and outcome policy; the selected
  profile owns interaction guidance. The runner alone supplies `TASK_COMPLETE`,
  `TASK_INFEASIBLE` and `ASK_USER` markers. See [Running OSWorld](docs/OSWORLD.md).
- Keep `AX` and `GUI` as the only public MCP tools.
- Never copy, mount or inject this repository's `AGENTS.md` or other
  documentation into an evaluated agent.
- The Go server and native providers share a private protocol. Update affected
  providers together and keep `computer-use/testdata/provider-wire/` consistent.
- Run a setup canary before any change that needs a real task run to verify;
  real runs spend model quota.

## Pull requests

Keep a pull request to one topic, describe the behavior change and how you
verified it, and add an entry under "Unreleased" in `CHANGELOG.md` for
user-visible changes. Commit subjects follow `area: summary`, for example
`fix(gateway): ...` or `docs: ...`.

## Releases

`VERSION` is the only place that holds the release version. A release commit
updates `VERSION` and moves the changelog entries under the new version.

## License

By contributing you agree that your contribution is licensed under the MIT
license. Files modified from OSWorld-V2 keep their Apache-2.0 header; see
`THIRD_PARTY_NOTICES.md`.
