namespace ManagedCode.CodexSharpSDK.Models;

public enum CliInstallationStage
{
    PackageManagerStarted,
    OutputObserved,
    Verifying,
    Installed
}

/// <summary>Safe progress from an SDK-owned CLI installation; process output content is never exposed.</summary>
public sealed record CliInstallationUpdate(
    CliInstallationStage Stage,
    int StandardOutputCharactersObserved,
    int StandardErrorCharactersObserved,
    CliInstallationResult? Result = null);
