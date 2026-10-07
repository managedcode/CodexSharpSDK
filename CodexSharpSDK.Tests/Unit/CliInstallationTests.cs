using System.Diagnostics;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Extensions.AI;
using ManagedCode.CodexSharpSDK.Internal;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

[NotInParallel]
public sealed class CliInstallationTests
{
    private const string TestsDirectoryName = "tests";
    private const string SandboxDirectoryName = ".sandbox";
    private const string NpmDirectoryName = "npm";
    private const string BinDirectoryName = "bin";
    private const string NpmCliFileName = "npm-cli.js";
    private const string PackageJsonFileName = "package.json";
    private const string PackageName = "@openai/codex";
    private const string PackageScopeDirectoryName = "@openai";
    private const string PackageDirectoryName = "codex";
    private const string NodeModulesDirectoryName = "node_modules";
    private const string NpmBinDirectoryName = ".bin";
    private const string LocalApplicationDataDirectoryName = "local-app-data";
    private const string ManagedCodeDirectoryName = "ManagedCode";
    private const string SdkDirectoryName = "ManagedCode.CodexSharpSDK";
    private const string InstallDirectoryName = "cli";
    private const string ChildPidFileName = "child.pid";
    private const string GuidFormat = "N";
    private const string NpmPackageName = "npm";
    private const string NpmPackagePlaceholder = "NPM_PACKAGE_NAME";
    private const string NpmPackageManifest = "{\"name\":\"NPM_PACKAGE_NAME\",\"version\":\"0.0.0\"}";
    private const string NodeRequiredMessage = "Node.js is required for CLI installation process tests.";
    private const string PackageCliName = "codex";
    private const string PackageEntry = "codex.js";
    private const string NodeName = "node";
    private const string NodeWindowsName = "node.exe";
    private const string PathName = "PATH";
    private const string SystemRootName = "SYSTEMROOT";
    private const string MismatchMarkerName = "mismatch";
    private const string HangMarkerName = "hang";
    private const string PipeHolderMarkerName = "pipe-holder";
    private const string PipeHolderPidFileName = "pipe-holder.pid";
    private const string SetsidPathEnvironmentName = "CODEX_TEST_SETSID_PATH";
    private const string PipeHolderPidFileEnvironmentName = "CODEX_TEST_PIPE_HOLDER_PID_FILE";
    private const string SetsidCommandName = "setsid";
    private const string CleanupMessage = "CLI installation process or output cleanup could not be confirmed within its configured timeout.";
    private const string DetachedPipeHolderSkipReason = "The detached pipe-holder cleanup fixture requires Linux setsid.";
    private const string OverflowMarkerName = "overflow";
    private const string PackagePlaceholder = "PACKAGE_NAME";
    private const string CliPlaceholder = "CLI_NAME";
    private const string EntryPlaceholder = "PACKAGE_ENTRY";
    private const string VersionMismatchMessage = "The installed Codex CLI version does not match the SDK compatibility target.";
    private const string OutputLimitMessage = "CLI installation exceeded its configured output limit.";
    private const string ChatPrompt = "installed cli descriptor reaches a fresh chat";
    private const string ChatScript = """
        const fs = require('node:fs');
        const input = fs.readFileSync(0, 'utf8');
        const threadId = 'installed-fixture';
        console.log(JSON.stringify({type:'thread.started',thread_id:threadId}));
        console.log(JSON.stringify({type:'item.completed',item:{id:threadId,type:'agent_message',text:input}}));
        console.log(JSON.stringify({type:'turn.completed',usage:{input_tokens:1,cached_input_tokens:0,output_tokens:1}}));
        """;
    private const string NpmScript = """
        const fs = require('node:fs');
        const path = require('node:path');
        const args = process.argv.slice(2);
        const prefixIndex = Math.max(args.indexOf('--prefix'), args.indexOf('--cwd'));
        const root = args[prefixIndex + 1];
        const packageSpec = args.at(-1);
        const version = packageSpec.slice(packageSpec.lastIndexOf('@') + 1);
        const packageRoot = path.join(root, 'node_modules', '@openai', 'codex');
        const mismatch = fs.existsSync(path.join(__dirname, 'mismatch'));
        fs.mkdirSync(packageRoot, { recursive: true });
        fs.writeFileSync(path.join(packageRoot, 'package.json'), JSON.stringify({name:'PACKAGE_NAME',version:mismatch?'0.0.0':version,bin:{codex:'PACKAGE_ENTRY'}}));
        fs.writeFileSync(path.join(packageRoot, 'PACKAGE_ENTRY'), 'process.exit(0);');
        const shimDirectory = path.join(root, 'node_modules', '.bin');
        fs.mkdirSync(shimDirectory, { recursive: true });
        const suffix = process.platform === 'win32' ? '.cmd' : '';
        const shimPath = path.join(shimDirectory, 'CLI_NAME' + suffix);
        const shellQuote = value => "'" + value.replaceAll("'", "'\\''") + "'";
        const shim = process.platform === 'win32' ? '@echo off\r\n' : `#!/bin/sh\nexec ${shellQuote(process.execPath)} ${shellQuote(path.join(packageRoot, 'PACKAGE_ENTRY'))} "$@"\n`;
        fs.writeFileSync(shimPath, shim);
        if (process.platform !== 'win32') fs.chmodSync(shimPath, 0o755);
        if (fs.existsSync(path.join(__dirname, 'hang'))) {
            fs.writeFileSync(path.join(__dirname, 'child.pid'), String(process.pid));
            setInterval(() => {}, 1000);
        }
        if (fs.existsSync(path.join(__dirname, 'pipe-holder'))) {
            const { spawnSync } = require('node:child_process');
            const shellCommand = '"$1" /bin/sh -c \'echo $$ > "$1"; exec /bin/sleep 60\' holder "$2" &';
            spawnSync('/bin/sh', ['-c', shellCommand, 'fixture', process.env.CODEX_TEST_SETSID_PATH,
                process.env.CODEX_TEST_PIPE_HOLDER_PID_FILE], { stdio: 'inherit' });
        }
        if (fs.existsSync(path.join(__dirname, 'overflow'))) {
            process.stdout.write('x'.repeat(8192));
            setInterval(() => {}, 1000);
        }
        console.log('installation output');
        console.error('installation diagnostics');
        """;

    [Test]
    public async Task InstallResult_ConfiguresPublicChatClientAndRunsControlledCli()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var updates = await RunInstallationAsync(fixture);
            var result = updates.Single(update => update.Stage == CliInstallationStage.Installed).Result!;
            var entryPoint = result.LaunchCommand.PrefixArguments.SingleOrDefault() ??
                Path.Combine(result.InstallationRootPath, NodeModulesDirectoryName, PackageScopeDirectoryName,
                    PackageDirectoryName, PackageEntry);
            await File.WriteAllTextAsync(entryPoint, ChatScript);
            var clientOptions = new CodexOptions
            {
                LaunchCommand = result.LaunchCommand,
                InheritEnvironmentVariables = false,
                EnvironmentVariables = fixture.Options.EnvironmentVariables,
            };
            using var client = new CodexChatClient(new CodexChatClientOptions { CodexOptions = clientOptions });
            var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, ChatPrompt)]);
            var text = response.Messages.SelectMany(message => message.Contents).OfType<TextContent>()
                .Select(content => content.Text).FirstOrDefault();

            await Assert.That(text).IsEqualTo(ChatPrompt);
        }
        finally
        {
            DeleteFixture(fixture.FixtureRoot);
        }
    }

    [Test]
    public async Task InstallOrUpdate_UsesIsolatedSdkRootAndReturnsVerifiedLaunch()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var updates = await RunInstallationAsync(fixture);
            var result = updates.Single(update => update.Stage == CliInstallationStage.Installed).Result!;
            await Assert.That(result.InstalledVersion).IsEqualTo(CodexCliCompatibility.TargetVersion);
            await Assert.That(result.TargetVersion).IsEqualTo(CodexCliCompatibility.TargetVersion);
            await Assert.That(result.InstallationRootPath).IsEqualTo(fixture.ExpectedInstallRoot);
            if (OperatingSystem.IsWindows())
            {
                await Assert.That(result.LaunchCommand.ExecutablePath).IsEqualTo(fixture.NodePath);
                await Assert.That(result.LaunchCommand.PrefixArguments.Single()).IsEqualTo(Path.Combine(
                    fixture.ExpectedInstallRoot, NodeModulesDirectoryName, PackageScopeDirectoryName,
                    PackageDirectoryName, PackageEntry));
            }
            else
            {
                await Assert.That(result.LaunchCommand.ExecutablePath).IsEqualTo(Path.Combine(
                    fixture.ExpectedInstallRoot, NodeModulesDirectoryName, NpmBinDirectoryName, PackageCliName));
            }

            await Assert.That(updates.Any(update => update.Stage == CliInstallationStage.OutputObserved &&
                update.StandardOutputCharactersObserved > 0 && update.StandardErrorCharactersObserved > 0)).IsTrue();
        }
        finally
        {
            DeleteFixture(fixture.FixtureRoot);
        }
    }

    [Test]
    public async Task InstallOrUpdate_RejectsPackageManagerVersionMismatch()
    {
        var fixture = await CreateFixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.NpmRoot, BinDirectoryName, MismatchMarkerName), string.Empty);
        try
        {
            var exception = await Assert.That(async () => await RunInstallationAsync(fixture)).ThrowsException();
            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).IsEqualTo(VersionMismatchMessage);
        }
        finally
        {
            DeleteFixture(fixture.FixtureRoot);
        }
    }

    [Test]
    public async Task InstallOrUpdate_StopsWhenOutputExceedsConfiguredLimit()
    {
        var fixture = await CreateFixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.NpmRoot, BinDirectoryName, OverflowMarkerName), string.Empty);
        try
        {
            var exception = await Assert.That(async () => await RunInstallationAsync(fixture)).ThrowsException();
            await Assert.That(exception!.Message).IsEqualTo(OutputLimitMessage);
        }
        finally
        {
            DeleteFixture(fixture.FixtureRoot);
        }
    }

    [Test]
    public async Task InstallOrUpdate_CancellationStopsAndJoinsPackageManager()
    {
        var fixture = await CreateFixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.NpmRoot, BinDirectoryName, HangMarkerName), string.Empty);
        using var cancellation = new CancellationTokenSource();
        try
        {
            await using var updates = CodexCliInstallation.InstallOrUpdateAsync(fixture.Options,
                fixture.LocalApplicationDataRoot, cancellation.Token).GetAsyncEnumerator();
            await Assert.That(await updates.MoveNextAsync()).IsTrue();
            await Assert.That(updates.Current.Stage).IsEqualTo(CliInstallationStage.PackageManagerStarted);
            var childPidPath = Path.Combine(fixture.NpmRoot, BinDirectoryName, ChildPidFileName);
            using var markerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (!File.Exists(childPidPath))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), markerTimeout.Token);
            }

            var childPid = int.Parse(await File.ReadAllTextAsync(childPidPath), System.Globalization.CultureInfo.InvariantCulture);
            var competingOptions = fixture.Options with { InstallationLockTimeout = TimeSpan.FromMilliseconds(100) };
            var lockException = await Assert.That(async () =>
                await RunInstallationAsync(fixture, competingOptions)).ThrowsException();
            await Assert.That(lockException).IsTypeOf<TimeoutException>();
            cancellation.Cancel();
            var exception = await Assert.That(async () =>
            {
                while (await updates.MoveNextAsync())
                {
                }
            }).ThrowsException();
            await Assert.That(exception).IsTypeOf<OperationCanceledException>();
            await Assert.That(IsProcessRunning(childPid)).IsFalse();
        }
        finally
        {
            DeleteFixture(fixture.FixtureRoot);
        }
    }

    [Test]
    public async Task CancellationWithDetachedPipeHolderSurfacesUnconfirmedCleanup()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test(DetachedPipeHolderSkipReason);
            return;
        }

        var fixture = await CreateFixtureAsync();
        var holderPidFile = Path.Combine(fixture.NpmRoot, BinDirectoryName, PipeHolderPidFileName);
        var packageManagerPidFile = Path.Combine(fixture.NpmRoot, BinDirectoryName, ChildPidFileName);
        var holderProcessId = 0;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(fixture.NpmRoot, BinDirectoryName, HangMarkerName), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(fixture.NpmRoot, BinDirectoryName, PipeHolderMarkerName), string.Empty);
            var environment = fixture.Options.EnvironmentVariables.ToDictionary(
                static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            environment[SetsidPathEnvironmentName] = FindExecutablePath(SetsidCommandName);
            environment[PipeHolderPidFileEnvironmentName] = holderPidFile;
            var options = fixture.Options with
            {
                EnvironmentVariables = environment,
                ProcessTerminationTimeout = TimeSpan.FromSeconds(1)
            };
            using var cancellation = new CancellationTokenSource();
            await using var updates = CodexCliInstallation.InstallOrUpdateAsync(options,
                fixture.LocalApplicationDataRoot, cancellation.Token).GetAsyncEnumerator();
            await Assert.That(await updates.MoveNextAsync()).IsTrue();
            using var markerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await WaitForFileAsync(packageManagerPidFile, markerTimeout.Token);
            await WaitForFileAsync(holderPidFile, markerTimeout.Token);
            var packageManagerPid = int.Parse(await File.ReadAllTextAsync(packageManagerPidFile),
                System.Globalization.CultureInfo.InvariantCulture);
            holderProcessId = int.Parse(await File.ReadAllTextAsync(holderPidFile),
                System.Globalization.CultureInfo.InvariantCulture);
            await Assert.That(IsProcessRunning(packageManagerPid)).IsTrue();
            await Assert.That(IsProcessRunning(holderProcessId)).IsTrue();

            cancellation.Cancel();
            var exception = await Assert.That(async () =>
            {
                while (await updates.MoveNextAsync())
                {
                }
            }).ThrowsException();
            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).IsEqualTo(CleanupMessage);
            await Assert.That(IsProcessRunning(packageManagerPid)).IsFalse();
            await Assert.That(IsProcessRunning(holderProcessId)).IsTrue();
        }
        finally
        {
            if (holderProcessId > 0)
            {
                StopProcess(holderProcessId);
            }

            DeleteFixture(fixture.FixtureRoot);
        }
    }

    private static async Task<List<CliInstallationUpdate>> RunInstallationAsync(
        InstallationFixture fixture,
        CliInstallationOptions? options = null)
    {
        var updates = new List<CliInstallationUpdate>();
        await foreach (var update in CodexCliInstallation.InstallOrUpdateAsync(options ?? fixture.Options,
                           fixture.LocalApplicationDataRoot, CancellationToken.None))
        {
            updates.Add(update);
        }

        return updates;
    }

    private static async Task<InstallationFixture> CreateFixtureAsync()
    {
        var root = Path.Combine(Environment.CurrentDirectory, TestsDirectoryName, SandboxDirectoryName,
            Guid.NewGuid().ToString(GuidFormat));
        var npmRoot = Path.Combine(root, NpmDirectoryName);
        var npmBin = Path.Combine(npmRoot, BinDirectoryName);
        Directory.CreateDirectory(npmBin);
        var npmScript = Path.Combine(npmBin, NpmCliFileName);
        await File.WriteAllTextAsync(Path.Combine(npmRoot, PackageJsonFileName),
            NpmPackageManifest.Replace(NpmPackagePlaceholder, NpmPackageName, StringComparison.Ordinal));
        var script = NpmScript.Replace(PackagePlaceholder, PackageName, StringComparison.Ordinal)
            .Replace(CliPlaceholder, PackageCliName, StringComparison.Ordinal)
            .Replace(EntryPlaceholder, PackageEntry, StringComparison.Ordinal);
        await File.WriteAllTextAsync(npmScript, script);
        var node = FindNode();
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PathName] = Path.GetDirectoryName(node)!
        };
        var systemRoot = Environment.GetEnvironmentVariable(SystemRootName);
        if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(systemRoot))
        {
            environment[SystemRootName] = systemRoot;
        }

        var localApplicationDataRoot = Path.Combine(root, LocalApplicationDataDirectoryName);
        var expectedRoot = Path.Combine(localApplicationDataRoot, ManagedCodeDirectoryName, SdkDirectoryName,
            InstallDirectoryName);
        var options = new CliInstallationOptions
        {
            PackageManager = CliPackageManager.Npm,
            PackageManagerExecutablePath = node,
            NpmCliScriptPath = npmScript,
            EnvironmentVariables = environment,
            InstallTimeout = TimeSpan.FromSeconds(10),
            ProcessTerminationTimeout = TimeSpan.FromSeconds(2),
            InstallationLockTimeout = TimeSpan.FromSeconds(2),
            MaximumMetadataFileCharacters = 4096,
            MaximumOutputCharacters = 4096
        };

        return new InstallationFixture(root, npmRoot, localApplicationDataRoot, expectedRoot, node, options);
    }

    private static string FindNode()
    {
        var executableName = OperatingSystem.IsWindows() ? NodeWindowsName : NodeName;
        var path = Environment.GetEnvironmentVariable(PathName) ?? string.Empty;
        var node = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executableName)).FirstOrDefault(File.Exists);
        return node is null ? throw new InvalidOperationException(NodeRequiredMessage) : Path.GetFullPath(node);
    }

    private static string FindExecutablePath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable(PathName) ?? string.Empty;
        var executable = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executableName)).FirstOrDefault(File.Exists);
        return Path.GetFullPath(executable ?? throw new InvalidOperationException(NodeRequiredMessage));
    }

    private static async Task WaitForFileAsync(string path, CancellationToken cancellationToken)
    {
        while (!File.Exists(path))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private static void StopProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (ArgumentException)
        {
            return;
        }
    }

    private static void DeleteFixture(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed record InstallationFixture(
        string FixtureRoot,
        string NpmRoot,
        string LocalApplicationDataRoot,
        string ExpectedInstallRoot,
        string NodePath,
        CliInstallationOptions Options);
}
