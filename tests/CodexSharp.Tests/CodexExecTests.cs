using System.Text.Json.Nodes;
using ManagedCode.CodexSharp.Tests.Fakes;

namespace ManagedCode.CodexSharp.Tests;

public class CodexExecTests
{
    [Test]
    public async Task RunAsync_BuildsCommandLineWithExpectedOrder()
    {
        var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines([], cancellationToken));
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
            },
            processRunner: runner);

        var args = new CodexExecArgs
        {
            Input = "test prompt",
            Model = "gpt-5",
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

        await DrainAsync(exec.RunAsync(args));

        var invocation = runner.Invocations.Single();
        var commandArgs = invocation.Arguments;

        await Assert.That(commandArgs[0]).IsEqualTo("exec");
        await Assert.That(commandArgs[1]).IsEqualTo("--experimental-json");
        await Assert.That(ContainsPair(commandArgs, "--model", "gpt-5")).IsTrue();
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
    public async Task RunAsync_UsesWebSearchEnabledWhenModeMissing()
    {
        var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines([], cancellationToken));
        var exec = new CodexExec("codex", null, null, runner);

        await DrainAsync(exec.RunAsync(new CodexExecArgs
        {
            Input = "test",
            WebSearchEnabled = false,
        }));

        var configValues = CollectConfigValues(runner.Invocations.Single().Arguments, "web_search");
        await Assert.That(configValues).IsEquivalentTo(["web_search=\"disabled\""]);
    }

    [Test]
    public async Task RunAsync_WebSearchModeOverridesLegacyFlag()
    {
        var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines([], cancellationToken));
        var exec = new CodexExec("codex", null, null, runner);

        await DrainAsync(exec.RunAsync(new CodexExecArgs
        {
            Input = "test",
            WebSearchMode = WebSearchMode.Live,
            WebSearchEnabled = false,
        }));

        var configValues = CollectConfigValues(runner.Invocations.Single().Arguments, "web_search");
        await Assert.That(configValues).IsEquivalentTo(["web_search=\"live\""]);
    }

    [Test]
    public async Task RunAsync_UsesProvidedEnvironmentWithoutLeakingProcessEnvironment()
    {
        Environment.SetEnvironmentVariable("CODEX_SHOULD_NOT_LEAK", "leak");

        try
        {
            var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines([], cancellationToken));
            var exec = new CodexExec(
                executablePath: "codex",
                environmentOverride: new Dictionary<string, string>
                {
                    ["CUSTOM_ENV"] = "custom",
                },
                configOverrides: null,
                processRunner: runner);

            await DrainAsync(exec.RunAsync(new CodexExecArgs
            {
                Input = "test",
                BaseUrl = "https://example.local",
                ApiKey = "secret",
            }));

            var env = runner.Invocations.Single().Environment;
            await Assert.That(env["CUSTOM_ENV"]).IsEqualTo("custom");
            await Assert.That(env.ContainsKey("CODEX_SHOULD_NOT_LEAK")).IsFalse();
            await Assert.That(env["OPENAI_BASE_URL"]).IsEqualTo("https://example.local");
            await Assert.That(env["CODEX_API_KEY"]).IsEqualTo("secret");
            await Assert.That(env["CODEX_INTERNAL_ORIGINATOR_OVERRIDE"]).IsEqualTo("codex_sdk_csharp");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_SHOULD_NOT_LEAK", null);
        }
    }

    [Test]
    public async Task RunAsync_InheritsEnvironmentWhenOverrideMissing()
    {
        Environment.SetEnvironmentVariable("CODEX_SHOULD_INHERIT", "yes");

        try
        {
            var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines([], cancellationToken));
            var exec = new CodexExec("codex", null, null, runner);

            await DrainAsync(exec.RunAsync(new CodexExecArgs { Input = "test" }));

            var env = runner.Invocations.Single().Environment;
            await Assert.That(env["CODEX_SHOULD_INHERIT"]).IsEqualTo("yes");
            await Assert.That(env.ContainsKey("CODEX_INTERNAL_ORIGINATOR_OVERRIDE")).IsTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_SHOULD_INHERIT", null);
        }
    }

    [Test]
    public async Task RunAsync_ThrowsWhenConfigContainsEmptyKey()
    {
        var runner = new FakeCodexProcessRunner((_, cancellationToken) => FakeCodexProcessRunner.YieldLines([], cancellationToken));
        var exec = new CodexExec(
            executablePath: "codex",
            environmentOverride: null,
            configOverrides: new JsonObject { [""] = "value" },
            processRunner: runner);

        var action = async () => await DrainAsync(exec.RunAsync(new CodexExecArgs { Input = "test" }));

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception!.Message).Contains("non-empty strings");
        await Assert.That(runner.Invocations).IsEmpty();
    }

    private static async Task DrainAsync(IAsyncEnumerable<string> lines)
    {
        await foreach (var _ in lines)
        {
            // Intentionally empty.
        }
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
