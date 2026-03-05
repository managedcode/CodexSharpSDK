using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Content;

public sealed class FileChangeContent : AIContent
{
    public required IReadOnlyList<FileUpdateChange> Changes { get; init; }
    public required PatchApplyStatus Status { get; init; }
}
