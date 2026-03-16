# Development Setup

## Prerequisites

- .NET SDK `10.0.103` (see `global.json`)
- Codex CLI available locally (`codex` in PATH) for real runtime usage
- Git with submodule support

## Windows Codex process lookup

- Runtime lookup order:
  - npm-installed native vendor binary under `node_modules/@openai/*/vendor/<target>/codex/codex.exe`
  - PATH candidates in order: `codex.exe`, `codex.cmd`, `codex.bat`, `codex`
- This allows both native npm optional packages and global npm shim installs to work on Windows.

## Bootstrap

```bash
git submodule update --init --recursive
dotnet restore ManagedCode.CodexSharpSDK.slnx
```

## Solution projects

- `CodexSharpSDK/CodexSharpSDK.csproj` — core `ManagedCode.CodexSharpSDK` package.
- `CodexSharpSDK.Extensions.AI/CodexSharpSDK.Extensions.AI.csproj` — optional `IChatClient` adapter package (`ManagedCode.CodexSharpSDK.Extensions.AI`).
- `CodexSharpSDK.Extensions.AgentFramework/CodexSharpSDK.Extensions.AgentFramework.csproj` — optional Microsoft Agent Framework adapter package (`ManagedCode.CodexSharpSDK.Extensions.AgentFramework`).
- `CodexSharpSDK.Tests/CodexSharpSDK.Tests.csproj` — core SDK tests (TUnit).
- `CodexSharpSDK.Tests/AgentFramework/*` — Microsoft Agent Framework adapter tests (TUnit, same test project).

## Local validation

```bash
dotnet build ManagedCode.CodexSharpSDK.slnx -c Release -warnaserror
dotnet test --solution ManagedCode.CodexSharpSDK.slnx -c Release
dotnet format ManagedCode.CodexSharpSDK.slnx
```

Focused run (TUnit/MTP):

```bash
dotnet test --project CodexSharpSDK.Tests/CodexSharpSDK.Tests.csproj -c Release -- --treenode-filter "/*/*/ThreadEventParserTests/*"
```

## Packaging check

```bash
dotnet pack CodexSharpSDK/CodexSharpSDK.csproj -c Release --no-build -o artifacts
```

## CI/workflows

- CI: `.github/workflows/ci.yml`
- Release: `.github/workflows/release.yml`
- CodeQL: `.github/workflows/codeql.yml`
- Codex CLI sync watcher: `.github/workflows/codex-cli-watch.yml`
- Real integration matrix: `.github/workflows/real-integration.yml`
