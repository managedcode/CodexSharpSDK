using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Models;
using ManagedCode.CodexSharpSDK.Tests.Shared;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

public class CodexClientTests
{
    [Test]
    public async Task StartAsync_CanBeCalledConcurrently()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexExecutablePath = "codex",
            },
            AutoStart = false,
        });

        var starts = Enumerable.Range(0, 64)
            .Select(_ => client.StartAsync())
            .ToArray();

        await Task.WhenAll(starts);
        await Assert.That(client.State).IsEqualTo(CodexClientState.Connected);
    }

    [Test]
    public async Task StartThread_CanBeCalledConcurrently()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexExecutablePath = "codex",
            },
            AutoStart = false,
        });

        await client.StartAsync();

        var createdThreads = await Task.WhenAll(
            Enumerable.Range(0, 64)
                .Select(_ => Task.Run(() => client.StartThread())));

        await Assert.That(createdThreads).HasCount(64);
        await Assert.That(createdThreads.All(thread => thread.Id is null)).IsTrue();
        await Assert.That(client.State).IsEqualTo(CodexClientState.Connected);
    }

    [Test]
    public async Task StopAsync_CanBeCalledConcurrently()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexExecutablePath = "codex",
            },
            AutoStart = false,
        });

        await client.StartAsync();

        var stops = Enumerable.Range(0, 64)
            .Select(_ => client.StopAsync())
            .ToArray();

        await Task.WhenAll(stops);
        await Assert.That(client.State).IsEqualTo(CodexClientState.Disconnected);
    }

    [Test]
    public async Task StartAsync_IsIdempotentAndSetsConnectedState()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexExecutablePath = "codex",
            },
            AutoStart = false,
        });

        await client.StartAsync();
        await client.StartAsync();

        await Assert.That(client.State).IsEqualTo(CodexClientState.Connected);
    }

    [Test]
    public async Task StartThread_AutoStartEnabledStartsImplicitly()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexExecutablePath = "codex",
            },
        });

        var thread = client.StartThread();

        await Assert.That(thread.Id).IsNull();
        await Assert.That(client.State).IsEqualTo(CodexClientState.Connected);
    }

    [Test]
    public async Task StartThread_ParameterlessClientUsesDefaultAutoStart()
    {
        using var client = new CodexClient();

        var thread = client.StartThread(new ThreadOptions
        {
            Model = CodexModels.Gpt53Codex,
            ModelReasoningEffort = ModelReasoningEffort.Medium,
        });

        await Assert.That(thread.Id).IsNull();
        await Assert.That(client.State).IsEqualTo(CodexClientState.Connected);
    }

    [Test]
    public async Task ResumeThread_CreatesThreadWithProvidedId()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexExecutablePath = "codex",
            },
        });

        var thread = client.ResumeThread("thread_1");
        await Assert.That(thread.Id).IsEqualTo("thread_1");
    }

    [Test]
    public async Task ResumeThread_ThrowsForInvalidId()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexExecutablePath = "codex",
            },
        });

        var action = () => client.ResumeThread(" ");
        await Assert.That(action).ThrowsException();
    }

    [Test]
    public async Task StartThread_ThrowsWhenAutoStartDisabled()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexExecutablePath = "codex",
            },
            AutoStart = false,
        });

        var action = () => client.StartThread();
        await Assert.That(action).ThrowsException();
        await Assert.That(client.State).IsEqualTo(CodexClientState.Disconnected);
    }

    [Test]
    public async Task StopAsync_SetsDisconnectedState()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexExecutablePath = "codex",
            },
            AutoStart = false,
        });

        await client.StartAsync();
        await client.StopAsync();

        await Assert.That(client.State).IsEqualTo(CodexClientState.Disconnected);
    }

    [Test]
    public async Task Dispose_SetsDisposedStateAndBlocksOperations()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexExecutablePath = "codex",
            },
            AutoStart = false,
        });

        client.Dispose();

        await Assert.That(client.State).IsEqualTo(CodexClientState.Disposed);

        var action = async () => await client.StartAsync();
        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public async Task Dispose_CanBeCalledConcurrently()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexExecutablePath = "codex",
            },
            AutoStart = false,
        });

        var disposals = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => client.Dispose()))
            .ToArray();

        await Task.WhenAll(disposals);
        await Assert.That(client.State).IsEqualTo(CodexClientState.Disposed);
    }

    [Test]
    public async Task ResumeThread_WithThreadOptions_RunsWithRealCodexCli()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var startedThread = client.StartThread(new ThreadOptions
        {
            Model = settings.Model,
            ModelReasoningEffort = ModelReasoningEffort.Medium,
            WebSearchMode = WebSearchMode.Disabled,
            SandboxMode = SandboxMode.WorkspaceWrite,
            NetworkAccessEnabled = true,
        });

        var firstResult = await startedThread.RunAsync(
            "Reply with short plain text: ok.",
            new TurnOptions { CancellationToken = cancellation.Token });

        var threadId = startedThread.Id;
        await Assert.That(threadId).IsNotNull();
        await Assert.That(firstResult.Usage).IsNotNull();

        var resumedThread = client.ResumeThread(threadId!, new ThreadOptions
        {
            Model = settings.Model,
            ModelReasoningEffort = ModelReasoningEffort.Medium,
            WebSearchMode = WebSearchMode.Disabled,
            SandboxMode = SandboxMode.WorkspaceWrite,
            NetworkAccessEnabled = true,
        });

        var secondResult = await resumedThread.RunAsync(
            "Reply with short plain text: ok.",
            new TurnOptions { CancellationToken = cancellation.Token });

        await Assert.That(secondResult.Usage).IsNotNull();
        await Assert.That(resumedThread.Id).IsEqualTo(threadId);
    }
}
