using System.Diagnostics;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Tests;

public sealed class ProviderFailureProcessTests
{
    private const string ScriptFileName = "codex-provider-failure.js";
    private const string SandboxDirectoryName = ".sandbox";
    private const string TestsDirectoryName = "tests";
    private const string VersionFlag = "--version";
    private const string CliVersion = "0.160.1";
    private const string MarkerEnvironmentName = "CODEX_FAILURE_MARKER";
    private const string ProviderFailureLine = "{\"type\":\"turn.failed\",\"error\":{\"message\":\"provider failed\"}}";
    private const string StreamErrorLine = "{\"type\":\"error\",\"message\":\"stream failed\"}";
    private const string LateEventLine = "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":0,\"cached_input_tokens\":0,\"output_tokens\":0}}";
    private const string UserPrompt = "bounded SDK fixture request";
    private const string CleanupSkipReason = "The detached inherited-pipe fixture requires POSIX process semantics.";

    [Test]
    public async Task CodexChatClient_DrainsDelayedEventsBeforeConfirmingProviderFailureAsync()
    {
        var directory = CreateSandboxDirectory();
        var scriptPath = Path.Combine(directory, ScriptFileName);
        var markerPath = Path.Combine(directory, "natural-eof.marker");
        await File.WriteAllTextAsync(scriptPath, CreateDelayedExitScript());

        try
        {
            using var client = CreateClient(scriptPath, markerPath);
            var updates = new List<ChatResponseUpdate>();
            var exception = await Assert.That(() => ConsumeAsync(client, CancellationToken.None, updates.Add)).ThrowsException();

            await Assert.That(exception).IsTypeOf<CliExecutionFailureException>();
            await Assert.That(((CliExecutionFailureException)exception!).RootProcessExitConfirmed).IsTrue();
            await Assert.That(((CliExecutionFailureException)exception!).ExitCode).IsNull();
            await Assert.That(File.Exists(markerPath)).IsTrue();
            await Assert.That(updates.Any(static update => update.FinishReason is not null)).IsFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task CodexChatClient_RetainedPipeCancellationDoesNotConfirmProviderFailureAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test(CleanupSkipReason);
            return;
        }

        var directory = CreateSandboxDirectory();
        var scriptPath = Path.Combine(directory, ScriptFileName);
        var childPidPath = Path.Combine(directory, "pipe-holder.pid");
        var readyPath = Path.Combine(directory, "pipe-holder.ready");
        await File.WriteAllTextAsync(scriptPath, CreateRetainedPipeScript(childPidPath));
        using var client = CreateClient(scriptPath, readyPath);
        using var cancellation = new CancellationTokenSource();
        var execution = CaptureFailureAsync(ConsumeAsync(client, cancellation.Token));

        try
        {
            await WaitForFileAsync(readyPath);
            cancellation.Cancel();
            var exception = await execution.WaitAsync(TimeSpan.FromSeconds(12));
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception).IsNotTypeOf<CliExecutionFailureException>();
        }
        finally
        {
            cancellation.Cancel();
            await StopChildIfPresentAsync(childPidPath);
            await execution.WaitAsync(TimeSpan.FromSeconds(12));
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CodexChatClient CreateClient(string scriptPath, string markerPath) => new(new CodexChatClientOptions
    {
        CodexOptions = new CodexOptions
        {
            CodexExecutablePath = scriptPath,
            EnvironmentVariables = new Dictionary<string, string> { [MarkerEnvironmentName] = markerPath },
            InheritEnvironmentVariables = true,
            CliMetadataProbeTimeout = TimeSpan.FromSeconds(5),
            ProcessTerminationTimeout = TimeSpan.FromSeconds(3),
        },
    });

    private static async Task ConsumeAsync(
        CodexChatClient client,
        CancellationToken cancellationToken,
        Action<ChatResponseUpdate>? onUpdate = null)
    {
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, UserPrompt)], cancellationToken: cancellationToken))
        {
            onUpdate?.Invoke(update);
        }
    }

    private static async Task<Exception?> CaptureFailureAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static string CreateDelayedExitScript() => string.Concat(
        "const fs=require('node:fs');\n",
        "if(process.argv.includes('", VersionFlag, "')){console.log('codex-cli ", CliVersion, "');process.exit(0);}\n",
        "process.stdin.resume();process.stdin.on('end',()=>{process.stdout.write('", ProviderFailureLine,
        "\\n');setTimeout(()=>{process.stdout.write('", LateEventLine,
        "\\n',()=>{fs.writeFileSync(process.env.", MarkerEnvironmentName, ",'natural-eof');process.exit(0);});},150);});\n");

    private static string CreateRetainedPipeScript(string pidPath) => string.Concat(
        "const fs=require('node:fs');const {spawn}=require('node:child_process');\n",
        "if(process.argv.includes('", VersionFlag, "')){console.log('codex-cli ", CliVersion, "');process.exit(0);}\n",
        "process.stdin.resume();process.stdin.on('end',()=>{process.stdout.write('", StreamErrorLine,
        "\\n',()=>setTimeout(()=>{const helperSource=\"const fs=require('node:fs');const{spawn}=require('node:child_process');const child=spawn(process.execPath,['-e','setTimeout(()=>{},30000)'],{stdio:['ignore','inherit','inherit'],detached:true});child.unref();fs.writeFileSync('",
        pidPath.Replace("'", "\\'", StringComparison.Ordinal),
        "',String(child.pid));\";const helper=spawn(process.execPath,['-e',helperSource],{stdio:['ignore','inherit','inherit'],detached:true});helper.unref();helper.on('exit',()=>fs.writeFileSync(process.env.",
        MarkerEnvironmentName,
        ",'ready'));setInterval(()=>{},1000);},100));});\n");

    private static string CreateSandboxDirectory()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, TestsDirectoryName, SandboxDirectoryName,
            $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task WaitForFileAsync(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(path))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    private static async Task StopChildIfPresentAsync(string pidPath)
    {
        if (!File.Exists(pidPath) || !int.TryParse(await File.ReadAllTextAsync(pidPath), out var processId))
        {
            return;
        }

        try
        {
            using var child = Process.GetProcessById(processId);
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        catch (ArgumentException)
        {
            // The fixture child already exited before cleanup.
        }
    }
}
