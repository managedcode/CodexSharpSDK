using System.Diagnostics;
using ManagedCode.CodexSharpSDK.Internal;

namespace ManagedCode.CodexSharpSDK.Tests.Integration;

public class CodexCliSmokeTests
{
    private const string PathEnvironmentVariable = "PATH";
    private const string VersionFlag = "--version";
    private const string ExecCommand = "exec";
    private const string HelpFlag = "--help";
    private const string VersionToken = "codex-cli";
    private const string ExecHelpToken = "Run Codex non-interactively";

    [Test]
    public async Task CodexCli_Smoke_FindExecutablePath_ResolvesExistingBinary()
    {
        var executablePath = ResolveExecutablePath();
        await Assert.That(File.Exists(executablePath)).IsTrue();
    }

    [Test]
    public async Task CodexCli_Smoke_VersionCommand_ReturnsCodexCliVersion()
    {
        var executablePath = ResolveExecutablePath();

        var result = await RunCodexAsync(executablePath, VersionFlag);
        await Assert.That(result.ExitCode).IsEqualTo(0);

        var output = string.Concat(result.StandardOutput, result.StandardError);
        await Assert.That(output.Contains(VersionToken, StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task CodexCli_Smoke_ExecHelpCommand_ReturnsSuccess()
    {
        var executablePath = ResolveExecutablePath();

        var result = await RunCodexAsync(executablePath, ExecCommand, HelpFlag);
        await Assert.That(result.ExitCode).IsEqualTo(0);

        var output = string.Concat(result.StandardOutput, result.StandardError);
        await Assert.That(output.Contains(ExecHelpToken, StringComparison.Ordinal)).IsTrue();
    }

    private static string ResolveExecutablePath()
    {
        var resolvedPath = CodexCliLocator.FindCodexPath(null);
        if (Path.IsPathRooted(resolvedPath))
        {
            if (File.Exists(resolvedPath))
            {
                return resolvedPath;
            }

            throw new InvalidOperationException($"Codex CLI path is rooted but missing: '{resolvedPath}'.");
        }

        if (CodexCliLocator.TryResolvePathExecutable(
                Environment.GetEnvironmentVariable(PathEnvironmentVariable),
                OperatingSystem.IsWindows(),
                out var pathExecutable))
        {
            return pathExecutable;
        }

        throw new InvalidOperationException("Failed to resolve Codex CLI path.");
    }

    private static async Task<CodexProcessResult> RunCodexAsync(
        string executablePath,
        string firstArgument,
        string? secondArgument = null)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add(firstArgument);
        if (!string.IsNullOrWhiteSpace(secondArgument))
        {
            startInfo.ArgumentList.Add(secondArgument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start Codex CLI at '{executablePath}'.");
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to start Codex CLI at '{executablePath}'.", exception);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        return new CodexProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private sealed record CodexProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
