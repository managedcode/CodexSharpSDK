using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Client;

namespace ManagedCode.CodexSharpSDK.Execution;

internal static class AppServerOptions
{
    public static JsonObject Thread(ThreadOptions options, string? threadId)
    {
        if (options.Color is not null || options.ProgressCursor || options.OutputLastMessageFile is not null || options.AdditionalCliArguments?.Count > 0)
        {
            throw new NotSupportedException("CLI output presentation and arbitrary exec arguments are not supported by live app-server streaming.");
        }
        var config = new JsonObject();
        if (options.Profile is { } profile)
        { config["profile"] = profile; }
        if (options.ModelReasoningEffort is { } effort)
        { config["model_reasoning_effort"] = effort.ToCliValue(); }
        if (options.ApprovalPolicy is { } approval)
        { config["approval_policy"] = approval.ToCliValue(); }
        if (options.SandboxMode is { } sandbox)
        { config["sandbox_mode"] = sandbox.ToCliValue(); }
        if (options.NetworkAccessEnabled is { } network)
        { config["sandbox_workspace_write.network_access"] = network; }
        if (options.AdditionalDirectories is { Count: > 0 } directories)
        {
            config["sandbox_workspace_write.writable_roots"] = new JsonArray(directories.Select(static path => (JsonNode?)JsonValue.Create(path)).ToArray());
        }
        if (options.WebSearchMode is { } search)
        { config["web_search"] = search.ToCliValue(); }
        else if (options.WebSearchEnabled is { } enabled)
        { config["web_search"] = enabled ? "live" : "disabled"; }
        if (options.FullAuto)
        {
            config["approval_policy"] = "on-request";
            config["sandbox_mode"] = "workspace-write";
        }
        if (options.DangerouslyBypassApprovalsAndSandbox)
        {
            config["approval_policy"] = "never";
            config["sandbox_mode"] = "danger-full-access";
        }
        if (options.LocalProvider is { } local)
        { config["model_provider"] = local.ToCliValue(); }
        else if (options.UseOss)
        { throw new NotSupportedException("Live OSS streaming requires an explicit local provider."); }
        foreach (var feature in options.EnabledFeatures ?? [])
        { config["features." + feature] = true; }
        foreach (var feature in options.DisabledFeatures ?? [])
        { config["features." + feature] = false; }
        var result = new JsonObject { ["model"] = options.Model, ["cwd"] = options.WorkingDirectory, ["config"] = config };
        if (threadId is null)
        { result["ephemeral"] = options.Ephemeral; }
        else
        { result["threadId"] = threadId; result["excludeTurns"] = true; }
        return result;
    }

    public static JsonObject Turn(CodexExecArgs args, ThreadOptions options, TurnOptions turn, string threadId)
    {
        var inputs = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = args.Input });
        foreach (var path in args.Images ?? [])
        {
            inputs.Add((JsonNode)new JsonObject { ["type"] = "localImage", ["path"] = path });
        }
        var result = new JsonObject { ["threadId"] = threadId, ["input"] = inputs };
        if (options.ModelReasoningEffort is { } effort)
        { result["effort"] = effort.ToCliValue(); }
        if (turn.OutputSchema is { } schema)
        { result["outputSchema"] = schema.ToJsonObject(); }
        return result;
    }
}
