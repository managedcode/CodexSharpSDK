# Codex TypeScript -> C# Porting TODO

This document tracks a full file-by-file and function-by-function migration from:
- Source: `openai/codex/sdk/typescript`
- Target: `ManagedCode.CodexSharp` (`.NET 10`, `C# 14`, NuGet package)

## Migration Rules

- Keep runtime behavior compatible with TypeScript SDK contracts.
- Prefer explicit parsing and serialization (AOT-friendly).
- Cover each behavior with TUnit tests.
- Preserve CLI argument order where TypeScript has order-sensitive behavior.

## Source Inventory

TypeScript SDK files reviewed:
- `src/index.ts`
- `src/codex.ts`
- `src/thread.ts`
- `src/exec.ts`
- `src/codexOptions.ts`
- `src/threadOptions.ts`
- `src/turnOptions.ts`
- `src/events.ts`
- `src/items.ts`
- `src/outputSchemaFile.ts`

TypeScript test files reviewed:
- `tests/run.test.ts`
- `tests/runStreamed.test.ts`
- `tests/exec.test.ts`
- `tests/abort.test.ts`

## Porting Matrix (File-by-File / Function-by-Function)

### `src/index.ts`

Status: `DONE`

- [x] Export surface for thread events -> `src/CodexSharp/Events.cs`
- [x] Export surface for thread items -> `src/CodexSharp/Items.cs`
- [x] Export `CodexThread` -> `src/CodexSharp/CodexThread.cs`
- [x] Export run result types -> `src/CodexSharp/RunResult.cs`
- [x] Export client entry point -> `src/CodexSharp/CodexClient.cs`
- [x] Export options -> `src/CodexSharp/CodexOptions.cs`, `src/CodexSharp/ThreadOptions.cs`, `src/CodexSharp/TurnOptions.cs`

### `src/codex.ts`

Status: `DONE`

- [x] `constructor(options)` -> `CodexClient(CodexOptions)` / `CodexClient(CodexClientOptions?)`
- [x] `startThread(options)` -> `StartThread(ThreadOptions?)`
- [x] `resumeThread(id, options)` -> `ResumeThread(string, ThreadOptions?)`

Tests:
- [x] `tests/CodexSharp.Tests/CodexClientTests.cs::StartThread_AutoStartEnabledStartsImplicitly`
- [x] `tests/CodexSharp.Tests/CodexClientTests.cs::ResumeThread_CreatesThreadWithProvidedId`
- [x] `tests/CodexSharp.Tests/CodexClientTests.cs::ResumeThread_ThrowsForInvalidId`

### `src/thread.ts`

Status: `DONE`

- [x] Type `Turn` -> `RunResult`
- [x] Type `RunResult` alias -> `RunResult`
- [x] Type `StreamedTurn` -> `RunStreamedResult`
- [x] Type `RunStreamedResult` alias -> `RunStreamedResult`
- [x] Type `UserInput` union (`text`, `local_image`) -> `TextInput`, `LocalImageInput`
- [x] Type `Input` (`string | UserInput[]`) -> method overloads (`RunAsync(string)` / `RunAsync(IReadOnlyList<UserInput>)`)
- [x] `id` getter -> `CodexThread.Id`
- [x] `runStreamed(input, turnOptions)` -> `RunStreamedAsync(...)`
- [x] `runStreamedInternal(...)` -> `RunStreamedInternalAsync(...)`
- [x] `run(input, turnOptions)` -> `RunAsync(...)`
- [x] `normalizeInput(input)` -> internal normalized input path in `CodexThread`

Behavior parity:
- [x] update thread id on `thread.started`
- [x] capture latest assistant message as `FinalResponse`
- [x] collect completed items only
- [x] capture usage from `turn.completed`
- [x] throw on `turn.failed`
- [x] parse-failure wraps with original line context
- [x] schema temp file cleanup in success/failure paths

Tests:
- [x] `RunAsync_ReturnsCompletedTurnAndUpdatesThreadId`
- [x] `RunAsync_WithStructuredInput_ForwardsPromptAndImages`
- [x] `RunAsync_SecondTurnUsesResumeArgument`
- [x] `RunAsync_ThrowsThreadRunExceptionOnTurnFailure`
- [x] `RunStreamedAsync_YieldsAllEvents`
- [x] `RunAsync_ThrowsWhenEventIsNotJson`
- [x] `RunAsync_HonorsCancellationToken`

### `src/exec.ts`

Status: `DONE`

- [x] `CodexExec` class -> `src/CodexSharp/CodexExec.cs`
- [x] `CodexExecArgs` type -> `src/CodexSharp/CodexExecArgs.cs`
- [x] `run(args)` -> `RunAsync(CodexExecArgs)`
- [x] `serializeConfigOverrides(...)` -> `TomlConfigSerializer.Serialize(...)`
- [x] `flattenConfigOverrides(...)` -> `TomlConfigSerializer.Flatten(...)`
- [x] `toTomlValue(...)` -> `TomlConfigSerializer.ToTomlValue(...)`
- [x] `formatTomlKey(...)` -> `TomlConfigSerializer.FormatTomlKey(...)`
- [x] `isPlainObject(...)` semantic mapping -> JSON object type checks
- [x] `findCodexPath()` -> `CodexCliLocator.FindCodexPath(...)`

CLI flag parity checklist:
- [x] `exec --experimental-json`
- [x] repeated `--config` from global config overrides
- [x] `--model`
- [x] `--sandbox`
- [x] `--cd`
- [x] repeated `--add-dir`
- [x] `--skip-git-repo-check`
- [x] `--output-schema`
- [x] `--config model_reasoning_effort=...`
- [x] `--config sandbox_workspace_write.network_access=...`
- [x] `--config web_search=...` from mode or legacy flag
- [x] `--config approval_policy=...`
- [x] `resume <threadId>` before `--image`
- [x] repeated `--image`

Environment parity checklist:
- [x] full environment override support
- [x] inherited environment when no override
- [x] inject `CODEX_INTERNAL_ORIGINATOR_OVERRIDE`
- [x] inject `OPENAI_BASE_URL`
- [x] inject `CODEX_API_KEY`

Tests:
- [x] `RunAsync_BuildsCommandLineWithExpectedOrder`
- [x] `RunAsync_UsesWebSearchEnabledWhenModeMissing`
- [x] `RunAsync_WebSearchModeOverridesLegacyFlag`
- [x] `RunAsync_UsesProvidedEnvironmentWithoutLeakingProcessEnvironment`
- [x] `RunAsync_InheritsEnvironmentWhenOverrideMissing`
- [x] `RunAsync_ThrowsWhenConfigContainsEmptyKey`
- [x] `TomlConfigSerializerTests::*`

### `src/codexOptions.ts`

Status: `DONE`

- [x] `codexPathOverride` -> `CodexOptions.CodexPathOverride`
- [x] `baseUrl` -> `CodexOptions.BaseUrl`
- [x] `apiKey` -> `CodexOptions.ApiKey`
- [x] `config` object -> `CodexOptions.Config (JsonObject)`
- [x] `env` dictionary -> `CodexOptions.EnvironmentVariables`

### `src/threadOptions.ts`

Status: `DONE`

- [x] `ApprovalMode` union -> `ApprovalMode enum`
- [x] `SandboxMode` union -> `SandboxMode enum`
- [x] `ModelReasoningEffort` union -> `ModelReasoningEffort enum`
- [x] `WebSearchMode` union -> `WebSearchMode enum`
- [x] `ThreadOptions` object -> `ThreadOptions record`

### `src/turnOptions.ts`

Status: `DONE`

- [x] `outputSchema` -> `TurnOptions.OutputSchema`
- [x] `signal` -> `TurnOptions.CancellationToken`

### `src/events.ts`

Status: `DONE`

- [x] `ThreadStartedEvent`
- [x] `TurnStartedEvent`
- [x] `Usage`
- [x] `TurnCompletedEvent`
- [x] `TurnFailedEvent`
- [x] `ItemStartedEvent`
- [x] `ItemUpdatedEvent`
- [x] `ItemCompletedEvent`
- [x] `ThreadError`
- [x] `ThreadErrorEvent`
- [x] Top-level union equivalent via base `ThreadEvent`

Tests:
- [x] `ThreadEventParserTests::Parse_RecognizesAllTopLevelEventKinds`

### `src/items.ts`

Status: `DONE`

- [x] `CommandExecutionStatus`
- [x] `CommandExecutionItem`
- [x] `PatchChangeKind`
- [x] `FileUpdateChange`
- [x] `PatchApplyStatus`
- [x] `FileChangeItem`
- [x] `McpToolCallStatus`
- [x] `McpToolCallItem`
- [x] `AgentMessageItem`
- [x] `ReasoningItem`
- [x] `WebSearchItem`
- [x] `ErrorItem`
- [x] `TodoItem`
- [x] `TodoListItem`
- [x] union equivalent via base `ThreadItem`

Tests:
- [x] `ThreadEventParserTests::Parse_RecognizesAllItemKinds`
- [x] `ThreadEventParserTests::Parse_ThrowsForUnsupportedItemStatus`

### `src/outputSchemaFile.ts`

Status: `DONE`

- [x] `createOutputSchemaFile(schema)` -> `OutputSchemaFile.CreateAsync(schema, ct)`
- [x] no-op handle when schema absent
- [x] temp dir + `schema.json` creation
- [x] cleanup on success path
- [x] cleanup on failure path

Tests:
- [x] `OutputSchemaFileTests::CreateAsync_ReturnsEmptyHandleWhenSchemaMissing`
- [x] `OutputSchemaFileTests::CreateAsync_WritesAndCleansSchemaFile`

## TypeScript Test Contract Mapping

### `tests/run.test.ts`
- [x] successful run returns completed items + usage
- [x] second run resumes same thread
- [x] thread options forwarded as CLI args
- [x] model reasoning effort forwarded
- [x] network access config forwarded
- [x] web search flags/mode precedence
- [x] approval policy forwarding
- [x] config override flattening
- [x] thread options override global config order
- [x] custom env behavior
- [x] additional directories as repeated flags
- [x] output schema temp file forwarding + cleanup
- [x] structured text segments combine with blank line
- [x] images forwarded as repeated `--image`

### `tests/runStreamed.test.ts`
- [x] streamed events consumed in order
- [x] second streamed run continues thread
- [x] resume by id for streamed flow
- [x] output schema in streamed flow

### `tests/exec.test.ts`
- [x] process failure surfaces non-zero exit
- [x] `resume` appears before `--image`

### `tests/abort.test.ts`
- [x] cancellation token path on `RunAsync`
- [x] cancellation token path on streamed flow

## Repository / Delivery TODO

### Packaging
- [x] NuGet metadata in `Directory.Build.props`
- [x] package project file with SourceLink and reproducible builds
- [x] README packed into nupkg
- [x] symbol package (`snupkg`)

### Testing
- [x] TUnit test project
- [x] behavior-focused unit tests for all core branches
- [x] parser coverage for all event/item kinds
- [x] cancellation, env, config, schema cleanup coverage
- [x] real process integration tests via `DefaultCodexProcessRunner` with executable harness

### CI/CD
- [x] CI workflow: restore/build/test
- [x] Release workflow: pack/publish NuGet/create GitHub release
- [x] CodeQL workflow
- [x] NativeAOT smoke publish in CI (`samples/CodexSharp.AotSmoke`)

### Documentation
- [x] README with quickstart + streaming + structured output + images
- [x] Migration TODO (this file)

## Remaining Backlog (Phase 2)

- [ ] Real integration tests against actual Codex CLI binary in CI matrix (Linux/macOS/Windows).
- [ ] Public samples package (`samples/basic`, `samples/streaming`, `samples/structured-output`).
- [ ] Optional logging abstractions for process diagnostics.
- [ ] Optional strongly-typed JSON schema builder helpers for C# callers.
- [ ] Performance profiling for large event streams.
- [ ] Semantic versioning and release notes automation improvements.
