# Feature: Release and Codex CLI Sync Automation

Links:
Architecture: [docs/Architecture/Overview.md](../Architecture/Overview.md)
Modules: [.github/workflows](../../.github/workflows)
ADRs: [001-codex-cli-wrapper.md](../ADR/001-codex-cli-wrapper.md)

---

## Purpose

Keep package quality and upstream Codex CLI parity automatically verified through GitHub workflows.

---

## Scope

### In scope

- CI workflow (`ci.yml`)
- release workflow (`release.yml`)
- CodeQL workflow (`codeql.yml`)
- upstream watch workflow (`codex-cli-watch.yml`)
- real integration matrix workflow (`real-integration.yml`)

### Out of scope

- external deployment environments
- branch protection settings configured outside repository

---

## Business Rules

- CI must run build and tests on every push/PR.
- CI and Release workflows must execute full solution tests before smoke subsets, excluding auth-required tests with `-- --treenode-filter "/*/*/*/*[RequiresCodexAuth!=true]"`.
- Codex CLI smoke test workflow steps must run `CodexCli_Smoke_*` via `CodexSharpSDK.Tests` project scope to avoid false `zero tests ran` failures in non-smoke test assemblies.
- Codex CLI smoke validation must cover both `codex --help` and `codex exec --help`, proving root and non-interactive help surfaces stay discoverable.
- Release workflow must build/test before pack/publish.
- Release workflow must read package version from `Directory.Build.props`.
- Release workflow must validate semantic version format before packaging.
- Release workflow must fail if the produced core `.nupkg` version does not match `Directory.Build.props`.
- Release workflow must also pack `ManagedCode.CodexSharpSDK.Extensions.AgentFramework` and use the project-specific prerelease suffix from `Directory.Build.props`.
- Release workflow must use generated GitHub release notes.
- Release workflow must create/push git tag `v<version>` before publishing GitHub release.
- Codex CLI watch runs daily and opens issue when upstream `openai/codex` changed since pinned submodule SHA.
- Completing a Codex CLI sync issue must update the pinned `submodules/openai-codex` commit after validation.
- Sync issue body must derive flag changes from CLI source snapshots, model changes from `codex-rs/core/models.json`, and feature changes from `codex-rs/core/config.schema.json` so alerts stay actionable.
- SDK model constants must cover every bundled slug from `submodules/openai-codex/codex-rs/core/models.json` whenever upstream Codex repo sync work updates the pinned submodule.
- Sync issue must assign Copilot by default.
- Duplicate sync issue for same upstream SHA is not allowed.

---

## Diagrams

```mermaid
flowchart LR
  Push["push / pull_request"] --> CI["ci.yml"]
  Main["push main"] --> Release["release.yml"]
  Daily["daily cron"] --> Watch["codex-cli-watch.yml"]
  Watch --> Issue["GitHub Issue: Codex CLI sync"]
  CI --> Quality["build + test"]
  Release --> NuGet["NuGet publish + GitHub release"]
```

---

## Verification

### Test commands

- `codex --help`
- `codex exec --help`
- `codex features list`
- `dotnet build ManagedCode.CodexSharpSDK.slnx -c Release -warnaserror`
- `dotnet test --solution ManagedCode.CodexSharpSDK.slnx -c Release`
- `dotnet pack CodexSharpSDK/CodexSharpSDK.csproj -c Release --no-build -o artifacts`
- `dotnet pack CodexSharpSDK.Extensions.AgentFramework/CodexSharpSDK.Extensions.AgentFramework.csproj -c Release --no-build -o artifacts`

### Workflow mapping

- CI: [ci.yml](../../.github/workflows/ci.yml)
- Release: [release.yml](../../.github/workflows/release.yml)
- CodeQL: [codeql.yml](../../.github/workflows/codeql.yml)
- CLI Watch: [codex-cli-watch.yml](../../.github/workflows/codex-cli-watch.yml)
- Real integration matrix: [real-integration.yml](../../.github/workflows/real-integration.yml)

---

## Definition of Done

- Workflows are versioned and valid in repository.
- Local commands match CI commands.
- Daily sync issue automation is configured and documented.
