using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace ManagedCode.CodexSharpSDK.Configuration;

public sealed record CodexOptions
{
    public static TimeSpan DefaultProcessTerminationTimeout { get; } = TimeSpan.FromSeconds(5);

    public static TimeSpan DefaultCliMetadataProbeTimeout { get; } = TimeSpan.FromSeconds(10);

    public static TimeSpan DefaultCliMetadataProbeLeaseTimeout { get; } = TimeSpan.FromMinutes(2);

    public const int DefaultCliMetadataMaximumOutputCharacters = 65536;

    public const int DefaultCliMetadataMaximumFileCharacters = 1048576;

    public const int DefaultMaximumProcessOutputCharacters = 1048576;

    public string? CodexExecutablePath { get; init; }

    public string? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public JsonObject? Config { get; init; }

    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; init; }

    public bool? InheritEnvironmentVariables { get; init; }

    public TimeSpan CliMetadataProbeTimeout { get; init; } = DefaultCliMetadataProbeTimeout;

    public TimeSpan CliMetadataProbeLeaseTimeout { get; init; } = DefaultCliMetadataProbeLeaseTimeout;

    public int CliMetadataMaximumOutputCharacters { get; init; } = DefaultCliMetadataMaximumOutputCharacters;

    public int CliMetadataMaximumFileCharacters { get; init; } = DefaultCliMetadataMaximumFileCharacters;

    public TimeSpan ProcessTerminationTimeout { get; init; } = DefaultProcessTerminationTimeout;

    public int MaximumProcessOutputCharacters { get; init; } = DefaultMaximumProcessOutputCharacters;

    public ILogger? Logger { get; init; }
}
