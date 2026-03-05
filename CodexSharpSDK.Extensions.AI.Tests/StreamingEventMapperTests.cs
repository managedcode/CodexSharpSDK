using ManagedCode.CodexSharpSDK.Extensions.AI.Content;
using ManagedCode.CodexSharpSDK.Extensions.AI.Internal;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Tests;

public class StreamingEventMapperTests
{
    [Test]
    public async Task ToUpdates_ThreadStarted_YieldsConversationId()
    {
        var events = ToAsyncEnumerable(new ThreadStartedEvent("thread-1"));
        var updates = await CollectUpdates(events);
        await Assert.That(updates[0].ConversationId).IsEqualTo("thread-1");
    }

    [Test]
    public async Task ToUpdates_AgentMessage_YieldsTextContent()
    {
        var events = ToAsyncEnumerable(
            new ItemCompletedEvent(new AgentMessageItem("m1", "Hello")));
        var updates = await CollectUpdates(events);
        await Assert.That(updates[0].Text).IsEqualTo("Hello");
        await Assert.That(updates[0].Role).IsEqualTo(ChatRole.Assistant);
    }

    [Test]
    public async Task ToUpdates_TurnCompleted_YieldsFinishReason()
    {
        var events = ToAsyncEnumerable(
            new TurnCompletedEvent(new Usage(10, 0, 5)));
        var updates = await CollectUpdates(events);
        await Assert.That(updates[0].FinishReason).IsEqualTo(ChatFinishReason.Stop);
    }

    [Test]
    public async Task ToUpdates_TurnFailed_ThrowsException()
    {
        var events = ToAsyncEnumerable(
            new TurnFailedEvent(new ThreadError("something broke")));

        await Assert.That(async () => await CollectUpdates(events))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task ToUpdates_CommandExecution_YieldsCustomContent()
    {
        var events = ToAsyncEnumerable(
            new ItemCompletedEvent(
                new CommandExecutionItem("c1", "ls", "file.txt", 0, CommandExecutionStatus.Completed)));
        var updates = await CollectUpdates(events);
        var content = updates[0].Contents.OfType<CommandExecutionContent>().Single();
        await Assert.That(content.Command).IsEqualTo("ls");
    }

    [Test]
    public async Task ToUpdates_FullSequence_MapsAllEvents()
    {
        var events = ToAsyncEnumerable(
            new ThreadStartedEvent("t1"),
            new TurnStartedEvent(),
            new ItemCompletedEvent(new ReasoningItem("r1", "thinking")),
            new ItemCompletedEvent(new AgentMessageItem("m1", "answer")),
            new TurnCompletedEvent(new Usage(10, 0, 5)));

        var updates = await CollectUpdates(events);
        // TurnStartedEvent is not matched in the switch, so 4 updates expected
        await Assert.That(updates.Count).IsGreaterThanOrEqualTo(4);
    }

    private static async IAsyncEnumerable<ThreadEvent> ToAsyncEnumerable(params ThreadEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
            await Task.CompletedTask;
        }
    }

    private static async Task<List<ChatResponseUpdate>> CollectUpdates(IAsyncEnumerable<ThreadEvent> events)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in StreamingEventMapper.ToUpdates(events))
        {
            updates.Add(update);
        }

        return updates;
    }
}
