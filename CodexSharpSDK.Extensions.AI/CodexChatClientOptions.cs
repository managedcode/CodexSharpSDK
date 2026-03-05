using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Configuration;

namespace ManagedCode.CodexSharpSDK.Extensions.AI;

public sealed record CodexChatClientOptions
{
    public CodexOptions? CodexOptions { get; init; }
    public string? DefaultModel { get; init; }
    public ThreadOptions? DefaultThreadOptions { get; init; }
}
