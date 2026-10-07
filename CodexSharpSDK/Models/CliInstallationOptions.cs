namespace ManagedCode.CodexSharpSDK.Models;

/// <summary>Selects the package manager used by the SDK-owned isolated CLI installation.</summary>
public enum CliPackageManager
{
    Npm,
    Bun
}

/// <summary>Required local process and filesystem configuration for installing the pinned CLI.</summary>
public sealed record CliInstallationOptions
{
    public required CliPackageManager PackageManager { get; init; }

    /// <summary>Absolute Bun executable path, or absolute Node.js executable path when using npm.</summary>
    public required string PackageManagerExecutablePath { get; init; }

    /// <summary>Absolute npm-cli.js path; required only when PackageManager is Npm.</summary>
    public string? NpmCliScriptPath { get; init; }

    /// <summary>Explicit allowlisted environment for the package manager; provider credentials are rejected.</summary>
    public required IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }

    public required TimeSpan InstallTimeout { get; init; }

    public required TimeSpan ProcessTerminationTimeout { get; init; }

    public required TimeSpan InstallationLockTimeout { get; init; }

    public required int MaximumOutputCharacters { get; init; }

    public required int MaximumMetadataFileCharacters { get; init; }
}
