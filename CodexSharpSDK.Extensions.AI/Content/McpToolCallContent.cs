using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Content;

public sealed class McpToolCallContent : AIContent
{
    public required string Server { get; init; }
    public required string Tool { get; init; }
    public JsonNode? Arguments { get; init; }
    public McpToolCallResult? Result { get; init; }
    public McpToolCallError? Error { get; init; }
    public required McpToolCallStatus Status { get; init; }
}
