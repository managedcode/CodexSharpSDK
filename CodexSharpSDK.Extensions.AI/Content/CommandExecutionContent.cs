using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Content;

public sealed class CommandExecutionContent : AIContent
{
    public required string Command { get; init; }
    public required string AggregatedOutput { get; init; }
    public int? ExitCode { get; init; }
    public required CommandExecutionStatus Status { get; init; }
}
