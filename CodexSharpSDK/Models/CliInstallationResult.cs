namespace ManagedCode.CodexSharpSDK.Models;

/// <summary>Describes the verified pinned CLI installation and its safe launch command.</summary>
public sealed record CliInstallationResult(
    string InstalledVersion,
    string TargetVersion,
    string InstallationRootPath,
    CliLaunchCommand LaunchCommand);
