using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Internal;

internal static class CliInstallationProcessRunner
{
    private const string StartFailedMessage = "The CLI package manager could not be started.";
    private const string ExitFailedMessage = "The CLI package manager failed to install the pinned CLI.";
    private const string TimeoutMessage = "CLI installation exceeded its configured process timeout.";
    private const string CleanupMessage = "CLI installation process or output cleanup could not be confirmed within its configured timeout.";
    private const string OutputLimitMessage = "CLI installation exceeded its configured output limit.";
    private const int ReadBufferSize = 4096;

    internal static async IAsyncEnumerable<CliInstallationUpdate> RunAsync(
        CliLaunchCommand command,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory,
        TimeSpan timeout,
        TimeSpan terminationTimeout,
        int maximumOutputCharacters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var progress = Channel.CreateBounded<CliInstallationUpdate>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
        using var stopOwner = new CancellationTokenSource();
        var started = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownerTask = PumpProcessAsync(command, arguments, environment, workingDirectory, timeout,
            terminationTimeout, maximumOutputCharacters, progress.Writer, started, cancellationToken,
            stopOwner.Token);
        var ownerFailureReported = false;
        try
        {
            var startupFailure = await started.Task.ConfigureAwait(false);
            if (startupFailure is not null)
            {
                ownerFailureReported = true;
                ExceptionDispatchInfo.Capture(startupFailure).Throw();
            }

            yield return new CliInstallationUpdate(CliInstallationStage.PackageManagerStarted, 0, 0);
            await foreach (var update in progress.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                yield return update;
            }

            var ownerFailure = await ownerTask.ConfigureAwait(false);
            if (ownerFailure is not null)
            {
                ownerFailureReported = true;
                ExceptionDispatchInfo.Capture(ownerFailure).Throw();
            }
        }
        finally
        {
            stopOwner.Cancel();
            var ownerFailure = await ownerTask.ConfigureAwait(false);
            if (!ownerFailureReported && ownerFailure is not null)
            {
                ExceptionDispatchInfo.Capture(ownerFailure).Throw();
            }
        }
    }

    private static async Task<Exception?> PumpProcessAsync(
        CliLaunchCommand command,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory,
        TimeSpan timeout,
        TimeSpan terminationTimeout,
        int maximumOutputCharacters,
        ChannelWriter<CliInstallationUpdate> progress,
        TaskCompletionSource<Exception?> started,
        CancellationToken cancellationToken,
        CancellationToken stopToken)
    {
        using var ownerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopToken);
        var processUpdates = RunProcessCoreAsync(command, arguments, environment, workingDirectory,
            timeout, terminationTimeout, maximumOutputCharacters, ownerCancellation.Token).GetAsyncEnumerator();
        var processStarted = false;
        Exception? failure = null;
        try
        {
            if (!await processUpdates.MoveNextAsync().ConfigureAwait(false))
            {
                throw new InvalidOperationException(StartFailedMessage);
            }

            processStarted = processUpdates.Current.Stage == CliInstallationStage.PackageManagerStarted;
            if (!processStarted)
            {
                throw new InvalidOperationException(StartFailedMessage);
            }

            started.TrySetResult(null);
            while (await processUpdates.MoveNextAsync().ConfigureAwait(false))
            {
                progress.TryWrite(processUpdates.Current);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            if (!processStarted)
            {
                started.TrySetResult(exception);
            }
        }
        finally
        {
            try
            {
                await processUpdates.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupFailure)
            {
                failure = cleanupFailure;
                if (!processStarted)
                {
                    started.TrySetResult(cleanupFailure);
                }
            }

            progress.TryComplete();
        }

        if (stopToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested &&
            failure is OperationCanceledException canceled && canceled.CancellationToken == ownerCancellation.Token)
        {
            failure = null;
        }

        return failure;
    }

    private static async IAsyncEnumerable<CliInstallationUpdate> RunProcessCoreAsync(
        CliLaunchCommand command,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory,
        TimeSpan timeout,
        TimeSpan terminationTimeout,
        int maximumOutputCharacters,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = CreateStartInfo(command, arguments, environment, workingDirectory);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(StartFailedMessage);
            }
        }
        catch (Exception)
        {
            throw new InvalidOperationException(StartFailedMessage);
        }

        using var readerCancellation = new CancellationTokenSource();
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        var progress = Channel.CreateBounded<OutputProgress>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        var counts = new OutputCounts();
        var exitTask = process.WaitForExitAsync(CancellationToken.None);
        var callerCancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        var timeoutSignal = Task.Delay(Timeout.InfiniteTimeSpan, timeoutCancellation.Token);
        var stdoutTask = Task.CompletedTask;
        var stderrTask = Task.CompletedTask;
        var lastObservedOutput = new OutputProgress(0, 0);
        try
        {
            process.StandardInput.Close();
            stdoutTask = DrainAsync(process.StandardOutput, maximumOutputCharacters, counts, true,
                progress.Writer, readerCancellation.Token);
            stderrTask = DrainAsync(process.StandardError, maximumOutputCharacters, counts, false,
                progress.Writer, readerCancellation.Token);
            yield return new CliInstallationUpdate(CliInstallationStage.PackageManagerStarted, 0, 0);
            ThrowIfStopped(timeoutCancellation, cancellationToken);
            var progressReady = progress.Reader.WaitToReadAsync(CancellationToken.None).AsTask();
            var stdoutCompleted = false;
            var stderrCompleted = false;
            while (!exitTask.IsCompleted || !stdoutTask.IsCompleted || !stderrTask.IsCompleted)
            {
                ThrowIfStopped(timeoutCancellation, cancellationToken);
                var pending = new List<Task> { progressReady, callerCancellation, timeoutSignal };
                if (!exitTask.IsCompleted)
                {
                    pending.Add(exitTask);
                }

                if (!stdoutCompleted)
                {
                    pending.Add(stdoutTask);
                }

                if (!stderrCompleted)
                {
                    pending.Add(stderrTask);
                }

                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
                ThrowIfStopped(timeoutCancellation, cancellationToken);
                if (completed == exitTask)
                {
                    await exitTask.ConfigureAwait(false);
                }

                if (completed == callerCancellation)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (completed == timeoutSignal)
                {
                    throw new TimeoutException(TimeoutMessage);
                }

                if (completed == stdoutTask)
                {
                    await stdoutTask.ConfigureAwait(false);
                    stdoutCompleted = true;
                }

                if (completed == stderrTask)
                {
                    await stderrTask.ConfigureAwait(false);
                    stderrCompleted = true;
                }

                while (progress.Reader.TryRead(out var output))
                {
                    yield return new CliInstallationUpdate(CliInstallationStage.OutputObserved,
                        output.StandardOutputCharacters, output.StandardErrorCharacters);
                    lastObservedOutput = output;
                    ThrowIfStopped(timeoutCancellation, cancellationToken);
                }

                progressReady = progress.Reader.WaitToReadAsync(CancellationToken.None).AsTask();
            }

            await exitTask.ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            var finalOutput = counts.Snapshot();
            if (finalOutput != lastObservedOutput &&
                (finalOutput.StandardOutputCharacters > 0 || finalOutput.StandardErrorCharacters > 0))
            {
                yield return new CliInstallationUpdate(CliInstallationStage.OutputObserved,
                    finalOutput.StandardOutputCharacters, finalOutput.StandardErrorCharacters);
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(ExitFailedMessage);
            }
        }
        finally
        {
            try
            {
                await StopAndJoinAsync(process, exitTask, stdoutTask, stderrTask,
                    readerCancellation, terminationTimeout).ConfigureAwait(false);
            }
            finally
            {
                progress.Writer.TryComplete();
            }
        }
    }

    private static void ThrowIfStopped(CancellationTokenSource timeoutCancellation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (timeoutCancellation.IsCancellationRequested)
        {
            throw new TimeoutException(TimeoutMessage);
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        CliLaunchCommand command,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(command.ExecutablePath)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment.Clear();
        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        foreach (var argument in command.PrefixArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task DrainAsync(
        TextReader reader,
        int maximumCharacters,
        OutputCounts counts,
        bool standardOutput,
        ChannelWriter<OutputProgress> progress,
        CancellationToken cancellationToken)
    {
        var buffer = new char[ReadBufferSize];
        var streamCount = 0;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            streamCount = checked(streamCount + read);
            if (streamCount > maximumCharacters)
            {
                throw new OutputLimitException(OutputLimitMessage);
            }

            if (standardOutput)
            {
                counts.StandardOutputCharacters = streamCount;
            }
            else
            {
                counts.StandardErrorCharacters = streamCount;
            }

            progress.TryWrite(counts.Snapshot());
        }
    }

    private static async Task StopAndJoinAsync(
        Process process,
        Task exitTask,
        Task stdoutTask,
        Task stderrTask,
        CancellationTokenSource readerCancellation,
        TimeSpan terminationTimeout)
    {
        Exception? failure = null;
        try
        {
            if (!exitTask.IsCompleted)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception)
        {
            if (!process.HasExited)
            {
                failure = exception;
            }
        }

        try
        {
            await exitTask.WaitAsync(terminationTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(terminationTimeout, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!IsOnlyOutputLimitFailure(stdoutTask, stderrTask))
            {
                failure ??= exception;
            }
        }

        readerCancellation.Cancel();
        DisposeReader(process.StandardOutput, ref failure);
        DisposeReader(process.StandardError, ref failure);
        failure = await JoinReadersAsync(stdoutTask, stderrTask, terminationTimeout, failure).ConfigureAwait(false);
        ThrowCleanupFailure(failure);
    }

    private static void DisposeReader(TextReader reader, ref Exception? failure)
    {
        try
        {
            reader.Dispose();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }
    }

    private static async Task<Exception?> JoinReadersAsync(
        Task stdoutTask,
        Task stderrTask,
        TimeSpan terminationTimeout,
        Exception? failure)
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(terminationTimeout, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (!IsOnlyOutputLimitFailure(stdoutTask, stderrTask))
            {
                failure ??= exception;
            }
        }

        return failure;
    }

    private static void ThrowCleanupFailure(Exception? failure)
    {
        if (failure is not null)
        {
            throw new InvalidOperationException(CleanupMessage, failure);
        }
    }

    private static bool IsOnlyOutputLimitFailure(Task stdoutTask, Task stderrTask)
    {
        var found = false;
        foreach (var task in new[] { stdoutTask, stderrTask })
        {
            if (task.IsCanceled)
            {
                return false;
            }

            if (task.Exception is not { } aggregate)
            {
                continue;
            }

            foreach (var exception in aggregate.Flatten().InnerExceptions)
            {
                if (exception is not OutputLimitException)
                {
                    return false;
                }

                found = true;
            }
        }

        return found;
    }

    private sealed class OutputCounts
    {
        private int _standardOutputCharacters;
        private int _standardErrorCharacters;

        internal int StandardOutputCharacters
        {
            get => Volatile.Read(ref _standardOutputCharacters);
            set => Volatile.Write(ref _standardOutputCharacters, value);
        }

        internal int StandardErrorCharacters
        {
            get => Volatile.Read(ref _standardErrorCharacters);
            set => Volatile.Write(ref _standardErrorCharacters, value);
        }

        internal OutputProgress Snapshot() => new(StandardOutputCharacters, StandardErrorCharacters);
    }

    private sealed record OutputProgress(int StandardOutputCharacters, int StandardErrorCharacters);

    private sealed class OutputLimitException(string message) : InvalidOperationException(message);
}
