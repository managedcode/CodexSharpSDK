using System.Diagnostics;
using ManagedCode.CodexSharpSDK.Internal;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

public class BoundedCliProcessProbeTests
{
    private const string ShellExecutableUnix = "/bin/sh";
    private const string ShellExecutableWindows = "cmd.exe";
    private const string ShellArgumentUnix = "-c";
    private const string ShellArgumentWindows = "/c";
    private const string SentinelVariable = "SDK_METADATA_PROBE_SENTINEL";
    private const string SentinelValue = "explicit-environment-value";
    private const string OverflowMessage = "CLI metadata process exceeded the configured output limit.";
    private const string TimeoutMessage = "CLI metadata process did not exit within the configured time limit.";
    private const string OutputTimeoutMessage = "CLI metadata process output did not close within the configured time limit.";
    private const string LinuxFixtureSkipReason = "The detached inherited-pipe fixture requires Linux setsid.";
    private const string DetachedChildCommandPrefix = "/usr/bin/setsid /bin/sleep 30 >&2 & echo $! > '";
    private const string DetachedChildCommandSuffix = "'; exit 0";
    private const string FixtureDirectoryPrefix = "BoundedCliProcessProbe-";
    private const string ChildProcessIdFileName = "child.pid";
    private const string StandardOutputPressureMarker = "stdout-pressure-1999";
    private const string StandardErrorPressureMarker = "stderr-pressure-1999";
    private const string StandardInputEofCommandUnix = "cat >/dev/null";
    private const string StandardInputEofCommandWindows = "more >NUL";
    private const int DetachedPipeProbeCount = 3;
    private const string SentinelCommandUnix = "printf '%s' \"$SDK_METADATA_PROBE_SENTINEL\"";
    private const string SentinelCommandWindows = "echo %SDK_METADATA_PROBE_SENTINEL%";
    private const string PressureCommandUnix =
        "i=0; while [ $i -lt 2000 ]; do printf 'stdout-pressure-%s\\n' \"$i\"; printf 'stderr-pressure-%s\\n' \"$i\" >&2; i=$((i+1)); done";
    private const string PressureCommandWindows =
        "for /L %i in (0,1,1999) do @(echo stdout-pressure-%i&echo stderr-pressure-%i 1>&2)";
    private const string OverflowCommandUnix = "printf '%01000d' 0";
    private const string OverflowCommandWindows = "for /L %i in (1,1,200) do @<nul set /p =0123456789";
    private const string LongRunningCommandUnix = "sleep 20";
    private const string LongRunningCommandWindows = "ping -n 21 127.0.0.1 >NUL";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Run_PreservesExplicitEnvironmentAndCanExcludeParentEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SentinelVariable] = SentinelValue,
        };

        var result = Run(SentinelCommandUnix, SentinelCommandWindows, environment, inheritEnvironmentVariables: false);

        await Assert.That(result.StandardOutput).Contains(SentinelValue);
    }

    [Test]
    public async Task Run_InheritsParentEnvironmentWhenRequested()
    {
        var previous = Environment.GetEnvironmentVariable(SentinelVariable);
        Environment.SetEnvironmentVariable(SentinelVariable, SentinelValue);
        try
        {
            var result = Run(SentinelCommandUnix, SentinelCommandWindows, environment: null, inheritEnvironmentVariables: true);

            await Assert.That(result.StandardOutput).Contains(SentinelValue);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SentinelVariable, previous);
        }
    }

    [Test]
    public async Task Run_DrainsStandardOutputAndErrorConcurrentlyUnderPressure()
    {
        var result = Run(PressureCommandUnix, PressureCommandWindows, environment: null,
            inheritEnvironmentVariables: true, maximumOutputCharacters: 100000);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput).Contains(StandardOutputPressureMarker);
        await Assert.That(result.StandardError).Contains(StandardErrorPressureMarker);
    }

    [Test]
    public async Task Run_ClosesStandardInputImmediatelyAfterStart()
    {
        var result = Run(StandardInputEofCommandUnix, StandardInputEofCommandWindows,
            environment: null, inheritEnvironmentVariables: true, timeout: ProbeTimeout);

        await Assert.That(result.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task Run_FailsExplicitlyWhenOutputExceedsConfiguredLimit()
    {
        var exception = await Assert.That(() => Run(OverflowCommandUnix, OverflowCommandWindows,
            environment: null, inheritEnvironmentVariables: true, maximumOutputCharacters: 64)).ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).IsEqualTo(OverflowMessage);
    }

    [Test]
    public async Task Run_StopsProcessWhenProbeTimesOut()
    {
        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.That(() => Run(LongRunningCommandUnix, LongRunningCommandWindows,
            environment: null, inheritEnvironmentVariables: true, timeout: ProbeTimeout)).ThrowsException();
        stopwatch.Stop();

        await Assert.That(exception).IsTypeOf<TimeoutException>();
        await Assert.That(exception!.Message).IsEqualTo(TimeoutMessage);
        await Assert.That(stopwatch.Elapsed).IsLessThan(ProcessTimeout);
    }

    [Test]
    public async Task Run_FailsWithinBoundWhenExitedRootHasDescendantHoldingPipes()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test(LinuxFixtureSkipReason);
            return;
        }

        var sandbox = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox",
            $"{FixtureDirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandbox);
        var childProcessIdPath = Path.Combine(sandbox, ChildProcessIdFileName);
        var detachedCommand = string.Concat(DetachedChildCommandPrefix, childProcessIdPath,
            DetachedChildCommandSuffix);
        try
        {
            for (var attempt = 0; attempt < DetachedPipeProbeCount; attempt++)
            {
                var activeReaders = 0;
                var stopwatch = Stopwatch.StartNew();
                var exception = await Assert.That(() => BoundedCliProcessProbe.Run(
                    ShellExecutableUnix,
                    [ShellArgumentUnix, detachedCommand],
                    environment: null,
                    inheritEnvironmentVariables: true,
                    timeout: ProbeTimeout,
                    maximumOutputCharacters: 64,
                    readerCountChanged: delta => Interlocked.Add(ref activeReaders, delta))).ThrowsException();
                stopwatch.Stop();

                await Assert.That(exception).IsTypeOf<TimeoutException>();
                await Assert.That(exception!.Message).IsEqualTo(OutputTimeoutMessage);
                await Assert.That(stopwatch.Elapsed).IsLessThan(ProcessTimeout);
                await Assert.That(activeReaders).IsEqualTo(0);
            }
        }
        finally
        {
            if (File.Exists(childProcessIdPath))
            {
                var processIdText = File.ReadAllText(childProcessIdPath);
                if (int.TryParse(processIdText, out var processId))
                {
                    using var child = Process.GetProcessById(processId);
                    if (!child.HasExited)
                    {
                        child.Kill(entireProcessTree: true);
                    }

                    child.WaitForExit(1000);
                }
            }

            Directory.Delete(sandbox, recursive: true);
        }
    }

    private static CliProcessProbeResult Run(
        string unixCommand,
        string windowsCommand,
        IReadOnlyDictionary<string, string>? environment,
        bool inheritEnvironmentVariables,
        TimeSpan? timeout = null,
        int maximumOutputCharacters = 200000)
    {
        var executablePath = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, ShellExecutableWindows)
            : ShellExecutableUnix;
        var arguments = new[]
        {
            OperatingSystem.IsWindows() ? ShellArgumentWindows : ShellArgumentUnix,
            OperatingSystem.IsWindows() ? windowsCommand : unixCommand,
        };
        return BoundedCliProcessProbe.Run(executablePath, arguments, environment, inheritEnvironmentVariables,
            timeout ?? ProcessTimeout, maximumOutputCharacters);
    }
}
