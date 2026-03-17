using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Execution;
using ManagedCode.CodexSharpSDK.Models;
using ManagedCode.CodexSharpSDK.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

public class CodexExecTests
{
    [Test]
    public async Task BuildCommandArgs_BuildsCommandLineWithExpectedOrder()
    {
        var exec = new CodexExec(
            executablePath: "codex",
            environmentOverride: null,
            configOverrides: new JsonObject
            {
                ["approval_policy"] = "never",
                ["sandbox_workspace_write"] = new JsonObject
                {
                    ["network_access"] = true,
                },
                ["retry_budget"] = 3,
                ["tool_rules"] = new JsonObject
                {
                    ["allow"] = new JsonArray("git status", "git diff"),
                },
            });

        var args = new CodexExecArgs
        {
            Input = "test prompt",
            Model = CodexModels.Gpt53Codex,
            SandboxMode = SandboxMode.WorkspaceWrite,
            WorkingDirectory = "/tmp/project",
            AdditionalDirectories = ["/tmp/shared", "/tmp/other"],
            SkipGitRepoCheck = true,
            OutputSchemaFile = "/tmp/schema.json",
            ModelReasoningEffort = ModelReasoningEffort.High,
            NetworkAccessEnabled = true,
            WebSearchMode = WebSearchMode.Cached,
            ApprovalPolicy = ApprovalMode.OnRequest,
            ThreadId = "thread_1",
            Images = ["first.png", "second.jpg"],
        };

        var commandArgs = exec.BuildCommandArgs(args);

        await Assert.That(commandArgs[0]).IsEqualTo("exec");
        await Assert.That(commandArgs[1]).IsEqualTo("--json");
        await Assert.That(ContainsPair(commandArgs, "--model", CodexModels.Gpt53Codex)).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--sandbox", "workspace-write")).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--cd", "/tmp/project")).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--output-schema", "/tmp/schema.json")).IsTrue();
        await Assert.That(commandArgs.Contains("--skip-git-repo-check")).IsTrue();

        await Assert.That(CollectConfigValues(commandArgs, "approval_policy"))
            .IsEquivalentTo(["approval_policy=\"never\"", "approval_policy=\"on-request\""]);
        await Assert.That(CollectConfigValues(commandArgs, "sandbox_workspace_write.network_access"))
            .IsEquivalentTo(["sandbox_workspace_write.network_access=true", "sandbox_workspace_write.network_access=true"]);
        await Assert.That(CollectConfigValues(commandArgs, "retry_budget"))
            .IsEquivalentTo(["retry_budget=3"]);
        await Assert.That(CollectConfigValues(commandArgs, "tool_rules.allow"))
            .IsEquivalentTo(["tool_rules.allow=[\"git status\", \"git diff\"]"]);
        await Assert.That(CollectConfigValues(commandArgs, "model_reasoning_effort"))
            .IsEquivalentTo(["model_reasoning_effort=\"high\""]);
        await Assert.That(CollectConfigValues(commandArgs, "web_search"))
            .IsEquivalentTo(["web_search=\"cached\""]);

        var addDirValues = CollectFlagValues(commandArgs, "--add-dir");
        await Assert.That(addDirValues).IsEquivalentTo(["/tmp/shared", "/tmp/other"]);

        var resumeIndex = commandArgs.IndexOf("resume");
        var firstImageIndex = commandArgs.IndexOf("--image");
        await Assert.That(resumeIndex).IsGreaterThan(-1);
        await Assert.That(firstImageIndex).IsGreaterThan(-1);
        await Assert.That(resumeIndex < firstImageIndex).IsTrue();
        await Assert.That(CollectFlagValues(commandArgs, "--image")).IsEquivalentTo(["first.png", "second.jpg"]);
    }

    [Test]
    public async Task BuildCommandArgs_UsesWebSearchEnabledWhenModeMissing()
    {
        var exec = new CodexExec("codex", null, null);

        var commandArgs = exec.BuildCommandArgs(new CodexExecArgs
        {
            Input = "test",
            WebSearchEnabled = false,
        });

        var configValues = CollectConfigValues(commandArgs, "web_search");
        await Assert.That(configValues).IsEquivalentTo(["web_search=\"disabled\""]);
    }

    [Test]
    public async Task BuildCommandArgs_WebSearchModeOverridesLegacyFlag()
    {
        var exec = new CodexExec("codex", null, null);

        var commandArgs = exec.BuildCommandArgs(new CodexExecArgs
        {
            Input = "test",
            WebSearchMode = WebSearchMode.Live,
            WebSearchEnabled = false,
        });

        var configValues = CollectConfigValues(commandArgs, "web_search");
        await Assert.That(configValues).IsEquivalentTo(["web_search=\"live\""]);
    }

    [Test]
    public async Task BuildCommandArgs_MapsExtendedCliFlags()
    {
        var exec = new CodexExec("codex", null, null);

        var commandArgs = exec.BuildCommandArgs(new CodexExecArgs
        {
            Input = "test",
            Profile = "strict",
            UseOss = true,
            LocalProvider = OssProvider.LmStudio,
            FullAuto = true,
            DangerouslyBypassApprovalsAndSandbox = true,
            Ephemeral = true,
            Color = ExecOutputColor.Never,
            ProgressCursor = true,
            OutputLastMessageFile = "/tmp/last-message.txt",
            EnabledFeatures = [CodexFeatureKeys.MultiAgent, CodexFeatureKeys.UnifiedExec],
            DisabledFeatures = [CodexFeatureKeys.Steer],
            AdditionalCliArguments = ["--some-future-flag", "custom-value"],
        });

        await Assert.That(ContainsPair(commandArgs, "--profile", "strict")).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--local-provider", "lmstudio")).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--color", "never")).IsTrue();
        await Assert.That(ContainsPair(commandArgs, "--output-last-message", "/tmp/last-message.txt")).IsTrue();

        await Assert.That(commandArgs.Contains("--oss")).IsTrue();
        await Assert.That(commandArgs.Contains("--full-auto")).IsTrue();
        await Assert.That(commandArgs.Contains("--dangerously-bypass-approvals-and-sandbox")).IsTrue();
        await Assert.That(commandArgs.Contains("--ephemeral")).IsTrue();
        await Assert.That(commandArgs.Contains("--progress-cursor")).IsTrue();

        await Assert.That(CollectFlagValues(commandArgs, "--enable")).IsEquivalentTo([CodexFeatureKeys.MultiAgent, CodexFeatureKeys.UnifiedExec]);
        await Assert.That(CollectFlagValues(commandArgs, "--disable")).IsEquivalentTo([CodexFeatureKeys.Steer]);

        await Assert.That(commandArgs.Contains("--some-future-flag")).IsTrue();
        await Assert.That(commandArgs.Contains("custom-value")).IsTrue();
    }

    [Test]
    public async Task BuildCommandArgs_KeepsConfiguredWebSearchWhenThreadOverridesMissing()
    {
        var exec = new CodexExec(
            executablePath: "codex",
            environmentOverride: null,
            configOverrides: new JsonObject
            {
                ["web_search"] = "disabled",
            });

        var commandArgs = exec.BuildCommandArgs(new CodexExecArgs
        {
            Input = "test",
        });

        var configValues = CollectConfigValues(commandArgs, "web_search");
        await Assert.That(configValues).IsEquivalentTo(["web_search=\"disabled\""]);
    }

    [Test]
    public async Task BuildEnvironment_UsesProvidedEnvironmentWithoutLeakingProcessEnvironment()
    {
        Environment.SetEnvironmentVariable("CODEX_SHOULD_NOT_LEAK", "leak");

        try
        {
            var exec = new CodexExec(
                executablePath: "codex",
                environmentOverride: new Dictionary<string, string>
                {
                    ["CUSTOM_ENV"] = "custom",
                },
                configOverrides: null);

            var environment = exec.BuildEnvironment("https://example.local", "secret");

            await Assert.That(environment["CUSTOM_ENV"]).IsEqualTo("custom");
            await Assert.That(environment.ContainsKey("CODEX_SHOULD_NOT_LEAK")).IsFalse();
            await Assert.That(environment["OPENAI_BASE_URL"]).IsEqualTo("https://example.local");
            await Assert.That(environment["CODEX_API_KEY"]).IsEqualTo("secret");
            await Assert.That(environment["CODEX_INTERNAL_ORIGINATOR_OVERRIDE"]).IsEqualTo("codex_sdk_csharp");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_SHOULD_NOT_LEAK", null);
        }
    }

    [Test]
    public async Task BuildEnvironment_InheritsEnvironmentWhenOverrideMissing()
    {
        Environment.SetEnvironmentVariable("CODEX_SHOULD_INHERIT", "yes");

        try
        {
            var exec = new CodexExec("codex", null, null);
            var environment = exec.BuildEnvironment(null, null);

            await Assert.That(environment["CODEX_SHOULD_INHERIT"]).IsEqualTo("yes");
            await Assert.That(environment.ContainsKey("CODEX_INTERNAL_ORIGINATOR_OVERRIDE")).IsTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_SHOULD_INHERIT", null);
        }
    }

    [Test]
    public async Task BuildCommandArgs_ThrowsWhenConfigContainsEmptyKey()
    {
        var exec = new CodexExec(
            executablePath: "codex",
            environmentOverride: null,
            configOverrides: new JsonObject { [""] = "value" });

        var action = () => exec.BuildCommandArgs(new CodexExecArgs { Input = "test" });

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception!.Message).Contains("non-empty strings");
    }

    [Test]
    public async Task RunAsync_WithNullLogger_StillPropagatesFailure()
    {
        var missingExecutable = Path.Combine(
            Environment.CurrentDirectory,
            "tests",
            ".sandbox",
            $"missing-codex-{Guid.NewGuid():N}",
            "codex");

        var exec = new CodexExec(missingExecutable, null, null, NullLogger.Instance);

        var action = async () => await DrainAsync(exec.RunAsync(new CodexExecArgs { Input = "test" }));

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("Failed to start Codex CLI");
    }

    [Test]
    [Property("RequiresCodexAuth", "true")]
    public async Task RunAsync_WithNullLogger_CompletesSuccessfully_WithRealCodexCli()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        var exec = new CodexExec(logger: NullLogger.Instance);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var lines = await DrainToListAsync(exec.RunAsync(new CodexExecArgs
        {
            Input = "Reply with short plain text: ok.",
            Model = settings.Model,
            ModelReasoningEffort = ModelReasoningEffort.Medium,
            WebSearchMode = WebSearchMode.Disabled,
            SandboxMode = SandboxMode.WorkspaceWrite,
            NetworkAccessEnabled = true,
            CancellationToken = cancellation.Token,
        }));

        await Assert.That(lines.Count).IsGreaterThan(0);
        await Assert.That(lines.Any(line => line.Contains("\"type\":\"turn.completed\"", StringComparison.Ordinal))).IsTrue();
    }

    private static async Task DrainAsync(IAsyncEnumerable<string> lines)
    {
        await foreach (var _ in lines)
        {
            // Intentionally empty.
        }
    }

    private static async Task<List<string>> DrainToListAsync(IAsyncEnumerable<string> lines)
    {
        var result = new List<string>();

        await foreach (var line in lines)
        {
            result.Add(line);
        }

        return result;
    }

    private static bool ContainsPair(IReadOnlyList<string> args, string key, string value)
    {
        for (var index = 0; index < args.Count - 1; index += 1)
        {
            if (args[index] == key && args[index + 1] == value)
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> CollectConfigValues(IReadOnlyList<string> args, string key)
    {
        var result = new List<string>();
        for (var index = 0; index < args.Count - 1; index += 1)
        {
            if (args[index] == "--config" && args[index + 1].StartsWith($"{key}=", StringComparison.Ordinal))
            {
                result.Add(args[index + 1]);
            }
        }

        return result;
    }

    private static List<string> CollectFlagValues(IReadOnlyList<string> args, string flag)
    {
        var result = new List<string>();
        for (var index = 0; index < args.Count - 1; index += 1)
        {
            if (args[index] == flag)
            {
                result.Add(args[index + 1]);
            }
        }

        return result;
    }
}
