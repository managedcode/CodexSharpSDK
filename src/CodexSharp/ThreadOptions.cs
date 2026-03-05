namespace ManagedCode.CodexSharp;

public enum ApprovalMode
{
    Never,
    OnRequest,
    OnFailure,
    Untrusted,
}

public enum SandboxMode
{
    ReadOnly,
    WorkspaceWrite,
    DangerFullAccess,
}

public enum ModelReasoningEffort
{
    Minimal,
    Low,
    Medium,
    High,
    XHigh,
}

public enum WebSearchMode
{
    Disabled,
    Cached,
    Live,
}

public sealed record ThreadOptions
{
    public string? Model { get; init; }

    public SandboxMode? SandboxMode { get; init; }

    public string? WorkingDirectory { get; init; }

    public bool SkipGitRepoCheck { get; init; }

    public ModelReasoningEffort? ModelReasoningEffort { get; init; }

    public bool? NetworkAccessEnabled { get; init; }

    public WebSearchMode? WebSearchMode { get; init; }

    public bool? WebSearchEnabled { get; init; }

    public ApprovalMode? ApprovalPolicy { get; init; }

    public IReadOnlyList<string>? AdditionalDirectories { get; init; }
}
