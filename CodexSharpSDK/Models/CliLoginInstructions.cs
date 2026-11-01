namespace ManagedCode.CodexSharpSDK.Models;

/// <summary>Instructions supplied by the native CLI for this sign-in attempt.</summary>
/// <param name="LoginUrl">The HTTPS address to present to the person signing in.</param>
/// <param name="UserCode">A device-flow code to enter on the provider's page, when required.</param>
/// <param name="RequiresAuthorizationCode">Whether the browser's returned code must be submitted to this session.</param>
public sealed record CliLoginInstructions(Uri LoginUrl, string? UserCode, bool RequiresAuthorizationCode);
