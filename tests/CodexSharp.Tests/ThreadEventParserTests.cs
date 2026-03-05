using ManagedCode.CodexSharp.Internal;

namespace ManagedCode.CodexSharp.Tests;

public class ThreadEventParserTests
{
    [Test]
    public async Task Parse_RecognizesAllTopLevelEventKinds()
    {
        var events = new[]
        {
            "{\"type\":\"thread.started\",\"thread_id\":\"thread_1\"}",
            "{\"type\":\"turn.started\"}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1,\"cached_input_tokens\":0,\"output_tokens\":2}}",
            "{\"type\":\"turn.failed\",\"error\":{\"message\":\"failed\"}}",
            "{\"type\":\"item.started\",\"item\":{\"id\":\"a\",\"type\":\"agent_message\",\"text\":\"x\"}}",
            "{\"type\":\"item.updated\",\"item\":{\"id\":\"b\",\"type\":\"reasoning\",\"text\":\"y\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"c\",\"type\":\"error\",\"message\":\"z\"}}",
            "{\"type\":\"error\",\"message\":\"fatal\"}",
        };

        var parsed = events.Select(ThreadEventParser.Parse).ToList();

        await Assert.That(parsed).HasCount(8);
        await Assert.That(parsed[0]).IsTypeOf<ThreadStartedEvent>();
        await Assert.That(parsed[1]).IsTypeOf<TurnStartedEvent>();
        await Assert.That(parsed[2]).IsTypeOf<TurnCompletedEvent>();
        await Assert.That(parsed[3]).IsTypeOf<TurnFailedEvent>();
        await Assert.That(parsed[4]).IsTypeOf<ItemStartedEvent>();
        await Assert.That(parsed[5]).IsTypeOf<ItemUpdatedEvent>();
        await Assert.That(parsed[6]).IsTypeOf<ItemCompletedEvent>();
        await Assert.That(parsed[7]).IsTypeOf<ThreadErrorEvent>();
    }

    [Test]
    public async Task Parse_RecognizesAllItemKinds()
    {
        var events = new[]
        {
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"1\",\"type\":\"agent_message\",\"text\":\"hi\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"2\",\"type\":\"reasoning\",\"text\":\"plan\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"3\",\"type\":\"command_execution\",\"command\":\"ls\",\"aggregated_output\":\"ok\",\"exit_code\":0,\"status\":\"completed\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"4\",\"type\":\"file_change\",\"changes\":[{\"path\":\"a.txt\",\"kind\":\"update\"}],\"status\":\"completed\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"5\",\"type\":\"mcp_tool_call\",\"server\":\"srv\",\"tool\":\"tool\",\"arguments\":{\"x\":1},\"result\":{\"content\":[{\"type\":\"text\"}],\"structured_content\":{\"ok\":true}},\"status\":\"in_progress\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"6\",\"type\":\"web_search\",\"query\":\"q\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"7\",\"type\":\"todo_list\",\"items\":[{\"text\":\"a\",\"completed\":true}]}}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"8\",\"type\":\"error\",\"message\":\"boom\"}}",
        };

        var parsedItems = events
            .Select(ThreadEventParser.Parse)
            .OfType<ItemCompletedEvent>()
            .Select(itemCompleted => itemCompleted.Item)
            .ToList();

        await Assert.That(parsedItems).HasCount(8);
        await Assert.That(parsedItems[0]).IsTypeOf<AgentMessageItem>();
        await Assert.That(parsedItems[1]).IsTypeOf<ReasoningItem>();
        await Assert.That(parsedItems[2]).IsTypeOf<CommandExecutionItem>();
        await Assert.That(parsedItems[3]).IsTypeOf<FileChangeItem>();
        await Assert.That(parsedItems[4]).IsTypeOf<McpToolCallItem>();
        await Assert.That(parsedItems[5]).IsTypeOf<WebSearchItem>();
        await Assert.That(parsedItems[6]).IsTypeOf<TodoListItem>();
        await Assert.That(parsedItems[7]).IsTypeOf<ErrorItem>();
    }

    [Test]
    public async Task Parse_ThrowsForUnsupportedEventType()
    {
        var action = () => ThreadEventParser.Parse("{\"type\":\"unknown\"}");

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception!.Message).Contains("Unsupported thread event type");
    }

    [Test]
    public async Task Parse_ThrowsForUnsupportedItemStatus()
    {
        var action = () => ThreadEventParser.Parse(
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"1\",\"type\":\"command_execution\",\"command\":\"ls\",\"aggregated_output\":\"\",\"status\":\"unknown\"}}");

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception!.Message).Contains("Unsupported command execution status");
    }
}
