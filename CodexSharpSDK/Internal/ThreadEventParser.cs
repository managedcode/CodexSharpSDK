using System.Text.Json;
using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Internal;

internal static class ThreadEventParser
{
    public static ThreadEvent Parse(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var type = GetRequiredString(root, CodexProtocolConstants.Properties.Type);

        return type switch
        {
            CodexProtocolConstants.EventTypes.ThreadStarted => new ThreadStartedEvent(GetRequiredString(root, CodexProtocolConstants.Properties.ThreadId)),
            CodexProtocolConstants.EventTypes.TurnStarted => new TurnStartedEvent(),
            CodexProtocolConstants.EventTypes.TurnCompleted => new TurnCompletedEvent(ParseUsage(GetRequiredProperty(root, CodexProtocolConstants.Properties.Usage))),
            CodexProtocolConstants.EventTypes.TurnFailed => new TurnFailedEvent(ParseThreadError(GetRequiredProperty(root, CodexProtocolConstants.Properties.Error))),
            CodexProtocolConstants.EventTypes.ItemStarted => new ItemStartedEvent(ParseItem(GetRequiredProperty(root, CodexProtocolConstants.Properties.Item))),
            CodexProtocolConstants.EventTypes.ItemUpdated => new ItemUpdatedEvent(ParseItem(GetRequiredProperty(root, CodexProtocolConstants.Properties.Item))),
            CodexProtocolConstants.EventTypes.ItemCompleted => new ItemCompletedEvent(ParseItem(GetRequiredProperty(root, CodexProtocolConstants.Properties.Item))),
            CodexProtocolConstants.EventTypes.Error => new ThreadErrorEvent(GetRequiredString(root, CodexProtocolConstants.Properties.Message)),
            _ => new UnknownThreadEvent(type, JsonNode.Parse(root.GetRawText())!),
        };
    }

    private static Usage ParseUsage(JsonElement usageElement)
    {
        return new Usage(
            GetRequiredInt32(usageElement, CodexProtocolConstants.Properties.InputTokens),
            GetRequiredInt32(usageElement, CodexProtocolConstants.Properties.CachedInputTokens),
            GetRequiredInt32(usageElement, CodexProtocolConstants.Properties.OutputTokens));
    }

    private static ThreadError ParseThreadError(JsonElement errorElement)
    {
        return new ThreadError(GetRequiredString(errorElement, CodexProtocolConstants.Properties.Message));
    }

    private static ThreadItem ParseItem(JsonElement itemElement)
    {
        var itemType = GetRequiredString(itemElement, CodexProtocolConstants.Properties.Type);
        return itemType switch
        {
            CodexProtocolConstants.ItemTypes.AgentMessage => new AgentMessageItem(
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Id),
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Text)),

            CodexProtocolConstants.ItemTypes.Reasoning => new ReasoningItem(
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Id),
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Text)),

            CodexProtocolConstants.ItemTypes.CommandExecution => new CommandExecutionItem(
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Id),
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Command),
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.AggregatedOutput),
                GetOptionalInt32(itemElement, CodexProtocolConstants.Properties.ExitCode),
                ParseCommandExecutionStatus(GetRequiredString(itemElement, CodexProtocolConstants.Properties.Status))),

            CodexProtocolConstants.ItemTypes.FileChange => new FileChangeItem(
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Id),
                ParseFileUpdateChanges(GetRequiredProperty(itemElement, CodexProtocolConstants.Properties.Changes)),
                ParsePatchApplyStatus(GetRequiredString(itemElement, CodexProtocolConstants.Properties.Status))),

            CodexProtocolConstants.ItemTypes.McpToolCall => new McpToolCallItem(
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Id),
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Server),
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Tool),
                ParseOptionalNode(itemElement, CodexProtocolConstants.Properties.Arguments),
                ParseMcpResult(itemElement),
                ParseMcpError(itemElement),
                ParseMcpToolCallStatus(GetRequiredString(itemElement, CodexProtocolConstants.Properties.Status))),

            CodexProtocolConstants.ItemTypes.CollabToolCall => ParseCollabToolCall(itemElement),

            CodexProtocolConstants.ItemTypes.WebSearch => new WebSearchItem(
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Id),
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Query)),

            CodexProtocolConstants.ItemTypes.TodoList => new TodoListItem(
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Id),
                ParseTodoItems(GetRequiredProperty(itemElement, CodexProtocolConstants.Properties.Items))),

            CodexProtocolConstants.ItemTypes.Error => new ErrorItem(
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Id),
                GetRequiredString(itemElement, CodexProtocolConstants.Properties.Message)),

            _ => new UnknownThreadItem(
                GetOptionalString(itemElement, CodexProtocolConstants.Properties.Id) ?? string.Empty,
                itemType,
                JsonNode.Parse(itemElement.GetRawText())!),
        };
    }

    private static List<FileUpdateChange> ParseFileUpdateChanges(JsonElement changesElement)
    {
        if (changesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("file_change.changes must be an array");
        }

        var changes = new List<FileUpdateChange>();
        foreach (var change in changesElement.EnumerateArray())
        {
            changes.Add(new FileUpdateChange(
                GetRequiredString(change, CodexProtocolConstants.Properties.Path),
                ParsePatchChangeKind(GetRequiredString(change, CodexProtocolConstants.Properties.Kind))));
        }

        return changes;
    }

    private static List<TodoItem> ParseTodoItems(JsonElement itemsElement)
    {
        if (itemsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("todo_list.items must be an array");
        }

        var items = new List<TodoItem>();
        foreach (var item in itemsElement.EnumerateArray())
        {
            items.Add(new TodoItem(
                GetRequiredString(item, CodexProtocolConstants.Properties.Text),
                GetRequiredBoolean(item, CodexProtocolConstants.Properties.Completed)));
        }

        return items;
    }

    private static McpToolCallResult? ParseMcpResult(JsonElement element)
    {
        if (!element.TryGetProperty(CodexProtocolConstants.Properties.Result, out var resultElement)
            || resultElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var content = new List<JsonNode>();
        if (resultElement.TryGetProperty(CodexProtocolConstants.Properties.Content, out var contentElement)
            && contentElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in contentElement.EnumerateArray())
            {
                var parsed = JsonNode.Parse(entry.GetRawText());
                if (parsed is not null)
                {
                    content.Add(parsed);
                }
            }
        }

        JsonNode? structuredContent = null;
        if (resultElement.TryGetProperty(CodexProtocolConstants.Properties.StructuredContent, out var structuredContentElement)
            && structuredContentElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            structuredContent = JsonNode.Parse(structuredContentElement.GetRawText());
        }

        return new McpToolCallResult(content, structuredContent);
    }

    private static McpToolCallError? ParseMcpError(JsonElement element)
    {
        if (!element.TryGetProperty(CodexProtocolConstants.Properties.Error, out var errorElement)
            || errorElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return new McpToolCallError(GetRequiredString(errorElement, CodexProtocolConstants.Properties.Message));
    }

    private static CollabToolCallItem ParseCollabToolCall(JsonElement itemElement)
    {
        return new CollabToolCallItem(
            GetRequiredString(itemElement, CodexProtocolConstants.Properties.Id),
            ParseCollabTool(GetRequiredString(itemElement, CodexProtocolConstants.Properties.Tool)),
            GetRequiredString(itemElement, CodexProtocolConstants.Properties.SenderThreadId),
            ParseStringArray(
                GetRequiredProperty(itemElement, CodexProtocolConstants.Properties.ReceiverThreadIds),
                "collab_tool_call.receiver_thread_ids"),
            GetOptionalString(itemElement, CodexProtocolConstants.Properties.Prompt),
            ParseCollabAgentStates(GetRequiredProperty(itemElement, CodexProtocolConstants.Properties.AgentsStates)),
            ParseCollabToolCallStatus(GetRequiredString(itemElement, CodexProtocolConstants.Properties.Status)));
    }

    private static Dictionary<string, CollabAgentState> ParseCollabAgentStates(JsonElement agentsStatesElement)
    {
        if (agentsStatesElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("collab_tool_call.agents_states must be an object");
        }

        var states = new Dictionary<string, CollabAgentState>(StringComparer.Ordinal);
        foreach (var property in agentsStatesElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("collab_tool_call.agents_states values must be objects");
            }

            states[property.Name] = new CollabAgentState(
                ParseCollabAgentStatus(GetRequiredString(property.Value, CodexProtocolConstants.Properties.Status)),
                GetOptionalString(property.Value, CodexProtocolConstants.Properties.Message));
        }

        return states;
    }

    private static List<string> ParseStringArray(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{context} must be an array");
        }

        var items = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"{context} entries must be strings");
            }

            items.Add(item.GetString() ?? string.Empty);
        }

        return items;
    }

    private static JsonNode? ParseOptionalNode(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var valueElement)
            || valueElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonNode.Parse(valueElement.GetRawText());
    }

    private static PatchChangeKind ParsePatchChangeKind(string kind)
    {
        return kind switch
        {
            CodexProtocolConstants.PatchKinds.Add => PatchChangeKind.Add,
            CodexProtocolConstants.PatchKinds.Delete => PatchChangeKind.Delete,
            CodexProtocolConstants.PatchKinds.Update => PatchChangeKind.Update,
            _ => throw new InvalidOperationException($"Unsupported patch change kind: {kind}"),
        };
    }

    private static PatchApplyStatus ParsePatchApplyStatus(string status)
    {
        return status switch
        {
            CodexProtocolConstants.Statuses.InProgress => PatchApplyStatus.InProgress,
            CodexProtocolConstants.Statuses.Completed => PatchApplyStatus.Completed,
            CodexProtocolConstants.Statuses.Failed => PatchApplyStatus.Failed,
            _ => throw new InvalidOperationException($"Unsupported patch apply status: {status}"),
        };
    }

    private static McpToolCallStatus ParseMcpToolCallStatus(string status)
    {
        return status switch
        {
            CodexProtocolConstants.Statuses.InProgress => McpToolCallStatus.InProgress,
            CodexProtocolConstants.Statuses.Completed => McpToolCallStatus.Completed,
            CodexProtocolConstants.Statuses.Failed => McpToolCallStatus.Failed,
            _ => throw new InvalidOperationException($"Unsupported MCP tool call status: {status}"),
        };
    }

    private static CollabToolCallStatus ParseCollabToolCallStatus(string status)
    {
        return status switch
        {
            CodexProtocolConstants.Statuses.InProgress => CollabToolCallStatus.InProgress,
            CodexProtocolConstants.Statuses.Completed => CollabToolCallStatus.Completed,
            CodexProtocolConstants.Statuses.Failed => CollabToolCallStatus.Failed,
            _ => throw new InvalidOperationException($"Unsupported collab tool call status: {status}"),
        };
    }

    private static CollabTool ParseCollabTool(string tool)
    {
        return tool switch
        {
            CodexProtocolConstants.CollabTools.SpawnAgent => CollabTool.SpawnAgent,
            CodexProtocolConstants.CollabTools.SendInput => CollabTool.SendInput,
            CodexProtocolConstants.CollabTools.Wait => CollabTool.Wait,
            CodexProtocolConstants.CollabTools.CloseAgent => CollabTool.CloseAgent,
            _ => throw new InvalidOperationException($"Unsupported collab tool: {tool}"),
        };
    }

    private static CollabAgentStatus ParseCollabAgentStatus(string status)
    {
        return status switch
        {
            CodexProtocolConstants.CollabAgentStatuses.PendingInit => CollabAgentStatus.PendingInit,
            CodexProtocolConstants.CollabAgentStatuses.Running => CollabAgentStatus.Running,
            CodexProtocolConstants.CollabAgentStatuses.Completed => CollabAgentStatus.Completed,
            CodexProtocolConstants.CollabAgentStatuses.Errored => CollabAgentStatus.Errored,
            CodexProtocolConstants.CollabAgentStatuses.Shutdown => CollabAgentStatus.Shutdown,
            CodexProtocolConstants.CollabAgentStatuses.NotFound => CollabAgentStatus.NotFound,
            _ => throw new InvalidOperationException($"Unsupported collab agent status: {status}"),
        };
    }

    private static CommandExecutionStatus ParseCommandExecutionStatus(string status)
    {
        return status switch
        {
            CodexProtocolConstants.Statuses.InProgress => CommandExecutionStatus.InProgress,
            CodexProtocolConstants.Statuses.Completed => CommandExecutionStatus.Completed,
            CodexProtocolConstants.Statuses.Failed => CommandExecutionStatus.Failed,
            _ => throw new InvalidOperationException($"Unsupported command execution status: {status}"),
        };
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var propertyValue))
        {
            throw new InvalidOperationException($"Missing required property '{property}'");
        }

        return propertyValue;
    }

    private static string GetRequiredString(JsonElement element, string property)
    {
        var value = GetRequiredProperty(element, property);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Property '{property}' must be a string");
        }

        return value.GetString() ?? string.Empty;
    }

    private static string? GetOptionalString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Property '{property}' must be a string");
        }

        return value.GetString();
    }

    private static int GetRequiredInt32(JsonElement element, string property)
    {
        var value = GetRequiredProperty(element, property);
        if (!value.TryGetInt32(out var intValue))
        {
            throw new InvalidOperationException($"Property '{property}' must be an integer");
        }

        return intValue;
    }

    private static int? GetOptionalInt32(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (!value.TryGetInt32(out var intValue))
        {
            throw new InvalidOperationException($"Property '{property}' must be an integer");
        }

        return intValue;
    }

    private static bool GetRequiredBoolean(JsonElement element, string property)
    {
        var value = GetRequiredProperty(element, property);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException($"Property '{property}' must be a boolean"),
        };
    }
}
