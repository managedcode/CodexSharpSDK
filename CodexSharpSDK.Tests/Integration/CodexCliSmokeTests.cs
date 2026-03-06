using System.Diagnostics;
using ManagedCode.CodexSharpSDK.Internal;

namespace ManagedCode.CodexSharpSDK.Tests.Integration;

public class CodexCliSmokeTests
{
    private const string SolutionFileName = "ManagedCode.CodexSharpSDK.slnx";
    private const string TestsDirectoryName = "tests";
    private const string SandboxDirectoryName = ".sandbox";
    private const string SandboxPrefix = "CodexCliSmokeTests-";
    private const string PathEnvironmentVariable = "PATH";
    private const string HomeEnvironmentVariable = "HOME";
    private const string UserProfileEnvironmentVariable = "USERPROFILE";
    private const string XdgConfigHomeEnvironmentVariable = "XDG_CONFIG_HOME";
    private const string AppDataEnvironmentVariable = "APPDATA";
    private const string LocalAppDataEnvironmentVariable = "LOCALAPPDATA";
    private const string OpenAiApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    private const string OpenAiBaseUrlEnvironmentVariable = "OPENAI_BASE_URL";
    private const string CodexApiKeyEnvironmentVariable = "CODEX_API_KEY";
    private const string CodexHomeEnvironmentVariable = "CODEX_HOME";
    private const string CodexHomeDirectoryName = ".codex";
    private const string AppDataDirectoryName = "AppData";
    private const string RoamingDirectoryName = "Roaming";
    private const string LocalDirectoryName = "Local";
    private const string ConfigDirectoryName = ".config";
    private const string VersionFlag = "--version";
    private const string ExecCommand = "exec";
    private const string LoginCommand = "login";
    private const string StatusCommand = "status";
    private const string HelpFlag = "--help";
    private const string VersionToken = "codex-cli";
    private const string RootHelpToken = "Codex CLI";
    private const string RootHelpCommandToken = "exec";
    private const string ExecHelpToken = "Run Codex non-interactively";
    private const string NotLoggedInToken = "Not logged in";
    private const string NotAuthenticatedToken = "Not authenticated";
    private const string LoginGuidanceToken = "codex login";

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

        var result = await RunCodexAsync(executablePath, null, VersionFlag);
        await Assert.That(result.ExitCode).IsEqualTo(0);

        var output = string.Concat(result.StandardOutput, result.StandardError);
        await Assert.That(output.Contains(VersionToken, StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    [Test]
    public async Task CodexCli_Smoke_HelpCommand_ReturnsRootCommands()
    {
        var executablePath = ResolveExecutablePath();

        var result = await RunCodexAsync(executablePath, null, HelpFlag);
        await Assert.That(result.ExitCode).IsEqualTo(0);

        var output = string.Concat(result.StandardOutput, result.StandardError);
        await Assert.That(output.Contains(RootHelpToken, StringComparison.Ordinal)).IsTrue();
        await Assert.That(output.Contains(RootHelpCommandToken, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task CodexCli_Smoke_ExecHelpCommand_ReturnsSuccess()
    {
        var executablePath = ResolveExecutablePath();

        var result = await RunCodexAsync(executablePath, null, ExecCommand, HelpFlag);
        await Assert.That(result.ExitCode).IsEqualTo(0);

        var output = string.Concat(result.StandardOutput, result.StandardError);
        await Assert.That(output.Contains(ExecHelpToken, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task CodexCli_Smoke_LoginStatusWithoutAuth_ReportsNotLoggedIn()
    {
        var executablePath = ResolveExecutablePath();
        var sandboxDirectory = CreateSandboxDirectory();

        try
        {
            var environmentOverrides = CreateUnauthenticatedEnvironmentOverrides(sandboxDirectory);
            var result = await RunCodexAsync(executablePath, environmentOverrides, LoginCommand, StatusCommand);
            await Assert.That(result.ExitCode).IsNotEqualTo(0);

            var output = string.Concat(result.StandardOutput, result.StandardError);
            await Assert.That(ContainsUnauthenticatedSignal(output)).IsTrue();
        }
        finally
        {
            Directory.Delete(sandboxDirectory, recursive: true);
        }
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

    private static string CreateSandboxDirectory()
    {
        var repositoryRoot = ResolveRepositoryRootPath();
        var sandboxDirectory = Path.Combine(
            repositoryRoot,
            TestsDirectoryName,
            SandboxDirectoryName,
            $"{SandboxPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandboxDirectory);
        return sandboxDirectory;
    }

    private static string ResolveRepositoryRootPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, SolutionFileName)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test execution directory.");
    }

    private static Dictionary<string, string> CreateUnauthenticatedEnvironmentOverrides(string sandboxDirectory)
    {
        var codexHome = Path.Combine(sandboxDirectory, CodexHomeDirectoryName);
        var configHome = Path.Combine(sandboxDirectory, ConfigDirectoryName);
        var appData = Path.Combine(sandboxDirectory, AppDataDirectoryName, RoamingDirectoryName);
        var localAppData = Path.Combine(sandboxDirectory, AppDataDirectoryName, LocalDirectoryName);

        Directory.CreateDirectory(codexHome);
        Directory.CreateDirectory(configHome);
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(localAppData);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [CodexHomeEnvironmentVariable] = codexHome,
            [HomeEnvironmentVariable] = sandboxDirectory,
            [UserProfileEnvironmentVariable] = sandboxDirectory,
            [XdgConfigHomeEnvironmentVariable] = configHome,
            [AppDataEnvironmentVariable] = appData,
            [LocalAppDataEnvironmentVariable] = localAppData,
            [OpenAiApiKeyEnvironmentVariable] = string.Empty,
            [OpenAiBaseUrlEnvironmentVariable] = string.Empty,
            [CodexApiKeyEnvironmentVariable] = string.Empty,
        };
    }

    private static async Task<CodexProcessResult> RunCodexAsync(
        string executablePath,
        IReadOnlyDictionary<string, string>? environmentOverrides,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            startInfo.ArgumentList.Add(argument);
        }

        if (environmentOverrides is not null)
        {
            foreach (var (key, value) in environmentOverrides)
            {
                startInfo.Environment[key] = value;
            }
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

    private static bool ContainsUnauthenticatedSignal(string output)
    {
        return output.Contains(NotLoggedInToken, StringComparison.OrdinalIgnoreCase)
               || output.Contains(NotAuthenticatedToken, StringComparison.OrdinalIgnoreCase)
               || output.Contains(LoginGuidanceToken, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CodexProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
