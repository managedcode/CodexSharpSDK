using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Content;

public sealed class CollabToolCallContent : AIContent
{
    public required CollabTool Tool { get; init; }
    public required string SenderThreadId { get; init; }
    public required IReadOnlyList<string> ReceiverThreadIds { get; init; }
    public required IReadOnlyDictionary<string, CollabAgentState> AgentsStates { get; init; }
    public required CollabToolCallStatus Status { get; init; }
}
