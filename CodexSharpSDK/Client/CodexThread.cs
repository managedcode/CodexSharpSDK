using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Exceptions;
using ManagedCode.CodexSharpSDK.Execution;
using ManagedCode.CodexSharpSDK.Internal;
using ManagedCode.CodexSharpSDK.Logging;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagedCode.CodexSharpSDK.Client;

public sealed class CodexThread
    : IDisposable
{
    private const string TypedRunRequiresOutputSchemaMessage = "Typed run requires TurnOptions.OutputSchema to be set.";
    private const string EmptyTypedRunResponseMessagePrefix = "Model returned empty structured output for type";
    private const string TypedRunDeserializeFailedMessagePrefix = "Failed to deserialize model response to type";
    private const string TypedRunRequiresTypeInfoMessage =
        "Reflection-based JSON serialization is disabled. Use RunAsync<TResponse>(..., JsonTypeInfo<TResponse>, ...) for typed output.";
    private const string ReflectionDisabledErrorToken = "Reflection-based serialization has been disabled";
    private const string AotUnsafeTypedRunMessage =
        "This overload relies on reflection-based JSON serialization and is not AOT/trimming-safe. Use the JsonTypeInfo<TResponse> overload.";

    private readonly CodexExec _exec;
    private readonly CodexOptions _options;
    private readonly ThreadOptions _threadOptions;
    // One active turn per thread instance (ADR 002).
    private readonly SemaphoreSlim _turnGate = new(1, 1);
    private int _disposed;
    private string? _id;

    internal CodexThread(
        CodexExec exec,
        CodexOptions options,
        ThreadOptions threadOptions,
        string? id = null)
    {
        _exec = exec;
        _options = options;
        _threadOptions = threadOptions;
        _id = id;
    }

    public string? Id => Volatile.Read(ref _id);

    public Task<RunStreamedResult> RunStreamedAsync(string input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        var normalizedInput = new NormalizedInput(input, []);
        var resolvedTurnOptions = turnOptions ?? new TurnOptions();
        return Task.FromResult(new RunStreamedResult(RunStreamedWithTurnGateAsync(normalizedInput, resolvedTurnOptions)));
    }

    public Task<RunStreamedResult> RunStreamedAsync(IReadOnlyList<UserInput> input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        var normalizedInput = NormalizeInput(input);
        var resolvedTurnOptions = turnOptions ?? new TurnOptions();
        return Task.FromResult(new RunStreamedResult(RunStreamedWithTurnGateAsync(normalizedInput, resolvedTurnOptions)));
    }

    public Task<RunResult> RunAsync(string input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        var normalizedInput = new NormalizedInput(input, []);
        return RunInternalAsync(normalizedInput, turnOptions ?? new TurnOptions());
    }

    public Task<RunResult> RunAsync(IReadOnlyList<UserInput> input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        var normalizedInput = NormalizeInput(input);
        return RunInternalAsync(normalizedInput, turnOptions ?? new TurnOptions());
    }

    [RequiresDynamicCode(AotUnsafeTypedRunMessage)]
    [RequiresUnreferencedCode(AotUnsafeTypedRunMessage)]
    public async Task<RunResult<TResponse>> RunAsync<TResponse>(string input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        var runOptions = EnsureTypedRunOptions(turnOptions);
        var normalizedInput = new NormalizedInput(input, []);
        return await RunTypedInternalAsync<TResponse>(normalizedInput, runOptions, jsonTypeInfo: null).ConfigureAwait(false);
    }

    public async Task<RunResult<TResponse>> RunAsync<TResponse>(
        string input,
        JsonTypeInfo<TResponse> jsonTypeInfo,
        TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        var runOptions = EnsureTypedRunOptions(turnOptions);
        var normalizedInput = new NormalizedInput(input, []);
        return await RunTypedInternalAsync(normalizedInput, runOptions, jsonTypeInfo).ConfigureAwait(false);
    }

    [RequiresDynamicCode(AotUnsafeTypedRunMessage)]
    [RequiresUnreferencedCode(AotUnsafeTypedRunMessage)]
    public async Task<RunResult<TResponse>> RunAsync<TResponse>(IReadOnlyList<UserInput> input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        var runOptions = EnsureTypedRunOptions(turnOptions);
        var normalizedInput = NormalizeInput(input);
        return await RunTypedInternalAsync<TResponse>(normalizedInput, runOptions, jsonTypeInfo: null).ConfigureAwait(false);
    }

    public async Task<RunResult<TResponse>> RunAsync<TResponse>(
        IReadOnlyList<UserInput> input,
        JsonTypeInfo<TResponse> jsonTypeInfo,
        TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        var runOptions = EnsureTypedRunOptions(turnOptions);
        var normalizedInput = NormalizeInput(input);
        return await RunTypedInternalAsync(normalizedInput, runOptions, jsonTypeInfo).ConfigureAwait(false);
    }

    [RequiresDynamicCode(AotUnsafeTypedRunMessage)]
    [RequiresUnreferencedCode(AotUnsafeTypedRunMessage)]
    public Task<RunResult<TResponse>> RunAsync<TResponse>(
        string input,
        StructuredOutputSchema outputSchema,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        var normalizedInput = new NormalizedInput(input, []);
        var runOptions = CreateTypedTurnOptions(outputSchema, cancellationToken);
        return RunTypedInternalAsync<TResponse>(normalizedInput, runOptions, jsonTypeInfo: null);
    }

    public Task<RunResult<TResponse>> RunAsync<TResponse>(
        string input,
        StructuredOutputSchema outputSchema,
        JsonTypeInfo<TResponse> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        var normalizedInput = new NormalizedInput(input, []);
        var runOptions = CreateTypedTurnOptions(outputSchema, cancellationToken);
        return RunTypedInternalAsync(normalizedInput, runOptions, jsonTypeInfo);
    }

    [RequiresDynamicCode(AotUnsafeTypedRunMessage)]
    [RequiresUnreferencedCode(AotUnsafeTypedRunMessage)]
    public Task<RunResult<TResponse>> RunAsync<TResponse>(
        IReadOnlyList<UserInput> input,
        StructuredOutputSchema outputSchema,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var normalizedInput = NormalizeInput(input);
        var runOptions = CreateTypedTurnOptions(outputSchema, cancellationToken);
        return RunTypedInternalAsync<TResponse>(normalizedInput, runOptions, jsonTypeInfo: null);
    }

    public Task<RunResult<TResponse>> RunAsync<TResponse>(
        IReadOnlyList<UserInput> input,
        StructuredOutputSchema outputSchema,
        JsonTypeInfo<TResponse> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        var normalizedInput = NormalizeInput(input);
        var runOptions = CreateTypedTurnOptions(outputSchema, cancellationToken);
        return RunTypedInternalAsync(normalizedInput, runOptions, jsonTypeInfo);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _turnGate.Dispose();
    }

    private async Task<RunResult> RunInternalAsync(NormalizedInput normalizedInput, TurnOptions turnOptions)
    {
        var items = new List<ThreadItem>();
        var finalResponse = string.Empty;
        Usage? usage = null;
        ThreadError? turnFailure = null;

        await foreach (var threadEvent in RunStreamedWithTurnGateAsync(normalizedInput, turnOptions)
                           .ConfigureAwait(false))
        {
            switch (threadEvent)
            {
                case ItemCompletedEvent itemCompletedEvent:
                    if (itemCompletedEvent.Item is AgentMessageItem message)
                    {
                        finalResponse = message.Text;
                    }

                    items.Add(itemCompletedEvent.Item);
                    break;

                case TurnCompletedEvent turnCompletedEvent:
                    usage = turnCompletedEvent.Usage;
                    break;

                case TurnFailedEvent turnFailedEvent:
                    turnFailure = turnFailedEvent.Error;
                    break;
            }

            if (turnFailure is not null)
            {
                break;
            }
        }

        if (turnFailure is not null)
        {
            throw new ThreadRunException(turnFailure.Message);
        }

        return new RunResult(items, finalResponse, usage);
    }

    private async IAsyncEnumerable<ThreadEvent> RunStreamedWithTurnGateAsync(
        NormalizedInput normalizedInput,
        TurnOptions turnOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            turnOptions.CancellationToken,
            cancellationToken);

        var linkedCancellationToken = linkedCancellationTokenSource.Token;

        await _turnGate.WaitAsync(linkedCancellationToken).ConfigureAwait(false);
        try
        {
            await foreach (var threadEvent in RunStreamedInternalAsync(normalizedInput, turnOptions, linkedCancellationToken)
                               .WithCancellation(linkedCancellationToken)
                               .ConfigureAwait(false))
            {
                yield return threadEvent;
            }
        }
        finally
        {
            _turnGate.Release();
        }
    }

    private async IAsyncEnumerable<ThreadEvent> RunStreamedInternalAsync(
        NormalizedInput normalizedInput,
        TurnOptions turnOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var resolvedImages = await ResolvedImages
            .CreateAsync(normalizedInput.Images, _options.Logger, cancellationToken)
            .ConfigureAwait(false);

        await using var outputSchemaFile = await OutputSchemaFile
            .CreateAsync(turnOptions.OutputSchema, _options.Logger, cancellationToken)
            .ConfigureAwait(false);

        var execArgs = new CodexExecArgs
        {
            Input = normalizedInput.Prompt,
            BaseUrl = _options.BaseUrl,
            ApiKey = _options.ApiKey,
            ThreadId = _id,
            Images = resolvedImages.Paths,
            Model = _threadOptions.Model,
            SandboxMode = _threadOptions.SandboxMode,
            WorkingDirectory = _threadOptions.WorkingDirectory,
            AdditionalDirectories = _threadOptions.AdditionalDirectories,
            Profile = _threadOptions.Profile,
            UseOss = _threadOptions.UseOss,
            LocalProvider = _threadOptions.LocalProvider,
            FullAuto = _threadOptions.FullAuto,
            DangerouslyBypassApprovalsAndSandbox = _threadOptions.DangerouslyBypassApprovalsAndSandbox,
            Ephemeral = _threadOptions.Ephemeral,
            Color = _threadOptions.Color,
            ProgressCursor = _threadOptions.ProgressCursor,
            OutputLastMessageFile = _threadOptions.OutputLastMessageFile,
            EnabledFeatures = _threadOptions.EnabledFeatures,
            DisabledFeatures = _threadOptions.DisabledFeatures,
            AdditionalCliArguments = _threadOptions.AdditionalCliArguments,
            SkipGitRepoCheck = _threadOptions.SkipGitRepoCheck,
            OutputSchemaFile = outputSchemaFile.SchemaPath,
            ModelReasoningEffort = _threadOptions.ModelReasoningEffort,
            NetworkAccessEnabled = _threadOptions.NetworkAccessEnabled,
            WebSearchMode = _threadOptions.WebSearchMode,
            WebSearchEnabled = _threadOptions.WebSearchEnabled,
            ApprovalPolicy = _threadOptions.ApprovalPolicy,
            CancellationToken = cancellationToken,
        };

        await foreach (var line in _exec.RunAsync(execArgs)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            ThreadEvent parsedEvent;
            try
            {
                parsedEvent = ThreadEventParser.Parse(line);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"Failed to parse item: {line}", exception);
            }

            if (parsedEvent is ThreadStartedEvent startedEvent)
            {
                Volatile.Write(ref _id, startedEvent.ThreadId);
            }

            yield return parsedEvent;
        }
    }

    private static NormalizedInput NormalizeInput(IReadOnlyList<UserInput> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var promptParts = new List<string>();
        var images = new List<LocalImageInput>();

        foreach (var item in input)
        {
            switch (item)
            {
                case TextInput textInput:
                    promptParts.Add(textInput.Text);
                    break;
                case LocalImageInput localImageInput:
                    images.Add(localImageInput);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported input type: {item.GetType().Name}");
            }
        }

        return new NormalizedInput(string.Join("\n\n", promptParts), images);
    }

    private static TurnOptions EnsureTypedRunOptions(TurnOptions? turnOptions)
    {
        var resolvedOptions = turnOptions ?? new TurnOptions();
        if (resolvedOptions.OutputSchema is null)
        {
            throw new InvalidOperationException(TypedRunRequiresOutputSchemaMessage);
        }

        return resolvedOptions;
    }

    private static TurnOptions CreateTypedTurnOptions(
        StructuredOutputSchema outputSchema,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outputSchema);
        return new TurnOptions
        {
            OutputSchema = outputSchema,
            CancellationToken = cancellationToken,
        };
    }

    private async Task<RunResult<TResponse>> RunTypedInternalAsync<TResponse>(
        NormalizedInput normalizedInput,
        TurnOptions turnOptions,
        JsonTypeInfo<TResponse>? jsonTypeInfo)
    {
        var runResult = await RunInternalAsync(normalizedInput, turnOptions).ConfigureAwait(false);
        var typedResponse = DeserializeTypedResponse(runResult.FinalResponse, jsonTypeInfo);

        return new RunResult<TResponse>(
            runResult.Items,
            runResult.FinalResponse,
            runResult.Usage,
            typedResponse);
    }

    private static TResponse DeserializeTypedResponse<TResponse>(
        string payload,
        JsonTypeInfo<TResponse>? jsonTypeInfo)
    {
        try
        {
            var response = jsonTypeInfo is null
                ? JsonSerializer.Deserialize<TResponse>(payload)
                : JsonSerializer.Deserialize(payload, jsonTypeInfo);
            return response
                ?? throw new InvalidOperationException(
                    $"{EmptyTypedRunResponseMessagePrefix} '{typeof(TResponse).Name}'.");
        }
        catch (InvalidOperationException exception)
            when (jsonTypeInfo is null && IsReflectionDisabledSerializationError(exception))
        {
            throw new InvalidOperationException(TypedRunRequiresTypeInfoMessage, exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"{TypedRunDeserializeFailedMessagePrefix} '{typeof(TResponse).Name}'.",
                exception);
        }
    }

    private static bool IsReflectionDisabledSerializationError(InvalidOperationException exception)
    {
        return exception.Message.Contains(ReflectionDisabledErrorToken, StringComparison.Ordinal);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, nameof(CodexThread));
    }

    private readonly record struct NormalizedInput(string Prompt, IReadOnlyList<LocalImageInput> Images);

    private sealed class ResolvedImages : IAsyncDisposable
    {
        private readonly ILogger _logger;
        private readonly IReadOnlyList<string> _paths;
        private readonly IReadOnlyList<string> _temporaryFiles;
        private readonly IReadOnlyList<Stream> _streamsToDispose;

        private ResolvedImages(
            ILogger? logger,
            IReadOnlyList<string> paths,
            IReadOnlyList<string> temporaryFiles,
            IReadOnlyList<Stream> streamsToDispose)
        {
            _logger = logger ?? NullLogger.Instance;
            _paths = paths;
            _temporaryFiles = temporaryFiles;
            _streamsToDispose = streamsToDispose;
        }

        public IReadOnlyList<string> Paths => _paths;

        public static async Task<ResolvedImages> CreateAsync(
            IReadOnlyList<LocalImageInput> images,
            ILogger? logger,
            CancellationToken cancellationToken)
        {
            if (images.Count == 0)
            {
                return new ResolvedImages(logger, [], [], []);
            }

            var resolvedPaths = new List<string>(images.Count);
            var temporaryFiles = new List<string>();
            var streamsToDispose = new List<Stream>();

            foreach (var image in images)
            {
                if (image.Path is not null)
                {
                    resolvedPaths.Add(image.Path);
                    continue;
                }

                if (image.File is not null)
                {
                    resolvedPaths.Add(image.File.FullName);
                    continue;
                }

                if (image.Content is null)
                {
                    throw new InvalidOperationException("Unsupported local image input shape.");
                }

                var tempPath = BuildTempImagePath(image.FileName);
                await using (var tempFile = new FileStream(
                                 tempPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.Read,
                                 bufferSize: 81920,
                                 FileOptions.Asynchronous))
                {
                    await image.Content.CopyToAsync(tempFile, cancellationToken).ConfigureAwait(false);
                }

                resolvedPaths.Add(tempPath);
                temporaryFiles.Add(tempPath);

                if (!image.LeaveOpen)
                {
                    streamsToDispose.Add(image.Content);
                }
            }

            return new ResolvedImages(logger, resolvedPaths, temporaryFiles, streamsToDispose);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var stream in _streamsToDispose)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }

            foreach (var tempFile in _temporaryFiles)
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (IOException exception)
                {
                    CodexThreadLog.TemporaryImageDeleteFailed(_logger, tempFile, exception);
                }
                catch (UnauthorizedAccessException exception)
                {
                    CodexThreadLog.TemporaryImageDeleteFailed(_logger, tempFile, exception);
                }
            }
        }

        private static string BuildTempImagePath(string? fileName)
        {
            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".img";
            }

            return Path.Combine(
                Path.GetTempPath(),
                $"codexsharp-image-{Guid.NewGuid():N}{extension}");
        }
    }
}
