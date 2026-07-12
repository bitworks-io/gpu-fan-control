# Security Policy

## Supported Versions

The latest release of **Lightweight AMD GPU Fan Control** is the only supported
version. Please update to the latest release before reporting an issue.

## Reporting a Vulnerability

If you believe you've found a security vulnerability in this project, please
report it privately rather than opening a public GitHub issue.

- **Email:** brian@bitworks.io
- Please include a description of the issue, steps to reproduce, and any
  relevant logs or environment details (see `docs/agent-handoff.md` for
  where application logs live: `%LOCALAPPDATA%\Bitworks\LightweightAmdGpuFanControl\log.txt`).

We'll acknowledge your report on a best-effort basis and work with you on a
fix and disclosure timeline. This is a small, independently maintained
project — there is no formal SLA and no bug bounty program, but we take
security reports seriously and will respond as quickly as we're able.

## Scope

**In scope:**
- The Lightweight AMD GPU Fan Control application (this repository).
- The Windows installer produced from this repository.

**Out of scope:**
- The AMD graphics driver, AMD Software: Adrenalin Edition, and the AMD ADLX
  SDK itself (`external/ADLX`). Vulnerabilities in AMD's driver or SDK should
  be reported directly to AMD.

## Notes on the Application's Security Model

- The application P/Invokes into AMD GPU driver DLLs (via the ADLX SDK
  binding) to read temperature and set fan speed. It does **not** run
  elevated — it runs as `asInvoker`, and the installer requests no
  administrator rights (`PrivilegesRequired=lowest`).
- The application makes no outbound network calls except user-initiated
  browser opens (e.g. the feedback/contact link to bitworks.io). It collects
  no telemetry and stores no secrets.
- The current release build is unsigned; Windows SmartScreen will warn on
  first run. Code signing is a planned fast-follow.
