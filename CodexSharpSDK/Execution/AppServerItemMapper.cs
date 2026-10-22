using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Execution;

internal static class AppServerItemMapper
{
    public static ThreadItem Parse(JsonObject item)
    {
        var id = item["id"]!.GetValue<string>();
        var type = item["type"]!.GetValue<string>();
        return type switch
        {
            "agentMessage" => new AgentMessageItem(id, item["text"]?.GetValue<string>() ?? string.Empty),
            "commandExecution" => new CommandExecutionItem(id, item["command"]?.GetValue<string>() ?? string.Empty,
                item["aggregatedOutput"]?.GetValue<string>() ?? string.Empty, item["exitCode"]?.GetValue<int>(),
                Status<CommandExecutionStatus>(item)),
            "fileChange" => new FileChangeItem(id, Files(item["changes"] as JsonArray), Status<PatchApplyStatus>(item)),
            "mcpToolCall" => new McpToolCallItem(id, item["server"]?.GetValue<string>() ?? string.Empty,
                item["tool"]?.GetValue<string>() ?? string.Empty, item["arguments"]?.DeepClone(), McpResult(item["result"]),
                item["error"] is { } error ? new McpToolCallError(error["message"]?.GetValue<string>() ?? string.Empty) : null,
                Status<McpToolCallStatus>(item)),
            "reasoning" => new ReasoningItem(id, string.Join("\n", (item["summary"] as JsonArray ?? []).Select(static value => value!.GetValue<string>()))),
            "collabAgentToolCall" => Collaboration(id, item),
            "webSearch" => new WebSearchItem(id, item["query"]?.GetValue<string>() ?? string.Empty),
            _ => new UnknownThreadItem(id, type, item.DeepClone()),
        };
    }

    private static T Status<T>(JsonObject item) where T : struct, Enum =>
        Enum.TryParse<T>(item["status"]?.GetValue<string>(), true, out var status) && Enum.IsDefined(status)
            ? status : throw new InvalidDataException("Codex app-server returned an unsupported native tool status.");

    private static McpToolCallResult? McpResult(JsonNode? result) => result is null ? null : new McpToolCallResult(
        (result["content"] as JsonArray ?? []).Select(static value => value!.DeepClone()).ToArray(), result["structuredContent"]?.DeepClone());

    private static ThreadItem Collaboration(string id, JsonObject item)
    {
        if (!Enum.TryParse<CollabTool>(item["tool"]?.GetValue<string>(), true, out var tool) || !Enum.IsDefined(tool))
        {
            return new UnknownThreadItem(id, item["type"]!.GetValue<string>(), item.DeepClone());
        }
        var states = new Dictionary<string, CollabAgentState>(StringComparer.Ordinal);
        foreach (var state in item["agentsStates"] as JsonObject ?? [])
        {
            states.Add(state.Key, new CollabAgentState(Enum.Parse<CollabAgentStatus>(state.Value!["status"]!.GetValue<string>(), true),
                state.Value["message"]?.GetValue<string>()));
        }
        return new CollabToolCallItem(id, tool, item["senderThreadId"]!.GetValue<string>(),
            (item["receiverThreadIds"] as JsonArray ?? []).Select(static value => value!.GetValue<string>()).ToArray(),
            item["prompt"]?.GetValue<string>(), states, Status<CollabToolCallStatus>(item));
    }

    private static FileUpdateChange[] Files(JsonArray? changes) => changes is null ? [] : changes.Select(change =>
        new FileUpdateChange(change!["path"]!.GetValue<string>(), Kind(change["kind"]))).ToArray();

    private static PatchChangeKind Kind(JsonNode? value)
    {
        var kind = value is JsonObject kindObject ? kindObject["type"]?.GetValue<string>() : value?.GetValue<string>();
        return Enum.TryParse<PatchChangeKind>(kind, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : throw new InvalidDataException("Codex app-server returned an unsupported file change kind.");
    }
}
