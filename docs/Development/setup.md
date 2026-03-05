# Development Setup

## Prerequisites

- .NET SDK `10.0.103` (see `global.json`)
- Codex CLI available locally (`codex` in PATH) for real runtime usage
- Git with submodule support

## Bootstrap

```bash
git submodule update --init --recursive
dotnet restore CodexSharp.slnx
```

## Local validation

```bash
dotnet build CodexSharp.slnx -c Release -warnaserror
dotnet test --solution CodexSharp.slnx -c Release
dotnet format CodexSharp.slnx
```

## AOT smoke check

```bash
dotnet publish samples/CodexSharp.AotSmoke/CodexSharp.AotSmoke.csproj -c Release -r osx-arm64 /p:PublishAot=true
```

## Packaging check

```bash
dotnet pack src/CodexSharp/CodexSharp.csproj -c Release --no-build -o artifacts
```

## CI/workflows

- CI: `.github/workflows/ci.yml`
- Release: `.github/workflows/release.yml`
- CodeQL: `.github/workflows/codeql.yml`
- TypeScript sync watcher: `.github/workflows/typescript-sdk-watch.yml`
