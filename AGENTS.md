# AGENTS.md

Project: ManagedCode.CodexSharp
Stack: .NET 10, C# 14, TUnit, GitHub Actions, NativeAOT, NuGet, Codex CLI integration

Follows [MCAF](https://mcaf.managed-code.com/)

---

## Conversations (Self-Learning)

Learn the user's habits, preferences, and working style. Extract rules from conversations, save to "## Rules to follow", and generate code according to the user's personal rules.

**Update requirement (core mechanism):**

Before doing ANY task, evaluate the latest user message.
If you detect a new rule, correction, preference, or change -> update `AGENTS.md` first.
Only after updating the file you may produce the task output.
If no new rule is detected -> do not update the file.

**When to extract rules:**

- prohibition words (never, don't, stop, avoid) or similar -> add NEVER rule
- requirement words (always, must, make sure, should) or similar -> add ALWAYS rule
- memory words (remember, keep in mind, note that) or similar -> add rule
- process words (the process is, the workflow is, we do it like) or similar -> add to workflow
- future words (from now on, going forward) or similar -> add permanent rule

**Preferences -> add to Preferences section:**

- positive (I like, I prefer, this is better) or similar -> Likes
- negative (I don't like, I hate, this is bad) or similar -> Dislikes
- comparison (prefer X over Y, use X instead of Y) or similar -> preference rule

**Corrections -> update or add rule:**

- error indication (this is wrong, incorrect, broken) or similar -> fix and add rule
- repetition frustration (don't do this again, you ignored, you missed) or similar -> emphatic rule
- manual fixes by user -> extract what changed and why

**Strong signal (add IMMEDIATELY):**

- swearing, frustration, anger, sarcasm -> critical rule
- ALL CAPS, excessive punctuation (!!!, ???) -> high priority
- same mistake twice -> permanent emphatic rule
- user undoes your changes -> understand why, prevent

**Ignore (do NOT add):**

- temporary scope (only for now, just this time, for this task) or similar
- one-off exceptions
- context-specific instructions for current task only

**Rule format:**

- One instruction per bullet
- Tie to category (Testing, Code, Docs, etc.)
- Capture WHY, not just what
- Remove obsolete rules when superseded

---

## Rules to follow (Mandatory, no exceptions)

### Commands

- build: `dotnet build CodexSharp.slnx -c Release -warnaserror`
- test: `dotnet test --solution CodexSharp.slnx -c Release`
- format: `dotnet format CodexSharp.slnx`
- analyze: `dotnet build CodexSharp.slnx -c Release -warnaserror /p:TreatWarningsAsErrors=true`
- coverage: `dotnet test --solution CodexSharp.slnx -c Release -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml`
- aot-smoke: `dotnet publish samples/CodexSharp.AotSmoke/CodexSharp.AotSmoke.csproj -c Release -r osx-arm64 /p:PublishAot=true`

### Task Delivery (ALL TASKS)

- Always start from `docs/Architecture/Overview.md`:
  - identify impacted modules and boundaries
  - identify entry points and contracts
  - follow linked ADR/Feature docs before code changes
- Keep scope explicit before coding:
  - in scope
  - out of scope
- Keep context minimal and relevant; do not scan the whole repository unless required.
- Update docs for every behavior or architecture change:
  - `docs/Features/*` for behavior
  - `docs/ADR/*` for design/architecture decisions
  - `docs/Architecture/Overview.md` when module boundaries or interactions change
- Implement code and tests together.
- Run verification in this order:
  - focused tests for changed behavior
  - full solution tests
  - format
  - final build
- If changes impact trimming/AOT safety, run `aot-smoke`.

### Documentation (ALL TASKS)

- Canonical structure:
  - `docs/Architecture/` for system-level architecture
  - `docs/Features/` for feature behavior and flows
  - `docs/ADR/` for architecture decisions
  - `docs/Testing/` for testing strategy
  - `docs/Development/` for local setup and workflow
- All TODO lists and implementation plans must live in repository root (for example `PORTING_TODO.md`, `PLAN.md`); do not store task-tracking checklists under `docs/`.
- Keep `docs/Architecture/Overview.md` short and navigational (diagrams + links), not an implementation dump.
- Each architecture/feature/ADR doc must contain at least one Mermaid diagram.
- Avoid duplicated rules across docs; link to canonical source instead.

### Testing (ALL TASKS)

- Testing framework is TUnit (`tests/CodexSharp.Tests`).
- Every behavior change must include or update tests.
- Prefer behavior-level tests over trivial implementation tests.
- For CLI process interactions, use `FakeCodexProcessRunner` test doubles rather than invoking external binaries.
- Parser changes require tests in `ThreadEventParserTests` for supported and invalid payloads.
- Client/thread concurrency changes require explicit concurrent tests.
- Never delete/skip tests to get green CI.

### Advisor stance (ALL TASKS)

- Be direct and technically precise.
- Call out underspecified, risky, or contradictory requirements.
- Do not invent facts; verify via code/tests/docs.

### Code Style

- Follow `.editorconfig` and analyzer rules.
- Always build with `-warnaserror` so warnings fail the build.
- No magic literals: extract constants/enums/config values.
- Protocol and CLI string tokens are mandatory constants: never inline literals in parsing, mapping, or switch branches.
- In SDK model records, never inline protocol type literals in constructors (`ThreadItem(..., "...")`, `ThreadEvent("...")`); always reference protocol constants.
- Do not expose a public SDK type named `Thread`; use `CodexThread` to avoid .NET type-name conflicts.
- Keep public API and naming aligned with package/namespace `ManagedCode.CodexSharp`.
- Default to AOT/trimming-safe patterns (explicit JSON handling, avoid reflection-heavy designs).

### Critical (NEVER violate)

- Never commit secrets, keys, or tokens.
- Never use destructive git operations (`reset --hard`, forced branch rewrite) without explicit user approval.
- Never publish NuGet packages from local machine without explicit user confirmation.
- Never bypass CI quality gates by weakening tests or analyzers.

### Boundaries

**Always:**

- Read `AGENTS.md` and relevant docs before editing code.
- Keep API compatibility with TypeScript SDK mapping documented in `PORTING_TODO.md` unless instructed otherwise.
- Maintain GitHub workflow health (`.github/workflows`).

**Ask first:**

- Breaking public API changes
- New external runtime dependencies
- NuGet package metadata changes impacting consumers
- Removing tests or source files
- Changes to release/publish credentials or secret names

---

## Preferences

### Likes

- Explicit, deterministic behavior
- Full test coverage for new logic
- Clear docs with diagrams and direct code links

### Dislikes

- Magic strings in protocol parsing and CLI mappings
- Hidden assumptions in CI/release pipelines
- Template placeholders left in production repository docs
