using System.Diagnostics;
using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Execution;
using ManagedCode.CodexSharpSDK.Internal;
using ManagedCode.CodexSharpSDK.Models;
using ManagedCode.CodexSharpSDK.Tests.Shared;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

public class CodexClientTests
{
    private const string NpmFixtureSkipReason = "The npm.cmd invocation fixture is Windows-specific.";
    private const string NpmFixtureDirectoryPrefix = "CodexNpmProbe-";
    private const string NpmFixtureScriptFileName = "npm.cmd";
    private const string NpmFixtureArgumentsFileName = "npm-arguments.txt";
    private const string NpmFixtureVersionOutput = "99.0.0";
    private const string NpmFixtureExpectedArguments = "view @openai/codex version --silent";
    private const string NpmFixtureScriptContent = "@echo off\r\necho %*>> \"%SDK_NPM_ARGS_FILE%\"\r\necho " + NpmFixtureVersionOutput + "\r\n";
    private const string NpmArgumentsEnvironmentVariable = "SDK_NPM_ARGS_FILE";
    private const string SystemRootEnvironmentVariable = "SystemRoot";
    private const string CodexHomeEnvironmentVariable = "CODEX_HOME";
    private const string HomeEnvironmentVariable = "HOME";
    private const string UserProfileEnvironmentVariable = "USERPROFILE";
    private const string PathEnvironmentVariable = "PATH";
    private const string DotCodexDirectoryName = ".codex";
    private const string CodexConfigFileName = "config.toml";
    private const string CodexModelsCacheFileName = "models_cache.json";
    private const string MetadataLeaseScriptFileName = "codex-metadata-lease.sh";
    private const string MetadataLeaseMarkerFileName = "metadata-lease-starts.txt";
    private const string MetadataLeaseProbeCommandTemplate = "#!/bin/sh\nprintf 'started\\n' >> '{0}'\nsleep 2\nprintf 'codex 0.0.1\\n'\n";
    private const string MetadataLeaseProbePathPlaceholder = "{0}";
    private const string ProbeLeaseBusyMessage = "CLI metadata process probe could not acquire its bounded process lease.";
    private const int SmallMetadataFileLimit = 256;
    private const string MetadataFileLimitMessage = "CLI metadata file exceeded the configured character limit.";
    private const string CodexConfigFixture = "model = \"" + CodexModels.Gpt53Codex + "\"";
    private const string CodexModelsCacheFixture =
        "{ \"models\": [ { \"slug\": \"" + CodexModels.Gpt53Codex +
        "\", \"display_name\": \"" + CodexModels.Gpt53Codex + "\", \"visibility\": \"list\", \"supported_in_api\": true } ] }";
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

        await Assert.That(createdThreads).Count().IsEqualTo(64);
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
    public async Task StopAsync_CancelsActiveRun()
    {
        var runner = new BlockingProcessRunner();
        using var client = CreateClientWithRunner(runner);
        var thread = client.StartThread();

        var runTask = thread.RunAsync("long-running");
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await client.StopAsync();

        var exception = await Assert.That(async () => await runTask).ThrowsException();
        await Assert.That(exception).IsTypeOf<OperationCanceledException>();
        await runner.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
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
    public async Task Dispose_CancelsActiveRun()
    {
        var runner = new BlockingProcessRunner();
        var client = CreateClientWithRunner(runner);
        var thread = client.StartThread();

        var runTask = thread.RunAsync("long-running");
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        client.Dispose();

        var exception = await Assert.That(async () => await runTask).ThrowsException();
        await Assert.That(exception).IsTypeOf<OperationCanceledException>();
        await runner.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.That(client.State).IsEqualTo(CodexClientState.Disposed);
    }

    [Test]
    public async Task CodexCli_Smoke_GetCliMetadata_ReturnsInstalledVersion()
    {
        using var client = new CodexClient(new CodexOptions());

        var metadata = client.GetCliMetadata();

        await Assert.That(string.IsNullOrWhiteSpace(metadata.InstalledVersion)).IsFalse();
        await Assert.That(metadata.InstalledVersion.Contains('.')).IsTrue();
    }

    [Test]
    public async Task CodexCli_GetCliMetadata_UsesConfiguredCodexHomeWithEnvironmentAllowlist()
    {
        var codexHome = CreateMetadataSandbox();
        try
        {
            File.WriteAllText(Path.Combine(codexHome, CodexConfigFileName), CodexConfigFixture);
            File.WriteAllText(Path.Combine(codexHome, CodexModelsCacheFileName), CodexModelsCacheFixture);
            using var client = CreateMetadataClient(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PathEnvironmentVariable] = Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty,
                [CodexHomeEnvironmentVariable] = codexHome,
                [UserProfileEnvironmentVariable] = Environment.GetEnvironmentVariable(UserProfileEnvironmentVariable) ?? string.Empty,
                [SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable) ?? string.Empty,
            });

            var metadata = client.GetCliMetadata();

            await Assert.That(metadata.DefaultModel).IsEqualTo(CodexModels.Gpt53Codex);
            await Assert.That(metadata.Models).Count().IsEqualTo(1);
            await Assert.That(metadata.Models[0].Slug).IsEqualTo(CodexModels.Gpt53Codex);
        }
        finally
        {
            Directory.Delete(codexHome, recursive: true);
        }
    }

    [Test]
    public async Task CodexCli_GetCliMetadata_UsesExplicitHomeFallbackWhenInheritanceIsDisabled()
    {
        var home = CreateMetadataSandbox();
        var codexHome = Path.Combine(home, DotCodexDirectoryName);
        Directory.CreateDirectory(codexHome);
        try
        {
            File.WriteAllText(Path.Combine(codexHome, CodexConfigFileName), CodexConfigFixture);
            using var client = CreateMetadataClient(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PathEnvironmentVariable] = Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty,
                [HomeEnvironmentVariable] = home,
                [UserProfileEnvironmentVariable] = Environment.GetEnvironmentVariable(UserProfileEnvironmentVariable) ?? string.Empty,
                [SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable) ?? string.Empty,
            });

            var metadata = client.GetCliMetadata();

            await Assert.That(metadata.DefaultModel).IsEqualTo(CodexModels.Gpt53Codex);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Test]
    [NotInParallel]
    public async Task CodexCli_MetadataLeaseTimeoutIsIndependentAndPreventsSecondProbeStart()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("The metadata lease fixture uses a POSIX executable script.");
            return;
        }

        var sandbox = CreateMetadataSandbox();
        var markerPath = Path.Combine(sandbox, MetadataLeaseMarkerFileName);
        var executablePath = Path.Combine(sandbox, MetadataLeaseScriptFileName);
        File.WriteAllText(executablePath, MetadataLeaseProbeCommandTemplate.Replace(
            MetadataLeaseProbePathPlaceholder,
            markerPath,
            StringComparison.Ordinal));
        File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PathEnvironmentVariable] = Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty,
        };
        using var firstClient = new CodexClient(new CodexOptions
        {
            CodexExecutablePath = executablePath,
            EnvironmentVariables = environment,
            InheritEnvironmentVariables = false,
            CliMetadataProbeTimeout = TimeSpan.FromSeconds(5),
            CliMetadataProbeLeaseTimeout = TimeSpan.FromSeconds(5),
        });
        using var secondClient = new CodexClient(new CodexOptions
        {
            CodexExecutablePath = executablePath,
            EnvironmentVariables = environment,
            InheritEnvironmentVariables = false,
            CliMetadataProbeTimeout = TimeSpan.FromSeconds(5),
            CliMetadataProbeLeaseTimeout = TimeSpan.FromMilliseconds(100),
        });

        try
        {
            var firstProbe = Task.Run(firstClient.GetCliMetadata);
            var started = Stopwatch.StartNew();
            while (!File.Exists(markerPath) && started.Elapsed < TimeSpan.FromSeconds(3))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }

            await Assert.That(File.Exists(markerPath)).IsTrue();
            var secondProbe = () => secondClient.GetCliMetadata();
            var exception = await Assert.That(secondProbe).ThrowsException();

            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).IsEqualTo(ProbeLeaseBusyMessage);
            await Assert.That(started.Elapsed).IsLessThan(TimeSpan.FromSeconds(2));
            var result = await firstProbe.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(result.InstalledVersion).IsEqualTo("codex 0.0.1");
            await Assert.That(File.ReadAllLines(markerPath)).Count().IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    [Test]
    public async Task CodexCli_GetCliMetadataRejectsNonPositiveProbeLeaseTimeout()
    {
        foreach (var timeout in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(-1) })
        {
            using var client = new CodexClient(new CodexOptions { CliMetadataProbeLeaseTimeout = timeout });
            var action = () => client.GetCliMetadata();
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<ArgumentOutOfRangeException>();
        }
    }

    [Test]
    public async Task CodexCli_GetCliMetadata_RejectsOversizedConfigLine()
    {
        var codexHome = CreateMetadataSandbox();
        try
        {
            File.WriteAllText(Path.Combine(codexHome, CodexConfigFileName), new string('x', SmallMetadataFileLimit + 1));
            using var client = CreateMetadataClient(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PathEnvironmentVariable] = Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty,
                [CodexHomeEnvironmentVariable] = codexHome,
                [UserProfileEnvironmentVariable] = Environment.GetEnvironmentVariable(UserProfileEnvironmentVariable) ?? string.Empty,
                [SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable) ?? string.Empty,
            }, SmallMetadataFileLimit);

            var action = () => client.GetCliMetadata();
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).Contains(MetadataFileLimitMessage);
        }
        finally
        {
            Directory.Delete(codexHome, recursive: true);
        }
    }

    [Test]
    public async Task CodexCli_GetCliMetadata_RejectsOversizedModelsCache()
    {
        var codexHome = CreateMetadataSandbox();
        try
        {
            File.WriteAllText(Path.Combine(codexHome, CodexConfigFileName), CodexConfigFixture);
            File.WriteAllText(Path.Combine(codexHome, CodexModelsCacheFileName), new string('x', SmallMetadataFileLimit + 1));
            using var client = CreateMetadataClient(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [PathEnvironmentVariable] = Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty,
                [CodexHomeEnvironmentVariable] = codexHome,
                [UserProfileEnvironmentVariable] = Environment.GetEnvironmentVariable(UserProfileEnvironmentVariable) ?? string.Empty,
                [SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable) ?? string.Empty,
            }, SmallMetadataFileLimit);

            var action = () => client.GetCliMetadata();
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).Contains(MetadataFileLimitMessage);
        }
        finally
        {
            Directory.Delete(codexHome, recursive: true);
        }
    }

    [Test]
    public async Task CodexCli_Smoke_GetCliUpdateStatus_ReturnsInstalledVersion()
    {
        using var client = new CodexClient(new CodexOptions());

        var status = client.GetCliUpdateStatus();

        await Assert.That(string.IsNullOrWhiteSpace(status.InstalledVersion)).IsFalse();
        await Assert.That(status.InstalledVersion.Contains('.')).IsTrue();
    }

    [Test]
    public async Task CodexCli_UpdateStatus_InvokesScopedNpmPackageThroughWindowsCommandProcessor()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test(NpmFixtureSkipReason);
            return;
        }

        var sandbox = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox",
            $"{NpmFixtureDirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandbox);
        var npmScriptPath = Path.Combine(sandbox, NpmFixtureScriptFileName);
        var argumentsPath = Path.Combine(sandbox, NpmFixtureArgumentsFileName);
        try
        {
            File.WriteAllText(npmScriptPath, NpmFixtureScriptContent);
            using var client = new CodexClient(new CodexOptions
            {
                CodexExecutablePath = CodexCliLocator.FindCodexPath(null),
                InheritEnvironmentVariables = false,
                EnvironmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PathEnvironmentVariable] = string.Concat(sandbox, Path.PathSeparator,
                        Environment.GetEnvironmentVariable(PathEnvironmentVariable)),
                    [SystemRootEnvironmentVariable] = Environment.GetEnvironmentVariable(SystemRootEnvironmentVariable) ?? string.Empty,
                    [NpmArgumentsEnvironmentVariable] = argumentsPath,
                },
            });

            var status = client.GetCliUpdateStatus();

            await Assert.That(status.LatestVersion).IsEqualTo(NpmFixtureVersionOutput);
            await Assert.That(status.IsUpdateAvailable).IsTrue();
            await Assert.That(File.ReadAllText(argumentsPath).Trim()).IsEqualTo(NpmFixtureExpectedArguments);
        }
        finally
        {
            Directory.Delete(sandbox, recursive: true);
        }
    }

    private static CodexClient CreateMetadataClient(
        IReadOnlyDictionary<string, string> environment,
        int maximumFileCharacters = CodexOptions.DefaultCliMetadataMaximumFileCharacters) =>
        new(new CodexOptions
        {
            CodexExecutablePath = CodexCliLocator.FindCodexPath(null),
            EnvironmentVariables = environment,
            InheritEnvironmentVariables = false,
            CliMetadataMaximumFileCharacters = maximumFileCharacters,
        });

    private static string CreateMetadataSandbox()
    {
        var sandbox = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox",
            $"CodexClientMetadata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandbox);
        return sandbox;
    }

    [Test]
    [Property("RequiresCodexAuth", "true")]
    public async Task ResumeThread_WithThreadOptions_RunsWithRealCodexCli()
    {
        using var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient(settings);
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

    private static CodexClient CreateClientWithRunner(ICodexProcessRunner runner)
    {
        return new CodexClient(
            new CodexClientOptions
            {
                AutoStart = false,
                CodexOptions = new CodexOptions
                {
                    CodexExecutablePath = "codex",
                },
            },
            new CodexExec("codex", null, null, runner));
    }

    private sealed class BlockingProcessRunner : ICodexProcessRunner
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<string> RunAsync(
            CodexProcessInvocation invocation,
            Microsoft.Extensions.Logging.ILogger logger,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            yield return "{\"type\":\"thread.started\",\"thread_id\":\"thread_1\"}";

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult(true);
                throw;
            }
        }
    }
}
