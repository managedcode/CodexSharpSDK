# AGENTS.md

Project: ManagedCode.CodexSharpSDK
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

- build: `dotnet build ManagedCode.CodexSharpSDK.slnx -c Release -warnaserror`
- test: `dotnet test --solution ManagedCode.CodexSharpSDK.slnx -c Release`
- format: `dotnet format ManagedCode.CodexSharpSDK.slnx`
- analyze: `dotnet build ManagedCode.CodexSharpSDK.slnx -c Release -warnaserror /p:TreatWarningsAsErrors=true`
- coverage: `dotnet test --solution ManagedCode.CodexSharpSDK.slnx -c Release -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml`
- aot-smoke: `dotnet publish tests/AotSmoke/ManagedCode.CodexSharpSDK.AotSmoke.csproj -c Release -r osx-arm64 /p:PublishAot=true`
- For Codex CLI metadata checks (for example model list/default), always use the installed `codex` CLI directly first; do not use `cargo`/submodule helper binaries unless explicitly requested.

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
- When asked to fix review findings, close every confirmed finding in the same pass; do not leave partial fixes.
- Do not keep or add public sample projects; repository focus is SDK + tests (including AOT tests) only.
- Upstream sync automation must track real `openai/codex` CLI changes (flags/models/features), not TypeScript SDK surface diffs, and open actionable repository issues for required SDK follow-up.
- Automatically opened upstream sync issues must include change summary/checklist and assign Copilot by default.
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

- Testing framework is TUnit (`tests`).
- TUnit/MTP does not support `--filter`; for focused runs use `dotnet test ... -- --treenode-filter "<pattern>"`.
- Always invoke runner options after `--` (for example `dotnet test --solution ManagedCode.CodexSharpSDK.slnx -c Release -- --treenode-filter "<pattern>"`); if a focused filter runs zero tests, treat it as an invalid filter and correct it before reporting results.
- In this repository, method-level `treenode-filter` patterns resolve at depth 3 (`/*/*/*/<TestMethodName>`). For integration subset runs use `dotnet test --solution ManagedCode.CodexSharpSDK.slnx -c Release -- --treenode-filter "/*/*/*/RunAsync_*_EndToEnd"` (matches current `CodexExecIntegrationTests`).
- For reliable discovery when selecting focused tests, use the built test app directly: `tests/bin/Release/net10.0/ManagedCode.CodexSharpSDK.Tests --list-tests` (the `dotnet test ... -- --list-tests` wrapper can report zero tests in this setup).
- Every behavior change must include or update tests.
- Prefer behavior-level tests over trivial implementation tests.
- Integration test sandboxes must be created under the repository `tests` tree (for example `tests/.sandbox/*`) for deterministic, inspectable paths; do not use `Path.GetTempPath()` for test sandbox directories.
- For CLI process interaction tests, use the real installed `codex` CLI (no `FakeCodexProcessRunner` test doubles).
- Treat `codex` CLI as a test prerequisite: ensure local/CI test setup installs `codex` before running CLI interaction tests; do not replace this with fakes.
- CI must validate SDK on all Codex-supported desktop/server platforms (macOS, Linux, Windows): run build + tests and include a non-auth smoke check that `codex` is discoverable and invokable.
- Real Codex integration tests must rely on existing local Codex CLI login/session only; do not read or require `OPENAI_API_KEY` in test setup.
- Do not use nullable `TryGetSettings()` + early `return` skip patterns in real integration tests; resolve required settings directly and fail fast with actionable errors when missing.
- Do not bypass integration tests on Windows with unconditional early returns; keep tests cross-platform for supported Codex CLI environments.
- Parser changes require tests in `ThreadEventParserTests` for supported and invalid payloads.
- Parser performance tests must use representative mixed payloads across supported event/item types and assert parsed output shape; avoid single-payload stopwatch loops that do not validate branch coverage.
- Client/thread concurrency changes require explicit concurrent tests.
- Never delete/skip tests to get green CI.

### Advisor stance (ALL TASKS)

- Be direct and technically precise.
- Call out underspecified, risky, or contradictory requirements.
- Do not invent facts; verify via code/tests/docs.

### Code Style

- Follow `.editorconfig` and analyzer rules.
- Always build with `-warnaserror` so warnings fail the build.
- Prefer idiomatic, readable C# with clear naming and straightforward control flow so code can be understood quickly during review and maintenance.
- Use synchronization primitives only when there is a proven shared-state invariant; prefer simpler designs over ad-hoc locking for maintainable production code.
- Keep a clear, domain-oriented folder structure (for example `Models`, `Client`, `Execution`, `Internal`) so project layout remains predictable and easy to navigate.
- Never use sync-over-async bridges like `.GetAwaiter().GetResult()` in SDK code; keep disposal and lifecycle APIs non-blocking and explicit.
- Do not implement both `IDisposable` and `IAsyncDisposable` on the same SDK type unless the type truly owns asynchronous resources that require async-only cleanup.
- Never use `#pragma warning disable`; fix the underlying warning cause instead.
- Keep short constructor initializers on the same line as the signature (for example `Ctor(...) : base(...)`), not on a separate line.
- Never use empty/silent `catch` blocks; every caught exception must be either logged with context or rethrown with context.
- Never add fake fallback calls/mocks in production paths; unsupported runtime cases must fail explicitly with actionable errors.
- No magic literals: extract constants/enums/config values.
- In SDK production code, do not inline string literals in implementation logic; promote them to named constants (paths, env vars, command names, switch/comparison tokens) for reviewability and consistency.
- Do not inline filesystem/path segment string literals in implementation logic; define named constants and reuse them.
- Never override or silently mutate explicit user-provided Codex CLI settings (for example `web_search=disabled`); pass through user intent exactly.
- Protocol and CLI string tokens are mandatory constants: never inline literals in parsing, mapping, or switch branches.
- In SDK model records, never inline protocol type literals in constructors (`ThreadItem(..., "...")`, `ThreadEvent("...")`); always reference protocol constants.
- Do not expose a public SDK type named `Thread`; use `CodexThread` to avoid .NET type-name conflicts.
- Keep public API and naming aligned with package/namespace `ManagedCode.CodexSharpSDK`.
- Solution/workspace file naming must use `ManagedCode.CodexSharpSDK` prefix for consistency with package identity.
- Keep package/version metadata centralized in `Directory.Build.props`; avoid duplicating version structure or release metadata blocks in individual `.csproj` files unless a project-specific override is required.
- Never hardcode guessed Codex/OpenAI model names in tests, docs, or defaults; verify supported models and active default via Codex CLI first.
- Before setting or changing any `Model` value, read available models and current default from the local `codex` CLI in the same environment/account and only then update code/tests/docs.
- Model identifiers in code/tests must come from centralized constants or a shared resolver helper; do not inline model string literals repeatedly.
- For SDK option/model/flag support, use the real `codex` CLI behavior (`codex --help`, `codex exec --help`, runtime checks) as the only source of truth; do not use TypeScript SDK surface as the decision baseline.
- Do not hardcode npm-only Codex install/update guidance; when generating CLI update/install command text, detect and support `bun` where applicable.
- Image input API must support passing local image data as file path, `FileInfo`, and `Stream`.
- Use `Microsoft.Extensions.Logging.ILogger` for SDK logging extension points; do not introduce custom logger interfaces or custom log-level enums.
- In tests, prefer `Microsoft.Extensions.Logging.Abstractions.NullLogger` instead of custom fake logger implementations when log capture is not required.
- Default to AOT/trimming-safe patterns (explicit JSON handling, avoid reflection-heavy designs).
- When API offers both convenience and AOT-safe overloads, explicitly mark AOT-unsafe methods with `RequiresDynamicCode`/`RequiresUnreferencedCode` and point to the `JsonTypeInfo<T>` alternative.
- Avoid ambiguous option names like `*Override` for primary settings; prefer explicit names (for example executable path / working directory).
- For this project, remove legacy/compatibility shims immediately (including `[Obsolete]` bridges and duplicate old properties); keep only the current API surface.
- README first examples must be beginner-friendly: avoid advanced/optional knobs (for example `CodexExecutablePath`) in the very first snippet.
- README in this repository must describe only current first-version product behavior; do not add migration/legacy compatibility sections or old-namespace guidance.
- When a README snippet shows model tuning, include `ModelReasoningEffort` together with `Model`.
- Public examples should build output schemas with typed `StructuredOutputSchema` models and map responses to typed DTOs for readability and maintainability.
- Do not keep or add `JsonSchema` helper abstractions in SDK API/tests; use typed request/response DTO models instead of schema-builder utilities.
- Do not handcraft JSON with `JsonValue`/`JsonNode` literals for structured outputs in API/tests/examples; define typed DTO models and map schema/fields from those models.
- Keep consumer usage examples in `README.md`; do not introduce standalone sample projects.

### Critical (NEVER violate)

- Never commit secrets, keys, or tokens.
- Never use destructive git operations (`reset --hard`, forced branch rewrite) without explicit user approval.
- Never publish NuGet packages from local machine without explicit user confirmation.
- Never bypass CI quality gates by weakening tests or analyzers.

### Boundaries

**Always:**

- Read `AGENTS.md` and relevant docs before editing code.
- Keep API behavior aligned with actual Codex CLI contracts first; TypeScript SDK mapping may be used only as an optional historical reference, not as a blocker for C# SDK design.
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
- Simple onboarding examples first, advanced configuration later.

### Dislikes

- Magic strings in protocol parsing and CLI mappings
- Hidden assumptions in CI/release pipelines
- Unreadable, non-idiomatic C# that looks chaotic and hard to reason about
- Unjustified `lock`/thread-synchronization complexity that obscures intent and increases maintenance risk
- Flat, mixed file layouts where model, client, execution, and utility types are interleaved without folder boundaries
- Blocking sync-over-async patterns (`GetAwaiter().GetResult()`) in production SDK code
- Legacy compatibility layers in a new project (`Obsolete` bridges, duplicate old option names, migration shims)
- Suppressing warnings with `#pragma warning disable` instead of fixing root causes
- Constructor initializer formatting split onto a separate line (`)` then next line `: base(...)`) in simple cases
- Silent exception swallowing (`catch {}` or catch-without logging/context) that hides cleanup/runtime failures
- Fake fallback behavior that masks real runtime/CLI integration issues
- Template placeholders left in production repository docs
- Raw nested `JsonObject`/`JsonArray` schema literals in user-facing examples.
- Public sample projects in this repository; prefer tests (including AOT tests) instead.
- Custom logging abstractions when `ILogger` already solves the integration use case.
- Performance tests that exercise only one parser payload and do not cover supported parsing branches.
- Example code scattered across standalone sample projects instead of `README.md`.
