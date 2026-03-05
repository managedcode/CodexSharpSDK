using ManagedCode.CodexSharpSDK.Extensions.AI.Content;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Internal;

internal static class ChatResponseMapper
{
    internal static ChatResponse ToChatResponse(RunResult result, string? threadId)
    {
        var contents = new List<AIContent>
        {
            new TextContent(result.FinalResponse),
        };

        foreach (var item in result.Items)
        {
            switch (item)
            {
                case ReasoningItem r:
                    contents.Add(new TextReasoningContent(r.Text));
                    break;

                case CommandExecutionItem c:
                    contents.Add(new CommandExecutionContent
                    {
                        Command = c.Command,
                        AggregatedOutput = c.AggregatedOutput,
                        ExitCode = c.ExitCode,
                        Status = c.Status,
                    });
                    break;

                case FileChangeItem f:
                    contents.Add(new FileChangeContent
                    {
                        Changes = f.Changes,
                        Status = f.Status,
                    });
                    break;

                case McpToolCallItem m:
                    contents.Add(new McpToolCallContent
                    {
                        Server = m.Server,
                        Tool = m.Tool,
                        Arguments = m.Arguments,
                        Result = m.Result,
                        Error = m.Error,
                        Status = m.Status,
                    });
                    break;

                case WebSearchItem w:
                    contents.Add(new WebSearchContent
                    {
                        Query = w.Query,
                    });
                    break;

                case CollabToolCallItem col:
                    contents.Add(new CollabToolCallContent
                    {
                        Tool = col.Tool,
                        SenderThreadId = col.SenderThreadId,
                        ReceiverThreadIds = col.ReceiverThreadIds,
                        AgentsStates = col.AgentsStates,
                        Status = col.Status,
                    });
                    break;
            }
        }

        var assistantMessage = new ChatMessage(ChatRole.Assistant, contents);
        var response = new ChatResponse(assistantMessage)
        {
            ConversationId = threadId,
        };

        if (result.Usage is { } usage)
        {
            response.Usage = new UsageDetails
            {
                InputTokenCount = usage.InputTokens,
                OutputTokenCount = usage.OutputTokens,
                TotalTokenCount = usage.InputTokens + usage.OutputTokens,
                CachedInputTokenCount = usage.CachedInputTokens > 0 ? usage.CachedInputTokens : null,
            };
        }

        return response;
    }
}
