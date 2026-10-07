using System.Runtime.CompilerServices;
using ManagedCode.CodexSharpSDK.Extensions.AI.Content;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Internal;

internal static class StreamingEventMapper
{
    private const string NativeActivityProperty = "managedcode:activity";
    private const string NativeActivityPhaseProperty = "managedcode:activity_phase";
    private const string NativeActivityStarted = "started";
    private const string NativeActivityUpdated = "updated";
    private const string NativeActivityCompleted = "completed";
    private const string CommandExecutionActivity = "command_execution";
    private const string FileChangeActivity = "file_change";
    private const string McpToolActivity = "mcp_tool";
    private const string WebSearchActivity = "web_search";
    private const string CollaborationActivity = "collaboration";

    internal static async IAsyncEnumerable<ChatResponseUpdate> ToUpdates(
        IAsyncEnumerable<ThreadEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var completedMessageIds = new HashSet<string>(StringComparer.Ordinal);
        string? conversationId = null;
        string? failureMessage = null;
        await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case ThreadStartedEvent started:
                    conversationId = started.ThreadId;
                    yield return new ChatResponseUpdate { ConversationId = conversationId };
                    break;

                case ItemCompletedEvent { Item: AgentMessageItem msg } when completedMessageIds.Add(msg.Id):
                    yield return new ChatResponseUpdate
                    {
                        ConversationId = conversationId,
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(msg.Text)],
                    };
                    break;

                case ItemCompletedEvent { Item: ReasoningItem r }:
                    yield return new ChatResponseUpdate
                    {
                        ConversationId = conversationId,
                        Contents = [new TextReasoningContent(r.Text)],
                    };
                    break;

                case ItemCompletedEvent { Item: CommandExecutionItem command }:
                    yield return CreateNativeActivityUpdate(CommandExecutionActivity, NativeActivityCompleted, conversationId,
                        new CommandExecutionContent
                        {
                            Command = command.Command,
                            AggregatedOutput = command.AggregatedOutput,
                            ExitCode = command.ExitCode,
                            Status = command.Status,
                        });
                    break;

                case ItemCompletedEvent { Item: FileChangeItem fileChange }:
                    yield return CreateNativeActivityUpdate(FileChangeActivity, NativeActivityCompleted, conversationId,
                        new FileChangeContent { Changes = fileChange.Changes, Status = fileChange.Status });
                    break;

                case ItemCompletedEvent { Item: McpToolCallItem mcp }:
                    yield return CreateNativeActivityUpdate(McpToolActivity, NativeActivityCompleted, conversationId,
                        new McpToolCallContent
                        {
                            Server = mcp.Server,
                            Tool = mcp.Tool,
                            Arguments = mcp.Arguments,
                            Result = mcp.Result,
                            Error = mcp.Error,
                            Status = mcp.Status,
                        });
                    break;

                case ItemCompletedEvent { Item: WebSearchItem search }:
                    yield return CreateNativeActivityUpdate(WebSearchActivity, NativeActivityCompleted, conversationId,
                        new WebSearchContent { Query = search.Query });
                    break;

                case ItemCompletedEvent { Item: CollabToolCallItem collaboration }:
                    yield return CreateNativeActivityUpdate(CollaborationActivity, NativeActivityCompleted, conversationId,
                        new CollabToolCallContent
                        {
                            Tool = collaboration.Tool,
                            SenderThreadId = collaboration.SenderThreadId,
                            ReceiverThreadIds = collaboration.ReceiverThreadIds,
                            AgentsStates = collaboration.AgentsStates,
                            Status = collaboration.Status,
                        });
                    break;

                case ItemUpdatedEvent { Item: AgentMessageItem }:
                    // Codex exec JSONL carries full item snapshots here; its separate app-server
                    // agentMessage/delta event is not part of this SDK's ThreadEvent contract.
                    // Wait for item/completed, the authoritative final snapshot, to avoid duplicates.
                    break;

                case ItemStartedEvent { Item: CommandExecutionItem }:
                    yield return CreateNativeActivityUpdate(CommandExecutionActivity, NativeActivityStarted, conversationId);
                    break;

                case ItemUpdatedEvent { Item: CommandExecutionItem }:
                    yield return CreateNativeActivityUpdate(CommandExecutionActivity, NativeActivityUpdated, conversationId);
                    break;

                case ItemStartedEvent { Item: McpToolCallItem }:
                    yield return CreateNativeActivityUpdate(McpToolActivity, NativeActivityStarted, conversationId);
                    break;

                case ItemUpdatedEvent { Item: McpToolCallItem }:
                    yield return CreateNativeActivityUpdate(McpToolActivity, NativeActivityUpdated, conversationId);
                    break;

                case ItemStartedEvent { Item: CollabToolCallItem }:
                    yield return CreateNativeActivityUpdate(CollaborationActivity, NativeActivityStarted, conversationId);
                    break;

                case ItemUpdatedEvent { Item: CollabToolCallItem }:
                    yield return CreateNativeActivityUpdate(CollaborationActivity, NativeActivityUpdated, conversationId);
                    break;

                case TurnCompletedEvent tc:
                    yield return new ChatResponseUpdate
                    {
                        ConversationId = conversationId,
                        FinishReason = ChatFinishReason.Stop,
                        Contents =
                        [
                            new UsageContent(new UsageDetails
                            {
                                InputTokenCount = tc.Usage.InputTokens,
                                OutputTokenCount = tc.Usage.OutputTokens,
                                TotalTokenCount = tc.Usage.InputTokens + tc.Usage.OutputTokens,
                            }),
                        ],
                    };
                    break;

                case TurnFailedEvent tf:
                    failureMessage ??= tf.Error.Message;
                    break;

                case ThreadErrorEvent te:
                    failureMessage ??= te.Message;
                    break;
            }

            if (failureMessage is not null)
            {
                break;
            }
        }

        if (failureMessage is not null)
        {
            throw CliExecutionFailureException.FromProviderFailure(failureMessage);
        }
    }

    private static ChatResponseUpdate CreateNativeActivityUpdate(string category, string phase, string? conversationId, AIContent? content = null) => new()
    {
        ConversationId = conversationId,
        Contents = content is null ? [] : [content],
        AdditionalProperties = new AdditionalPropertiesDictionary
        {
            [NativeActivityProperty] = category,
            [NativeActivityPhaseProperty] = phase,
        },
    };
}
