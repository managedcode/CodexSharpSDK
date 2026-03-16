# ManagedCode.CodexSharpSDK

[![CI](https://github.com/managedcode/CodexSharpSDK/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/managedcode/CodexSharpSDK/actions/workflows/ci.yml)
[![Release](https://github.com/managedcode/CodexSharpSDK/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/managedcode/CodexSharpSDK/actions/workflows/release.yml)
[![CodeQL](https://github.com/managedcode/CodexSharpSDK/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/managedcode/CodexSharpSDK/actions/workflows/codeql.yml)
[![NuGet](https://img.shields.io/nuget/v/ManagedCode.CodexSharpSDK.svg)](https://www.nuget.org/packages/ManagedCode.CodexSharpSDK)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/managedcode/CodexSharpSDK/blob/main/LICENSE)

`ManagedCode.CodexSharpSDK` is an open-source .NET SDK for driving the Codex CLI from C#.

It is a CLI-first `.NET 10 / C# 14` SDK aligned with real `codex` runtime behavior, with:
- thread-based API (`start` / `resume`)
- streamed JSONL events
- structured output schema support
- image attachments
- `--config` flattening to TOML
- NativeAOT-friendly implementation and tests on TUnit

All consumer usage examples are documented in this README; this repository intentionally does not keep standalone sample projects.

## Installation

```bash
dotnet add package ManagedCode.CodexSharpSDK
```

## Prerequisites

Before using this SDK, you must have:
- `codex` CLI installed and available in `PATH`
- an already authenticated Codex session (`codex login`)

Quick check:

```bash
codex --version
codex login
```

## Quickstart

```csharp
using ManagedCode.CodexSharpSDK;

using var client = new CodexClient();

var thread = client.StartThread(new ThreadOptions
{
    Model = CodexModels.Gpt54,
    ModelReasoningEffort = ModelReasoningEffort.Medium,
});

var turn = await thread.RunAsync("Diagnose failing tests and propose a fix");

Console.WriteLine(turn.FinalResponse);
Console.WriteLine($"Items: {turn.Items.Count}");
```

`AutoStart` is enabled by default, so `StartThread()` works immediately.

## Advanced Configuration (Optional)

```csharp
using var client = new CodexClient(new CodexClientOptions
{
    CodexOptions = new CodexOptions
    {
        // Override only when `codex` is not discoverable via npm/PATH.
        CodexExecutablePath = "/custom/path/to/codex",
    },
});

var thread = client.StartThread(new ThreadOptions
{
    Model = CodexModels.Gpt54,
    ModelReasoningEffort = ModelReasoningEffort.High,
    SandboxMode = SandboxMode.WorkspaceWrite,
});
```

## Extended CLI Options

`ThreadOptions` supports full `codex exec` control.

```csharp
var thread = client.StartThread(new ThreadOptions
{
    Profile = "strict",
    UseOss = true,
    LocalProvider = OssProvider.LmStudio,
    FullAuto = true,
    Ephemeral = true,
    Color = ExecOutputColor.Auto,
    EnabledFeatures = ["multi_agent"],
    DisabledFeatures = ["steer"],
    AdditionalCliArguments = ["--some-future-flag", "custom-value"],
});
```

## Codex CLI Metadata

```csharp
using var client = new CodexClient();

var metadata = client.GetCliMetadata();
Console.WriteLine($"Installed codex-cli: {metadata.InstalledVersion}");
Console.WriteLine($"Default model: {metadata.DefaultModel ?? "(not set)"}");

foreach (var model in metadata.Models.Where(model => model.IsListed))
{
    Console.WriteLine(model.Slug);
}
```

`GetCliMetadata()` reads:
- installed CLI version from `codex --version`
- default model from `~/.codex/config.toml`
- model catalog from `~/.codex/models_cache.json`

```csharp
var update = client.GetCliUpdateStatus();
if (update.IsUpdateAvailable)
{
    Console.WriteLine(update.UpdateMessage);
    Console.WriteLine(update.UpdateCommand);
}
```

`GetCliUpdateStatus()` compares installed CLI version with latest published `@openai/codex` npm version and returns an update command matched to your install context (`bun` or `npm`).

When thread-level web search options are omitted, SDK does not emit a `web_search` override and leaves your existing CLI/config value as-is.

## Client Lifecycle and Thread Safety

- `CodexClient` is safe for concurrent use from multiple threads.
- `StartAsync()` is idempotent and guarded.
- `StopAsync()` cleanly disconnects client state.
- `Dispose()` transitions client to `Disposed`.
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
using System.Text.Json.Serialization;

public sealed record RepositorySummary(string Summary, string Status);

[JsonSerializable(typeof(RepositorySummary))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

var schema = StructuredOutputSchema.Map<RepositorySummary>(
    additionalProperties: false,
    (response => response.Summary, StructuredOutputSchema.PlainText()),
    (response => response.Status, StructuredOutputSchema.PlainText()));

var result = await thread.RunAsync<RepositorySummary>(
    "Summarize repository status",
    schema,
    AppJsonContext.Default.RepositorySummary);
Console.WriteLine(result.TypedResponse.Status);
Console.WriteLine(result.TypedResponse.Summary);
```

For advanced options (for example cancellation), use the `TurnOptions` overload:

```csharp
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

var result = await thread.RunAsync<RepositorySummary>(
    "Summarize repository status",
    AppJsonContext.Default.RepositorySummary,
    new TurnOptions
    {
        OutputSchema = schema,
        CancellationToken = cancellation.Token,
    });
```

`RunAsync<TResponse>` always requires `OutputSchema` (direct parameter or `TurnOptions.OutputSchema`).
For AOT/trimming-safe typed deserialization, pass `JsonTypeInfo<TResponse>` from a source-generated context.
Overloads without `JsonTypeInfo<TResponse>` are explicitly marked with `RequiresDynamicCode` and `RequiresUnreferencedCode`.

## Diagnostics Logging (Optional)

```csharp
using Microsoft.Extensions.Logging;

public sealed class ConsoleCodexLogger : ILogger
{
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Console.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        if (exception is not null)
        {
            Console.WriteLine(exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}

using var client = new CodexClient(new CodexOptions
{
    Logger = new ConsoleCodexLogger(),
});
```

## Images + Text Input

```csharp
using var imageStream = File.OpenRead("./photo.png");

var result = await thread.RunAsync(
[
    new TextInput("Describe these images"),
    new LocalImageInput("./ui.png"),
    new LocalImageInput(new FileInfo("./diagram.jpg")),
    new LocalImageInput(imageStream, "photo.png"),
]);
```

## Resume an Existing CodexThread

```csharp
var resumed = client.ResumeThread("thread_123");
await resumed.RunAsync("Continue from previous plan");
```

## Microsoft.Extensions.AI Integration

An optional adapter package lets you use CodexSharpSDK through the standard `IChatClient` interface from `Microsoft.Extensions.AI`.

```bash
dotnet add package ManagedCode.CodexSharpSDK.Extensions.AI
```

### Basic usage

```csharp
using Microsoft.Extensions.AI;
using ManagedCode.CodexSharpSDK.Extensions.AI;

IChatClient client = new CodexChatClient(new CodexChatClientOptions
{
    DefaultModel = CodexModels.Gpt54,
});

var response = await client.GetResponseAsync("Diagnose failing tests and propose a fix");
Console.WriteLine(response.Text);
```

### DI registration

```csharp
using ManagedCode.CodexSharpSDK.Extensions.AI.Extensions;

builder.Services.AddCodexChatClient(options =>
{
    options.DefaultModel = CodexModels.Gpt54;
});

// Then inject IChatClient anywhere:
app.MapGet("/ask", async (IChatClient client) =>
{
    var response = await client.GetResponseAsync("Summarize the repo");
    return response.Text;
});
```

### Resolve `IChatClient` from `IServiceProvider`

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ManagedCode.CodexSharpSDK.Models;
using ManagedCode.CodexSharpSDK.Extensions.AI.Extensions;

var services = new ServiceCollection();
services.AddCodexChatClient(options =>
{
    options.DefaultModel = CodexModels.Gpt54;
});

using var provider = services.BuildServiceProvider();
var chatClient = provider.GetRequiredService<IChatClient>();
```

Keyed registration is also supported:

```csharp
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ManagedCode.CodexSharpSDK.Models;
using ManagedCode.CodexSharpSDK.Extensions.AI.Extensions;

var services = new ServiceCollection();
services.AddKeyedCodexChatClient("codex-main", options =>
{
    options.DefaultModel = CodexModels.Gpt54;
});

using var provider = services.BuildServiceProvider();
var keyedChatClient = provider.GetRequiredKeyedService<IChatClient>("codex-main");
```

### Streaming

```csharp
await foreach (var update in client.GetStreamingResponseAsync("Implement the fix"))
{
    Console.Write(update.Text);
}
```

### Codex-specific options via ChatOptions

```csharp
var options = new ChatOptions
{
    ModelId = CodexModels.Gpt54,
    AdditionalProperties = new AdditionalPropertiesDictionary
    {
        ["codex:sandbox_mode"] = "workspace-write",
        ["codex:reasoning_effort"] = "high",
    },
};

var response = await client.GetResponseAsync("Refactor the auth module", options);
```

### Rich content types

Codex-specific output items (commands, file changes, MCP tool calls, web searches) are preserved as typed `AIContent` subclasses:

```csharp
foreach (var content in response.Messages.SelectMany(m => m.Contents))
{
    switch (content)
    {
        case CommandExecutionContent cmd:
            Console.WriteLine($"Command: {cmd.Command} (exit {cmd.ExitCode})");
            break;
        case FileChangeContent file:
            Console.WriteLine($"File changes: {file.Changes.Count}");
            break;
    }
}
```

See [docs/Features/meai-integration.md](https://github.com/managedcode/CodexSharpSDK/blob/main/docs/Features/meai-integration.md) and [ADR 003](https://github.com/managedcode/CodexSharpSDK/blob/main/docs/ADR/003-microsoft-extensions-ai-integration.md) for full details.

## Microsoft Agent Framework Integration

An optional adapter package lets you use CodexSharpSDK with Microsoft Agent Framework `AIAgent`.

```bash
dotnet add package ManagedCode.CodexSharpSDK.Extensions.AgentFramework --prerelease
```

This package currently ships as a prerelease because it depends on `Microsoft.Agents.AI` `1.0.0-rc4`.

### Basic usage

```csharp
using ManagedCode.CodexSharpSDK.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

IChatClient chatClient = new CodexChatClient();

AIAgent agent = chatClient.AsAIAgent(
    name: "CodexAssistant",
    instructions: "You are a helpful coding assistant.");

AgentResponse response = await agent.RunAsync("Summarize the repository");
Console.WriteLine(response);
```

### DI registration

```csharp
using ManagedCode.CodexSharpSDK.Extensions.AgentFramework.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

builder.Services.AddCodexAIAgent(
    configureAgent: options =>
    {
        options.Name = "CodexAssistant";
        options.ChatOptions = new ChatOptions
        {
            Instructions = "You are a helpful coding assistant."
        };
    });

app.MapGet("/agent", async (AIAgent agent) =>
{
    var response = await agent.RunAsync("Summarize the repository");
    return response.ToString();
});
```

### Keyed DI registration

```csharp
using ManagedCode.CodexSharpSDK.Extensions.AgentFramework.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddKeyedCodexAIAgent(
    "codex-main",
    configureAgent: options =>
    {
        options.Name = "CodexAssistant";
        options.ChatOptions = new ChatOptions
        {
            Instructions = "You are a helpful coding assistant."
        };
    });

using var provider = services.BuildServiceProvider();
var keyedAgent = provider.GetRequiredKeyedService<AIAgent>("codex-main");
```

This package builds on the existing `IChatClient` adapter, so the canonical MAF path remains `IChatClient.AsAIAgent(...)`; the new package adds a supported Codex-specific package boundary and DI convenience methods.

See [docs/Features/agent-framework-integration.md](https://github.com/managedcode/CodexSharpSDK/blob/main/docs/Features/agent-framework-integration.md) and [ADR 004](https://github.com/managedcode/CodexSharpSDK/blob/main/docs/ADR/004-microsoft-agent-framework-integration.md) for full details.

## Build and Test

```bash
dotnet build ManagedCode.CodexSharpSDK.slnx -c Release -warnaserror
dotnet test --solution ManagedCode.CodexSharpSDK.slnx -c Release
```
