using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Execution;
using ManagedCode.CodexSharpSDK.Internal;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Client;

public sealed class CodexClient : IDisposable
{
    private readonly CodexOptions _options;
    private readonly bool _autoStart;
    private readonly ConnectionState _connectionState;

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
        _connectionState = new ConnectionState(exec);
    }

    public CodexClientState State => _connectionState.GetSnapshot();

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connectionState.Start(CreateExec);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connectionState.Stop();
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

    public CodexCliMetadata GetCliMetadata()
    {
        var executablePath = CodexCliLocator.FindCodexPath(_options.CodexExecutablePath);
        return CodexCliMetadataReader.Read(executablePath);
    }

    public CodexCliUpdateStatus GetCliUpdateStatus()
    {
        var executablePath = CodexCliLocator.FindCodexPath(_options.CodexExecutablePath);
        return CodexCliMetadataReader.ReadUpdateStatus(executablePath);
    }

    public void Dispose() => _connectionState.Dispose();

    private CodexExec GetOrCreateExec() => _connectionState.GetOrCreate(_autoStart, CreateExec);

    private CodexExec CreateExec()
    {
        return new CodexExec(
            _options.CodexExecutablePath,
            _options.EnvironmentVariables,
            _options.Config,
            _options.Logger);
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

    private sealed class ConnectionState
    {
        private readonly Lock _gate = new();
        private CodexExec? _exec;
        private bool _disposed;

        internal ConnectionState(CodexExec? exec)
        {
            _exec = exec;
        }

        internal CodexClientState GetSnapshot()
        {
            lock (_gate)
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

        internal void Start(Func<CodexExec> execFactory)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _exec ??= execFactory();
            }
        }

        internal void Stop()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                _exec = null;
            }
        }

        internal CodexExec GetOrCreate(bool autoStart, Func<CodexExec> execFactory)
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                if (_exec is not null)
                {
                    return _exec;
                }

                if (!autoStart)
                {
                    throw new InvalidOperationException($"Client not connected. Call {nameof(StartAsync)} first.");
                }

                _exec = execFactory();
                return _exec;
            }
        }

        internal void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _exec = null;
                _disposed = true;
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, nameof(CodexClient));
    }
}
