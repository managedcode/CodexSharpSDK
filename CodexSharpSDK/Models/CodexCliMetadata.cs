namespace ManagedCode.CodexSharpSDK.Models;

public sealed record CodexCliMetadata(
    string InstalledVersion,
    string? DefaultModel,
    IReadOnlyList<CodexModelMetadata> Models);

public sealed record CodexCliUpdateStatus(
    string InstalledVersion,
    string? LatestVersion,
    bool IsUpdateAvailable,
    string? UpdateMessage,
    string? UpdateCommand);

public sealed record CodexModelMetadata(
    string Slug,
    string DisplayName,
    string? Description,
    bool IsListed,
    bool IsApiSupported,
    IReadOnlyList<string> SupportedReasoningEfforts);
