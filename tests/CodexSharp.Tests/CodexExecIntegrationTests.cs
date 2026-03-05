namespace ManagedCode.CodexSharp.Tests;

public class CodexExecIntegrationTests
{
    [Test]
    public async Task RunAsync_UsesDefaultProcessRunner_EndToEnd()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var sandboxDirectory = CreateSandboxDirectory();
        try
        {
            var argsLog = Path.Combine(sandboxDirectory, "args.log");
            var inputLog = Path.Combine(sandboxDirectory, "input.log");
            var executablePath = Path.Combine(sandboxDirectory, "fake-codex.sh");

            WriteExecutableScript(executablePath, BuildSuccessScript(argsLog, inputLog, "thread_it_1", "integration_ok"));

            await using var client = new CodexClient(new CodexOptions
            {
                CodexPathOverride = executablePath,
            });

            var thread = client.StartThread(new ThreadOptions
            {
                Model = "gpt-5",
                SandboxMode = SandboxMode.WorkspaceWrite,
            });

            var result = await thread.RunAsync("hello from integration");

            await Assert.That(thread.Id).IsEqualTo("thread_it_1");
            await Assert.That(result.FinalResponse).IsEqualTo("integration_ok");
            await Assert.That(result.Usage).IsNotNull();

            var args = await File.ReadAllLinesAsync(argsLog);
            await Assert.That(args).Contains("exec");
            await Assert.That(args).Contains("--experimental-json");
            await Assert.That(args).Contains("--model");
            await Assert.That(args).Contains("gpt-5");
            await Assert.That(args).Contains("--sandbox");
            await Assert.That(args).Contains("workspace-write");

            var input = await File.ReadAllTextAsync(inputLog);
            await Assert.That(input).IsEqualTo("hello from integration");
        }
        finally
        {
            CleanupSandboxDirectory(sandboxDirectory);
        }
    }

    [Test]
    public async Task RunAsync_SecondCallPassesResumeArgument_EndToEnd()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var sandboxDirectory = CreateSandboxDirectory();
        try
        {
            var argsLog = Path.Combine(sandboxDirectory, "args.log");
            var inputLog = Path.Combine(sandboxDirectory, "input.log");
            var executablePath = Path.Combine(sandboxDirectory, "fake-codex.sh");

            WriteExecutableScript(executablePath, BuildSuccessScript(argsLog, inputLog, "thread_it_2", "ok"));

            await using var client = new CodexClient(new CodexOptions
            {
                CodexPathOverride = executablePath,
            });

            var thread = client.StartThread();

            await thread.RunAsync("first");
            await thread.RunAsync("second");

            var args = await File.ReadAllLinesAsync(argsLog);
            var resumeIndex = Array.IndexOf(args, "resume");

            await Assert.That(resumeIndex).IsGreaterThan(-1);
            await Assert.That(args[resumeIndex + 1]).IsEqualTo("thread_it_2");
        }
        finally
        {
            CleanupSandboxDirectory(sandboxDirectory);
        }
    }

    [Test]
    public async Task RunAsync_PropagatesNonZeroExitCode_EndToEnd()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var sandboxDirectory = CreateSandboxDirectory();
        try
        {
            var executablePath = Path.Combine(sandboxDirectory, "fake-codex.sh");
            WriteExecutableScript(executablePath, BuildFailureScript());

            await using var client = new CodexClient(new CodexOptions
            {
                CodexPathOverride = executablePath,
            });

            var thread = client.StartThread();
            var action = async () => await thread.RunAsync("trigger failure");

            var exception = await Assert.That(action).ThrowsException();
            await Assert.That(exception).IsTypeOf<InvalidOperationException>();
            await Assert.That(exception!.Message).Contains("exited with code 9");
        }
        finally
        {
            CleanupSandboxDirectory(sandboxDirectory);
        }
    }

    private static string CreateSandboxDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"codexsharp-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CleanupSandboxDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Suppress cleanup errors.
        }
    }

    private static string BuildSuccessScript(string argsLog, string inputLog, string threadId, string response)
    {
        var escapedThreadId = EscapeJsonString(threadId);
        var escapedResponse = EscapeJsonString(response);

        return string.Join('\n',
        [
            "#!/usr/bin/env bash",
            "set -euo pipefail",
            $"args_log={ToBashLiteral(argsLog)}",
            $"input_log={ToBashLiteral(inputLog)}",
            "printf '%s\\n' \"$@\" > \"$args_log\"",
            "cat > \"$input_log\"",
            $"echo '{{\"type\":\"thread.started\",\"thread_id\":\"{escapedThreadId}\"}}'",
            $"echo '{{\"type\":\"item.completed\",\"item\":{{\"id\":\"item_1\",\"type\":\"agent_message\",\"text\":\"{escapedResponse}\"}}}}'",
            "echo '{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":2,\"cached_input_tokens\":0,\"output_tokens\":3}}'",
        ]) + "\n";
    }

    private static string BuildFailureScript()
    {
        return """
#!/usr/bin/env bash
set -euo pipefail
cat > /dev/null
echo "forced integration failure" >&2
exit 9
""";
    }

    private static string ToBashLiteral(string value)
    {
        return $"'{value.Replace("'", "'\"'\"'")}'";
    }

    private static string EscapeJsonString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static void WriteExecutableScript(string path, string scriptContent)
    {
        File.WriteAllText(path, scriptContent);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
