using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Models;
using ManagedCode.CodexSharpSDK.Tests.Shared;

namespace ManagedCode.CodexSharpSDK.Tests.Integration;

public class RealCodexIntegrationTests
{
    [Test]
    public async Task RunAsync_WithRealCodexCli_ReturnsStructuredOutput()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var schema = IntegrationOutputSchemas.StatusOnly();

        var result = await thread.RunAsync(
            "Reply with a JSON object where status is exactly \"ok\".",
            new TurnOptions
            {
                OutputSchema = schema,
                CancellationToken = cancellation.Token,
            });

        var response = IntegrationOutputDeserializer.Deserialize<StatusResponse>(result.FinalResponse);
        await Assert.That(response.Status).IsEqualTo("ok");
        await Assert.That(result.Usage).IsNotNull();
    }

    [Test]
    public async Task RunStreamedAsync_WithRealCodexCli_YieldsCompletedTurnEvent()
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
    public async Task RunAsync_WithRealCodexCli_SecondTurnKeepsThreadId()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var schema = IntegrationOutputSchemas.StatusOnly();

        var first = await thread.RunAsync(
            "Reply with a JSON object where status is exactly \"ok\".",
            new TurnOptions
            {
                OutputSchema = schema,
                CancellationToken = cancellation.Token,
            });

        var firstThreadId = thread.Id;
        await Assert.That(firstThreadId).IsNotNull();
        await Assert.That(first.Usage).IsNotNull();

        var second = await thread.RunAsync(
            "Again: reply with a JSON object where status is exactly \"ok\".",
            new TurnOptions
            {
                OutputSchema = schema,
                CancellationToken = cancellation.Token,
            });

        var secondResponse = IntegrationOutputDeserializer.Deserialize<StatusResponse>(second.FinalResponse);
        await Assert.That(secondResponse.Status).IsEqualTo("ok");
        await Assert.That(second.Usage).IsNotNull();
        await Assert.That(thread.Id).IsEqualTo(firstThreadId);
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
