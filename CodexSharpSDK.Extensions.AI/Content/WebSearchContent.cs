using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Content;

public sealed class WebSearchContent : AIContent
{
    public required string Query { get; init; }
}
