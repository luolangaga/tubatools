# Security Policy

## Supported versions

Security fixes are made for the latest 1.x release.

## Reporting a vulnerability

Please report vulnerabilities privately through GitHub's private
vulnerability reporting for this repository ("Report a vulnerability" under
the Security tab). Do not open a public issue for a suspected vulnerability.
Include the affected version (`computer-use --version` or the `VERSION`
file), the component (MCP server, provider, OSWorld runner or gateway) and
steps to reproduce.

## Scope

`computer-use` gives an agent control of the desktop it runs on, and the
OSWorld runner starts agents with shell access and bypassed permission
prompts inside disposable guests. These are intended capabilities, not
vulnerabilities; run them only in environments the agent is allowed to
control. [Running OSWorld](docs/OSWORLD.md#isolation-and-instructions)
describes the intended boundaries.

In scope are, for example, ways for an evaluated agent to reach host
credentials held by the gateways, to escape the task guest through the
runner, or to make the MCP server act outside the desktop session it serves.
