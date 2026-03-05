using System.Runtime.CompilerServices;
using ManagedCode.CodexSharpSDK.Extensions.AI.Content;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Internal;

internal static class StreamingEventMapper
{
    internal static async IAsyncEnumerable<ChatResponseUpdate> ToUpdates(
        IAsyncEnumerable<ThreadEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var evt in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case ThreadStartedEvent started:
                    yield return new ChatResponseUpdate { ConversationId = started.ThreadId };
                    break;

                case ItemCompletedEvent { Item: AgentMessageItem msg }:
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        Contents = [new TextContent(msg.Text)],
                    };
                    break;

                case ItemCompletedEvent { Item: ReasoningItem r }:
                    yield return new ChatResponseUpdate
                    {
                        Contents = [new TextReasoningContent(r.Text)],
                    };
                    break;

                case ItemCompletedEvent { Item: CommandExecutionItem c }:
                    yield return new ChatResponseUpdate
                    {
                        Contents =
                        [
                            new CommandExecutionContent
                            {
                                Command = c.Command,
                                AggregatedOutput = c.AggregatedOutput,
                                ExitCode = c.ExitCode,
                                Status = c.Status,
                            },
                        ],
                    };
                    break;

                case ItemCompletedEvent { Item: FileChangeItem f }:
                    yield return new ChatResponseUpdate
                    {
                        Contents =
                        [
                            new FileChangeContent
                            {
                                Changes = f.Changes,
                                Status = f.Status,
                            },
                        ],
                    };
                    break;

                case ItemCompletedEvent { Item: McpToolCallItem m }:
                    yield return new ChatResponseUpdate
                    {
                        Contents =
                        [
                            new McpToolCallContent
                            {
                                Server = m.Server,
                                Tool = m.Tool,
                                Arguments = m.Arguments,
                                Result = m.Result,
                                Error = m.Error,
                                Status = m.Status,
                            },
                        ],
                    };
                    break;

                case ItemCompletedEvent { Item: WebSearchItem w }:
                    yield return new ChatResponseUpdate
                    {
                        Contents =
                        [
                            new WebSearchContent
                            {
                                Query = w.Query,
                            },
                        ],
                    };
                    break;

                case ItemCompletedEvent { Item: CollabToolCallItem col }:
                    yield return new ChatResponseUpdate
                    {
                        Contents =
                        [
                            new CollabToolCallContent
                            {
                                Tool = col.Tool,
                                SenderThreadId = col.SenderThreadId,
                                ReceiverThreadIds = col.ReceiverThreadIds,
                                AgentsStates = col.AgentsStates,
                                Status = col.Status,
                            },
                        ],
                    };
                    break;

                case ItemUpdatedEvent { Item: AgentMessageItem msg }:
                    yield return new ChatResponseUpdate
                    {
                        Contents = [new TextContent(msg.Text)],
                    };
                    break;

                case TurnCompletedEvent tc:
                    yield return new ChatResponseUpdate
                    {
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
                    throw new InvalidOperationException(tf.Error.Message);

                case ThreadErrorEvent te:
                    throw new InvalidOperationException(te.Message);
            }
        }
    }
}
