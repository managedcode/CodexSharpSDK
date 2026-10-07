using System.Collections.Immutable;

namespace ManagedCode.CodexSharpSDK.Models;

/// <summary>Describes a CLI executable and literal arguments that must precede caller arguments.</summary>
public sealed record CliLaunchCommand
{
    public CliLaunchCommand(string executablePath, ImmutableArray<string> prefixArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("The CLI executable path must be an existing absolute file path.", nameof(executablePath));
        }

        var absoluteExecutablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(absoluteExecutablePath))
        {
            throw new ArgumentException("The CLI executable path must be an existing absolute file path.", nameof(executablePath));
        }

        ExecutablePath = absoluteExecutablePath;
        PrefixArguments = prefixArguments.IsDefault ? ImmutableArray<string>.Empty : prefixArguments;
    }

    /// <summary>Gets the absolute executable path.</summary>
    public string ExecutablePath { get; }

    /// <summary>Gets literal arguments to append before the CLI caller arguments.</summary>
    public ImmutableArray<string> PrefixArguments { get; }
}
