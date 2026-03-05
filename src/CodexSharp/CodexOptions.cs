using System.Text.Json.Nodes;

namespace ManagedCode.CodexSharp;

public sealed record CodexOptions
{
    public string? CodexPathOverride { get; init; }

    public string? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public JsonObject? Config { get; init; }

    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }
}
