# Feature: Release and TypeScript Sync Automation

Links:
Architecture: [docs/Architecture/Overview.md](../Architecture/Overview.md)
Modules: [.github/workflows](../../.github/workflows)
ADRs: [001-codex-cli-wrapper.md](../ADR/001-codex-cli-wrapper.md)

---

## Purpose

Keep package quality and TypeScript parity automatically verified through GitHub workflows.

---

## Scope

### In scope

- CI workflow (`ci.yml`)
- release workflow (`release.yml`)
- CodeQL workflow (`codeql.yml`)
- upstream watch workflow (`typescript-sdk-watch.yml`)

### Out of scope

- external deployment environments
- branch protection settings configured outside repository

---

## Business Rules

- CI must run build and tests on every push/PR.
- Release workflow must build/test before pack/publish.
- TypeScript watch runs daily and opens issue only when `sdk/typescript` changed upstream.
- Duplicate sync issue for same upstream SHA is not allowed.

---

## Diagrams

```mermaid
flowchart LR
  Push["push / pull_request"] --> CI["ci.yml"]
  Main["push main"] --> Release["release.yml"]
  Daily["daily cron"] --> Watch["typescript-sdk-watch.yml"]
  Watch --> Issue["GitHub Issue: TS SDK sync"]
  CI --> Quality["build + test + aot smoke"]
  Release --> NuGet["NuGet publish + GitHub release"]
```

---

## Verification

### Test commands

- `dotnet build CodexSharp.slnx -c Release -warnaserror`
- `dotnet test --solution CodexSharp.slnx -c Release`
- `dotnet publish samples/CodexSharp.AotSmoke/CodexSharp.AotSmoke.csproj -c Release -r osx-arm64 /p:PublishAot=true`

### Workflow mapping

- CI: [ci.yml](../../.github/workflows/ci.yml)
- Release: [release.yml](../../.github/workflows/release.yml)
- CodeQL: [codeql.yml](../../.github/workflows/codeql.yml)
- TS Watch: [typescript-sdk-watch.yml](../../.github/workflows/typescript-sdk-watch.yml)

---

## Definition of Done

- Workflows are versioned and valid in repository.
- Local commands match CI commands.
- Daily sync issue automation is configured and documented.
