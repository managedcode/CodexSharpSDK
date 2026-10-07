using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Extensions.AI.Content;
using ManagedCode.CodexSharpSDK.Extensions.AI.Internal;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Tests;

public class StreamingEventMapperTests
{
    private const string NativeActivityProperty = "managedcode:activity";
    private const string NativeActivityPhaseProperty = "managedcode:activity_phase";
    private const string CommandExecutionActivity = "command_execution";
    private const string ActivityCompleted = "completed";
    private const string NativeToolActivity = "mcp_tool";
    private const string ActivityStarted = "started";

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
    public async Task ToUpdates_TurnFailed_DisposesSourceBeforeThrowingConfirmedFailure()
    {
        var sourceDisposed = false;

        var exception = await Assert.That(async () => await CollectUpdates(ProviderFailureEvents(() => sourceDisposed = true)))
            .ThrowsException();

        await Assert.That(exception).IsTypeOf<CliExecutionFailureException>();
        await Assert.That(((CliExecutionFailureException)exception!).RootProcessExitConfirmed).IsTrue();
        await Assert.That(((CliExecutionFailureException)exception!).ExitCode).IsNull();
        await Assert.That(sourceDisposed).IsTrue();
    }

    private static async IAsyncEnumerable<ThreadEvent> ProviderFailureEvents(Action onDispose)
    {
        try
        {
            yield return new TurnFailedEvent(new ThreadError("something broke"));
            await Task.Yield();
        }
        finally
        {
            onDispose();
        }
    }

    [Test]
    public async Task ToUpdates_CommandExecution_PreservesTypedContentAndAddsSafeActivityMetadata()
    {
        var events = ToAsyncEnumerable(
            new ItemCompletedEvent(
                new CommandExecutionItem("c1", "ls", "file.txt", 0, CommandExecutionStatus.Completed)));
        var updates = await CollectUpdates(events);
        await Assert.That(updates[0].Contents.Count).IsEqualTo(1);
        var content = updates[0].Contents.OfType<CommandExecutionContent>().Single();
        await Assert.That(content.Command).IsEqualTo("ls");
        await Assert.That(content.AggregatedOutput).IsEqualTo("file.txt");
        await Assert.That(updates[0].AdditionalProperties![NativeActivityProperty]).IsEqualTo(CommandExecutionActivity);
        await Assert.That(updates[0].AdditionalProperties![NativeActivityPhaseProperty]).IsEqualTo(ActivityCompleted);
    }

    [Test]
    public async Task ToUpdates_CompletedNativeItems_PreserveTypedContentAndAddSafeMetadata()
    {
        var events = ToAsyncEnumerable(
            new ItemCompletedEvent(new FileChangeItem("f", [new FileUpdateChange("private-path", PatchChangeKind.Update)], PatchApplyStatus.Completed)),
            new ItemCompletedEvent(new McpToolCallItem("m", "private-server", "private-tool", new JsonObject { ["secret"] = "value" }, null, null, McpToolCallStatus.Completed)),
            new ItemCompletedEvent(new WebSearchItem("w", "private search")),
            new ItemCompletedEvent(new CollabToolCallItem("c", CollabTool.SpawnAgent, "sender", ["receiver"], "private prompt",
                new Dictionary<string, CollabAgentState> { ["receiver"] = new(CollabAgentStatus.Running, "private state") }, CollabToolCallStatus.Completed)));

        var updates = await CollectUpdates(events);
        await Assert.That(updates[0].Contents.OfType<FileChangeContent>().Single().Changes[0].Path).IsEqualTo("private-path");
        await Assert.That(updates[1].Contents.OfType<McpToolCallContent>().Single().Tool).IsEqualTo("private-tool");
        await Assert.That(updates[2].Contents.OfType<WebSearchContent>().Single().Query).IsEqualTo("private search");
        await Assert.That(updates[3].Contents.OfType<CollabToolCallContent>().Single().Tool).IsEqualTo(CollabTool.SpawnAgent);
        foreach (var update in updates)
        {
            await Assert.That(update.AdditionalProperties![NativeActivityProperty]).IsNotNull();
            await Assert.That(update.AdditionalProperties![NativeActivityPhaseProperty]).IsEqualTo(ActivityCompleted);
        }
    }

    [Test]
    public async Task ToUpdates_McpToolLifecycle_ExposesOnlySafeCategoryAndPhase()
    {
        var item = new McpToolCallItem(
            "tool-call-id",
            "sensitive-server",
            "sensitive-tool-name",
            new JsonObject { ["argument"] = "private" },
            null,
            null,
            McpToolCallStatus.InProgress);
        var updates = await CollectUpdates(ToAsyncEnumerable(
            new ThreadStartedEvent("thread-1"),
            new ItemStartedEvent(item)));

        var update = updates[^1];
        await Assert.That(update.Contents.Count).IsEqualTo(0);
        await Assert.That(update.ConversationId).IsEqualTo("thread-1");
        await Assert.That(update.AdditionalProperties!.Count).IsEqualTo(2);
        await Assert.That(update.AdditionalProperties[NativeActivityProperty]).IsEqualTo(NativeToolActivity);
        await Assert.That(update.AdditionalProperties[NativeActivityPhaseProperty]).IsEqualTo(ActivityStarted);
    }

    [Test]
    public async Task ToUpdates_AgentMessageUpdateAndCompletion_EmitsOneAuthoritativeSnapshotPerIdentity()
    {
        var events = ToAsyncEnumerable(
            new ItemUpdatedEvent(new AgentMessageItem("message-1", "Hello")),
            new ItemUpdatedEvent(new AgentMessageItem("message-1", "Hello world")),
            new ItemCompletedEvent(new AgentMessageItem("message-1", "Hello world")),
            new ItemCompletedEvent(new AgentMessageItem("message-1", "Hello world")));

        var updates = await CollectUpdates(events);

        await Assert.That(updates.Count).IsEqualTo(1);
        await Assert.That(updates[0].Text).IsEqualTo("Hello world");
        await Assert.That(updates[0].Role).IsEqualTo(ChatRole.Assistant);
    }

    [Test]
    public async Task ToUpdates_CompletionOnlyAgentMessages_EmitsEachDistinctItem()
    {
        var updates = await CollectUpdates(ToAsyncEnumerable(
            new ItemCompletedEvent(new AgentMessageItem("message-1", "first")),
            new ItemCompletedEvent(new AgentMessageItem("message-2", "second"))));

        await Assert.That(updates.Count).IsEqualTo(2);
        await Assert.That(updates[0].Text).IsEqualTo("first");
        await Assert.That(updates[1].Text).IsEqualTo("second");
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
