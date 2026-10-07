using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Internal;
using Microsoft.Extensions.Logging;

namespace ManagedCode.CodexSharpSDK.Configuration;

public sealed record CodexOptions
{
    private const string PathEnvironmentVariable = "PATH";
    public static TimeSpan DefaultProcessTerminationTimeout { get; } = TimeSpan.FromSeconds(5);

    public static TimeSpan DefaultCliMetadataProbeTimeout { get; } = TimeSpan.FromSeconds(10);

    public static TimeSpan DefaultCliMetadataProbeLeaseTimeout { get; } = TimeSpan.FromMinutes(2);

    public const int DefaultCliMetadataMaximumOutputCharacters = 65536;

    public const int DefaultCliMetadataMaximumFileCharacters = 1048576;

    public const int DefaultMaximumProcessOutputCharacters = 1048576;

    public string? CodexExecutablePath { get; init; }

    /// <summary>Uses a previously verified SDK launch descriptor, such as the result of CLI installation.</summary>
    public ManagedCode.CodexSharpSDK.Models.CliLaunchCommand? LaunchCommand { get; init; }

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

    /// <summary>Resolves the installed CLI to an executable and safe literal prefix arguments.</summary>
    public ManagedCode.CodexSharpSDK.Models.CliLaunchCommand GetCliLaunchCommand()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CliMetadataMaximumFileCharacters);
        return LaunchCommand ?? CliLaunchCommandResolver.Resolve(
            CodexExecutablePath, GetEffectivePath(), CliMetadataMaximumFileCharacters);
    }

    internal string? GetEffectivePath()
    {
        if (EnvironmentVariables is not null)
        {
            foreach (var (key, value) in EnvironmentVariables)
            {
                if (string.Equals(key, PathEnvironmentVariable, OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
                {
                    return value;
                }
            }
        }

        return (InheritEnvironmentVariables ?? EnvironmentVariables is null)
            ? Environment.GetEnvironmentVariable(PathEnvironmentVariable)
            : null;
    }
}
