using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagedCode.CodexSharpSDK.Execution;

public sealed class CodexExec : IDisposable
{
    private const string ExecCommandName = "exec";
    private const string ResumeCommandName = "resume";

    private const string JsonFlag = "--json";
    private const string ConfigFlag = "--config";
    private const string EnableFeatureFlag = "--enable";
    private const string DisableFeatureFlag = "--disable";
    private const string ModelFlag = "--model";
    private const string SandboxFlag = "--sandbox";
    private const string WorkingDirectoryFlag = "--cd";
    private const string AddDirectoryFlag = "--add-dir";
    private const string ProfileFlag = "--profile";
    private const string SkipGitRepoCheckFlag = "--skip-git-repo-check";
    private const string OutputSchemaFlag = "--output-schema";
    private const string UseOssFlag = "--oss";
    private const string LocalProviderFlag = "--local-provider";
    private const string FullAutoFlag = "--full-auto";
    private const string DangerouslyBypassApprovalsAndSandboxFlag = "--dangerously-bypass-approvals-and-sandbox";
    private const string EphemeralFlag = "--ephemeral";
    private const string ColorFlag = "--color";
    private const string ProgressCursorFlag = "--progress-cursor";
    private const string OutputLastMessageFlag = "--output-last-message";
    private const string ImageFlag = "--image";

    private const string ModelReasoningEffortConfigKey = "model_reasoning_effort";
    private const string SandboxNetworkAccessConfigKey = "sandbox_workspace_write.network_access";
    private const string WebSearchConfigKey = "web_search";
    private const string ApprovalPolicyConfigKey = "approval_policy";
    private const string EphemeralConfigKey = "ephemeral";
    private const string WebSearchLiveValue = "live";
    private const string WebSearchDisabledValue = "disabled";
    private const string BooleanTrueLiteral = "true";
    private const string BooleanFalseLiteral = "false";

    private const string InternalOriginatorEnv = "CODEX_INTERNAL_ORIGINATOR_OVERRIDE";
    private const string CSharpSdkOriginator = "codex_sdk_csharp";
    private const string OpenAiBaseUrlEnv = "OPENAI_BASE_URL";
    private const string CodexApiKeyEnv = "CODEX_API_KEY";

    private readonly string _executablePath;
    private readonly IReadOnlyDictionary<string, string>? _environmentOverride;
    private readonly bool _inheritEnvironmentVariables;
    private readonly JsonObject? _configOverrides;
    private readonly ICodexProcessRunner _processRunner;
    private readonly ILogger _logger;
    private readonly TimeSpan _processTerminationTimeout;
    private readonly int _maximumProcessOutputCharacters;
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    public CodexExec(
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? environmentOverride = null,
        JsonObject? configOverrides = null,
        ILogger? logger = null)
        : this(executablePath, environmentOverride, configOverrides, null, logger, CodexOptions.DefaultProcessTerminationTimeout, null)
    {
    }

    public CodexExec(
        TimeSpan processTerminationTimeout,
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? environmentOverride = null,
        JsonObject? configOverrides = null,
        ILogger? logger = null)
        : this(executablePath, environmentOverride, configOverrides, null, logger, processTerminationTimeout, null)
    {
    }

    internal CodexExec(
        string? executablePath,
        IReadOnlyDictionary<string, string>? environmentOverride,
        JsonObject? configOverrides,
        ICodexProcessRunner? processRunner,
        ILogger? logger = null,
        TimeSpan? processTerminationTimeout = null,
        bool? inheritEnvironmentVariables = null,
        int maximumProcessOutputCharacters = CodexOptions.DefaultMaximumProcessOutputCharacters)
    {
        _executablePath = CodexCliLocator.FindCodexPath(executablePath);
        _environmentOverride = environmentOverride;
        _inheritEnvironmentVariables = inheritEnvironmentVariables ?? environmentOverride is null;
        _configOverrides = configOverrides;
        _processRunner = processRunner ?? new DefaultCodexProcessRunner();
        _logger = logger ?? NullLogger.Instance;
        _processTerminationTimeout = processTerminationTimeout ?? CodexOptions.DefaultProcessTerminationTimeout;
        if (_processTerminationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(processTerminationTimeout));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumProcessOutputCharacters);
        _maximumProcessOutputCharacters = maximumProcessOutputCharacters;
    }

    public IAsyncEnumerable<string> RunAsync(CodexExecArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var commandArgs = BuildCommandArgs(args);
        var environment = BuildEnvironment(args.BaseUrl, args.ApiKey);
        var invocation = new CodexProcessInvocation(_executablePath, commandArgs, environment, args.Input)
        {
            ProcessTerminationTimeout = _processTerminationTimeout,
            MaximumProcessOutputCharacters = _maximumProcessOutputCharacters,
        };

        return RunWithDiagnosticsAsync(invocation, args.CancellationToken);
    }

    internal void CancelActiveRuns() => _lifetimeCancellation.Cancel();

    public void Dispose()
    {
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }

    private async IAsyncEnumerable<string> RunWithDiagnosticsAsync(
        CodexProcessInvocation invocation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        var effectiveCancellationToken = linkedCancellation.Token;

        Logging.CodexExecLog.Starting(_logger, invocation.ExecutablePath, invocation.Arguments.Count);

        if (effectiveCancellationToken.IsCancellationRequested)
        {
            Logging.CodexExecLog.Cancelled(_logger);
            effectiveCancellationToken.ThrowIfCancellationRequested();
        }

        var lineCount = 0;

        IAsyncEnumerator<string> enumerator;
        try
        {
            enumerator = _processRunner
                .RunAsync(invocation, _logger, effectiveCancellationToken)
                .GetAsyncEnumerator(effectiveCancellationToken);
        }
        catch (OperationCanceledException) when (effectiveCancellationToken.IsCancellationRequested)
        {
            Logging.CodexExecLog.Cancelled(_logger);
            throw;
        }
        catch (Exception)
        {
            Logging.CodexExecLog.Failed(_logger);
            throw;
        }

        await using (enumerator)
        {
            while (true)
            {
                string line;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    line = enumerator.Current;
                }
                catch (OperationCanceledException) when (effectiveCancellationToken.IsCancellationRequested)
                {
                    Logging.CodexExecLog.Cancelled(_logger);
                    throw;
                }
                catch (Exception)
                {
                    Logging.CodexExecLog.Failed(_logger);
                    throw;
                }

                lineCount += 1;
                yield return line;
            }

            Logging.CodexExecLog.Completed(_logger, lineCount);
        }
    }

    internal IReadOnlyList<string> BuildCommandArgs(CodexExecArgs args)
    {
        var commandArgs = new List<string> { ExecCommandName, JsonFlag };

        if (_configOverrides is not null)
        {
            foreach (var overrideValue in TomlConfigSerializer.Serialize(_configOverrides))
            {
                commandArgs.Add(ConfigFlag);
                commandArgs.Add(overrideValue);
            }
        }

        AddRepeatedFlag(commandArgs, EnableFeatureFlag, args.EnabledFeatures);
        AddRepeatedFlag(commandArgs, DisableFeatureFlag, args.DisabledFeatures);

        if (args.UseOss)
        {
            commandArgs.Add(UseOssFlag);
        }

        if (args.LocalProvider.HasValue)
        {
            commandArgs.Add(LocalProviderFlag);
            commandArgs.Add(args.LocalProvider.Value.ToCliValue());
        }

        if (!string.IsNullOrWhiteSpace(args.Profile))
        {
            commandArgs.Add(ProfileFlag);
            commandArgs.Add(args.Profile);
        }

        if (!string.IsNullOrWhiteSpace(args.Model))
        {
            commandArgs.Add(ModelFlag);
            commandArgs.Add(args.Model);
        }

        if (args.SandboxMode.HasValue)
        {
            commandArgs.Add(SandboxFlag);
            commandArgs.Add(args.SandboxMode.Value.ToCliValue());
        }

        if (!string.IsNullOrWhiteSpace(args.WorkingDirectory))
        {
            commandArgs.Add(WorkingDirectoryFlag);
            commandArgs.Add(args.WorkingDirectory);
        }

        if (args.AdditionalDirectories is not null)
        {
            foreach (var directory in args.AdditionalDirectories)
            {
                commandArgs.Add(AddDirectoryFlag);
                commandArgs.Add(directory);
            }
        }

        if (args.FullAuto)
        {
            commandArgs.Add(FullAutoFlag);
        }

        if (args.DangerouslyBypassApprovalsAndSandbox)
        {
            commandArgs.Add(DangerouslyBypassApprovalsAndSandboxFlag);
        }

        if (args.HasEphemeralOverride)
        {
            if (args.Ephemeral)
            {
                commandArgs.Add(EphemeralFlag);
            }
            else
            {
                commandArgs.Add(ConfigFlag);
                commandArgs.Add(BuildBooleanConfig(EphemeralConfigKey, false));
            }
        }

        if (args.Color.HasValue)
        {
            commandArgs.Add(ColorFlag);
            commandArgs.Add(args.Color.Value.ToCliValue());
        }

        if (args.ProgressCursor)
        {
            commandArgs.Add(ProgressCursorFlag);
        }

        if (!string.IsNullOrWhiteSpace(args.OutputLastMessageFile))
        {
            commandArgs.Add(OutputLastMessageFlag);
            commandArgs.Add(args.OutputLastMessageFile);
        }

        if (args.SkipGitRepoCheck)
        {
            commandArgs.Add(SkipGitRepoCheckFlag);
        }

        if (!string.IsNullOrWhiteSpace(args.OutputSchemaFile))
        {
            commandArgs.Add(OutputSchemaFlag);
            commandArgs.Add(args.OutputSchemaFile);
        }

        if (args.ModelReasoningEffort.HasValue)
        {
            commandArgs.Add(ConfigFlag);
            commandArgs.Add(BuildQuotedConfig(ModelReasoningEffortConfigKey, args.ModelReasoningEffort.Value.ToCliValue()));
        }

        if (args.NetworkAccessEnabled.HasValue)
        {
            commandArgs.Add(ConfigFlag);
            commandArgs.Add(BuildBooleanConfig(SandboxNetworkAccessConfigKey, args.NetworkAccessEnabled.Value));
        }

        if (args.WebSearchMode.HasValue)
        {
            commandArgs.Add(ConfigFlag);
            commandArgs.Add(BuildQuotedConfig(WebSearchConfigKey, args.WebSearchMode.Value.ToCliValue()));
        }
        else if (args.WebSearchEnabled.HasValue)
        {
            commandArgs.Add(ConfigFlag);
            commandArgs.Add(BuildQuotedConfig(
                WebSearchConfigKey,
                args.WebSearchEnabled.Value ? WebSearchLiveValue : WebSearchDisabledValue));
        }

        if (args.ApprovalPolicy.HasValue)
        {
            commandArgs.Add(ConfigFlag);
            commandArgs.Add(BuildQuotedConfig(ApprovalPolicyConfigKey, args.ApprovalPolicy.Value.ToCliValue()));
        }

        if (args.AdditionalCliArguments is not null)
        {
            foreach (var argument in args.AdditionalCliArguments)
            {
                if (string.IsNullOrWhiteSpace(argument))
                {
                    continue;
                }

                commandArgs.Add(argument);
            }
        }

        if (!string.IsNullOrWhiteSpace(args.ThreadId))
        {
            commandArgs.Add(ResumeCommandName);
            commandArgs.Add(args.ThreadId);
        }

        if (args.Images is not null)
        {
            foreach (var image in args.Images)
            {
                commandArgs.Add(ImageFlag);
                commandArgs.Add(image);
            }
        }

        return commandArgs;
    }

    internal IReadOnlyDictionary<string, string> BuildEnvironment(string? baseUrl, string? apiKey)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_inheritEnvironmentVariables)
        {
            foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            {
                if (variable.Key is string key && variable.Value is string value)
                {
                    environment[key] = value;
                }
            }
        }

        if (_environmentOverride is not null)
        {
            foreach (var (key, value) in _environmentOverride)
            {
                environment[key] = value;
            }
        }

        if (!environment.ContainsKey(InternalOriginatorEnv))
        {
            environment[InternalOriginatorEnv] = CSharpSdkOriginator;
        }

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            environment[OpenAiBaseUrlEnv] = baseUrl;
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            environment[CodexApiKeyEnv] = apiKey;
        }

        return environment;
    }

    private static void AddRepeatedFlag(
        List<string> commandArgs,
        string flag,
        IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            commandArgs.Add(flag);
            commandArgs.Add(value);
        }
    }

    private static string BuildQuotedConfig(string key, string value) => $"{key}=\"{value}\"";

    private static string BuildBooleanConfig(string key, bool value)
    {
        var literal = value ? BooleanTrueLiteral : BooleanFalseLiteral;
        return $"{key}={literal}";
    }
}

internal sealed record CodexProcessInvocation(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string Input)
{
    public TimeSpan ProcessTerminationTimeout { get; init; } = CodexOptions.DefaultProcessTerminationTimeout;
    public int MaximumProcessOutputCharacters { get; init; } = CodexOptions.DefaultMaximumProcessOutputCharacters;
    public Action? StandardErrorReaderCompleted { get; init; }
    public Action? StandardOutputReadCompleted { get; init; }
    public Action? StandardErrorOutputLimitExceeded { get; init; }
}

internal interface ICodexProcessRunner
{
    IAsyncEnumerable<string> RunAsync(
        CodexProcessInvocation invocation,
        ILogger logger,
        CancellationToken cancellationToken);
}

internal sealed class DefaultCodexProcessRunner : ICodexProcessRunner
{
    private const string ProcessTerminationUnconfirmedMessage = "Could not confirm that the Codex CLI process exited within the configured process termination timeout.";
    private const string StderrTerminationUnconfirmedMessage = "Could not confirm that the Codex CLI stderr stream closed within the configured process termination timeout.";
    private const string ProcessOutputLimitExceededMessage = "Codex CLI process exceeded the configured output limit.";
    private const string StandardOutputTerminationUnconfirmedMessage = "Codex CLI standard output did not close within the configured time limit.";
    private const string ProcessAndReaderCleanupUnconfirmedMessage = "Codex CLI process and output cleanup could not be confirmed.";
    private const int ProcessOutputBufferCharacters = 4096;

    public async IAsyncEnumerable<string> RunAsync(
        CodexProcessInvocation invocation,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(invocation.ExecutablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var (key, value) in invocation.Environment)
        {
            startInfo.Environment[key] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        Task<BoundedProcessOutput>? standardErrorTask = null;
        Task<string?>? standardOutputReadTask = null;
        Task? standardInputWriteTask = null;
        using var outputCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var standardErrorLimitExceeded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? cleanupFailure = null;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start Codex CLI at '{invocation.ExecutablePath}'");
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to start Codex CLI at '{invocation.ExecutablePath}'", exception);
        }

        try
        {
            standardErrorTask = ReadBoundedProcessOutputAsync(process.StandardError,
                invocation.MaximumProcessOutputCharacters, () =>
                {
                    standardErrorLimitExceeded.TrySetResult(true);
                    invocation.StandardErrorOutputLimitExceeded?.Invoke();
                    TryKillProcess(process, invocation.ExecutablePath, logger);
                    outputCancellation.Cancel();
                }, outputCancellation.Token, invocation.StandardErrorReaderCompleted);
            var standardOutput = new BoundedProcessOutputReader(process.StandardOutput,
                invocation.MaximumProcessOutputCharacters, invocation.StandardOutputReadCompleted);
            standardOutputReadTask = standardOutput.ReadLineAsync(outputCancellation.Token).AsTask();
            standardInputWriteTask = WriteStandardInputAsync(process.StandardInput, invocation.Input, outputCancellation.Token);
            while (true)
            {
                var readLineTask = standardOutputReadTask!;
                var completedTask = standardInputWriteTask is null
                    ? await Task.WhenAny(readLineTask, standardErrorLimitExceeded.Task).ConfigureAwait(false)
                    : await Task.WhenAny(readLineTask, standardErrorLimitExceeded.Task, standardInputWriteTask).ConfigureAwait(false);
                if (completedTask == standardErrorLimitExceeded.Task || standardErrorLimitExceeded.Task.IsCompleted)
                {
                    try
                    {
                        await readLineTask.WaitAsync(invocation.ProcessTerminationTimeout, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // The stderr overflow handler canceled the sibling reader after requesting process exit.
                    }
                    catch (TimeoutException)
                    {
                        // Closing the redirected stream in finally gets a second bounded chance to settle the read.
                    }

                    await EnsureProcessExitedAfterCancellationAsync(process, invocation, logger).ConfigureAwait(false);
                    throw new InvalidOperationException(ProcessOutputLimitExceededMessage);
                }

                if (standardInputWriteTask is not null && completedTask == standardInputWriteTask)
                {
                    await AwaitStandardInputWriteAsync(standardInputWriteTask, process, standardErrorTask!,
                        invocation, logger, cancellationToken).ConfigureAwait(false);
                    standardInputWriteTask = null;
                    continue;
                }

                string? line;
                try
                {
                    line = await readLineTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    standardOutputReadTask = null;
                    var terminatedByCancellation = await EnsureProcessExitedAfterCancellationAsync(
                        process,
                        invocation,
                        logger).ConfigureAwait(false);
                    var capturedStandardError = await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                    if (!terminatedByCancellation && process.ExitCode != 0)
                    {
                        throw new InvalidOperationException($"Codex Exec exited with code {process.ExitCode}: {capturedStandardError.Text}");
                    }

                    throw;
                }

                standardOutputReadTask = null;
                if (line is null)
                {
                    if (standardInputWriteTask is not null)
                    {
                        await AwaitStandardInputWriteAsync(standardInputWriteTask, process, standardErrorTask!,
                            invocation, logger, cancellationToken).ConfigureAwait(false);
                        standardInputWriteTask = null;
                    }
                    break;
                }

                yield return line;
                standardOutputReadTask = standardOutput.ReadLineAsync(outputCancellation.Token).AsTask();
            }

            var standardError = await CompleteProcessAsync(
                process,
                standardErrorTask,
                invocation,
                logger,
                cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Codex Exec exited with code {process.ExitCode}: {standardError.Text}");
            }
        }
        finally
        {
            Exception? processExitFailure = null;
            try
            {
                await EnsureProcessExitedAfterCancellationAsync(process, invocation, logger).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                processExitFailure = exception;
            }

            var readerFailures = new List<Exception>(4);
            try
            {
                process.StandardInput.BaseStream.Dispose();
            }
            catch (Exception exception)
            {
                readerFailures.Add(exception);
            }

            if (standardInputWriteTask is not null)
            {
                try
                {
                    await standardInputWriteTask.WaitAsync(invocation.ProcessTerminationTimeout, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (outputCancellation.IsCancellationRequested && standardInputWriteTask.IsCanceled)
                {
                    // Owned I/O cancellation ends the stdin pump after root termination was requested.
                }
                catch (IOException) when (standardErrorLimitExceeded.Task.IsCompleted &&
                                          standardInputWriteTask.IsFaulted &&
                                          standardInputWriteTask.Exception?.GetBaseException() is IOException)
                {
                    // The visible stderr output-limit failure owns this induced broken pipe.
                }
                catch (Exception exception)
                {
                    readerFailures.Add(exception);
                }
            }
            try
            {
                outputCancellation.Cancel();
            }
            catch (Exception exception)
            {
                readerFailures.Add(exception);
            }

            try
            {
                process.StandardOutput.Dispose();
            }
            catch (Exception exception)
            {
                readerFailures.Add(exception);
            }

            try
            {
                process.StandardError.Dispose();
            }
            catch (Exception exception)
            {
                readerFailures.Add(exception);
            }
            var standardOutputFailure = await ObserveStandardOutputReadAsync(
                standardOutputReadTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
            var standardOutputLimitFailureIsExpected = standardOutputReadTask is { IsFaulted: true } &&
                standardOutputReadTask.Exception?.GetBaseException() is InvalidOperationException standardOutputLimitException &&
                string.Equals(standardOutputLimitException.Message, ProcessOutputLimitExceededMessage, StringComparison.Ordinal);
            Exception? standardErrorFailure = null;
            if (standardErrorTask is not null)
            {
                try
                {
                    await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    Logging.CodexExecLog.StandardErrorReadFailed(logger, invocation.ExecutablePath);
                    standardErrorFailure = new InvalidOperationException(StderrTerminationUnconfirmedMessage);
                }
            }

            if (standardOutputFailure is not null && !standardOutputLimitFailureIsExpected)
            {
                readerFailures.Add(standardOutputFailure);
            }

            var standardErrorLimitFailureIsExpected = standardErrorTask is { IsFaulted: true } &&
                standardErrorTask.Exception?.GetBaseException() is InvalidOperationException standardErrorLimitException &&
                string.Equals(standardErrorLimitException.Message, ProcessOutputLimitExceededMessage, StringComparison.Ordinal);
            if (standardErrorFailure is not null && !standardErrorLimitFailureIsExpected)
            {
                readerFailures.Add(standardErrorFailure);
            }

            if (processExitFailure is not null && readerFailures.Count > 0)
            {
                cleanupFailure = new InvalidOperationException(
                    processExitFailure.Message,
                    new AggregateException(new[] { processExitFailure }.Concat(readerFailures)));
            }
            else if (processExitFailure is not null)
            {
                cleanupFailure = processExitFailure;
            }
            else if (readerFailures.Count == 1)
            {
                cleanupFailure = readerFailures[0];
            }
            else if (readerFailures.Count > 1)
            {
                cleanupFailure = new InvalidOperationException(
                    ProcessAndReaderCleanupUnconfirmedMessage,
                    new AggregateException(readerFailures));
            }

            if (cleanupFailure is not null)
            {
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            }
        }
    }

    private static async Task<Exception?> ObserveStandardOutputReadAsync(
        Task<string?>? standardOutputReadTask,
        TimeSpan timeout)
    {
        if (standardOutputReadTask is null)
        {
            return null;
        }

        try
        {
            await standardOutputReadTask.WaitAsync(timeout).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (TimeoutException exception)
        {
            return new InvalidOperationException(StandardOutputTerminationUnconfirmedMessage, exception);
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<BoundedProcessOutput> CompleteProcessAsync(
        Process process,
        Task<BoundedProcessOutput> standardErrorTask,
        CodexProcessInvocation invocation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var terminatedByCancellation = await EnsureProcessExitedAfterCancellationAsync(
                process,
                invocation,
                logger).ConfigureAwait(false);
            var standardError = await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
            if (!terminatedByCancellation)
            {
                return standardError;
            }

            throw;
        }

        return await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout).ConfigureAwait(false);
    }

    private static async Task<BoundedProcessOutput> ReadStandardErrorAsync(Task<BoundedProcessOutput> standardErrorTask, TimeSpan timeout)
    {
        try
        {
            return await standardErrorTask.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(StderrTerminationUnconfirmedMessage, exception);
        }
    }

    private static async Task<BoundedProcessOutput> ReadBoundedProcessOutputAsync(
        TextReader reader,
        int maximumCharacters,
        Action onLimitExceeded,
        CancellationToken cancellationToken,
        Action? onCompleted = null)
    {
        var output = new StringBuilder(Math.Min(maximumCharacters, ProcessOutputBufferCharacters));
        var buffer = new char[ProcessOutputBufferCharacters];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            {
                var append = Math.Min(maximumCharacters - output.Length, read);
                if (append > 0)
                {
                    output.Append(buffer, 0, append);
                }

                if (append < read)
                {
                    onLimitExceeded();
                    throw new InvalidOperationException(ProcessOutputLimitExceededMessage);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Process cancellation is handled by the owner and the partially captured text stays bounded.
        }
        finally
        {
            onCompleted?.Invoke();
        }

        return new BoundedProcessOutput(output.ToString());
    }

    private static async Task WriteStandardInputAsync(
        StreamWriter standardInput,
        string input,
        CancellationToken cancellationToken)
    {
        await standardInput.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
        await standardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        await standardInput.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task AwaitStandardInputWriteAsync(
        Task standardInputWriteTask,
        Process process,
        Task<BoundedProcessOutput> standardErrorTask,
        CodexProcessInvocation invocation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await standardInputWriteTask.WaitAsync(invocation.ProcessTerminationTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var terminatedByCancellation = await EnsureProcessExitedAfterCancellationAsync(process, invocation, logger)
                .ConfigureAwait(false);
            var capturedStandardError = await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout)
                .ConfigureAwait(false);
            if (!terminatedByCancellation && process.HasExited && process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Codex Exec exited with code {process.ExitCode}: {capturedStandardError.Text}");
            }

            throw;
        }
        catch (Exception) when (process.HasExited)
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(invocation.ProcessTerminationTimeout, CancellationToken.None)
                .ConfigureAwait(false);
            var capturedStandardError = await ReadStandardErrorAsync(standardErrorTask, invocation.ProcessTerminationTimeout)
                .ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Codex Exec exited with code {process.ExitCode}: {capturedStandardError.Text}");
            }

            throw;
        }
    }

    private static async Task<bool> EnsureProcessExitedAfterCancellationAsync(
        Process process,
        CodexProcessInvocation invocation,
        ILogger logger)
    {
        if (process.HasExited)
        {
            return false;
        }

        TryKillProcess(process, invocation.ExecutablePath, logger);
        try
        {
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(invocation.ProcessTerminationTimeout)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(
                ProcessTerminationUnconfirmedMessage,
                exception);
        }
    }

    private static void TryKillProcess(Process process, string executablePath, ILogger logger)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            Logging.CodexExecLog.ProcessKillFailed(logger, executablePath);
        }
    }
}

internal sealed record BoundedProcessOutput(string Text);

internal sealed class BoundedProcessOutputReader(
    TextReader reader,
    int maximumCharacters,
    Action? onReadCompleted = null)
{
    private const int BufferCharacters = 4096;
    private const string OutputLimitExceededMessage = "Codex CLI process exceeded the configured output limit.";
    private readonly char[] _buffer = new char[BufferCharacters];
    private readonly StringBuilder _line = new(Math.Min(maximumCharacters, BufferCharacters));
    private int _bufferCount;
    private int _bufferIndex;
    private int _charactersRead;
    private bool _endOfStream;

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_bufferIndex >= _bufferCount)
            {
                if (_endOfStream)
                {
                    return TakeFinalLine();
                }

                _bufferCount = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                _bufferIndex = 0;
                if (_bufferCount == 0)
                {
                    _endOfStream = true;
                    onReadCompleted?.Invoke();
                    return TakeFinalLine();
                }
            }

            var character = _buffer[_bufferIndex++];
            if (_charactersRead >= maximumCharacters)
            {
                throw new InvalidOperationException(OutputLimitExceededMessage);
            }

            _charactersRead++;
            if (character == '\n')
            {
                if (_line.Length > 0 && _line[^1] == '\r')
                {
                    _line.Length--;
                }

                var result = _line.ToString();
                _line.Clear();
                return result;
            }

            _line.Append(character);
        }
    }

    private string? TakeFinalLine()
    {
        if (_line.Length == 0)
        {
            return null;
        }

        var result = _line.ToString();
        _line.Clear();
        return result;
    }
}

internal static class CliValueExtensions
{
    private const string SandboxReadOnly = "read-only";
    private const string SandboxWorkspaceWrite = "workspace-write";
    private const string SandboxDangerFullAccess = "danger-full-access";

    private const string ReasoningMinimal = "minimal";
    private const string ReasoningLow = "low";
    private const string ReasoningMedium = "medium";
    private const string ReasoningHigh = "high";
    private const string ReasoningXHigh = "xhigh";

    private const string WebSearchDisabled = "disabled";
    private const string WebSearchCached = "cached";
    private const string WebSearchLive = "live";

    private const string ApprovalNever = "never";
    private const string ApprovalOnRequest = "on-request";
    private const string ApprovalOnFailure = "on-failure";
    private const string ApprovalUntrusted = "untrusted";

    private const string OssProviderLmStudio = "lmstudio";
    private const string OssProviderOllama = "ollama";

    private const string ColorAlways = "always";
    private const string ColorNever = "never";
    private const string ColorAuto = "auto";

    public static string ToCliValue(this SandboxMode mode)
    {
        return mode switch
        {
            SandboxMode.ReadOnly => SandboxReadOnly,
            SandboxMode.WorkspaceWrite => SandboxWorkspaceWrite,
            SandboxMode.DangerFullAccess => SandboxDangerFullAccess,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static string ToCliValue(this ModelReasoningEffort effort)
    {
        return effort switch
        {
            ModelReasoningEffort.Minimal => ReasoningMinimal,
            ModelReasoningEffort.Low => ReasoningLow,
            ModelReasoningEffort.Medium => ReasoningMedium,
            ModelReasoningEffort.High => ReasoningHigh,
            ModelReasoningEffort.XHigh => ReasoningXHigh,
            _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null),
        };
    }

    public static string ToCliValue(this WebSearchMode mode)
    {
        return mode switch
        {
            WebSearchMode.Disabled => WebSearchDisabled,
            WebSearchMode.Cached => WebSearchCached,
            WebSearchMode.Live => WebSearchLive,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static string ToCliValue(this ApprovalMode mode)
    {
        return mode switch
        {
            ApprovalMode.Never => ApprovalNever,
            ApprovalMode.OnRequest => ApprovalOnRequest,
            ApprovalMode.OnFailure => ApprovalOnFailure,
            ApprovalMode.Untrusted => ApprovalUntrusted,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static string ToCliValue(this OssProvider provider)
    {
        return provider switch
        {
            OssProvider.LmStudio => OssProviderLmStudio,
            OssProvider.Ollama => OssProviderOllama,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
    }

    public static string ToCliValue(this ExecOutputColor color)
    {
        return color switch
        {
            ExecOutputColor.Always => ColorAlways,
            ExecOutputColor.Never => ColorNever,
            ExecOutputColor.Auto => ColorAuto,
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
        };
    }
}
