using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ManagedCode.CodexSharpSDK.Configuration;

public sealed record CodexOptions
{
    public static TimeSpan DefaultProcessTerminationTimeout { get; } = TimeSpan.FromSeconds(5);

    public string? CodexExecutablePath { get; init; }

    public string? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public JsonObject? Config { get; init; }

    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    public TimeSpan ProcessTerminationTimeout { get; init; } = DefaultProcessTerminationTimeout;

    public ILogger? Logger { get; init; }
}
