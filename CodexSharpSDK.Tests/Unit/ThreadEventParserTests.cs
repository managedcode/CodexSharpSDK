using ManagedCode.CodexSharpSDK.Internal;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

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

        await Assert.That(parsed).Count().IsEqualTo(8);
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
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"9\",\"type\":\"collab_tool_call\",\"tool\":\"spawn_agent\",\"sender_thread_id\":\"sender_thread\",\"receiver_thread_ids\":[\"agent_1\"],\"prompt\":\"delegate\",\"agents_states\":{\"agent_1\":{\"status\":\"completed\",\"message\":\"done\"}},\"status\":\"completed\"}}",
        };

        var parsedItems = events
            .Select(ThreadEventParser.Parse)
            .OfType<ItemCompletedEvent>()
            .Select(itemCompleted => itemCompleted.Item)
            .ToList();

        await Assert.That(parsedItems).Count().IsEqualTo(9);
        await Assert.That(parsedItems[0]).IsTypeOf<AgentMessageItem>();
        await Assert.That(parsedItems[1]).IsTypeOf<ReasoningItem>();
        await Assert.That(parsedItems[2]).IsTypeOf<CommandExecutionItem>();
        await Assert.That(parsedItems[3]).IsTypeOf<FileChangeItem>();
        await Assert.That(parsedItems[4]).IsTypeOf<McpToolCallItem>();
        await Assert.That(parsedItems[5]).IsTypeOf<WebSearchItem>();
        await Assert.That(parsedItems[6]).IsTypeOf<TodoListItem>();
        await Assert.That(parsedItems[7]).IsTypeOf<ErrorItem>();
        await Assert.That(parsedItems[8]).IsTypeOf<CollabToolCallItem>();
    }

    [Test]
    public async Task Parse_FileChangeStartedWithInProgressStatus_IsSupported()
    {
        var parsed = ThreadEventParser.Parse(
            "{\"type\":\"item.started\",\"item\":{\"id\":\"item_1\",\"type\":\"file_change\",\"changes\":[{\"path\":\"C:/git/CodexSandbox/apple.txt\",\"kind\":\"add\"},{\"path\":\"C:/git/CodexSandbox/banana.txt\",\"kind\":\"add\"}],\"status\":\"in_progress\"}}");

        var item = (FileChangeItem)((ItemStartedEvent)parsed).Item;

        await Assert.That(item.Id).IsEqualTo("item_1");
        await Assert.That(item.Status).IsEqualTo(PatchApplyStatus.InProgress);
        await Assert.That(item.Changes.Select(change => change.Path))
            .IsEquivalentTo(["C:/git/CodexSandbox/apple.txt", "C:/git/CodexSandbox/banana.txt"]);
        await Assert.That(item.Changes.Select(change => change.Kind))
            .IsEquivalentTo([PatchChangeKind.Add, PatchChangeKind.Add]);
    }

    [Test]
    public async Task Parse_ParsesCollabToolCallDetails()
    {
        var parsed = ThreadEventParser.Parse(
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"1\",\"type\":\"collab_tool_call\",\"tool\":\"wait\",\"sender_thread_id\":\"sender\",\"receiver_thread_ids\":[\"a1\",\"a2\"],\"prompt\":null,\"agents_states\":{\"a1\":{\"status\":\"running\",\"message\":null},\"a2\":{\"status\":\"not_found\",\"message\":\"missing\"}},\"status\":\"in_progress\"}}");

        var item = (CollabToolCallItem)((ItemCompletedEvent)parsed).Item;

        await Assert.That(item.Tool).IsEqualTo(CollabTool.Wait);
        await Assert.That(item.SenderThreadId).IsEqualTo("sender");
        await Assert.That(item.ReceiverThreadIds).IsEquivalentTo(["a1", "a2"]);
        await Assert.That(item.Prompt).IsNull();
        await Assert.That(item.Status).IsEqualTo(CollabToolCallStatus.InProgress);
        await Assert.That(item.AgentsStates["a1"].Status).IsEqualTo(CollabAgentStatus.Running);
        await Assert.That(item.AgentsStates["a2"].Status).IsEqualTo(CollabAgentStatus.NotFound);
        await Assert.That(item.AgentsStates["a2"].Message).IsEqualTo("missing");
    }

    [Test]
    public async Task Parse_ParsesAllSupportedCollabTools()
    {
        var expectedByTool = new Dictionary<string, CollabTool>(StringComparer.Ordinal)
        {
            ["spawn_agent"] = CollabTool.SpawnAgent,
            ["send_input"] = CollabTool.SendInput,
            ["wait"] = CollabTool.Wait,
            ["close_agent"] = CollabTool.CloseAgent,
        };

        foreach (var pair in expectedByTool)
        {
            var parsed = ThreadEventParser.Parse(
                $"{{\"type\":\"item.completed\",\"item\":{{\"id\":\"tool_{pair.Key}\",\"type\":\"collab_tool_call\",\"tool\":\"{pair.Key}\",\"sender_thread_id\":\"sender\",\"receiver_thread_ids\":[\"agent_1\"],\"prompt\":\"p\",\"agents_states\":{{\"agent_1\":{{\"status\":\"pending_init\",\"message\":null}}}},\"status\":\"completed\"}}}}");

            var item = (CollabToolCallItem)((ItemCompletedEvent)parsed).Item;
            await Assert.That(item.Tool).IsEqualTo(pair.Value);
        }
    }

    [Test]
    public async Task Parse_PreservesUnknownEventTypeAndPayload()
    {
        var parsed = (UnknownThreadEvent)ThreadEventParser.Parse("{\"type\":\"future.event\",\"value\":42}");

        await Assert.That(parsed.EventType).IsEqualTo("future.event");
        await Assert.That(parsed.Payload["value"]!.GetValue<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task Parse_PreservesUnknownItemTypeAndPayload()
    {
        var parsed = (ItemCompletedEvent)ThreadEventParser.Parse(
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"1\",\"type\":\"future_item\",\"value\":42}}");
        var item = (UnknownThreadItem)parsed.Item;

        await Assert.That(item.ItemType).IsEqualTo("future_item");
        await Assert.That(item.Payload["value"]!.GetValue<int>()).IsEqualTo(42);
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
