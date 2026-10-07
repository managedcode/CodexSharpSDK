# Feature: Codex CLI Metadata

Links:
Architecture: [docs/Architecture/Overview.md](../Architecture/Overview.md)
Modules: [CodexClient.cs](../../CodexSharpSDK/Client/CodexClient.cs), [CodexCliMetadataReader.cs](../../CodexSharpSDK/Internal/CodexCliMetadataReader.cs), [CodexCliMetadata.cs](../../CodexSharpSDK/Models/CodexCliMetadata.cs)
Source of truth: local `codex` CLI + upstream npm package metadata (`@openai/codex`)

---

## Purpose

Expose runtime Codex CLI metadata to SDK consumers:

- installed `codex-cli` version
- default model configured in local Codex config
- local model catalog currently cached by Codex CLI
- update availability status vs latest published npm `@openai/codex` version

---

## Scope

### In scope

- `CodexClient.GetCliMetadata()` public API.
- `CodexClient.GetCliUpdateStatus()` public API.
- Reading version from `codex --version`.
- Reading default model from `~/.codex/config.toml`.
- Reading model catalog from `~/.codex/models_cache.json`.
- Reading latest published package version from `npm view @openai/codex version`.
- Returning package-manager-appropriate update command (`bun` or `npm`) in update status.

### Out of scope

- Remote model discovery over network APIs.
- Mutating user Codex config files.
- Replacing Codex CLI model selection logic.

---

## Business Rules

- Metadata read is read-only and does not mutate local Codex state.
- If thread-level web search settings are not specified, SDK does not emit `web_search` overrides.
- SDK option and metadata decisions are based on real Codex CLI behavior, not TypeScript SDK surface.
- Update check failures (for example missing `npm`) must return actionable status messages and never silently fail.
- Update command text must not assume npm-only installs; SDK must emit `bun` update command when bun-managed install is detected.
- Metadata probes inherit only the configured environment policy and use `CodexOptions.CliMetadataProbeTimeout` plus `CodexOptions.CliMetadataMaximumOutputCharacters` to bound process time and captured output.
- CLI metadata uses the same immutable `CliLaunchCommand` resolver as model execution. Windows npm `.cmd` wrappers are not executed: only the selected wrapper's adjacent known-package manifest, bounded by `CodexOptions.CliMetadataMaximumFileCharacters`, can resolve an in-package JavaScript entrypoint (through absolute Node/Bun) or native `.exe`/`.com`. The npm update probe follows the same rule for the selected `npm.cmd`; unknown or malformed wrappers fail closed.
- Stdout and stderr are drained concurrently. A timed-out probe kills the process tree and confirms root-process exit; output exceeding either stream's cap fails instead of parsing truncated content. Cached API-support metadata is not a guarantee that a model is enabled for this CLI account.

---

## Diagram

```mermaid
flowchart LR
  Client["CodexClient.GetCliMetadata()"] --> Resolve["Resolve immutable executable + prefix arguments"]
  Resolve --> Version["codex --version"]
  Client --> Update["CodexClient.GetCliUpdateStatus()"]
  Client --> Config["~/.codex/config.toml"]
  Client --> Cache["~/.codex/models_cache.json"]
  Update --> ResolveNpm["Resolve Node/Bun npm entrypoint"]
  ResolveNpm --> Npm["npm view @openai/codex version"]
  Version --> Metadata["CodexCliMetadata"]
  Npm --> UpdateStatus["CodexCliUpdateStatus"]
  Config --> Metadata
  Cache --> Metadata
```

---

## Verification

- Unit parsing/update-check coverage: [CodexCliMetadataReaderTests.cs](../../CodexSharpSDK.Tests/Unit/CodexCliMetadataReaderTests.cs)
- CLI arg behavior: [CodexExecTests.cs](../../CodexSharpSDK.Tests/Unit/CodexExecTests.cs)
