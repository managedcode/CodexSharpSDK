using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Client;

/// <summary>Owns one native CLI sign-in and its process until completion or disposal.</summary>
public sealed partial class CliLoginSession : IAsyncDisposable
{
    private const string LoginFailedMessage = "The CLI exited without completing sign-in. Exit code: ";
    private const string OutputLimitMessage = "The CLI sign-in exceeded the configured process output limit.";
    private const string CleanupFailedMessage = "Could not confirm CLI sign-in process and reader cleanup within the configured termination timeout.";
    private const string CodeNotSupportedMessage = "This sign-in does not accept an authorization code through the SDK.";
    private const string CodePendingMessage = "An authorization code is already awaiting a response.";
    private const string InvalidCodeMessage = "Paste only the authorization code, as one line without control characters or JSON messages.";
    private const string InvalidUrlMessage = "The CLI returned an invalid sign-in URL.";
    private const string HttpsScheme = "https";
    private const string AnsiPattern = @"\x1B(?:\][^\x07\x1B]*(?:\x07|\x1B\\)|\[[0-?]*[ -/]*[@-~])";
    private const int BufferCharacters = 4096;
    private readonly Process _process;
    private readonly CancellationTokenSource _lifetime;
    private readonly TimeSpan _terminationTimeout;
    private readonly int _maximumOutputCharacters;
    private readonly Channel<(bool StandardError, string Text)> _output = Channel.CreateUnbounded<(bool, string)>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly TaskCompletionSource<CliLoginInstructions?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StringBuilder[] _pendingText = [new(), new()];
    private readonly Task _completion;
    private TaskCompletionSource<CliLoginInstructions?>? _pendingCode;
    private CliLoginInstructions? _instructions;
    private bool _authenticated;
    private int _readersRemaining = 2;
    private int _shutdownRequested;
    private int _disposed;

    private CliLoginSession(ProcessStartInfo startInfo, TimeSpan terminationTimeout, int maximumOutputCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(terminationTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOutputCharacters);
        cancellationToken.ThrowIfCancellationRequested();
        _terminationTimeout = terminationTimeout;
        _maximumOutputCharacters = maximumOutputCharacters;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _process = new Process { StartInfo = startInfo };
        try
        {
            if (!_process.Start())
            {
                throw new InvalidOperationException(LoginFailedMessage);
            }
        }
        catch
        {
            _process.Dispose();
            _lifetime.Dispose();
            throw;
        }
        _completion = RunAsync();
    }

    internal int ProcessId => _process.Id;

    /// <summary>Gets the current instructions, or null when native cached credentials completed sign-in.</summary>
    public CliLoginInstructions? Instructions => Volatile.Read(ref _instructions);

    /// <summary>Waits for native success. Cancelling this wait does not cancel the sign-in.</summary>
    public Task WaitForCompletionAsync(CancellationToken cancellationToken = default) => _completion.WaitAsync(cancellationToken);

    /// <summary>Submits a code and waits for success (null) or fresh instructions after a native retry.</summary>
    public async Task<CliLoginInstructions?> SubmitAuthorizationCodeAsync(string authorizationCode,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);
        var code = authorizationCode.Trim();
        if (code.Any(char.IsControl) || code.Any(char.IsWhiteSpace) || code[0] is '{' or '[')
        {
            throw new ArgumentException(InvalidCodeMessage, nameof(authorizationCode));
        }
        if (Instructions?.RequiresAuthorizationCode != true)
        {
            throw new NotSupportedException(CodeNotSupportedMessage);
        }
        if (_completion.IsCompleted)
        {
            await _completion.ConfigureAwait(false);
            return null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var response = new TaskCompletionSource<CliLoginInstructions?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _pendingCode, response, null) is not null)
        {
            throw new InvalidOperationException(CodePendingMessage);
        }
        try
        {
            // Once submitted, cancellation affects observation only; the native CLI owns the outcome.
            await _process.StandardInput.WriteLineAsync(code.AsMemory(), _lifetime.Token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(_lifetime.Token).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.CompareExchange(ref _pendingCode, null, response);
            throw;
        }
        return await response.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Cancels the owned process and confirms cleanup. Read the sign-in outcome with WaitForCompletionAsync.</summary>
    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        RequestShutdown();
        await _completion.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        cancellationToken.ThrowIfCancellationRequested();
        await _stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Cancels an unfinished sign-in and releases resources; authentication failures remain on the completion task.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            await _stopped.Task.ConfigureAwait(false);
            return;
        }
        try
        {
            await CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            _process.Dispose();
            _lifetime.Dispose();
        }
    }

    private static async Task<CliLoginSession> StartAsync(ProcessStartInfo startInfo, TimeSpan terminationTimeout,
        int maximumOutputCharacters, CancellationToken cancellationToken)
    {
        var session = new CliLoginSession(startInfo, terminationTimeout, maximumOutputCharacters, cancellationToken);
        try
        {
            await session._ready.Task.ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(CliLaunchCommand launchCommand,
        IReadOnlyDictionary<string, string> environment, string[] arguments)
    {
        var info = new ProcessStartInfo(launchCommand.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in launchCommand.PrefixArguments)
        {
            info.ArgumentList.Add(argument);
        }
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }
        info.Environment.Clear();
        foreach (var (key, value) in environment)
        {
            info.Environment[key] = value;
        }
        return info;
    }

    private async Task RunAsync()
    {
        var stdout = PumpAsync(_process.StandardOutput, false);
        var stderr = PumpAsync(_process.StandardError, true);
        Exception? failure = null;
        try
        {
            await InitializeNativeAsync().ConfigureAwait(false);
            await foreach (var chunk in _output.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                await ProcessTextAsync(chunk.StandardError, chunk.Text).ConfigureAwait(false);
            }
            await _process.WaitForExitAsync(_lifetime.Token).ConfigureAwait(false);
            ValidateNativeExit();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        Exception? cleanupFailure = null;
        try
        {
            RequestShutdown();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
            await Task.WhenAll(_process.WaitForExitAsync(), ObserveReadersAsync(stdout, stderr)).WaitAsync(_terminationTimeout).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            cleanupFailure = new InvalidOperationException(CleanupFailedMessage, exception);
            failure = failure is null ? cleanupFailure : new AggregateException(failure, cleanupFailure);
        }
        if (cleanupFailure is null)
        {
            _stopped.TrySetResult();
        }
        else
        {
            _stopped.TrySetException(cleanupFailure);
        }
        if (failure is OperationCanceledException && _lifetime.IsCancellationRequested)
        {
            _ready.TrySetCanceled(_lifetime.Token);
            Interlocked.Exchange(ref _pendingCode, null)?.TrySetCanceled(_lifetime.Token);
        }
        else if (failure is not null)
        {
            _ready.TrySetException(failure);
            Interlocked.Exchange(ref _pendingCode, null)?.TrySetException(failure);
        }
        else
        {
            _ready.TrySetResult(null);
            Interlocked.Exchange(ref _pendingCode, null)?.TrySetResult(null);
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static async Task ObserveReadersAsync(Task stdout, Task stderr)
    {
        // The read failure is reported by the output channel; cleanup checks that both readers have stopped.
        await Task.WhenAll(stdout, stderr).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private async Task PumpAsync(StreamReader reader, bool standardError)
    {
        var buffer = new char[BufferCharacters];
        var total = 0;
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), _lifetime.Token).ConfigureAwait(false)) != 0)
            {
                if (read > _maximumOutputCharacters - total)
                {
                    throw new InvalidOperationException(OutputLimitMessage);
                }
                total += read;
                await _output.Writer.WriteAsync((standardError, new string(buffer, 0, read)), _lifetime.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Shutdown owns cancellation; no reader task may outlive process cleanup.
        }
        catch (Exception exception)
        {
            _output.Writer.TryComplete(exception);
            throw;
        }
        finally
        {
            if (Interlocked.Decrement(ref _readersRemaining) == 0)
            {
                _output.Writer.TryComplete();
            }
        }
    }

    private async Task ProcessTextAsync(bool standardError, string text)
    {
        var pending = _pendingText[standardError ? 1 : 0];
        foreach (var character in text)
        {
            if (character == '\n')
            {
                var line = StripAnsi().Replace(pending.ToString(), string.Empty).Trim();
                pending.Clear();
                await ProcessNativeLineAsync(standardError, line).ConfigureAwait(false);
            }
            else
            {
                pending.Append(character);
            }
        }
    }

    private void PublishInstructions(Uri url, string? userCode, bool requiresAuthorizationCode)
    {
        if (!url.IsAbsoluteUri || url.Scheme != HttpsScheme)
        {
            throw new InvalidDataException(InvalidUrlMessage);
        }
        var instructions = new CliLoginInstructions(url, userCode, requiresAuthorizationCode);
        Volatile.Write(ref _instructions, instructions);
        _ready.TrySetResult(instructions);
        Interlocked.Exchange(ref _pendingCode, null)?.TrySetResult(instructions);
    }

    private void RequestShutdown()
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) == 0)
        {
            _lifetime.Cancel();
        }
    }

    [GeneratedRegex(AnsiPattern, RegexOptions.CultureInvariant)]
    private static partial Regex StripAnsi();

}
