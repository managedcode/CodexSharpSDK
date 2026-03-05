# Testing Strategy

## Goal

Verify `ManagedCode.CodexSharp` behavior against TypeScript SDK semantics with deterministic automated tests.

## Test levels used in this repository

- Primary: TUnit behavior tests in `tests/CodexSharp.Tests`
- Secondary: NativeAOT smoke publish in `samples/CodexSharp.AotSmoke`

## Principles

- Test observable behavior, not implementation details.
- Keep CLI tests deterministic using `FakeCodexProcessRunner`.
- Cover error paths and cancellation paths.
- Keep protocol parser coverage for all supported event/item kinds.

## Commands

- build: `dotnet build CodexSharp.slnx -c Release -warnaserror`
- test: `dotnet test --solution CodexSharp.slnx -c Release`
- coverage: `dotnet test --solution CodexSharp.slnx -c Release -- --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml`
- aot-smoke: `dotnet publish samples/CodexSharp.AotSmoke/CodexSharp.AotSmoke.csproj -c Release -r osx-arm64 /p:PublishAot=true`

## Test map

- Client lifecycle and concurrency: [CodexClientTests.cs](../../tests/CodexSharp.Tests/CodexClientTests.cs)
- `CodexClient` API surface behavior: [CodexClientTests.cs](../../tests/CodexSharp.Tests/CodexClientTests.cs)
- CodexThread run/stream/failure behavior: [CodexThreadTests.cs](../../tests/CodexSharp.Tests/CodexThreadTests.cs)
- CLI arg/env/config behavior: [CodexExecTests.cs](../../tests/CodexSharp.Tests/CodexExecTests.cs)
- Real process integration behavior: [CodexExecIntegrationTests.cs](../../tests/CodexSharp.Tests/CodexExecIntegrationTests.cs)
- Protocol parser behavior: [ThreadEventParserTests.cs](../../tests/CodexSharp.Tests/ThreadEventParserTests.cs)
- Serialization and schema temp file behavior: [TomlConfigSerializerTests.cs](../../tests/CodexSharp.Tests/TomlConfigSerializerTests.cs), [OutputSchemaFileTests.cs](../../tests/CodexSharp.Tests/OutputSchemaFileTests.cs)
