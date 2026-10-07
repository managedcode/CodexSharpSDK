using System.Diagnostics;
using System.Runtime.CompilerServices;
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
        try
        {
            process.StandardInput.Close();
            stdoutTask = DrainAsync(process.StandardOutput, maximumOutputCharacters, counts, true,
                progress.Writer, readerCancellation.Token);
            stderrTask = DrainAsync(process.StandardError, maximumOutputCharacters, counts, false,
                progress.Writer, readerCancellation.Token);
            yield return new CliInstallationUpdate(CliInstallationStage.PackageManagerStarted, 0, 0);
            var progressReady = progress.Reader.WaitToReadAsync(CancellationToken.None).AsTask();
            var stdoutCompleted = false;
            var stderrCompleted = false;
            while (!exitTask.IsCompleted || !stdoutTask.IsCompleted || !stderrTask.IsCompleted)
            {
                var pending = new List<Task> { exitTask, progressReady, callerCancellation, timeoutSignal };
                if (!stdoutCompleted)
                {
                    pending.Add(stdoutTask);
                }

                if (!stderrCompleted)
                {
                    pending.Add(stderrTask);
                }

                var completed = await Task.WhenAny(pending).ConfigureAwait(false);
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
                }

                progressReady = progress.Reader.WaitToReadAsync(CancellationToken.None).AsTask();
            }

            await exitTask.ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(ExitFailedMessage);
            }
        }
        finally
        {
            await StopAndJoinAsync(process, exitTask, stdoutTask, stderrTask,
                readerCancellation, terminationTimeout).ConfigureAwait(false);
            progress.Writer.TryComplete();
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

        readerCancellation.Cancel();
        try
        {
            process.StandardOutput.Dispose();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        try
        {
            process.StandardError.Dispose();
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
            if (IsOnlyOutputLimitFailure(stdoutTask, stderrTask))
            {
                ThrowCleanupFailure(failure);
                return;
            }

            failure ??= exception;
        }

        ThrowCleanupFailure(failure);
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
