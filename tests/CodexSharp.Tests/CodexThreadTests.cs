using System.Text.Json.Nodes;
using ManagedCode.CodexSharp.Exceptions;
using ManagedCode.CodexSharp.Tests.Fakes;

namespace ManagedCode.CodexSharp.Tests;

public class CodexThreadTests
{
    [Test]
    public async Task RunAsync_ReturnsCompletedTurnAndUpdatesThreadId()
    {
        var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines(
        [
            "{\"type\":\"thread.started\",\"thread_id\":\"thread_1\"}",
            "{\"type\":\"turn.started\"}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"agent_message\",\"text\":\"Hi!\"}}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":42,\"cached_input_tokens\":12,\"output_tokens\":5}}",
        ], cancellationToken));

        var exec = new CodexExec("codex", null, null, runner);
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        var result = await thread.RunAsync("Hello");

        await Assert.That(thread.Id).IsEqualTo("thread_1");
        await Assert.That(result.FinalResponse).IsEqualTo("Hi!");
        await Assert.That(result.Items).HasCount(1);
        await Assert.That(result.Items[0]).IsTypeOf<AgentMessageItem>();
        await Assert.That(result.Usage).IsNotNull();
        await Assert.That(result.Usage!.InputTokens).IsEqualTo(42);
        await Assert.That(result.Usage.CachedInputTokens).IsEqualTo(12);
        await Assert.That(result.Usage.OutputTokens).IsEqualTo(5);
    }

    [Test]
    public async Task RunAsync_WithStructuredInput_ForwardsPromptAndImages()
    {
        string? outputSchemaPath = null;

        var runner = new FakeCodexProcessRunner((invocation, cancellationToken) =>
        {
            outputSchemaPath = FindFlagValue(invocation.Arguments, "--output-schema");
            return FakeCodexProcessRunner.YieldLines(
            [
                "{\"type\":\"thread.started\",\"thread_id\":\"thread_2\"}",
                "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"agent_message\",\"text\":\"Done\"}}",
                "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1,\"cached_input_tokens\":0,\"output_tokens\":1}}",
            ], cancellationToken);
        });

        var exec = new CodexExec("codex", null, null, runner);
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["answer"] = new JsonObject
                {
                    ["type"] = "string",
                },
            },
            ["required"] = new JsonArray("answer"),
            ["additionalProperties"] = false,
        };

        await thread.RunAsync(
        [
            new TextInput("Describe files"),
            new TextInput("Focus on tests"),
            new LocalImageInput("first.png"),
            new LocalImageInput("second.jpg"),
        ], new TurnOptions { OutputSchema = schema });

        var invocation = runner.Invocations.Single();
        await Assert.That(invocation.Input).IsEqualTo("Describe files\n\nFocus on tests");
        await Assert.That(CollectFlagValues(invocation.Arguments, "--image")).IsEquivalentTo(["first.png", "second.jpg"]);

        await Assert.That(outputSchemaPath).IsNotNull();
        await Assert.That(File.Exists(outputSchemaPath!)).IsFalse();
    }

    [Test]
    public async Task RunAsync_SecondTurnUsesResumeArgument()
    {
        var runs = new Queue<IReadOnlyList<string>>([
            [
                "{\"type\":\"thread.started\",\"thread_id\":\"thread_3\"}",
                "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"agent_message\",\"text\":\"First\"}}",
                "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1,\"cached_input_tokens\":0,\"output_tokens\":1}}",
            ],
            [
                "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_2\",\"type\":\"agent_message\",\"text\":\"Second\"}}",
                "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1,\"cached_input_tokens\":0,\"output_tokens\":1}}",
            ],
        ]);

        var runner = new FakeCodexProcessRunner((_, cancellationToken) =>
        {
            var lines = runs.Dequeue();
            return FakeCodexProcessRunner.YieldLines(lines, cancellationToken);
        });

        var exec = new CodexExec("codex", null, null, runner);
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        await thread.RunAsync("First prompt");
        await thread.RunAsync("Second prompt");

        var secondInvocation = runner.Invocations[1];
        var resumeIndex = secondInvocation.Arguments.IndexOf("resume");
        await Assert.That(resumeIndex).IsGreaterThan(-1);
        await Assert.That(secondInvocation.Arguments[resumeIndex + 1]).IsEqualTo("thread_3");
    }

    [Test]
    public async Task RunAsync_ThrowsThreadRunExceptionOnTurnFailure()
    {
        var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines(
        [
            "{\"type\":\"thread.started\",\"thread_id\":\"thread_fail\"}",
            "{\"type\":\"turn.failed\",\"error\":{\"message\":\"rate limit exceeded\"}}",
        ], cancellationToken));

        var exec = new CodexExec("codex", null, null, runner);
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        var action = async () => await thread.RunAsync("fail");
        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ThreadRunException>();
        await Assert.That(exception!.Message).Contains("rate limit exceeded");
    }

    [Test]
    public async Task RunStreamedAsync_YieldsAllEvents()
    {
        var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines(
        [
            "{\"type\":\"thread.started\",\"thread_id\":\"thread_stream\"}",
            "{\"type\":\"turn.started\"}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"agent_message\",\"text\":\"Hello\"}}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":2,\"cached_input_tokens\":0,\"output_tokens\":2}}",
        ], cancellationToken));

        var exec = new CodexExec("codex", null, null, runner);
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        var streamed = await thread.RunStreamedAsync("Hello");
        var events = await DrainEventsAsync(streamed.Events);

        await Assert.That(events).HasCount(4);
        await Assert.That(events[0]).IsTypeOf<ThreadStartedEvent>();
        await Assert.That(events[1]).IsTypeOf<TurnStartedEvent>();
        await Assert.That(events[2]).IsTypeOf<ItemCompletedEvent>();
        await Assert.That(events[3]).IsTypeOf<TurnCompletedEvent>();
        await Assert.That(thread.Id).IsEqualTo("thread_stream");
    }

    [Test]
    public async Task RunAsync_ThrowsWhenEventIsNotJson()
    {
        var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines(
        [
            "not-json",
        ], cancellationToken));

        var exec = new CodexExec("codex", null, null, runner);
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        var action = async () => await thread.RunAsync("bad");
        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception!.Message).Contains("Failed to parse item");
    }

    [Test]
    public async Task RunAsync_HonorsCancellationToken()
    {
        var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines(
        [
            "{\"type\":\"turn.started\"}",
        ], cancellationToken));

        var exec = new CodexExec("codex", null, null, runner);
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var action = async () => await thread.RunAsync("cancel", new TurnOptions
        {
            CancellationToken = cancellationSource.Token,
        });

        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task RunAsync_IsSerializedForConcurrentCallsOnSameThread()
    {
        var activeRuns = 0;
        var maxActiveRuns = 0;
        var responseCounter = 0;

        var runner = new FakeCodexProcessRunner((_, cancellationToken) => Sequence(cancellationToken));
        var exec = new CodexExec("codex", null, null, runner);
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        var firstRun = thread.RunAsync("first");
        var secondRun = thread.RunAsync("second");

        await Task.WhenAll(firstRun, secondRun);

        await Assert.That(maxActiveRuns).IsEqualTo(1);
        await Assert.That(runner.Invocations).HasCount(2);

        var secondInvocation = runner.Invocations[1];
        var resumeIndex = secondInvocation.Arguments.IndexOf("resume");
        await Assert.That(resumeIndex).IsGreaterThan(-1);
        await Assert.That(secondInvocation.Arguments[resumeIndex + 1]).IsEqualTo("thread_serialized");

        return;

        async IAsyncEnumerable<string> Sequence([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var current = Interlocked.Increment(ref activeRuns);
            UpdateMax(current);
            var localCount = Interlocked.Increment(ref responseCounter);

            try
            {
                if (localCount == 1)
                {
                    yield return "{\"type\":\"thread.started\",\"thread_id\":\"thread_serialized\"}";
                }

                await Task.Delay(75, ct);
                yield return $"{{\"type\":\"item.completed\",\"item\":{{\"id\":\"item_{localCount}\",\"type\":\"agent_message\",\"text\":\"response_{localCount}\"}}}}";
                yield return "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1,\"cached_input_tokens\":0,\"output_tokens\":1}}";
            }
            finally
            {
                Interlocked.Decrement(ref activeRuns);
            }
        }

        void UpdateMax(int current)
        {
            while (true)
            {
                var snapshot = Volatile.Read(ref maxActiveRuns);
                if (current <= snapshot)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref maxActiveRuns, current, snapshot) == snapshot)
                {
                    return;
                }
            }
        }
    }

    [Test]
    public async Task RunAsync_ThrowsObjectDisposedExceptionAfterDispose()
    {
        var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines(
        [
            "{\"type\":\"thread.started\",\"thread_id\":\"thread_disposed\"}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1,\"cached_input_tokens\":0,\"output_tokens\":1}}",
        ], cancellationToken));

        var exec = new CodexExec("codex", null, null, runner);
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());
        thread.Dispose();

        var action = async () => await thread.RunAsync("after-dispose");
        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public Task Dispose_CanBeCalledMultipleTimes()
    {
        var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines([], cancellationToken));
        var exec = new CodexExec("codex", null, null, runner);
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        thread.Dispose();
        thread.Dispose();

        return Task.CompletedTask;
    }

    private static async Task<List<ThreadEvent>> DrainEventsAsync(IAsyncEnumerable<ThreadEvent> events)
    {
        var result = new List<ThreadEvent>();
        await foreach (var threadEvent in events)
        {
            result.Add(threadEvent);
        }

        return result;
    }

    private static string? FindFlagValue(IReadOnlyList<string> args, string flag)
    {
        var index = args.IndexOf(flag);
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }

    private static List<string> CollectFlagValues(IReadOnlyList<string> args, string flag)
    {
        var result = new List<string>();
        for (var index = 0; index < args.Count - 1; index += 1)
        {
            if (args[index] == flag)
            {
                result.Add(args[index + 1]);
            }
        }

        return result;
    }
}
