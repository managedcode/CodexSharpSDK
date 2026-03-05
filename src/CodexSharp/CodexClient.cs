namespace ManagedCode.CodexSharp;

public sealed class CodexClient : IDisposable, IAsyncDisposable
{
    private readonly CodexOptions _options;
    private readonly bool _autoStart;
    private readonly object _stateLock = new();
    private CodexExec? _exec;
    private bool _disposed;

    public CodexClient(CodexClientOptions? options = null)
        : this(options, null)
    {
    }

    public CodexClient(CodexOptions options)
        : this(CreateClientOptions(options), null)
    {
    }

    internal CodexClient(CodexClientOptions? options, CodexExec? exec)
    {
        var resolvedOptions = options ?? new CodexClientOptions();
        _options = resolvedOptions.CodexOptions ?? new CodexOptions();
        _autoStart = resolvedOptions.AutoStart;
        _exec = exec;
    }

    public CodexClientState State
    {
        get
        {
            lock (_stateLock)
            {
                if (_disposed)
                {
                    return CodexClientState.Disposed;
                }

                return _exec is null
                    ? CodexClientState.Disconnected
                    : CodexClientState.Connected;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            ThrowIfDisposed();
            _exec ??= CreateExec();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            ThrowIfDisposed();
            _exec = null;
        }

        return Task.CompletedTask;
    }

    public CodexThread StartThread(ThreadOptions? options = null)
    {
        var exec = GetOrCreateExec();
        return new CodexThread(exec, _options, options ?? new ThreadOptions());
    }

    public CodexThread ResumeThread(string id, ThreadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var exec = GetOrCreateExec();
        return new CodexThread(exec, _options, options ?? new ThreadOptions(), id);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _exec = null;
            _disposed = true;
        }

        return ValueTask.CompletedTask;
    }

    private CodexExec GetOrCreateExec()
    {
        lock (_stateLock)
        {
            ThrowIfDisposed();

            if (_exec is not null)
            {
                return _exec;
            }

            if (!_autoStart)
            {
                throw new InvalidOperationException($"Client not connected. Call {nameof(StartAsync)} first.");
            }

            _exec = CreateExec();
            return _exec;
        }
    }

    private CodexExec CreateExec()
    {
        return new CodexExec(
            _options.CodexPathOverride,
            _options.EnvironmentVariables,
            _options.Config);
    }

    private static CodexClientOptions CreateClientOptions(CodexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new CodexClientOptions
        {
            CodexOptions = options,
            AutoStart = true,
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(CodexClient));
    }
}
