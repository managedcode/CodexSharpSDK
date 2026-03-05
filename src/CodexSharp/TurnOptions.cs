using System.Text.Json.Nodes;

namespace ManagedCode.CodexSharp;

public sealed record TurnOptions
{
    public JsonObject? OutputSchema { get; init; }

    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
}
