using System.Diagnostics;
using System.Text;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Internal;

internal static class BoundedCliProcessProbe
{
    private const string ProbeFailedMessage = "CLI metadata process could not be started.";
    private const string ProbeTimedOutMessage = "CLI metadata process did not exit within the configured time limit.";
    private const string ProbeOutputTimedOutMessage = "CLI metadata process output did not close within the configured time limit.";
    private const string ProbeOutputCleanupFailedMessage = "CLI metadata process output readers did not stop within the configured time limit.";
    private const string ProbeBlockedByIncompleteCleanupMessage = "CLI metadata process probe is blocked by an earlier incomplete cleanup.";
    private const string ProbeBusyMessage = "CLI metadata process probe could not acquire its bounded process lease.";
    private const string ProbeKillFailedMessage = "CLI metadata process could not be stopped after the configured time limit.";
    private const string ProbeOutputLimitMessage = "CLI metadata process exceeded the configured output limit.";
    private const string InvalidTimeoutMessage = "CLI metadata timeout must be positive.";
    private const string InvalidOutputLimitMessage = "CLI metadata output limit must be positive.";
    private const int ReadBufferSize = 4096;
    private static readonly object ProbeGateLock = new();
    private static bool ProbeLeaseHeld;
    private static int PendingReaderCleanups;
    internal static int PendingReaderCleanupCount
    {
        get
        {
            lock (ProbeGateLock)
            {
                return PendingReaderCleanups;
            }
        }
    }

    internal static Task RetainProbeLeaseForTesting(Task drains)
    {
        ArgumentNullException.ThrowIfNull(drains);
        var probeGateLease = ProbeGateLease.Acquire(1);
        probeGateLease.ReleaseWhenDrained(drains);
        return probeGateLease.ReleaseCompletion;
    }

    public static CliProcessProbeResult Run(
        string executablePath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        bool inheritEnvironmentVariables,
        TimeSpan timeout,
        int maximumOutputCharacters,
        Action<int>? readerCountChanged = null,
        TimeSpan? leaseAcquisitionTimeout = null)
        => Run(new CliLaunchCommand(Path.GetFullPath(executablePath), System.Collections.Immutable.ImmutableArray<string>.Empty), arguments, environment,
            inheritEnvironmentVariables, timeout, maximumOutputCharacters, readerCountChanged, leaseAcquisitionTimeout);

    public static CliProcessProbeResult Run(
        CliLaunchCommand launchCommand,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        bool inheritEnvironmentVariables,
        TimeSpan timeout,
        int maximumOutputCharacters,
        Action<int>? readerCountChanged = null,
        TimeSpan? leaseAcquisitionTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(launchCommand);
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), InvalidTimeoutMessage);
        }

        if (maximumOutputCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumOutputCharacters), InvalidOutputLimitMessage);
        }

        var timeoutMilliseconds = GetTimeoutMilliseconds(timeout);
        var leaseTimeoutMilliseconds = GetTimeoutMilliseconds(leaseAcquisitionTimeout ?? timeout);
        using var probeGateLease = ProbeGateLease.Acquire(leaseTimeoutMilliseconds);
        var startInfo = new ProcessStartInfo(launchCommand.ExecutablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (!inheritEnvironmentVariables)
        {
            startInfo.Environment.Clear();
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        foreach (var argument in launchCommand.PrefixArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(ProbeFailedMessage);
            }
        }
        catch (Exception)
        {
            throw new InvalidOperationException(ProbeFailedMessage);
        }

        process.StandardInput.Close();

        using var drainCancellation = new CancellationTokenSource();
        var standardOutputTask = ReadBoundedAndDrainAsync(process.StandardOutput, maximumOutputCharacters,
            readerCountChanged, drainCancellation.Token);
        var standardErrorTask = ReadBoundedAndDrainAsync(process.StandardError, maximumOutputCharacters,
            readerCountChanged, drainCancellation.Token);
        if (!process.WaitForExit(timeoutMilliseconds))
        {
            var readersStopped = true;
            try
            {
                StopAndConfirm(process, timeoutMilliseconds);
            }
            finally
            {
                readersStopped = CancelAndCloseReaders(process, drainCancellation, standardOutputTask,
                    standardErrorTask, timeoutMilliseconds, probeGateLease);
            }

            if (!readersStopped)
            {
                throw new InvalidOperationException(ProbeOutputCleanupFailedMessage);
            }

            throw new TimeoutException(ProbeTimedOutMessage);
        }

        var drains = Task.WhenAll(standardOutputTask, standardErrorTask);
        try
        {
            if (!drains.Wait(timeoutMilliseconds))
            {
                var readersStopped = true;
                try
                {
                    StopAndConfirm(process, timeoutMilliseconds);
                }
                finally
                {
                    readersStopped = CancelAndCloseReaders(process, drainCancellation, standardOutputTask,
                        standardErrorTask, timeoutMilliseconds, probeGateLease);
                }

                if (!readersStopped)
                {
                    throw new InvalidOperationException(ProbeOutputCleanupFailedMessage);
                }

                throw new TimeoutException(ProbeOutputTimedOutMessage);
            }
        }
        catch (AggregateException)
        {
            throw new InvalidOperationException(ProbeFailedMessage);
        }

        var standardOutput = standardOutputTask.Result;
        var standardError = standardErrorTask.Result;
        if (standardOutput.Truncated || standardError.Truncated)
        {
            throw new InvalidOperationException(ProbeOutputLimitMessage);
        }

        return new CliProcessProbeResult(process.ExitCode, standardOutput.Text, standardError.Text);
    }

    private static async Task<BoundedText> ReadBoundedAndDrainAsync(
        TextReader reader,
        int maximumCharacters,
        Action<int>? readerCountChanged,
        CancellationToken cancellationToken)
    {
        readerCountChanged?.Invoke(1);
        try
        {
            return await ReadCoreAsync(reader, maximumCharacters, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            readerCountChanged?.Invoke(-1);
        }
    }

    private static async Task<BoundedText> ReadCoreAsync(
        TextReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, ReadBufferSize));
        var buffer = new char[ReadBufferSize];
        var truncated = false;
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            {
                var remaining = maximumCharacters - builder.Length;
                var append = Math.Min(remaining, read);
                if (append > 0)
                {
                    builder.Append(buffer, 0, append);
                }

                truncated |= append < read;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new BoundedText(builder.ToString(), truncated);
        }

        return new BoundedText(builder.ToString(), truncated);
    }

    private static bool CancelAndCloseReaders(
        Process process,
        CancellationTokenSource drainCancellation,
        Task<BoundedText> standardOutputTask,
        Task<BoundedText> standardErrorTask,
        int timeoutMilliseconds,
        ProbeGateLease probeGateLease)
    {
        drainCancellation.Cancel();
        var closeFailed = false;
        try
        {
            process.StandardOutput.Dispose();
        }
        catch (Exception)
        {
            closeFailed = true;
        }

        try
        {
            process.StandardError.Dispose();
        }
        catch (Exception)
        {
            closeFailed = true;
        }

        var drains = Task.WhenAll(standardOutputTask, standardErrorTask);
        try
        {
            if (!drains.Wait(timeoutMilliseconds))
            {
                ObserveLaterFailure(drains);
                probeGateLease.ReleaseWhenDrained(drains);
                return false;
            }
        }
        catch (AggregateException)
        {
            ObserveLaterFailure(drains);
        }

        if (closeFailed)
        {
            ObserveLaterFailure(drains);
        }

        return true;
    }

    private static void ObserveLaterFailure(Task drains)
    {
        _ = drains.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void ReleaseProbeLease(bool retainedCleanup)
    {
        lock (ProbeGateLock)
        {
            if (retainedCleanup)
            {
                PendingReaderCleanups--;
            }

            ProbeLeaseHeld = false;
            Monitor.PulseAll(ProbeGateLock);
        }
    }

    private static void StopAndConfirm(Process process, int timeoutMilliseconds)
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
            if (!process.HasExited)
            {
                throw new InvalidOperationException(ProbeKillFailedMessage);
            }
        }

        if (!process.WaitForExit(timeoutMilliseconds))
        {
            throw new InvalidOperationException(ProbeKillFailedMessage);
        }
    }

    private static int GetTimeoutMilliseconds(TimeSpan timeout) =>
        (int)Math.Clamp(Math.Ceiling(timeout.TotalMilliseconds), 1, int.MaxValue);

    private sealed class ProbeGateLease : IDisposable
    {
        private bool _retainedForCleanup;
        private readonly TaskCompletionSource _releaseCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReleaseCompletion => _releaseCompletion.Task;

        public static ProbeGateLease Acquire(int leaseTimeoutMilliseconds)
        {
            var stopwatch = Stopwatch.StartNew();
            var waitMilliseconds = leaseTimeoutMilliseconds;
            lock (ProbeGateLock)
            {
                while (ProbeLeaseHeld)
                {
                    if (PendingReaderCleanups > 0)
                    {
                        throw new InvalidOperationException(ProbeBlockedByIncompleteCleanupMessage);
                    }

                    var remainingMilliseconds = waitMilliseconds - stopwatch.ElapsedMilliseconds;
                    if (remainingMilliseconds <= 0)
                    {
                        throw new InvalidOperationException(ProbeBusyMessage);
                    }

                    Monitor.Wait(ProbeGateLock, (int)Math.Min(remainingMilliseconds, int.MaxValue));
                }

                ProbeLeaseHeld = true;
                return new ProbeGateLease();
            }
        }

        public void ReleaseWhenDrained(Task drains)
        {
            lock (ProbeGateLock)
            {
                PendingReaderCleanups++;
                _retainedForCleanup = true;
            }

            _ = drains.ContinueWith(
                task =>
                {
                    _ = task.Exception;
                    ReleaseProbeLease(retainedCleanup: true);
                    _releaseCompletion.TrySetResult();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public void Dispose()
        {
            if (!_retainedForCleanup)
            {
                ReleaseProbeLease(retainedCleanup: false);
            }
        }
    }

    private sealed record BoundedText(string Text, bool Truncated);
}

internal sealed record CliProcessProbeResult(int ExitCode, string StandardOutput, string StandardError);
