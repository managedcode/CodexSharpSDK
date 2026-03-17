namespace ManagedCode.CodexSharpSDK.Models;

/// <summary>
/// Known Codex CLI feature flag keys for use with <see cref="Client.ThreadOptions.EnabledFeatures"/>
/// and <see cref="Client.ThreadOptions.DisabledFeatures"/>.
/// Keys are sourced from <c>codex-rs/core/src/features.rs</c> in the upstream <c>openai/codex</c> repository.
/// </summary>
public static class CodexFeatureKeys
{
    public const string Undo = "undo";
    public const string ShellTool = "shell_tool";
    public const string UnifiedExec = "unified_exec";
    public const string ShellZshFork = "shell_zsh_fork";
    public const string ShellSnapshot = "shell_snapshot";
    public const string JsRepl = "js_repl";
    public const string JsReplToolsOnly = "js_repl_tools_only";
    public const string WebSearchRequest = "web_search_request";
    public const string WebSearchCached = "web_search_cached";
    public const string SearchTool = "search_tool";
    public const string CodexGitCommit = "codex_git_commit";
    public const string RuntimeMetrics = "runtime_metrics";
    public const string Sqlite = "sqlite";
    public const string Memories = "memories";
    public const string ChildAgentsMd = "child_agents_md";
    public const string ImageDetailOriginal = "image_detail_original";
    public const string ApplyPatchFreeform = "apply_patch_freeform";
    public const string RequestPermissions = "request_permissions";
    public const string UseLinuxSandboxBwrap = "use_linux_sandbox_bwrap";
    public const string RequestRule = "request_rule";
    public const string ExperimentalWindowsSandbox = "experimental_windows_sandbox";
    public const string ElevatedWindowsSandbox = "elevated_windows_sandbox";
    public const string RemoteModels = "remote_models";
    public const string PowershellUtf8 = "powershell_utf8";
    public const string EnableRequestCompression = "enable_request_compression";
    public const string MultiAgent = "multi_agent";
    public const string Apps = "apps";
    public const string Plugins = "plugins";
    public const string ImageGeneration = "image_generation";
    public const string AppsMcpGateway = "apps_mcp_gateway";
    public const string SkillMcpDependencyInstall = "skill_mcp_dependency_install";
    public const string SkillEnvVarDependencyPrompt = "skill_env_var_dependency_prompt";
    public const string Steer = "steer";
    public const string DefaultModeRequestUserInput = "default_mode_request_user_input";
    public const string CollaborationModes = "collaboration_modes";
    public const string ToolCallMcpElicitation = "tool_call_mcp_elicitation";
    public const string Personality = "personality";
    public const string Artifact = "artifact";
    public const string FastMode = "fast_mode";
    public const string VoiceTranscription = "voice_transcription";
    public const string RealtimeConversation = "realtime_conversation";
    public const string PreventIdleSleep = "prevent_idle_sleep";
    public const string ResponsesWebsockets = "responses_websockets";
    public const string ResponsesWebsocketsV2 = "responses_websockets_v2";
}
