using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ManagedCode.CodexSharp.Internal;

namespace ManagedCode.CodexSharp;

public sealed class CodexExec
{
    private const string InternalOriginatorEnv = "CODEX_INTERNAL_ORIGINATOR_OVERRIDE";
    private const string CSharpSdkOriginator = "codex_sdk_csharp";

    private readonly string _executablePath;
    private readonly IReadOnlyDictionary<string, string>? _environmentOverride;
    private readonly JsonObject? _configOverrides;
    private readonly ICodexProcessRunner _processRunner;

    public CodexExec(
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? environmentOverride = null,
        JsonObject? configOverrides = null)
        : this(executablePath, environmentOverride, configOverrides, null)
    {
    }

    internal CodexExec(
        string? executablePath,
        IReadOnlyDictionary<string, string>? environmentOverride,
        JsonObject? configOverrides,
        ICodexProcessRunner? processRunner)
    {
        _executablePath = CodexCliLocator.FindCodexPath(executablePath);
        _environmentOverride = environmentOverride;
        _configOverrides = configOverrides;
        _processRunner = processRunner ?? new DefaultCodexProcessRunner();
    }

    public IAsyncEnumerable<string> RunAsync(CodexExecArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var commandArgs = BuildCommandArgs(args);
        var environment = BuildEnvironment(args.BaseUrl, args.ApiKey);
        var invocation = new CodexProcessInvocation(_executablePath, commandArgs, environment, args.Input);

        return _processRunner.RunAsync(invocation, args.CancellationToken);
    }

    internal IReadOnlyList<string> BuildCommandArgs(CodexExecArgs args)
    {
        var commandArgs = new List<string> { "exec", "--experimental-json" };

        if (_configOverrides is not null)
        {
            foreach (var overrideValue in TomlConfigSerializer.Serialize(_configOverrides))
            {
                commandArgs.Add("--config");
                commandArgs.Add(overrideValue);
            }
        }

        if (!string.IsNullOrWhiteSpace(args.Model))
        {
            commandArgs.Add("--model");
            commandArgs.Add(args.Model);
        }

        if (args.SandboxMode.HasValue)
        {
            commandArgs.Add("--sandbox");
            commandArgs.Add(args.SandboxMode.Value.ToCliValue());
        }

        if (!string.IsNullOrWhiteSpace(args.WorkingDirectory))
        {
            commandArgs.Add("--cd");
            commandArgs.Add(args.WorkingDirectory);
        }

        if (args.AdditionalDirectories is not null)
        {
            foreach (var directory in args.AdditionalDirectories)
            {
                commandArgs.Add("--add-dir");
                commandArgs.Add(directory);
            }
        }

        if (args.SkipGitRepoCheck)
        {
            commandArgs.Add("--skip-git-repo-check");
        }

        if (!string.IsNullOrWhiteSpace(args.OutputSchemaFile))
        {
            commandArgs.Add("--output-schema");
            commandArgs.Add(args.OutputSchemaFile);
        }

        if (args.ModelReasoningEffort.HasValue)
        {
            commandArgs.Add("--config");
            commandArgs.Add($"model_reasoning_effort=\"{args.ModelReasoningEffort.Value.ToCliValue()}\"");
        }

        if (args.NetworkAccessEnabled.HasValue)
        {
            commandArgs.Add("--config");
            commandArgs.Add($"sandbox_workspace_write.network_access={args.NetworkAccessEnabled.Value.ToString().ToLowerInvariant()}");
        }

        if (args.WebSearchMode.HasValue)
        {
            commandArgs.Add("--config");
            commandArgs.Add($"web_search=\"{args.WebSearchMode.Value.ToCliValue()}\"");
        }
        else if (args.WebSearchEnabled.HasValue)
        {
            commandArgs.Add("--config");
            commandArgs.Add(args.WebSearchEnabled.Value
                ? "web_search=\"live\""
                : "web_search=\"disabled\"");
        }

        if (args.ApprovalPolicy.HasValue)
        {
            commandArgs.Add("--config");
            commandArgs.Add($"approval_policy=\"{args.ApprovalPolicy.Value.ToCliValue()}\"");
        }

        if (!string.IsNullOrWhiteSpace(args.ThreadId))
        {
            commandArgs.Add("resume");
            commandArgs.Add(args.ThreadId);
        }

        if (args.Images is not null)
        {
            foreach (var image in args.Images)
            {
                commandArgs.Add("--image");
                commandArgs.Add(image);
            }
        }

        return commandArgs;
    }

    internal IReadOnlyDictionary<string, string> BuildEnvironment(string? baseUrl, string? apiKey)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_environmentOverride is not null)
        {
            foreach (var (key, value) in _environmentOverride)
            {
                environment[key] = value;
            }
        }
        else
        {
            foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            {
                if (variable.Key is string key && variable.Value is string value)
                {
                    environment[key] = value;
                }
            }
        }

        if (!environment.ContainsKey(InternalOriginatorEnv))
        {
            environment[InternalOriginatorEnv] = CSharpSdkOriginator;
        }

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            environment["OPENAI_BASE_URL"] = baseUrl;
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            environment["CODEX_API_KEY"] = apiKey;
        }

        return environment;
    }
}

internal sealed record CodexProcessInvocation(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string Input);

internal interface ICodexProcessRunner
{
    IAsyncEnumerable<string> RunAsync(CodexProcessInvocation invocation, CancellationToken cancellationToken);
}

internal sealed class DefaultCodexProcessRunner : ICodexProcessRunner
{
    public async IAsyncEnumerable<string> RunAsync(
        CodexProcessInvocation invocation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(invocation.ExecutablePath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var (key, value) in invocation.Environment)
        {
            startInfo.Environment[key] = value;
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start Codex CLI at '{invocation.ExecutablePath}'");
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to start Codex CLI at '{invocation.ExecutablePath}'", exception);
        }

        try
        {
            await process.StandardInput.WriteAsync(invocation.Input.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();

            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                yield return line;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Codex Exec exited with code {process.ExitCode}: {standardError}");
            }
        }
        finally
        {
            TryKillProcess(process);
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Suppress cleanup errors.
        }
    }
}

internal static class CliValueExtensions
{
    public static string ToCliValue(this SandboxMode mode)
    {
        return mode switch
        {
            SandboxMode.ReadOnly => "read-only",
            SandboxMode.WorkspaceWrite => "workspace-write",
            SandboxMode.DangerFullAccess => "danger-full-access",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static string ToCliValue(this ModelReasoningEffort effort)
    {
        return effort switch
        {
            ModelReasoningEffort.Minimal => "minimal",
            ModelReasoningEffort.Low => "low",
            ModelReasoningEffort.Medium => "medium",
            ModelReasoningEffort.High => "high",
            ModelReasoningEffort.XHigh => "xhigh",
            _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null),
        };
    }

    public static string ToCliValue(this WebSearchMode mode)
    {
        return mode switch
        {
            WebSearchMode.Disabled => "disabled",
            WebSearchMode.Cached => "cached",
            WebSearchMode.Live => "live",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static string ToCliValue(this ApprovalMode mode)
    {
        return mode switch
        {
            ApprovalMode.Never => "never",
            ApprovalMode.OnRequest => "on-request",
            ApprovalMode.OnFailure => "on-failure",
            ApprovalMode.Untrusted => "untrusted",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
