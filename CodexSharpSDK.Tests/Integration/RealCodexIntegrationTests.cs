using System.Text;
using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Extensions.AI;
using ManagedCode.CodexSharpSDK.Models;
using ManagedCode.CodexSharpSDK.Tests.Shared;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Tests.Integration;

[Property("RequiresCodexAuth", "true")]
public class RealCodexIntegrationTests
{
    private const string InstructionsAnswer = "native-meai-instructions-confirmed";
    private const string NativeInstructions = "Reply with exactly native-meai-instructions-confirmed. Do not use tools, execute commands or change files.";
    private const string NativeUserPrompt = "Return the exact answer specified by your system instructions.";

    [Test]
    public async Task GetStreamingResponseAsync_WithRealCodexCli_HonorsStandardInstructions()
    {
        using var settings = RealCodexTestSupport.GetRequiredSettings();
        using var client = new CodexChatClient(new CodexChatClientOptions
        {
            CodexOptions = new CodexOptions { EnvironmentVariables = settings.EnvironmentOverrides },
            DefaultModel = settings.Model,
            DefaultThreadOptions = new ThreadOptions
            {
                Model = settings.Model,
                WebSearchMode = WebSearchMode.Disabled,
                SandboxMode = SandboxMode.ReadOnly,
                NetworkAccessEnabled = true,
            },
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var text = new StringBuilder();
        var hasUsage = false;
        var hasTerminal = false;
        await foreach (var update in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, NativeUserPrompt)],
            new ChatOptions { Instructions = NativeInstructions },
            cancellation.Token))
        {
            text.Append(update.Text);
            hasUsage |= update.Contents.OfType<UsageContent>().Any();
            hasTerminal |= update.FinishReason is not null;
        }
        await Assert.That(text.ToString().Trim()).IsEqualTo(InstructionsAnswer);
        await Assert.That(hasUsage).IsTrue();
        await Assert.That(hasTerminal).IsTrue();
    }

    [Test]
    public async Task RunAsync_WithRealCodexCli_ReturnsStructuredOutput()
    {
        using var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient(settings);
        var thread = StartRealIntegrationThread(client, settings.Model);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var schema = IntegrationOutputSchemas.StatusOnly();

        var result = await thread.RunAsync<StatusResponse>(
            "Reply with a JSON object where status is exactly \"ok\".",
            schema,
            IntegrationOutputJsonContext.Default.StatusResponse,
            cancellation.Token);

        await Assert.That(result.TypedResponse.Status).IsEqualTo("ok");
        await Assert.That(result.Usage).IsNotNull();
    }

    [Test]
    public async Task RunStreamedAsync_WithRealCodexCli_YieldsCompletedTurnEvent()
    {
        using var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient(settings);
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
        using var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient(settings);
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var schema = IntegrationOutputSchemas.StatusOnly();

        var first = await thread.RunAsync<StatusResponse>(
            "Reply with a JSON object where status is exactly \"ok\".",
            schema,
            IntegrationOutputJsonContext.Default.StatusResponse,
            cancellation.Token);

        var firstThreadId = thread.Id;
        await Assert.That(firstThreadId).IsNotNull();
        await Assert.That(first.Usage).IsNotNull();

        var second = await thread.RunAsync<StatusResponse>(
            "Again: reply with a JSON object where status is exactly \"ok\".",
            schema,
            IntegrationOutputJsonContext.Default.StatusResponse,
            cancellation.Token);

        await Assert.That(second.TypedResponse.Status).IsEqualTo("ok");
        await Assert.That(second.Usage).IsNotNull();
        await Assert.That(thread.Id).IsEqualTo(firstThreadId);
    }

    [Test]
    public async Task RunAsync_WithExplicitNonEphemeralOverride_PersistsRolloutWhenClientConfigEnablesEphemeral()
    {
        using var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient(settings, new CodexOptions
        {
            Config = new JsonObject
            {
                ["ephemeral"] = true,
            },
        });
        var thread = client.StartThread(new ThreadOptions
        {
            Model = settings.Model,
            ModelReasoningEffort = ModelReasoningEffort.Medium,
            WebSearchMode = WebSearchMode.Disabled,
            SandboxMode = SandboxMode.WorkspaceWrite,
            NetworkAccessEnabled = true,
            Ephemeral = false,
        });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var result = await thread.RunAsync(
            "Reply with short plain text: ok.",
            new TurnOptions { CancellationToken = cancellation.Token });

        await Assert.That(result.Usage).IsNotNull();
        await Assert.That(thread.Id).IsNotNull();

        var rolloutPath = await RealCodexTestSupport.FindPersistedRolloutPathAsync(
            settings,
            thread.Id!,
            TimeSpan.FromSeconds(10));

        await Assert.That(rolloutPath).IsNotNull();
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
