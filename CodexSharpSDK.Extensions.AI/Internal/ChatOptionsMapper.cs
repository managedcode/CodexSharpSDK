using ManagedCode.CodexSharpSDK.Client;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Internal;

internal static class ChatOptionsMapper
{
    internal const string SandboxModeKey = "codex:sandbox_mode";
    internal const string WorkingDirectoryKey = "codex:working_directory";
    internal const string ReasoningEffortKey = "codex:reasoning_effort";
    internal const string NetworkAccessKey = "codex:network_access";
    internal const string WebSearchKey = "codex:web_search";
    internal const string ApprovalPolicyKey = "codex:approval_policy";
    internal const string FullAutoKey = "codex:full_auto";
    internal const string EphemeralKey = "codex:ephemeral";
    internal const string ProfileKey = "codex:profile";
    internal const string SkipGitRepoCheckKey = "codex:skip_git_repo_check";

    internal static ThreadOptions ToThreadOptions(ChatOptions? chatOptions, CodexChatClientOptions clientOptions)
    {
        var defaults = clientOptions.DefaultThreadOptions ?? new ThreadOptions();

        var model = chatOptions?.ModelId ?? clientOptions.DefaultModel ?? defaults.Model;
        var sandboxMode = defaults.SandboxMode;
        var workingDirectory = defaults.WorkingDirectory;
        var reasoningEffort = defaults.ModelReasoningEffort;
        var networkAccess = defaults.NetworkAccessEnabled;
        var webSearch = defaults.WebSearchMode;
        var approvalPolicy = defaults.ApprovalPolicy;
        var fullAuto = defaults.FullAuto;
        var ephemeral = defaults.Ephemeral;
        var profile = defaults.Profile;
        var skipGitRepoCheck = defaults.SkipGitRepoCheck;

        if (chatOptions?.AdditionalProperties is { } props)
        {
            if (props.TryGetValue(SandboxModeKey, out var val) && val is SandboxMode sm)
            {
                sandboxMode = sm;
            }

            if (props.TryGetValue(WorkingDirectoryKey, out val) && val is string wd)
            {
                workingDirectory = wd;
            }

            if (props.TryGetValue(ReasoningEffortKey, out val) && val is ModelReasoningEffort mre)
            {
                reasoningEffort = mre;
            }

            if (props.TryGetValue(NetworkAccessKey, out val) && val is bool na)
            {
                networkAccess = na;
            }

            if (props.TryGetValue(WebSearchKey, out val) && val is WebSearchMode wsm)
            {
                webSearch = wsm;
            }

            if (props.TryGetValue(ApprovalPolicyKey, out val) && val is ApprovalMode am)
            {
                approvalPolicy = am;
            }

            if (props.TryGetValue(FullAutoKey, out val) && val is bool fa)
            {
                fullAuto = fa;
            }

            if (props.TryGetValue(EphemeralKey, out val) && val is bool eph)
            {
                ephemeral = eph;
            }

            if (props.TryGetValue(ProfileKey, out val) && val is string prof)
            {
                profile = prof;
            }

            if (props.TryGetValue(SkipGitRepoCheckKey, out val) && val is bool sgrc)
            {
                skipGitRepoCheck = sgrc;
            }
        }

        return defaults with
        {
            Model = model,
            SandboxMode = sandboxMode,
            WorkingDirectory = workingDirectory,
            ModelReasoningEffort = reasoningEffort,
            NetworkAccessEnabled = networkAccess,
            WebSearchMode = webSearch,
            ApprovalPolicy = approvalPolicy,
            FullAuto = fullAuto,
            Ephemeral = ephemeral,
            Profile = profile,
            SkipGitRepoCheck = skipGitRepoCheck,
        };
    }

    internal static TurnOptions ToTurnOptions(ChatOptions? chatOptions, CancellationToken cancellationToken)
    {
        _ = chatOptions; // reserved for future ResponseFormat→OutputSchema mapping
        return new TurnOptions
        {
            CancellationToken = cancellationToken,
        };
    }
}
