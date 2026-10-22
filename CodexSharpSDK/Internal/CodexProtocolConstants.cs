namespace ManagedCode.CodexSharpSDK.Internal;

internal static class CodexProtocolConstants
{
    internal static class Properties
    {
        internal const string AggregatedOutput = "aggregated_output";
        internal const string AgentsStates = "agents_states";
        internal const string Arguments = "arguments";
        internal const string CachedInputTokens = "cached_input_tokens";
        internal const string Changes = "changes";
        internal const string Command = "command";
        internal const string Completed = "completed";
        internal const string Content = "content";
        internal const string Error = "error";
        internal const string ExitCode = "exit_code";
        internal const string Id = "id";
        internal const string InputTokens = "input_tokens";
        internal const string Item = "item";
        internal const string Items = "items";
        internal const string Kind = "kind";
        internal const string Message = "message";
        internal const string OutputTokens = "output_tokens";
        internal const string Path = "path";
        internal const string Prompt = "prompt";
        internal const string Query = "query";
        internal const string ReceiverThreadIds = "receiver_thread_ids";
        internal const string Result = "result";
        internal const string SenderThreadId = "sender_thread_id";
        internal const string Server = "server";
        internal const string Status = "status";
        internal const string StructuredContent = "structured_content";
        internal const string Text = "text";
        internal const string ThreadId = "thread_id";
        internal const string Tool = "tool";
        internal const string Type = "type";
        internal const string Usage = "usage";
    }

    internal static class EventTypes
    {
        internal const string Error = "error";
        internal const string ItemCompleted = "item.completed";
        internal const string ItemStarted = "item.started";
        internal const string ItemUpdated = "item.updated";
        internal const string AgentMessageDelta = "item/agentMessage/delta";
        internal const string ThreadStarted = "thread.started";
        internal const string TurnCompleted = "turn.completed";
        internal const string TurnFailed = "turn.failed";
        internal const string TurnStarted = "turn.started";
    }

    internal static class ItemTypes
    {
        internal const string AgentMessage = "agent_message";
        internal const string CollabToolCall = "collab_tool_call";
        internal const string CommandExecution = "command_execution";
        internal const string Error = "error";
        internal const string FileChange = "file_change";
        internal const string McpToolCall = "mcp_tool_call";
        internal const string Reasoning = "reasoning";
        internal const string TodoList = "todo_list";
        internal const string WebSearch = "web_search";
    }

    internal static class PatchKinds
    {
        internal const string Add = "add";
        internal const string Delete = "delete";
        internal const string Update = "update";
    }

    internal static class Statuses
    {
        internal const string Completed = "completed";
        internal const string Failed = "failed";
        internal const string Declined = "declined";
        internal const string InProgress = "in_progress";
    }

    internal static class CollabTools
    {
        internal const string SpawnAgent = "spawn_agent";
        internal const string SendInput = "send_input";
        internal const string Wait = "wait";
        internal const string CloseAgent = "close_agent";
    }

    internal static class CollabAgentStatuses
    {
        internal const string PendingInit = "pending_init";
        internal const string Running = "running";
        internal const string Completed = "completed";
        internal const string Errored = "errored";
        internal const string Shutdown = "shutdown";
        internal const string NotFound = "not_found";
    }
}
