# Codex Project Profile

Project: /Users/braymond/Projects/amd-gpu-fan-control
Trust mode: full-auto
Codex profile: full-auto-local
Project type: desktop-app-service

## Recommended Plugins

Computer Use, Browser, Chrome, GitHub, Superpowers; optional Build macOS Apps, Sentry, Figma

## Optional Capability Candidates

Use Build macOS Apps for native Swift/AppKit work, Computer Use for local desktop UI validation, Chrome/Browser for Electron or webview surfaces, and Sentry for runtime issue triage.

## MCP and CLI Candidates

Electron, Tauri, SwiftPM, xcodebuild, Playwright CLI or MCP for Electron/browser surfaces, OS-specific packaging/signing tools, Context7 for framework docs, and secret scanners for release config.

## Capability Discovery

Codex should proactively suggest missing official or third-party plugins, MCP servers, skills, connectors, browser tools, design tools, test runners, security scanners, observability tools, and domain-specific CLIs when they would materially improve planning or execution. Recommendations should explain the benefit, setup cost, trust/security implications, and whether the capability is optional or important. Credential or account requirements are not a reason to omit a useful recommendation; they are a reason to clearly label setup, approval, data exposure, and trust implications before enabling the capability. Do not silently install, enable, authenticate, or connect account-level services.

## Methodology Skills

Codex should automatically use relevant installed methodology skills or plugins, including Superpowers when available, when they materially improve the task. Use systematic debugging for bugs, failures, flaky behavior, build errors, and unexpected behavior; verification-before-completion before any completion claim; test-driven-development for behavioral code changes and bug fixes where a meaningful failing test can be written first; brainstorming and writing-plans for ambiguous, high-impact, or multi-component feature work; parallel-agent methodology for bounded independent scopes; and code-review skills for major, risky, release, or external-review work.

Do not force full methodology workflows for low-risk config, docs, generated files, formatting, dependency metadata, or one-line mechanical fixes unless risk or ambiguity justifies it. Project-specific AGENTS.md guidance, trust mode, security rules, and direct user instructions remain authoritative.

## Context Hygiene And Delegation

Use project-scoped agents in `.codex/agents/` for bounded sidecar work: `codebase_explorer`, `docs_researcher`, `implementation_worker`, `reviewer`, and `verifier`. The main chat remains the integrating owner. Delegate with narrow context packets: objective, files or paths, constraints, expected output, and verification needs. Avoid forking full chat context unless the subagent truly needs it. Subagents should return concise findings with file references and should put long logs or artifacts in durable files rather than the main transcript.

## User Story And Acceptance Discipline

Before implementation, distill functional requests into user or operator stories, acceptance criteria, representative happy paths, relevant failure or edge paths, and proof methods. Keep implementation tasks grouped by independently testable story where practical. Before completion, report which criteria were proven by tests, browser/simulator/admin smoke, logs, screenshots, hosted checks, or generated artifacts, and identify any criteria that remain unverified.

## Project Documentation

Maintain `docs/agent-handoff.md` or the project's equivalent current-state handoff document so future Codex, Claude Code, or human maintainers can resume safely. Update it when architecture, workflows, verification commands, deployment/release steps, operational assumptions, security posture, or known risks change.

Maintain `docs/verification-evidence.md` for functional acceptance evidence that should survive across agent sessions. Use `docs/change-checklist.md` as the completion gate for meaningful changes.

## Task Inbox

Use `docs/task-inbox/` as the durable local capture queue for future work that should not be planned or implemented immediately. When the user asks to capture, backlog, save, remember, queue, or record a future task, create one dated Markdown file in `docs/task-inbox/` and preserve the raw request verbatim or near-verbatim. Treat captured tasks as planning seeds, not implementation plans. Add only light triage: status, task type, priority if stated, obvious open questions, owner/project if clear, and the next planning step. Do not expand scope, prioritize, estimate, plan, or implement the task unless the user explicitly asks for planning or execution. If GitHub Issues, Linear, Notion, or another external tracker would add value, suggest it separately; still capture locally first unless the user asks to file it externally.

## Expected Workflow

Desktop build/run/debug loops, UI smoke checks, installer/update packaging, signing/entitlement review, webview/browser testing, and release notes.

## Verification

Unit tests, local app smoke tests, Playwright/Electron checks when applicable, installer smoke tests, signing/notarization checks for releases, and crash/error review.

## Automation Candidates

- GitHub: PR review, CI failure triage, issue creation, release readiness.
- Slack or Teams: monitor operational channels, summarize incidents, turn threads into tracked tasks.
- Gmail or Outlook Email: monitor approved inboxes, extract action items, draft replies for review.
- Google Calendar or Outlook Calendar: daily briefs, meeting prep, follow-up reminders.
- Linear: convert repeatable findings into tracked engineering work.
- Notion, Google Drive, or SharePoint: maintain runbooks, specs, discovery logs, and decision records.
- Browser: periodic website/app/admin smoke checks and evidence capture.

## Automation Boundary

Account-level connectors such as email, chat, calendar, Drive, SharePoint, Notion, and Linear should be enabled deliberately per workspace. Do not silently connect them from project bootstrap.
