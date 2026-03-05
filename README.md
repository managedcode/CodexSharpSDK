# ManagedCode.CodexSharp

[![CI](https://github.com/managedcode/CodexSharp/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/managedcode/CodexSharp/actions/workflows/ci.yml)
[![Release](https://github.com/managedcode/CodexSharp/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/managedcode/CodexSharp/actions/workflows/release.yml)
[![CodeQL](https://github.com/managedcode/CodexSharp/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/managedcode/CodexSharp/actions/workflows/codeql.yml)
[![NuGet](https://img.shields.io/nuget/v/ManagedCode.CodexSharp.svg)](https://www.nuget.org/packages/ManagedCode.CodexSharp)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

`ManagedCode.CodexSharp` is an open-source .NET SDK for driving the Codex CLI from C#.

It ports the TypeScript SDK from `openai/codex` to `.NET 10 / C# 14` with:
- thread-based API (`start` / `resume`)
- streamed JSONL events
- structured output schema support
- image attachments
- `--config` flattening to TOML
- NativeAOT-friendly implementation and tests on TUnit

## Installation

```bash
dotnet add package ManagedCode.CodexSharp
```

## Quickstart

```csharp
using ManagedCode.CodexSharp;

await using var client = new CodexClient(new CodexClientOptions
{
    CodexOptions = new CodexOptions
    {
        // optional, otherwise uses local npm binary lookup and then falls back to `codex` from PATH
        CodexPathOverride = "codex",
    },
    AutoStart = true,
});

var thread = client.StartThread(new ThreadOptions
{
    Model = "gpt-5",
    SandboxMode = SandboxMode.WorkspaceWrite,
});

var turn = await thread.RunAsync("Diagnose failing tests and propose a fix");

Console.WriteLine(turn.FinalResponse);
Console.WriteLine($"Items: {turn.Items.Count}");
```

## Client Lifecycle and Thread Safety

- `CodexClient` is safe for concurrent use from multiple threads.
- `StartAsync()` is idempotent and guarded.
- `StopAsync()` cleanly disconnects client state.
- `Dispose()/DisposeAsync()` transition client to `Disposed`.
- A single `CodexThread` instance serializes turns (`RunAsync` and `RunStreamedAsync`) to prevent race conditions in shared conversation state.

## Streaming

```csharp
var streamed = await thread.RunStreamedAsync("Implement the fix");

await foreach (var evt in streamed.Events)
{
    switch (evt)
    {
        case ItemCompletedEvent completed:
            Console.WriteLine($"Item: {completed.Item.Type}");
            break;
        case TurnCompletedEvent done:
            Console.WriteLine($"Output tokens: {done.Usage.OutputTokens}");
            break;
    }
}
```

## Structured Output

```csharp
using System.Text.Json.Nodes;

var schema = new JsonObject
{
    ["type"] = "object",
    ["properties"] = new JsonObject
    {
        ["summary"] = new JsonObject { ["type"] = "string" },
        ["status"] = new JsonObject { ["type"] = "string" },
    },
    ["required"] = new JsonArray("summary", "status"),
    ["additionalProperties"] = false,
};

var result = await thread.RunAsync(
    "Summarize repository status",
    new TurnOptions { OutputSchema = schema });
```

## Images + Text Input

```csharp
var result = await thread.RunAsync(
[
    new TextInput("Describe these images"),
    new LocalImageInput("./ui.png"),
    new LocalImageInput("./diagram.jpg"),
]);
```

## Resume an Existing CodexThread

```csharp
var resumed = client.ResumeThread("thread_123");
await resumed.RunAsync("Continue from previous plan");
```

## Build and Test

```bash
dotnet build CodexSharp.slnx -c Release -warnaserror
dotnet test --solution CodexSharp.slnx -c Release
```

## AOT Smoke Check

```bash
dotnet publish samples/CodexSharp.AotSmoke/CodexSharp.AotSmoke.csproj \
  -c Release -r linux-x64 -p:PublishAot=true --self-contained true
```

## Porting References

- TypeScript source of truth: [`submodules/openai-codex/sdk/typescript`](submodules/openai-codex/sdk/typescript)
- Detailed migration checklist: [`PORTING_TODO.md`](PORTING_TODO.md)
- Dotnet style reference: [github/copilot-sdk](https://github.com/github/copilot-sdk/tree/main/dotnet)
- CI/release style reference: [managedcode/Storage](https://github.com/managedcode/Storage)
