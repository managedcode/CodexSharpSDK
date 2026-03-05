namespace ManagedCode.CodexSharpSDK.Client;

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

public enum OssProvider
{
    LmStudio,
    Ollama,
}

public enum ExecOutputColor
{
    Always,
    Never,
    Auto,
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

    public string? Profile { get; init; }

    public bool UseOss { get; init; }

    public OssProvider? LocalProvider { get; init; }

    public bool FullAuto { get; init; }

    public bool DangerouslyBypassApprovalsAndSandbox { get; init; }

    public bool Ephemeral { get; init; }

    public ExecOutputColor? Color { get; init; }

    public bool ProgressCursor { get; init; }

    public string? OutputLastMessageFile { get; init; }

    public IReadOnlyList<string>? EnabledFeatures { get; init; }

    public IReadOnlyList<string>? DisabledFeatures { get; init; }

    public IReadOnlyList<string>? AdditionalCliArguments { get; init; }
}
