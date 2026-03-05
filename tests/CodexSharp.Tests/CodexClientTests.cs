using ManagedCode.CodexSharp.Tests.Fakes;

namespace ManagedCode.CodexSharp.Tests;

public class CodexClientTests
{
    [Test]
    public async Task StartAsync_CanBeCalledConcurrently()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexPathOverride = "codex",
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
                CodexPathOverride = "codex",
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
    public async Task StartAsync_IsIdempotentAndSetsConnectedState()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexPathOverride = "codex",
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
                CodexPathOverride = "codex",
            },
        });

        var thread = client.StartThread();

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
                CodexPathOverride = "codex",
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
                CodexPathOverride = "codex",
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
                CodexPathOverride = "codex",
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
                CodexPathOverride = "codex",
            },
            AutoStart = false,
        });

        await client.StartAsync();
        await client.StopAsync();

        await Assert.That(client.State).IsEqualTo(CodexClientState.Disconnected);
    }

    [Test]
    public async Task DisposeAsync_SetsDisposedStateAndBlocksOperations()
    {
        var client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = new CodexOptions
            {
                CodexPathOverride = "codex",
            },
            AutoStart = false,
        });

        await client.DisposeAsync();

        await Assert.That(client.State).IsEqualTo(CodexClientState.Disposed);

        var action = async () => await client.StartAsync();
        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public async Task ResumeThread_PropagatesThreadOptionsToExec()
    {
        var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines(
        [
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"agent_message\",\"text\":\"ok\"}}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":1,\"cached_input_tokens\":0,\"output_tokens\":1}}",
        ], cancellationToken));

        var exec = new CodexExec("codex", null, null, runner);
        var client = new CodexClient(
            new CodexClientOptions
            {
                CodexOptions = new CodexOptions(),
                AutoStart = false,
            },
            exec);

        var thread = client.ResumeThread("thread_1", new ThreadOptions
        {
            Model = "gpt-5",
            SandboxMode = SandboxMode.WorkspaceWrite,
        });

        await thread.RunAsync("hello");

        var args = runner.Invocations.Single().Arguments;
        await Assert.That(args).Contains("--model");
        await Assert.That(args).Contains("gpt-5");
        await Assert.That(args).Contains("--sandbox");
        await Assert.That(args).Contains("workspace-write");

        var resumeIndex = args.IndexOf("resume");
        await Assert.That(resumeIndex).IsGreaterThan(-1);
        await Assert.That(args[resumeIndex + 1]).IsEqualTo("thread_1");
    }
}
