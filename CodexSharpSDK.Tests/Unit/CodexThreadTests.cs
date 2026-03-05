using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Execution;
using ManagedCode.CodexSharpSDK.Models;
using ManagedCode.CodexSharpSDK.Tests.Shared;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

public class CodexThreadTests
{
    [Test]
    public async Task RunAsync_WithRealCodexCli_ReturnsCompletedTurnAndUpdatesThreadId()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var result = await thread.RunAsync(
            "Reply with short plain text: ok.",
            new TurnOptions { CancellationToken = cancellation.Token });

        await Assert.That(thread.Id).IsNotNull();
        await Assert.That(result.FinalResponse).IsNotNull();
        await Assert.That(result.Usage).IsNotNull();
    }

    [Test]
    public async Task RunAsync_WithStructuredInput_ReturnsTypedJson()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var schema = IntegrationOutputSchemas.StatusOnly();

        var result = await thread.RunAsync(
        [
            new TextInput("Reply with a JSON object."),
            new TextInput("Set status exactly to \"ok\"."),
        ],
        new TurnOptions
        {
            OutputSchema = schema,
            CancellationToken = cancellation.Token,
        });

        var response = IntegrationOutputDeserializer.Deserialize<StatusResponse>(result.FinalResponse);
        await Assert.That(response.Status).IsEqualTo("ok");
    }

    [Test]
    public async Task RunAsync_SecondTurnKeepsThreadId_WithRealCodexCli()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var first = await thread.RunAsync(
            "Reply with short plain text: first.",
            new TurnOptions { CancellationToken = cancellation.Token });

        var firstThreadId = thread.Id;
        await Assert.That(firstThreadId).IsNotNull();
        await Assert.That(first.Usage).IsNotNull();

        var second = await thread.RunAsync(
            "Reply with short plain text: second.",
            new TurnOptions { CancellationToken = cancellation.Token });

        await Assert.That(second.Usage).IsNotNull();
        await Assert.That(thread.Id).IsEqualTo(firstThreadId);
    }

    [Test]
    public async Task RunStreamedAsync_YieldsCompletedTurnEvent_WithRealCodexCli()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var streamed = await thread.RunStreamedAsync(
            "Reply with short plain text: ok.",
            new TurnOptions { CancellationToken = cancellation.Token });

        var hasTurnCompleted = false;
        var hasTurnFailed = false;
        var hasCompletedItem = false;

        await foreach (var threadEvent in streamed.Events.WithCancellation(cancellation.Token))
        {
            hasTurnCompleted |= threadEvent is TurnCompletedEvent;
            hasTurnFailed |= threadEvent is TurnFailedEvent;
            hasCompletedItem |= threadEvent is ItemCompletedEvent;
        }

        await Assert.That(hasCompletedItem).IsTrue();
        await Assert.That(hasTurnCompleted).IsTrue();
        await Assert.That(hasTurnFailed).IsFalse();
        await Assert.That(thread.Id).IsNotNull();
    }

    [Test]
    public async Task RunAsync_HonorsCancellationToken()
    {
        var exec = new CodexExec("codex");
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var action = async () => await thread.RunAsync("cancel", new TurnOptions
        {
            CancellationToken = cancellationSource.Token,
        });

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<OperationCanceledException>();
    }

    [Test]
    public async Task RunAsync_ThrowsObjectDisposedExceptionAfterDispose()
    {
        var exec = new CodexExec("codex");
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());
        thread.Dispose();

        async Task<RunResult> Action() => await thread.RunAsync("after-dispose");
        var exception = await Assert.That(Action!).ThrowsException();
        await Assert.That(exception).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public Task Dispose_CanBeCalledMultipleTimes()
    {
        var exec = new CodexExec("codex");
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        thread.Dispose();
        thread.Dispose();

        return Task.CompletedTask;
    }

    private static CodexThread StartRealIntegrationThread(CodexClient client, string model)
    {
        return client.StartThread(new ThreadOptions
        {
            Model = model,
            ModelReasoningEffort = ModelReasoningEffort.Medium,
            WebSearchMode = WebSearchMode.Disabled,
            SandboxMode = SandboxMode.WorkspaceWrite,
            NetworkAccessEnabled = true,
        });
    }
}
