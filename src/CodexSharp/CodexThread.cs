using System.Runtime.CompilerServices;
using ManagedCode.CodexSharp.Exceptions;
using ManagedCode.CodexSharp.Internal;

namespace ManagedCode.CodexSharp;

public sealed class CodexThread
    : IDisposable
{
    private readonly CodexExec _exec;
    private readonly CodexOptions _options;
    private readonly ThreadOptions _threadOptions;
    private readonly SemaphoreSlim _turnLock = new(1, 1);
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
        return Task.FromResult(new RunStreamedResult(RunStreamedSerializedAsync(normalizedInput, resolvedTurnOptions)));
    }

    public Task<RunStreamedResult> RunStreamedAsync(IReadOnlyList<UserInput> input, TurnOptions? turnOptions = null)
    {
        ThrowIfDisposed();
        var normalizedInput = NormalizeInput(input);
        var resolvedTurnOptions = turnOptions ?? new TurnOptions();
        return Task.FromResult(new RunStreamedResult(RunStreamedSerializedAsync(normalizedInput, resolvedTurnOptions)));
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _turnLock.Dispose();
    }

    private async Task<RunResult> RunInternalAsync(NormalizedInput normalizedInput, TurnOptions turnOptions)
    {
        var items = new List<ThreadItem>();
        var finalResponse = string.Empty;
        Usage? usage = null;
        ThreadError? turnFailure = null;

        await foreach (var threadEvent in RunStreamedSerializedAsync(normalizedInput, turnOptions)
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

    private async IAsyncEnumerable<ThreadEvent> RunStreamedSerializedAsync(
        NormalizedInput normalizedInput,
        TurnOptions turnOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            turnOptions.CancellationToken,
            cancellationToken);

        var linkedCancellationToken = linkedCancellationTokenSource.Token;

        await _turnLock.WaitAsync(linkedCancellationToken).ConfigureAwait(false);
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
            _turnLock.Release();
        }
    }

    private async IAsyncEnumerable<ThreadEvent> RunStreamedInternalAsync(
        NormalizedInput normalizedInput,
        TurnOptions turnOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var outputSchemaFile = await OutputSchemaFile
            .CreateAsync(turnOptions.OutputSchema, cancellationToken)
            .ConfigureAwait(false);

        var execArgs = new CodexExecArgs
        {
            Input = normalizedInput.Prompt,
            BaseUrl = _options.BaseUrl,
            ApiKey = _options.ApiKey,
            ThreadId = _id,
            Images = normalizedInput.Images,
            Model = _threadOptions.Model,
            SandboxMode = _threadOptions.SandboxMode,
            WorkingDirectory = _threadOptions.WorkingDirectory,
            AdditionalDirectories = _threadOptions.AdditionalDirectories,
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
                Interlocked.Exchange(ref _id, startedEvent.ThreadId);
            }

            yield return parsedEvent;
        }
    }

    private static NormalizedInput NormalizeInput(IReadOnlyList<UserInput> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var promptParts = new List<string>();
        var images = new List<string>();

        foreach (var item in input)
        {
            switch (item)
            {
                case TextInput textInput:
                    promptParts.Add(textInput.Text);
                    break;
                case LocalImageInput localImageInput:
                    images.Add(localImageInput.Path);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported input type: {item.GetType().Name}");
            }
        }

        return new NormalizedInput(string.Join("\n\n", promptParts), images);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, nameof(CodexThread));
    }

    private readonly record struct NormalizedInput(string Prompt, IReadOnlyList<string> Images);
}
