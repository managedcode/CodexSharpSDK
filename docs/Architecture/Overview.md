# Architecture Overview

Goal: understand quickly what exists in `ManagedCode.CodexSharpSDK`, where it lives, and how modules interact.

Single source of truth: this file is navigational and coarse. Detailed behavior lives in `docs/Features/*`. Architectural rationale lives in `docs/ADR/*`.

## Summary

- **System:** .NET SDK wrapper over Codex CLI JSONL protocol.
- **Where is the code:** core SDK in `CodexSharpSDK`; optional M.E.AI adapter in `CodexSharpSDK.Extensions.AI`; optional Microsoft Agent Framework adapter in `CodexSharpSDK.Extensions.AgentFramework`; tests in `CodexSharpSDK.Tests`.
- **Entry points:** `CodexClient`, `CodexChatClient` (`IChatClient` adapter), `AddCodexAIAgent` / `AddKeyedCodexAIAgent` (`AIAgent` DI helpers).
- **Dependencies:** local `codex` CLI process, `System.Text.Json`, .NET SDK/toolchain, GitHub Actions.

## Scoping (read first)

- **In scope:** SDK API surface, CLI argument mapping, event parsing, thread lifecycle, docs, tests, CI workflows.
- **Out of scope:** Codex CLI internals (`submodules/openai-codex`), non-.NET SDKs, infrastructure outside this repository.
- Start by mapping the request to a module below, then follow linked feature/ADR docs.

## 1) Diagrams

### 1.1 System / module map

```mermaid
flowchart LR
  API["Public API\nCodexClient / CodexThread"]
  EXEC["Execution Layer\nCodexExec + process runner"]
  PARSER["Protocol Parsing\nThreadEventParser + Events/Items"]
  IO["Config & Schema IO\nTomlConfigSerializer + OutputSchemaFile"]
  META["CLI Metadata\nCodexCliMetadataReader"]
  MEAI["M.E.AI Adapter\nCodexChatClient : IChatClient"]
  MAF["MAF Adapter\nAIAgent DI helpers"]
  TESTS["TUnit Tests"]
  CI["GitHub Actions\nCI / Release / CLI Watch"]

  API --> EXEC
  EXEC --> IO
  API --> META
  EXEC --> PARSER
  PARSER --> API
  MEAI --> API
  MAF --> MEAI
  TESTS --> API
  TESTS --> MEAI
  TESTS --> MAF
  TESTS --> EXEC
  CI --> TESTS
```

### 1.2 Interfaces / contracts map

```mermaid
flowchart LR
  THREAD["CodexThread.RunAsync / RunAsync<T> / RunStreamedAsync"]
  EXECARGS["CodexExecArgs"]
  CLI["Codex CLI\n`exec --json`"]
  JSONL["JSONL stream events"]
  PARSE["ThreadEventParser.Parse"]
  EVENTS["ThreadEvent / ThreadItem models"]

  THREAD --"builds"--> EXECARGS
  EXECARGS --"maps to flags/env"--> CLI
  CLI --"emits"--> JSONL
  JSONL --"parsed by"--> PARSE
  PARSE --"returns"--> EVENTS
```

### 1.3 Key classes / types map

```mermaid
flowchart LR
  CC["CodexClient"]
  T["CodexThread"]
  E["CodexExec"]
  R["ICodexProcessRunner"]
  D["DefaultCodexProcessRunner"]
  P["ThreadEventParser"]

  CC --> T
  T --> E
  E --> R
  R --> D
  T --> P
```

## 2) Navigation index

### 2.1 Modules

- `Public API` — code: [CodexClient.cs](../../CodexSharpSDK/Client/CodexClient.cs), [CodexThread.cs](../../CodexSharpSDK/Client/CodexThread.cs); docs: [thread-run-flow.md](../Features/thread-run-flow.md)
- `Execution Layer` — code: [CodexExec.cs](../../CodexSharpSDK/Execution/CodexExec.cs), [CodexExecArgs.cs](../../CodexSharpSDK/Execution/CodexExecArgs.cs)
- `Protocol Parsing` — code: [ThreadEventParser.cs](../../CodexSharpSDK/Internal/ThreadEventParser.cs), [CodexProtocolConstants.cs](../../CodexSharpSDK/Internal/CodexProtocolConstants.cs), [Events.cs](../../CodexSharpSDK/Models/Events.cs), [Items.cs](../../CodexSharpSDK/Models/Items.cs)
- `Config & Schema IO` — code: [TomlConfigSerializer.cs](../../CodexSharpSDK/Internal/TomlConfigSerializer.cs), [OutputSchemaFile.cs](../../CodexSharpSDK/Internal/OutputSchemaFile.cs), [CodexOptions.cs](../../CodexSharpSDK/Configuration/CodexOptions.cs)
- `CLI Metadata` — code: [CodexCliMetadataReader.cs](../../CodexSharpSDK/Internal/CodexCliMetadataReader.cs), [CodexCliMetadata.cs](../../CodexSharpSDK/Models/CodexCliMetadata.cs); docs: [cli-metadata.md](../Features/cli-metadata.md)
- `M.E.AI Adapter` — code: [CodexSharpSDK.Extensions.AI](../../CodexSharpSDK.Extensions.AI); docs: [meai-integration.md](../Features/meai-integration.md); ADR: [003-microsoft-extensions-ai-integration.md](../ADR/003-microsoft-extensions-ai-integration.md)
- `MAF Adapter` — code: [CodexSharpSDK.Extensions.AgentFramework](../../CodexSharpSDK.Extensions.AgentFramework); docs: [agent-framework-integration.md](../Features/agent-framework-integration.md); ADR: [004-microsoft-agent-framework-integration.md](../ADR/004-microsoft-agent-framework-integration.md)
- `Testing` — code: [CodexSharpSDK.Tests](../../CodexSharpSDK.Tests); docs: [strategy.md](../Testing/strategy.md)
- `Automation` — workflows: [.github/workflows](../../.github/workflows) (including `real-integration.yml` and `codex-cli-watch.yml`); docs: [release-and-sync-automation.md](../Features/release-and-sync-automation.md)

### 2.2 Interfaces / contracts

- `Codex CLI invocation contract` — source: [CodexExec.cs](../../CodexSharpSDK/Execution/CodexExec.cs); producer: `CodexExec`; consumer: local `codex` binary; rationale: [001-codex-cli-wrapper.md](../ADR/001-codex-cli-wrapper.md)
- `JSONL thread event contract` — source: [ThreadEventParser.cs](../../CodexSharpSDK/Internal/ThreadEventParser.cs); producer: Codex CLI; consumer: `CodexThread`; rationale: [002-protocol-parsing-and-thread-serialization.md](../ADR/002-protocol-parsing-and-thread-serialization.md)

### 2.3 Key classes / types

- `CodexClient` — [CodexClient.cs](../../CodexSharpSDK/Client/CodexClient.cs)
- `CodexThread` — [CodexThread.cs](../../CodexSharpSDK/Client/CodexThread.cs)
- `CodexExec` — [CodexExec.cs](../../CodexSharpSDK/Execution/CodexExec.cs)
- `ThreadEventParser` — [ThreadEventParser.cs](../../CodexSharpSDK/Internal/ThreadEventParser.cs)
- `CodexProtocolConstants` — [CodexProtocolConstants.cs](../../CodexSharpSDK/Internal/CodexProtocolConstants.cs)
- `CodexCliMetadataReader` — [CodexCliMetadataReader.cs](../../CodexSharpSDK/Internal/CodexCliMetadataReader.cs)

## 3) Dependency rules

- Allowed dependencies:
  - `CodexSharpSDK.Tests/*` -> `CodexSharpSDK/*`, `CodexSharpSDK.Extensions.AI/*`, `CodexSharpSDK.Extensions.AgentFramework/*`
  - `CodexSharpSDK.Extensions.AI/*` -> `CodexSharpSDK/*`
  - `CodexSharpSDK.Extensions.AgentFramework/*` -> `CodexSharpSDK.Extensions.AI/*`
  - Public API (`CodexClient`, `CodexThread`) -> internal execution/parsing helpers.
- Forbidden dependencies:
  - No dependency from `CodexSharpSDK/*` to `CodexSharpSDK.Tests/*`.
  - No dependency from `CodexSharpSDK/*` to `CodexSharpSDK.Extensions.AI/*` (adapter is opt-in).
  - No dependency from `CodexSharpSDK/*` or `CodexSharpSDK.Extensions.AI/*` to `CodexSharpSDK.Extensions.AgentFramework/*` (MAF adapter is outermost and opt-in).
  - No runtime dependency on `submodules/openai-codex`; submodule is reference-only.
- Integration style:
  - sync configuration + async process stream consumption (`IAsyncEnumerable<string>`)
  - JSONL event protocol parsing and mapping to strongly-typed C# models.

## 4) Key decisions (ADRs)

- [001-codex-cli-wrapper.md](../ADR/001-codex-cli-wrapper.md) — wrap Codex CLI process as SDK transport.
- [002-protocol-parsing-and-thread-serialization.md](../ADR/002-protocol-parsing-and-thread-serialization.md) — explicit protocol constants and serialized per-thread turn execution.
- [003-microsoft-extensions-ai-integration.md](../ADR/003-microsoft-extensions-ai-integration.md) — IChatClient adapter in separate package.
- [004-microsoft-agent-framework-integration.md](../ADR/004-microsoft-agent-framework-integration.md) — AIAgent adapter layer built on top of the IChatClient package.

## 5) Where to go next

- Features: [docs/Features/](../Features/)
- Decisions: [docs/ADR/](../ADR/)
- Testing: [docs/Testing/strategy.md](../Testing/strategy.md)
- Development setup: [docs/Development/setup.md](../Development/setup.md)
