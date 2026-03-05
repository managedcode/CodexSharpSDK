using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagedCode.CodexSharpSDK.Execution;

public sealed class CodexExec
{
    private const string ExecCommandName = "exec";
    private const string ResumeCommandName = "resume";

    private const string JsonFlag = "--json";
    private const string ConfigFlag = "--config";
    private const string EnableFeatureFlag = "--enable";
    private const string DisableFeatureFlag = "--disable";
    private const string ModelFlag = "--model";
    private const string SandboxFlag = "--sandbox";
    private const string WorkingDirectoryFlag = "--cd";
    private const string AddDirectoryFlag = "--add-dir";
    private const string ProfileFlag = "--profile";
    private const string SkipGitRepoCheckFlag = "--skip-git-repo-check";
    private const string OutputSchemaFlag = "--output-schema";
    private const string UseOssFlag = "--oss";
    private const string LocalProviderFlag = "--local-provider";
    private const string FullAutoFlag = "--full-auto";
    private const string DangerouslyBypassApprovalsAndSandboxFlag = "--dangerously-bypass-approvals-and-sandbox";
    private const string EphemeralFlag = "--ephemeral";
    private const string ColorFlag = "--color";
    private const string ProgressCursorFlag = "--progress-cursor";
    private const string OutputLastMessageFlag = "--output-last-message";
    private const string ImageFlag = "--image";

    private const string ModelReasoningEffortConfigKey = "model_reasoning_effort";
    private const string SandboxNetworkAccessConfigKey = "sandbox_workspace_write.network_access";
    private const string WebSearchConfigKey = "web_search";
    private const string ApprovalPolicyConfigKey = "approval_policy";
    private const string WebSearchLiveValue = "live";
    private const string WebSearchDisabledValue = "disabled";
    private const string BooleanTrueLiteral = "true";
    private const string BooleanFalseLiteral = "false";

    private const string InternalOriginatorEnv = "CODEX_INTERNAL_ORIGINATOR_OVERRIDE";
    private const string CSharpSdkOriginator = "codex_sdk_csharp";
    private const string OpenAiBaseUrlEnv = "OPENAI_BASE_URL";
    private const string CodexApiKeyEnv = "CODEX_API_KEY";

    private readonly string _executablePath;
    private readonly IReadOnlyDictionary<string, string>? _environmentOverride;
    private readonly JsonObject? _configOverrides;
    private readonly ICodexProcessRunner _processRunner;
    private readonly ILogger _logger;

    public CodexExec(
        string? executablePath = null,
        IReadOnlyDictionary<string, string>? environmentOverride = null,
        JsonObject? configOverrides = null,
        ILogger? logger = null)
        : this(executablePath, environmentOverride, configOverrides, null, logger)
    {
    }

    internal CodexExec(
        string? executablePath,
        IReadOnlyDictionary<string, string>? environmentOverride,
        JsonObject? configOverrides,
        ICodexProcessRunner? processRunner,
        ILogger? logger = null)
    {
        _executablePath = CodexCliLocator.FindCodexPath(executablePath);
        _environmentOverride = environmentOverride;
        _configOverrides = configOverrides;
        _processRunner = processRunner ?? new DefaultCodexProcessRunner();
        _logger = logger ?? NullLogger.Instance;
    }

    public IAsyncEnumerable<string> RunAsync(CodexExecArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var commandArgs = BuildCommandArgs(args);
        var environment = BuildEnvironment(args.BaseUrl, args.ApiKey);
        var invocation = new CodexProcessInvocation(_executablePath, commandArgs, environment, args.Input);

        return RunWithDiagnosticsAsync(invocation, args.CancellationToken);
    }

    private async IAsyncEnumerable<string> RunWithDiagnosticsAsync(
        CodexProcessInvocation invocation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Logging.CodexExecLog.Starting(_logger, invocation.ExecutablePath, invocation.Arguments.Count);

        var lineCount = 0;

        IAsyncEnumerator<string> enumerator;
        try
        {
            enumerator = _processRunner
                .RunAsync(invocation, _logger, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            Logging.CodexExecLog.Cancelled(_logger, exception);
            throw;
        }
        catch (Exception exception)
        {
            Logging.CodexExecLog.Failed(_logger, exception);
            throw;
        }

        await using (enumerator)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string line;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    line = enumerator.Current;
                }
                catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
                {
                    Logging.CodexExecLog.Cancelled(_logger, exception);
                    throw;
                }
                catch (Exception exception)
                {
                    Logging.CodexExecLog.Failed(_logger, exception);
                    throw;
                }

                lineCount += 1;
                yield return line;
            }

            Logging.CodexExecLog.Completed(_logger, lineCount);
        }
    }

    internal IReadOnlyList<string> BuildCommandArgs(CodexExecArgs args)
    {
        var commandArgs = new List<string> { ExecCommandName, JsonFlag };

        if (_configOverrides is not null)
        {
            foreach (var overrideValue in TomlConfigSerializer.Serialize(_configOverrides))
            {
                commandArgs.Add(ConfigFlag);
                commandArgs.Add(overrideValue);
            }
        }

        AddRepeatedFlag(commandArgs, EnableFeatureFlag, args.EnabledFeatures);
        AddRepeatedFlag(commandArgs, DisableFeatureFlag, args.DisabledFeatures);

        if (args.UseOss)
        {
            commandArgs.Add(UseOssFlag);
        }

        if (args.LocalProvider.HasValue)
        {
            commandArgs.Add(LocalProviderFlag);
            commandArgs.Add(args.LocalProvider.Value.ToCliValue());
        }

        if (!string.IsNullOrWhiteSpace(args.Profile))
        {
            commandArgs.Add(ProfileFlag);
            commandArgs.Add(args.Profile);
        }

        if (!string.IsNullOrWhiteSpace(args.Model))
        {
            commandArgs.Add(ModelFlag);
            commandArgs.Add(args.Model);
        }

        if (args.SandboxMode.HasValue)
        {
            commandArgs.Add(SandboxFlag);
            commandArgs.Add(args.SandboxMode.Value.ToCliValue());
        }

        if (!string.IsNullOrWhiteSpace(args.WorkingDirectory))
        {
            commandArgs.Add(WorkingDirectoryFlag);
            commandArgs.Add(args.WorkingDirectory);
        }

        if (args.AdditionalDirectories is not null)
        {
            foreach (var directory in args.AdditionalDirectories)
            {
                commandArgs.Add(AddDirectoryFlag);
                commandArgs.Add(directory);
            }
        }

        if (args.FullAuto)
        {
            commandArgs.Add(FullAutoFlag);
        }

        if (args.DangerouslyBypassApprovalsAndSandbox)
        {
            commandArgs.Add(DangerouslyBypassApprovalsAndSandboxFlag);
        }

        if (args.Ephemeral)
        {
            commandArgs.Add(EphemeralFlag);
        }

        if (args.Color.HasValue)
        {
            commandArgs.Add(ColorFlag);
            commandArgs.Add(args.Color.Value.ToCliValue());
        }

        if (args.ProgressCursor)
        {
            commandArgs.Add(ProgressCursorFlag);
        }

        if (!string.IsNullOrWhiteSpace(args.OutputLastMessageFile))
        {
            commandArgs.Add(OutputLastMessageFlag);
            commandArgs.Add(args.OutputLastMessageFile);
        }

        if (args.SkipGitRepoCheck)
        {
            commandArgs.Add(SkipGitRepoCheckFlag);
        }

        if (!string.IsNullOrWhiteSpace(args.OutputSchemaFile))
        {
            commandArgs.Add(OutputSchemaFlag);
            commandArgs.Add(args.OutputSchemaFile);
        }

        if (args.ModelReasoningEffort.HasValue)
        {
            commandArgs.Add(ConfigFlag);
            commandArgs.Add(BuildQuotedConfig(ModelReasoningEffortConfigKey, args.ModelReasoningEffort.Value.ToCliValue()));
        }

        if (args.NetworkAccessEnabled.HasValue)
        {
            commandArgs.Add(ConfigFlag);
            commandArgs.Add(BuildBooleanConfig(SandboxNetworkAccessConfigKey, args.NetworkAccessEnabled.Value));
        }

        if (args.WebSearchMode.HasValue)
        {
            commandArgs.Add(ConfigFlag);
            commandArgs.Add(BuildQuotedConfig(WebSearchConfigKey, args.WebSearchMode.Value.ToCliValue()));
        }
        else if (args.WebSearchEnabled.HasValue)
        {
            commandArgs.Add(ConfigFlag);
            commandArgs.Add(BuildQuotedConfig(
                WebSearchConfigKey,
                args.WebSearchEnabled.Value ? WebSearchLiveValue : WebSearchDisabledValue));
        }

        if (args.ApprovalPolicy.HasValue)
        {
            commandArgs.Add(ConfigFlag);
            commandArgs.Add(BuildQuotedConfig(ApprovalPolicyConfigKey, args.ApprovalPolicy.Value.ToCliValue()));
        }

        if (args.AdditionalCliArguments is not null)
        {
            foreach (var argument in args.AdditionalCliArguments)
            {
                if (string.IsNullOrWhiteSpace(argument))
                {
                    continue;
                }

                commandArgs.Add(argument);
            }
        }

        if (!string.IsNullOrWhiteSpace(args.ThreadId))
        {
            commandArgs.Add(ResumeCommandName);
            commandArgs.Add(args.ThreadId);
        }

        if (args.Images is not null)
        {
            foreach (var image in args.Images)
            {
                commandArgs.Add(ImageFlag);
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
            environment[OpenAiBaseUrlEnv] = baseUrl;
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            environment[CodexApiKeyEnv] = apiKey;
        }

        return environment;
    }

    private static void AddRepeatedFlag(
        List<string> commandArgs,
        string flag,
        IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            commandArgs.Add(flag);
            commandArgs.Add(value);
        }
    }

    private static string BuildQuotedConfig(string key, string value) => $"{key}=\"{value}\"";

    private static string BuildBooleanConfig(string key, bool value)
    {
        var literal = value ? BooleanTrueLiteral : BooleanFalseLiteral;
        return $"{key}={literal}";
    }
}

internal sealed record CodexProcessInvocation(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    string Input);

internal interface ICodexProcessRunner
{
    IAsyncEnumerable<string> RunAsync(
        CodexProcessInvocation invocation,
        ILogger logger,
        CancellationToken cancellationToken);
}

internal sealed class DefaultCodexProcessRunner : ICodexProcessRunner
{
    public async IAsyncEnumerable<string> RunAsync(
        CodexProcessInvocation invocation,
        ILogger logger,
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

            while (!cancellationToken.IsCancellationRequested)
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
            TryKillProcess(process, invocation.ExecutablePath, logger);
        }
    }

    private static void TryKillProcess(Process process, string executablePath, ILogger logger)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception)
        {
            Logging.CodexExecLog.ProcessKillFailed(logger, executablePath, exception);
        }
    }
}

internal static class CliValueExtensions
{
    private const string SandboxReadOnly = "read-only";
    private const string SandboxWorkspaceWrite = "workspace-write";
    private const string SandboxDangerFullAccess = "danger-full-access";

    private const string ReasoningMinimal = "minimal";
    private const string ReasoningLow = "low";
    private const string ReasoningMedium = "medium";
    private const string ReasoningHigh = "high";
    private const string ReasoningXHigh = "xhigh";

    private const string WebSearchDisabled = "disabled";
    private const string WebSearchCached = "cached";
    private const string WebSearchLive = "live";

    private const string ApprovalNever = "never";
    private const string ApprovalOnRequest = "on-request";
    private const string ApprovalOnFailure = "on-failure";
    private const string ApprovalUntrusted = "untrusted";

    private const string OssProviderLmStudio = "lmstudio";
    private const string OssProviderOllama = "ollama";

    private const string ColorAlways = "always";
    private const string ColorNever = "never";
    private const string ColorAuto = "auto";

    public static string ToCliValue(this SandboxMode mode)
    {
        return mode switch
        {
            SandboxMode.ReadOnly => SandboxReadOnly,
            SandboxMode.WorkspaceWrite => SandboxWorkspaceWrite,
            SandboxMode.DangerFullAccess => SandboxDangerFullAccess,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static string ToCliValue(this ModelReasoningEffort effort)
    {
        return effort switch
        {
            ModelReasoningEffort.Minimal => ReasoningMinimal,
            ModelReasoningEffort.Low => ReasoningLow,
            ModelReasoningEffort.Medium => ReasoningMedium,
            ModelReasoningEffort.High => ReasoningHigh,
            ModelReasoningEffort.XHigh => ReasoningXHigh,
            _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null),
        };
    }

    public static string ToCliValue(this WebSearchMode mode)
    {
        return mode switch
        {
            WebSearchMode.Disabled => WebSearchDisabled,
            WebSearchMode.Cached => WebSearchCached,
            WebSearchMode.Live => WebSearchLive,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static string ToCliValue(this ApprovalMode mode)
    {
        return mode switch
        {
            ApprovalMode.Never => ApprovalNever,
            ApprovalMode.OnRequest => ApprovalOnRequest,
            ApprovalMode.OnFailure => ApprovalOnFailure,
            ApprovalMode.Untrusted => ApprovalUntrusted,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    public static string ToCliValue(this OssProvider provider)
    {
        return provider switch
        {
            OssProvider.LmStudio => OssProviderLmStudio,
            OssProvider.Ollama => OssProviderOllama,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
    }

    public static string ToCliValue(this ExecOutputColor color)
    {
        return color switch
        {
            ExecOutputColor.Always => ColorAlways,
            ExecOutputColor.Never => ColorNever,
            ExecOutputColor.Auto => ColorAuto,
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
        };
    }
}
