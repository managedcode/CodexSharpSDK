using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Internal;

namespace ManagedCode.CodexSharpSDK.Models;

public enum CommandExecutionStatus
{
    InProgress,
    Completed,
    Failed,
}

public enum PatchChangeKind
{
    Add,
    Delete,
    Update,
}

public enum PatchApplyStatus
{
    InProgress,
    Completed,
    Failed,
}

public enum McpToolCallStatus
{
    InProgress,
    Completed,
    Failed,
}

public enum CollabToolCallStatus
{
    InProgress,
    Completed,
    Failed,
}

public enum CollabTool
{
    SpawnAgent,
    SendInput,
    Wait,
    CloseAgent,
}

public enum CollabAgentStatus
{
    PendingInit,
    Running,
    Completed,
    Errored,
    Shutdown,
    NotFound,
}

public sealed record FileUpdateChange(string Path, PatchChangeKind Kind);

public sealed record McpToolCallResult(IReadOnlyList<JsonNode> Content, JsonNode? StructuredContent);

public sealed record McpToolCallError(string Message);

public sealed record TodoItem(string Text, bool Completed);

public sealed record CollabAgentState(CollabAgentStatus Status, string? Message);

public abstract record ThreadItem(string Id, string Type);

public sealed record UnknownThreadItem(string Id, string ItemType, JsonNode Payload) : ThreadItem(Id, ItemType);

public sealed record AgentMessageItem(string Id, string Text)
    : ThreadItem(Id, CodexProtocolConstants.ItemTypes.AgentMessage);

public sealed record ReasoningItem(string Id, string Text)
    : ThreadItem(Id, CodexProtocolConstants.ItemTypes.Reasoning);

public sealed record CommandExecutionItem(
    string Id,
    string Command,
    string AggregatedOutput,
    int? ExitCode,
    CommandExecutionStatus Status) : ThreadItem(Id, CodexProtocolConstants.ItemTypes.CommandExecution);

public sealed record FileChangeItem(string Id, IReadOnlyList<FileUpdateChange> Changes, PatchApplyStatus Status)
    : ThreadItem(Id, CodexProtocolConstants.ItemTypes.FileChange);

public sealed record McpToolCallItem(
    string Id,
    string Server,
    string Tool,
    JsonNode? Arguments,
    McpToolCallResult? Result,
    McpToolCallError? Error,
    McpToolCallStatus Status) : ThreadItem(Id, CodexProtocolConstants.ItemTypes.McpToolCall);

public sealed record CollabToolCallItem(
    string Id,
    CollabTool Tool,
    string SenderThreadId,
    IReadOnlyList<string> ReceiverThreadIds,
    string? Prompt,
    IReadOnlyDictionary<string, CollabAgentState> AgentsStates,
    CollabToolCallStatus Status) : ThreadItem(Id, CodexProtocolConstants.ItemTypes.CollabToolCall);

public sealed record WebSearchItem(string Id, string Query)
    : ThreadItem(Id, CodexProtocolConstants.ItemTypes.WebSearch);

public sealed record TodoListItem(string Id, IReadOnlyList<TodoItem> Items)
    : ThreadItem(Id, CodexProtocolConstants.ItemTypes.TodoList);

public sealed record ErrorItem(string Id, string Message)
    : ThreadItem(Id, CodexProtocolConstants.ItemTypes.Error);
