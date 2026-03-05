namespace ManagedCode.CodexSharp;

public sealed record CodexExecArgs
{
    public required string Input { get; init; }

    public string? BaseUrl { get; init; }

    public string? ApiKey { get; init; }

    public string? ThreadId { get; init; }

    public IReadOnlyList<string>? Images { get; init; }

    public string? Model { get; init; }

    public SandboxMode? SandboxMode { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyList<string>? AdditionalDirectories { get; init; }

    public bool SkipGitRepoCheck { get; init; }

    public string? OutputSchemaFile { get; init; }

    public ModelReasoningEffort? ModelReasoningEffort { get; init; }

    public bool? NetworkAccessEnabled { get; init; }

    public WebSearchMode? WebSearchMode { get; init; }

    public bool? WebSearchEnabled { get; init; }

    public ApprovalMode? ApprovalPolicy { get; init; }

    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
}
