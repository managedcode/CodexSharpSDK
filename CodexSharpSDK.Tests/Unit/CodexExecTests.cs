using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Execution;
using ManagedCode.CodexSharpSDK.Models;
using ManagedCode.CodexSharpSDK.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

public class CodexExecTests
{
    private const string LongRunningCliScript = "#!/bin/sh\necho $$\nexec /bin/sleep 30\n";
    private const string DescendantHoldingStderrScript = "/usr/bin/setsid /bin/sleep 30 >&2 & echo $!; exit 0";
    private const string DescendantHoldingStderrWhileParentRunsScript =
        "/usr/bin/setsid /bin/sleep 30 >&2 & echo $!; exec /bin/sleep 30";
    private const string WindowsDuplexStdinCommandTemplate = "$pressure = 'o' * {0}; [Console]::Out.WriteLine($pressure); [Console]::Out.Flush(); [Console]::Error.WriteLine(('e' * {0})); [Console]::Error.Flush(); $prompt = [Console]::In.ReadToEnd(); [Console]::Out.Write($prompt); [Console]::Out.WriteLine(\"stdin-length:$($prompt.Length)\"); [Console]::Out.WriteLine('stdin-eof')";
    private const string PosixDuplexStdinScriptTemplate = "head -c {0} /dev/zero | tr '\\000' 'o'; printf '\\n'; head -c {0} /dev/zero | tr '\\000' 'e' >&2; printf '\\n' >&2; prompt=$(cat); printf '%s\\n' \"$prompt\"; printf 'stdin-length:%s\\n' \"${#prompt}\"; printf 'stdin-eof\\n'";
    private const string DuplexPressureTemplatePlaceholder = "{0}";
    private const string PosixFixtureSkipReason = "The public CLI yield-boundary fixture currently uses a POSIX executable script.";
    private const string LinuxFixtureSkipReason = "The detached stderr-retention fixture requires Linux setsid.";
    private const string StderrClosureFailure = "stderr stream closed";
    private const string ProcessOutputLimitMessage = "Codex CLI process exceeded the configured output limit.";
    private const string PosixSingleLineOverflowCommand = "printf '%100s\\n' x; exec /bin/sleep 30";
    private const string PosixMultiLineOverflowCommand = "printf '1234567890\\n1234567890\\n1234567890\\n'";
    private const string PosixStandardErrorPressureCommand = "printf '%100s' x >&2; exec /bin/sleep 30";
    private const string PosixNormalMultiLineCommand = "printf 'first\\nsecond\\n'";
    private const string WindowsPowerShellPath = "powershell.exe";
    private const string WindowsNoProfileFlag = "-NoProfile";
    private const string WindowsNonInteractiveFlag = "-NonInteractive";
    private const string WindowsCommandFlag = "-Command";
    private const string WindowsSingleLineOverflowCommand = "[Console]::Out.WriteLine('x' * 100); Start-Sleep -Seconds 30";
    private const string WindowsMultiLineOverflowCommand = "Write-Output '1234567890'; Write-Output '1234567890'; Write-Output '1234567890'";
    private const string WindowsStandardErrorPressureCommand = "[Console]::Error.Write('x' * 100); Start-Sleep -Seconds 30";
    private const string WindowsNormalMultiLineCommand = "Write-Output 'first'; Write-Output 'second'";
    private const string ExpectedFirstLine = "first";
    private const string ExpectedSecondLine = "second";
    private const int SmallOutputLimitCharacters = 64;
    private const int AggregateOutputLimitCharacters = 24;
    private const int DuplexPipePressureCharacters = 131072;
    private const int DuplexPromptCharacters = 262144;
    private const int DuplexMaximumProcessOutputCharacters = 1048576;
    private static readonly TimeSpan ProcessOutputCleanupAssertionBound = TimeSpan.FromSeconds(8);
    private const string TestInput = "test";
    private const string CancellationScriptFileName = "codex-cancel.sh";
    private const string MissingExecutableNamePrefix = "missing-codex-cli-";
    private const string PosixShellPath = "/bin/sh";
    private const string PosixShellCommandFlag = "-c";

    [Test]
    public async Task DefaultProcessRunner_DrainsBothOutputPipesWhileWritingLargePrompt()
    {
        var sandboxDirectory = CreateSandboxDirectory();
        var prompt = new string('p', DuplexPromptCharacters);

        try
        {
            var invocation = OperatingSystem.IsWindows()
                ? new CodexProcessInvocation(
                    WindowsPowerShellPath,
                    [
                        WindowsNoProfileFlag,
                        WindowsNonInteractiveFlag,
                        WindowsCommandFlag,
                        WindowsDuplexStdinCommandTemplate.Replace(
                            DuplexPressureTemplatePlaceholder,
                            DuplexPipePressureCharacters.ToString(CultureInfo.InvariantCulture),
                            StringComparison.Ordinal),
                    ],
                    CreateProcessEnvironment(),
                    prompt)
                : CreateShellScript(
                    sandboxDirectory,
                    "duplex-stdin",
                    PosixDuplexStdinScriptTemplate.Replace(
                        DuplexPressureTemplatePlaceholder,
                        DuplexPipePressureCharacters.ToString(CultureInfo.InvariantCulture),
                        StringComparison.Ordinal))
                    .Invocation with
                { Input = prompt };
            invocation = invocation with { MaximumProcessOutputCharacters = DuplexMaximumProcessOutputCharacters };
            var runner = new DefaultCodexProcessRunner();

            var lines = await DrainToListAsync(runner.RunAsync(invocation, NullLogger.Instance, CancellationToken.None));

            await Assert.That(lines).Contains(prompt);
            await Assert.That(lines).Contains(new string('o', DuplexPipePressureCharacters));
            await Assert.That(lines).Contains($"stdin-length:{prompt.Length}");
            await Assert.That(lines).Contains("stdin-eof");
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task CancellationWithDescendantHoldingStderrSurfacesUnconfirmedCleanup()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test(LinuxFixtureSkipReason);
            return;
        }

        using var cancellation = new CancellationTokenSource();
        var standardErrorReaderCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var standardOutputReadCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = new CodexProcessInvocation(
            PosixShellPath,
            [PosixShellCommandFlag, DescendantHoldingStderrWhileParentRunsScript],
            CreateProcessEnvironment(),
            string.Empty)
        {
            ProcessTerminationTimeout = TimeSpan.FromMilliseconds(250),
            StandardErrorReaderCompleted = () => standardErrorReaderCompleted.TrySetResult(),
            StandardOutputReadCompleted = () => standardOutputReadCompleted.TrySetResult(),
        };
        var runner = new DefaultCodexProcessRunner();
        await using var enumerator = runner.RunAsync(invocation, NullLogger.Instance, cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        var childProcessId = 0;

        try
        {
            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            childProcessId = int.Parse(enumerator.Current, CultureInfo.InvariantCulture);
            cancellation.Cancel();

            var action = async () => await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).Contains(StderrClosureFailure);
            await standardOutputReadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await standardErrorReaderCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            try
            {
                using var child = Process.GetProcessById(childProcessId);
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException)
            {
                // The detached fixture child already exited.
            }
        }
    }

    [Test]
    public async Task DefaultProcessRunner_PreservesMultilineOutputWithinConfiguredBudget()
    {
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsNormalMultiLineCommand : PosixNormalMultiLineCommand,
            TimeSpan.FromSeconds(5), SmallOutputLimitCharacters);
        var runner = new DefaultCodexProcessRunner();
        var lines = await DrainToListAsync(runner.RunAsync(invocation, NullLogger.Instance, CancellationToken.None));

        await Assert.That(lines).Count().IsEqualTo(2);
        await Assert.That(lines[0]).IsEqualTo(ExpectedFirstLine);
        await Assert.That(lines[1]).IsEqualTo(ExpectedSecondLine);
    }

    [Test]
    public async Task DefaultProcessRunner_RejectsSingleLineAboveConfiguredBudget()
    {
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsSingleLineOverflowCommand : PosixSingleLineOverflowCommand,
            TimeSpan.FromSeconds(2), SmallOutputLimitCharacters);
        var runner = new DefaultCodexProcessRunner();
        var action = async () => await DrainToListAsync(runner.RunAsync(invocation, NullLogger.Instance, CancellationToken.None));

        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(ProcessOutputLimitMessage);
    }

    [Test]
    public async Task DefaultProcessRunner_RejectsAggregateMultilineOutputAboveConfiguredBudget()
    {
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsMultiLineOverflowCommand : PosixMultiLineOverflowCommand,
            TimeSpan.FromSeconds(2), AggregateOutputLimitCharacters);
        var runner = new DefaultCodexProcessRunner();
        var action = async () => await DrainToListAsync(runner.RunAsync(invocation, NullLogger.Instance, CancellationToken.None));

        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(ProcessOutputLimitMessage);
    }

    [Test]
    public async Task DefaultProcessRunner_StandardErrorPressureStopsQuietRootWithinBound()
    {
        var stopwatch = new Stopwatch();
        var invocation = CreateOutputInvocation(
            OperatingSystem.IsWindows() ? WindowsStandardErrorPressureCommand : PosixStandardErrorPressureCommand,
            TimeSpan.FromSeconds(2), SmallOutputLimitCharacters, stopwatch.Start);
        var runner = new DefaultCodexProcessRunner();
        var action = async () => await DrainToListAsync(runner.RunAsync(invocation, NullLogger.Instance, CancellationToken.None));

        var exception = await Assert.That(action).ThrowsException();
        stopwatch.Stop();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(ProcessOutputLimitMessage);
        await Assert.That(stopwatch.Elapsed < ProcessOutputCleanupAssertionBound).IsTrue();
    }

    [Test]
    public async Task PublicExec_CancellationBetweenYieldedLinesIsNotReportedAsSuccess()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test(PosixFixtureSkipReason);
            return;
        }

        var sandboxDirectory = CreateSandboxDirectory();
        var scriptPath = Path.Combine(sandboxDirectory, CancellationScriptFileName);
        File.WriteAllText(scriptPath, LongRunningCliScript);
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var cancellation = new CancellationTokenSource();
        using var exec = new CodexExec(TimeSpan.FromSeconds(5), scriptPath, CreateProcessEnvironment());

        try
        {
            await using var enumerator = exec.RunAsync(new CodexExecArgs
            {
                Input = TestInput,
                CancellationToken = cancellation.Token,
            }).GetAsyncEnumerator(cancellation.Token);

            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            var processId = int.Parse(enumerator.Current, CultureInfo.InvariantCulture);
            using var process = Process.GetProcessById(processId);
            cancellation.Cancel();

            var action = async () => await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<OperationCanceledException>();
            await Assert.That(process.HasExited).IsTrue();
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task PublicExec_PreCanceledTokenDoesNotStartCliProcess()
    {
        var sandboxDirectory = CreateSandboxDirectory();
        var executablePath = Path.Combine(sandboxDirectory, $"{MissingExecutableNamePrefix}{Guid.NewGuid():N}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var exec = new CodexExec(TimeSpan.FromSeconds(5), executablePath, CreateProcessEnvironment());

        try
        {
            await using var enumerator = exec.RunAsync(new CodexExecArgs
            {
                Input = TestInput,
                CancellationToken = cancellation.Token,
            }).GetAsyncEnumerator(cancellation.Token);

            var action = async () => await enumerator.MoveNextAsync();
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<OperationCanceledException>();
        }
        finally
        {
            // The intentionally missing executable proves cancellation is observed before process start.
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task DefaultProcessRunner_RootExitWithDescendantHoldingStderr_FailsWithinConfiguredBound()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test(LinuxFixtureSkipReason);
            return;
        }

        var standardErrorReaderCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var standardOutputReadCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocation = new CodexProcessInvocation(
            PosixShellPath,
            [PosixShellCommandFlag, DescendantHoldingStderrScript],
            CreateProcessEnvironment(),
            string.Empty)
        {
            ProcessTerminationTimeout = TimeSpan.FromMilliseconds(250),
            StandardErrorReaderCompleted = () => standardErrorReaderCompleted.TrySetResult(),
            StandardOutputReadCompleted = () => standardOutputReadCompleted.TrySetResult(),
        };
        var runner = new DefaultCodexProcessRunner();
        await using var enumerator = runner.RunAsync(invocation, NullLogger.Instance, CancellationToken.None)
            .GetAsyncEnumerator();
        var childProcessId = 0;

        try
        {
            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            childProcessId = int.Parse(enumerator.Current, CultureInfo.InvariantCulture);
            var stopwatch = Stopwatch.StartNew();
            var action = async () => await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            var exception = await Assert.That(action).ThrowsException();
            stopwatch.Stop();

            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).Contains(StderrClosureFailure);
            await Assert.That(stopwatch.Elapsed < TimeSpan.FromSeconds(2)).IsTrue();
            await standardOutputReadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await standardErrorReaderCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            try
            {
                using var child = Process.GetProcessById(childProcessId);
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException)
            {
                // The detached fixture child already exited.
            }
        }
    }

    [Test]
    public async Task BuildCommandArgs_BuildsCommandLineWithExpectedOrder()
    {
        var exec = new CodexExec(
            executablePath: "codex",
            environmentOverride: null,
            configOverrides: new JsonObject
            {
                ["approval_policy"] = "never",
                ["sandbox_workspace_write"] = new JsonObject
                {
                    ["network_access"] = true,
                },
                ["retry_budget"] = 3,
                ["tool_rules"] = new JsonObject
                {
                    ["allow"] = new JsonArray("git status", "git diff"),
                },
            });

        var args = new CodexExecArgs
        {
            Input = "test prompt",
            Model = CodexModels.Gpt53Codex,
            SandboxMode = SandboxMode.WorkspaceWrite,
            WorkingDirectory = "/tmp/project",
            AdditionalDirectories = ["/tmp/shared", "/tmp/other"],
            SkipGitRepoCheck = true,
            OutputSchemaFile = "/tmp/schema.json",
            ModelReasoningEffort = ModelReasoningEffort.High,
            NetworkAccessEnabled = true,
            WebSearchMode = WebSearchMode.Cached,
            ApprovalPolicy = ApprovalMode.OnRequest,
            ThreadId = "thread_1",
            Images = ["first.png", "second.jpg"],
        };

        var commandArgs = exec.BuildCommandArgs(args);

        await Assert.That(commandArgs[0]).IsEqualTo("exec");
        await Assert.That(commandArgs[1]).IsEqualTo("--json");
        await Assert.That(ContainsPair(commandArgs, "--model", CodexModels.Gpt53Codex)).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--sandbox", "workspace-write")).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--cd", "/tmp/project")).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--output-schema", "/tmp/schema.json")).IsTrue();
        await Assert.That(commandArgs.Contains("--skip-git-repo-check")).IsTrue();

        await Assert.That(CollectConfigValues(commandArgs, "approval_policy"))
            .IsEquivalentTo(["approval_policy=\"never\"", "approval_policy=\"on-request\""]);
        await Assert.That(CollectConfigValues(commandArgs, "sandbox_workspace_write.network_access"))
            .IsEquivalentTo(["sandbox_workspace_write.network_access=true", "sandbox_workspace_write.network_access=true"]);
        await Assert.That(CollectConfigValues(commandArgs, "retry_budget"))
            .IsEquivalentTo(["retry_budget=3"]);
        await Assert.That(CollectConfigValues(commandArgs, "tool_rules.allow"))
            .IsEquivalentTo(["tool_rules.allow=[\"git status\", \"git diff\"]"]);
        await Assert.That(CollectConfigValues(commandArgs, "model_reasoning_effort"))
            .IsEquivalentTo(["model_reasoning_effort=\"high\""]);
        await Assert.That(CollectConfigValues(commandArgs, "web_search"))
            .IsEquivalentTo(["web_search=\"cached\""]);

        var addDirValues = CollectFlagValues(commandArgs, "--add-dir");
        await Assert.That(addDirValues).IsEquivalentTo(["/tmp/shared", "/tmp/other"]);

        var resumeIndex = commandArgs.IndexOf("resume");
        var firstImageIndex = commandArgs.IndexOf("--image");
        await Assert.That(resumeIndex).IsGreaterThan(-1);
        await Assert.That(firstImageIndex).IsGreaterThan(-1);
        await Assert.That(resumeIndex < firstImageIndex).IsTrue();
        await Assert.That(CollectFlagValues(commandArgs, "--image")).IsEquivalentTo(["first.png", "second.jpg"]);
    }

    [Test]
    public async Task BuildCommandArgs_UsesWebSearchEnabledWhenModeMissing()
    {
        var exec = new CodexExec("codex", null, null);

        var commandArgs = exec.BuildCommandArgs(new CodexExecArgs
        {
            Input = "test",
            WebSearchEnabled = false,
        });

        var configValues = CollectConfigValues(commandArgs, "web_search");
        await Assert.That(configValues).IsEquivalentTo(["web_search=\"disabled\""]);
    }

    [Test]
    public async Task BuildCommandArgs_WebSearchModeOverridesLegacyFlag()
    {
        var exec = new CodexExec("codex", null, null);

        var commandArgs = exec.BuildCommandArgs(new CodexExecArgs
        {
            Input = "test",
            WebSearchMode = WebSearchMode.Live,
            WebSearchEnabled = false,
        });

        var configValues = CollectConfigValues(commandArgs, "web_search");
        await Assert.That(configValues).IsEquivalentTo(["web_search=\"live\""]);
    }

    [Test]
    public async Task BuildCommandArgs_MapsExtendedCliFlags()
    {
        var exec = new CodexExec("codex", null, null);

        var commandArgs = exec.BuildCommandArgs(new CodexExecArgs
        {
            Input = "test",
            Profile = "strict",
            UseOss = true,
            LocalProvider = OssProvider.LmStudio,
            FullAuto = true,
            DangerouslyBypassApprovalsAndSandbox = true,
            Ephemeral = true,
            Color = ExecOutputColor.Never,
            ProgressCursor = true,
            OutputLastMessageFile = "/tmp/last-message.txt",
            EnabledFeatures = ["multi_agent", "unified_exec"],
            DisabledFeatures = ["steer"],
            AdditionalCliArguments = ["--some-future-flag", "custom-value"],
        });

        await Assert.That(ContainsPair(commandArgs, "--profile", "strict")).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--local-provider", "lmstudio")).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--color", "never")).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--output-last-message", "/tmp/last-message.txt")).IsTrue();

        await Assert.That(commandArgs.Contains("--oss")).IsTrue();
        await Assert.That(commandArgs.Contains("--full-auto")).IsTrue();
        await Assert.That(commandArgs.Contains("--dangerously-bypass-approvals-and-sandbox")).IsTrue();
        await Assert.That(commandArgs.Contains("--ephemeral")).IsTrue();
        await Assert.That(commandArgs.Contains("--progress-cursor")).IsTrue();

        await Assert.That(CollectFlagValues(commandArgs, "--enable")).IsEquivalentTo(["multi_agent", "unified_exec"]);
        await Assert.That(CollectFlagValues(commandArgs, "--disable")).IsEquivalentTo(["steer"]);

        await Assert.That(commandArgs.Contains("--some-future-flag")).IsTrue();
        await Assert.That(commandArgs.Contains("custom-value")).IsTrue();
    }

    [Test]
    public async Task BuildCommandArgs_ExplicitFalseEphemeral_OverridesConfigPersistence()
    {
        var exec = new CodexExec(
            executablePath: "codex",
            environmentOverride: null,
            configOverrides: new JsonObject
            {
                ["ephemeral"] = true,
            });

        var commandArgs = exec.BuildCommandArgs(new CodexExecArgs
        {
            Input = "test",
            Ephemeral = false,
        });

        await Assert.That(commandArgs.Contains("--ephemeral")).IsFalse();
        await Assert.That(CollectConfigValues(commandArgs, "ephemeral"))
            .IsEquivalentTo(["ephemeral=true", "ephemeral=false"]);
    }

    [Test]
    public async Task BuildCommandArgs_KeepsConfiguredWebSearchWhenThreadOverridesMissing()
    {
        var exec = new CodexExec(
            executablePath: "codex",
            environmentOverride: null,
            configOverrides: new JsonObject
            {
                ["web_search"] = "disabled",
            });

        var commandArgs = exec.BuildCommandArgs(new CodexExecArgs
        {
            Input = "test",
        });

        var configValues = CollectConfigValues(commandArgs, "web_search");
        await Assert.That(configValues).IsEquivalentTo(["web_search=\"disabled\""]);
    }

    [Test]
    public async Task BuildEnvironment_UsesProvidedEnvironmentWithoutLeakingProcessEnvironment()
    {
        Environment.SetEnvironmentVariable("CODEX_SHOULD_NOT_LEAK", "leak");

        try
        {
            var exec = new CodexExec(
                executablePath: "codex",
                environmentOverride: new Dictionary<string, string>
                {
                    ["CUSTOM_ENV"] = "custom",
                },
                configOverrides: null);

            var environment = exec.BuildEnvironment("https://example.local", "secret");

            await Assert.That(environment["CUSTOM_ENV"]).IsEqualTo("custom");
            await Assert.That(environment.ContainsKey("CODEX_SHOULD_NOT_LEAK")).IsFalse();
            await Assert.That(environment["OPENAI_BASE_URL"]).IsEqualTo("https://example.local");
            await Assert.That(environment["CODEX_API_KEY"]).IsEqualTo("secret");
            await Assert.That(environment["CODEX_INTERNAL_ORIGINATOR_OVERRIDE"]).IsEqualTo("codex_sdk_csharp");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_SHOULD_NOT_LEAK", null);
        }
    }

    [Test]
    public async Task BuildEnvironment_InheritsEnvironmentWhenOverrideMissing()
    {
        Environment.SetEnvironmentVariable("CODEX_SHOULD_INHERIT", "yes");

        try
        {
            var exec = new CodexExec("codex", null, null);
            var environment = exec.BuildEnvironment(null, null);

            await Assert.That(environment["CODEX_SHOULD_INHERIT"]).IsEqualTo("yes");
            await Assert.That(environment.ContainsKey("CODEX_INTERNAL_ORIGINATOR_OVERRIDE")).IsTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_SHOULD_INHERIT", null);
        }
    }

    [Test]
    public async Task BuildCommandArgs_ThrowsWhenConfigContainsEmptyKey()
    {
        var exec = new CodexExec(
            executablePath: "codex",
            environmentOverride: null,
            configOverrides: new JsonObject { [""] = "value" });

        var action = () => exec.BuildCommandArgs(new CodexExecArgs { Input = "test" });

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception!.Message).Contains("non-empty strings");
    }

    [Test]
    public async Task RunAsync_WithNullLogger_StillPropagatesFailure()
    {
        var missingExecutable = Path.Combine(
            Environment.CurrentDirectory,
            "tests",
            ".sandbox",
            $"missing-codex-{Guid.NewGuid():N}",
            "codex");

        var exec = new CodexExec(missingExecutable, null, null, NullLogger.Instance);

        var action = async () => await DrainAsync(exec.RunAsync(new CodexExecArgs { Input = "test" }));

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("Failed to start Codex CLI");
    }

    [Test]
    [Property("RequiresCodexAuth", "true")]
    public async Task RunAsync_WithNullLogger_CompletesSuccessfully_WithRealCodexCli()
    {
        using var settings = RealCodexTestSupport.GetRequiredSettings();

        var exec = RealCodexTestSupport.CreateExec(settings, NullLogger.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var lines = await DrainToListAsync(exec.RunAsync(new CodexExecArgs
        {
            Input = "Reply with short plain text: ok.",
            Model = settings.Model,
            ModelReasoningEffort = ModelReasoningEffort.Medium,
            WebSearchMode = WebSearchMode.Disabled,
            SandboxMode = SandboxMode.WorkspaceWrite,
            NetworkAccessEnabled = true,
            CancellationToken = cancellation.Token,
        }));

        await Assert.That(lines.Count).IsGreaterThan(0);
        await Assert.That(lines.Any(line => line.Contains("\"type\":\"turn.completed\"", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task DefaultProcessRunner_CancellationAfterProcessExit_StillReturnsProcessFailure()
    {
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var invocation = OperatingSystem.IsWindows()
                ? new CodexProcessInvocation(
                    "powershell.exe",
                    [
                        "-NoProfile",
                        "-NonInteractive",
                        "-Command",
                        "$PID; 1..20000 | ForEach-Object { [Console]::Error.WriteLine(\"error-line-$_\") }; [Console]::Out.WriteLine('done'); exit 23",
                    ],
                    CreateProcessEnvironment(),
                    string.Empty)
                : CreateShellScript(
                    sandboxDirectory,
                    "stderr-race",
                    """
                    echo $$
                    i=1
                    while [ "$i" -le 20000 ]
                    do
                      echo "error-line-$i" 1>&2
                      i=$((i + 1))
                    done
                    echo done
                    exit 23
                    """).Invocation;
            var runner = new DefaultCodexProcessRunner();
            using var cancellation = new CancellationTokenSource();

            await using var enumerator = runner.RunAsync(
                    invocation,
                    NullLogger.Instance,
                    cancellation.Token)
                .GetAsyncEnumerator(cancellation.Token);

            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            await WaitForProcessExitAsync(int.Parse(enumerator.Current, System.Globalization.CultureInfo.InvariantCulture));

            cancellation.Cancel();

            var action = async () => await enumerator.MoveNextAsync();
            var exception = await Assert.That(action).ThrowsException();

            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).Contains("Codex Exec exited with code 23");
            await Assert.That(exception.Message).Contains("error-line-1");
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    [Test]
    public async Task DefaultProcessRunner_CancellationWhileProcessStillRunning_ThrowsOperationCanceledException()
    {
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var invocation = OperatingSystem.IsWindows()
                ? new CodexProcessInvocation(
                    "powershell.exe",
                    ["-NoProfile", "-NonInteractive", "-Command", "[Console]::Out.WriteLine($PID); [Console]::Out.WriteLine('started'); while ($true) { [Console]::Error.WriteLine('error-line'); Start-Sleep -Milliseconds 5 }"],
                    CreateProcessEnvironment(),
                    string.Empty)
                : CreateShellScript(
                    sandboxDirectory,
                    "stderr-cancel",
                    """
                    echo $$
                    echo started
                    while :
                    do
                      echo error-line 1>&2
                    done
                    """).Invocation;
            var runner = new DefaultCodexProcessRunner();
            using var cancellation = new CancellationTokenSource();

            await using var enumerator = runner.RunAsync(
                    invocation,
                    NullLogger.Instance,
                    cancellation.Token)
                .GetAsyncEnumerator(cancellation.Token);

            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            var processId = int.Parse(enumerator.Current, System.Globalization.CultureInfo.InvariantCulture);
            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            await Assert.That(enumerator.Current).IsEqualTo("started");

            cancellation.Cancel();

            var action = async () => await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var exception = await Assert.That(action).ThrowsException();
            await Assert.That(exception).IsTypeOf<OperationCanceledException>();
            await WaitForProcessExitAsync(processId);
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
    }

    private static async Task DrainAsync(IAsyncEnumerable<string> lines)
    {
        await foreach (var _ in lines)
        {
            // Intentionally empty.
        }
    }

    private static async Task WaitForProcessExitAsync(int processId)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
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

    private static string CreateSandboxDirectory()
    {
        var sandboxDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "tests",
            ".sandbox",
            $"CodexExecTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandboxDirectory);
        return sandboxDirectory;
    }

    private static CodexProcessInvocation CreateOutputInvocation(
        string command,
        TimeSpan processTerminationTimeout,
        int maximumProcessOutputCharacters,
        Action? standardErrorOutputLimitExceeded = null)
    {
        return new CodexProcessInvocation(
            OperatingSystem.IsWindows() ? WindowsPowerShellPath : PosixShellPath,
            OperatingSystem.IsWindows()
                ? [WindowsNoProfileFlag, WindowsNonInteractiveFlag, WindowsCommandFlag, command]
                : [PosixShellCommandFlag, command],
            CreateProcessEnvironment(),
            string.Empty)
        {
            ProcessTerminationTimeout = processTerminationTimeout,
            MaximumProcessOutputCharacters = maximumProcessOutputCharacters,
            StandardErrorOutputLimitExceeded = standardErrorOutputLimitExceeded,
        };
    }

    private static ShellScriptHandle CreateShellScript(string sandboxDirectory, string name, string body)
    {
        if (OperatingSystem.IsWindows())
        {
            var scriptPath = Path.Combine(sandboxDirectory, $"{name}.cmd");
            File.WriteAllText(scriptPath, $"@echo off{Environment.NewLine}{body}{Environment.NewLine}");
            return new ShellScriptHandle(
                scriptPath,
                new CodexProcessInvocation(
                    "cmd.exe",
                    ["/d", "/s", "/c", scriptPath],
                    CreateProcessEnvironment(),
                    string.Empty));
        }

        var scriptFilePath = Path.Combine(sandboxDirectory, $"{name}.sh");
        File.WriteAllText(scriptFilePath, $"#!/bin/sh{Environment.NewLine}{body}{Environment.NewLine}");
        return new ShellScriptHandle(
            scriptFilePath,
            new CodexProcessInvocation(
                "/bin/sh",
                [scriptFilePath],
                CreateProcessEnvironment(),
                string.Empty));
    }

    private static Dictionary<string, string> CreateProcessEnvironment()
    {
        return Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .ToDictionary(
                entry => entry.Key?.ToString() ?? string.Empty,
                entry => entry.Value?.ToString() ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static bool ContainsPair(IReadOnlyList<string> args, string key, string value)
    {
        for (var index = 0; index < args.Count - 1; index += 1)
        {
            if (args[index] == key && args[index + 1] == value)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> CollectConfigValues(IReadOnlyList<string> args, string key)
    {
        var result = new List<string>();
        for (var index = 0; index < args.Count - 1; index += 1)
        {
            if (args[index] == "--config" && args[index + 1].StartsWith($"{key}=", StringComparison.Ordinal))
            {
                result.Add(args[index + 1]);
            }
        }

        return result;
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

    private sealed record ShellScriptHandle(string ScriptPath, CodexProcessInvocation Invocation);
}
