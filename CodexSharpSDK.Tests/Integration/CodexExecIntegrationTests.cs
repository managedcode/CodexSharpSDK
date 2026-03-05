using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Execution;
using ManagedCode.CodexSharpSDK.Tests.Shared;

namespace ManagedCode.CodexSharpSDK.Tests.Integration;

[Property("RequiresCodexAuth", "true")]
public class CodexExecIntegrationTests
{
    private const string FirstPrompt = "Reply with short plain text: first.";
    private const string SecondPrompt = "Reply with short plain text: second.";
    private const string InvalidModel = "__codexsharp_invalid_model__";

    [Test]
    public async Task RunAsync_UsesDefaultProcessRunner_EndToEnd()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        var exec = new CodexExec();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var lines = await DrainToListAsync(exec.RunAsync(new CodexExecArgs
        {
            Input = FirstPrompt,
            Model = settings.Model,
            ModelReasoningEffort = ModelReasoningEffort.Medium,
            WebSearchMode = WebSearchMode.Disabled,
            SandboxMode = SandboxMode.WorkspaceWrite,
            NetworkAccessEnabled = true,
            CancellationToken = cancellation.Token,
        }));

        await Assert.That(lines.Any(line => line.Contains("\"type\":\"thread.started\"", StringComparison.Ordinal))).IsTrue();
        await Assert.That(lines.Any(line => line.Contains("\"type\":\"turn.completed\"", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task RunAsync_SecondCallPassesResumeArgument_EndToEnd()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var thread = client.StartThread(new ThreadOptions
        {
            Model = settings.Model,
            ModelReasoningEffort = ModelReasoningEffort.Medium,
            WebSearchMode = WebSearchMode.Disabled,
            SandboxMode = SandboxMode.WorkspaceWrite,
            NetworkAccessEnabled = true,
        });

        var firstResult = await thread.RunAsync(
            FirstPrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        var threadId = thread.Id;
        await Assert.That(threadId).IsNotNull();
        await Assert.That(firstResult.Usage).IsNotNull();

        var secondResult = await thread.RunAsync(
            SecondPrompt,
            new TurnOptions { CancellationToken = cancellation.Token });

        await Assert.That(secondResult.Usage).IsNotNull();
        await Assert.That(thread.Id).IsEqualTo(threadId);
    }

    [Test]
    public async Task RunAsync_PropagatesNonZeroExitCode_EndToEnd()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        var exec = new CodexExec();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var action = async () => await DrainAsync(exec.RunAsync(new CodexExecArgs
        {
            Input = FirstPrompt,
            Model = InvalidModel,
            WebSearchMode = WebSearchMode.Disabled,
            SandboxMode = SandboxMode.WorkspaceWrite,
            NetworkAccessEnabled = true,
            CancellationToken = cancellation.Token,
        }));

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("exited with code");
    }

    private static async Task DrainAsync(IAsyncEnumerable<string> lines)
    {
        await foreach (var _ in lines)
        {
            // Intentionally empty.
        }
    }

    private static async Task<List<string>> DrainToListAsync(IAsyncEnumerable<string> lines)
    {
        var result = new List<string>();

        await foreach (var line in lines)
        {
            result.Add(line);
        }

        return result;
    }
}
