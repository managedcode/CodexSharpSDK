# Testing Strategy

## Goal

Verify `ManagedCode.CodexSharpSDK` behavior against real Codex CLI contracts, with deterministic automated tests for both baseline and additive C# capabilities.

## Test levels used in this repository

- Primary: TUnit behavior tests in `CodexSharpSDK.Tests`
- Optional CI matrix: cross-platform Codex CLI smoke verification (`.github/workflows/real-integration.yml`)

## Principles

- Test observable behavior, not implementation details.
- Use the real installed `codex` CLI for process interaction tests; do not use `FakeCodexProcessRunner` doubles.
- Treat `codex` as a prerequisite for real integration runs and install it in CI/local setup before running those tests.
- CI validates Codex CLI smoke behavior on Linux/macOS/Windows without requiring login: CLI must be discoverable and invokable.
- Smoke coverage validates both `codex --help` and `codex exec --help` before unauthenticated login checks.
- Cross-platform CI smoke also validates unauthenticated behavior in an isolated profile (`codex login status` must report `Not logged in`), proving binary discovery + process launch without relying on local credentials.
- Real integration runs must use existing Codex CLI login/session; test harness does not use API key environment variables.
- Real integration model selection must be explicit: set `CODEX_TEST_MODEL` or define `model` in `~/.codex/config.toml` (no hardcoded fallback model).
- Cover error paths and cancellation paths.
- Keep protocol parser coverage for all supported event/item kinds.
- Keep a large-stream parser performance profile test to catch regressions.

## Commands

- build: `dotnet build ManagedCode.CodexSharpSDK.slnx -c Release -warnaserror`
- test: `dotnet test --solution ManagedCode.CodexSharpSDK.slnx -c Release`
- coverage: `dotnet test --solution ManagedCode.CodexSharpSDK.slnx -c Release -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml`
- codex smoke subset: `dotnet test --project CodexSharpSDK.Tests/CodexSharpSDK.Tests.csproj -c Release -- --treenode-filter "/*/*/*/CodexCli_Smoke_*"`
- ci/release non-auth full run: `dotnet test --solution ManagedCode.CodexSharpSDK.slnx -c Release -- --treenode-filter "/*/*/*/*[RequiresCodexAuth!=true]"`

Smoke subset is an additional gate and does not replace full-solution test execution.

TUnit on Microsoft Testing Platform does not support `--filter`; run focused tests with `-- --treenode-filter "/*/*/<ClassName>/*"`.

## Test map

- Client lifecycle and concurrency: [CodexClientTests.cs](../../CodexSharpSDK.Tests/Unit/CodexClientTests.cs)
- `CodexClient` API surface behavior: [CodexClientTests.cs](../../CodexSharpSDK.Tests/Unit/CodexClientTests.cs)
- CodexThread run/stream/failure behavior: [CodexThreadTests.cs](../../CodexSharpSDK.Tests/Unit/CodexThreadTests.cs)
- CLI arg/env/config behavior: [CodexExecTests.cs](../../CodexSharpSDK.Tests/Unit/CodexExecTests.cs)
- CLI metadata parsing behavior: [CodexCliMetadataReaderTests.cs](../../CodexSharpSDK.Tests/Unit/CodexCliMetadataReaderTests.cs)
- Cross-platform Codex CLI smoke behavior: [CodexCliSmokeTests.cs](../../CodexSharpSDK.Tests/Integration/CodexCliSmokeTests.cs)
- Real process integration behavior: [CodexExecIntegrationTests.cs](../../CodexSharpSDK.Tests/Integration/CodexExecIntegrationTests.cs)
- Real Codex CLI integration behavior (local login required): [RealCodexIntegrationTests.cs](../../CodexSharpSDK.Tests/Integration/RealCodexIntegrationTests.cs)
- Protocol parser behavior: [ThreadEventParserTests.cs](../../CodexSharpSDK.Tests/Unit/ThreadEventParserTests.cs)
- Protocol parser large-stream performance profile: [ThreadEventParserPerformanceTests.cs](../../CodexSharpSDK.Tests/Performance/ThreadEventParserPerformanceTests.cs)
- Serialization and schema temp file behavior: [TomlConfigSerializerTests.cs](../../CodexSharpSDK.Tests/Unit/TomlConfigSerializerTests.cs), [OutputSchemaFileTests.cs](../../CodexSharpSDK.Tests/Unit/OutputSchemaFileTests.cs)
