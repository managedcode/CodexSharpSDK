using System.Text.Json;
using ManagedCode.CodexSharpSDK.Internal;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

public class CodexCliMetadataReaderTests
{
    [Test]
    public async Task ParseInstalledVersion_ReturnsVersionTokenForCodexCliOutput()
    {
        const string versionOutput = "codex-cli 0.110.0";

        var parsed = CodexCliMetadataReader.ParseInstalledVersion(versionOutput);

        await Assert.That(parsed).IsEqualTo("0.110.0");
    }

    [Test]
    public async Task ParseLatestPublishedVersion_ReturnsVersionTokenForNpmOutput()
    {
        const string npmOutput = "0.111.0";

        var parsed = CodexCliMetadataReader.ParseLatestPublishedVersion(npmOutput);

        await Assert.That(parsed).IsEqualTo("0.111.0");
    }

    [Test]
    public async Task ParseLatestPublishedVersion_ExtractsVersionFromNoisyOutput()
    {
        const string npmOutput = """
                                 npm notice
                                 codex-cli release: "0.112.0-beta.1"
                                 """;

        var parsed = CodexCliMetadataReader.ParseLatestPublishedVersion(npmOutput);

        await Assert.That(parsed).IsEqualTo("0.112.0-beta.1");
    }

    [Test]
    public async Task IsNewerVersion_ReturnsTrueForHigherStableVersion()
    {
        var isNewer = CodexCliMetadataReader.IsNewerVersion("0.111.0", "0.110.0");

        await Assert.That(isNewer).IsTrue();
    }

    [Test]
    public async Task IsNewerVersion_ReturnsFalseForLowerVersion()
    {
        var isNewer = CodexCliMetadataReader.IsNewerVersion("0.110.0", "0.111.0");

        await Assert.That(isNewer).IsFalse();
    }

    [Test]
    public async Task IsNewerVersion_ReturnsFalseWhenLatestIsPrereleaseAndInstalledIsStable()
    {
        var isNewer = CodexCliMetadataReader.IsNewerVersion("0.111.0-beta.1", "0.111.0");

        await Assert.That(isNewer).IsFalse();
    }

    [Test]
    public async Task ResolveUpdateCommand_ReturnsBunCommand_ForBunManagedPath()
    {
        const string executablePath = "/Users/example/.bun/bin/codex";

        var command = CodexCliMetadataReader.ResolveUpdateCommand(executablePath);

        await Assert.That(command).IsEqualTo("bun add --global @openai/codex@latest");
    }

    [Test]
    public async Task ResolveUpdateCommand_ReturnsBunCommand_ForBunUserAgent()
    {
        const string executablePath = "/usr/local/bin/codex";

        var command = CodexCliMetadataReader.ResolveUpdateCommand(
            executablePath,
            npmUserAgent: "bun/1.2.0 npm/? node/v20.0.0");

        await Assert.That(command).IsEqualTo("bun add --global @openai/codex@latest");
    }

    [Test]
    public async Task ResolveUpdateCommand_ReturnsNpmCommand_ByDefault()
    {
        const string executablePath = "/usr/local/bin/codex";

        var command = CodexCliMetadataReader.ResolveUpdateCommand(executablePath, npmUserAgent: "npm/10.0.0");

        await Assert.That(command).IsEqualTo("npm install --global @openai/codex@latest");
    }

    [Test]
    public async Task ParseDefaultModelFromTomlLines_UsesTopLevelModelOnly()
    {
        string[] lines =
        [
            "model = \"gpt-5.3-codex\"",
            "",
            "[profiles.fast]",
            "model = \"gpt-5.2-codex\"",
        ];

        var parsed = CodexCliMetadataReader.ParseDefaultModelFromTomlLines(lines);

        await Assert.That(parsed).IsEqualTo("gpt-5.3-codex");
    }

    [Test]
    public async Task ParseDefaultModelFromTomlLines_HandlesInlineComments()
    {
        string[] lines =
        [
            "model = \"gpt-5.3-codex\" # default model",
        ];

        var parsed = CodexCliMetadataReader.ParseDefaultModelFromTomlLines(lines);

        await Assert.That(parsed).IsEqualTo("gpt-5.3-codex");
    }

    [Test]
    public async Task ParseModelsCache_ParsesListedAndHiddenModels()
    {
        const string json = """
                            {
                              "models": [
                                {
                                  "slug": "gpt-5.3-codex",
                                  "display_name": "gpt-5.3-codex",
                                  "description": "Latest frontier agentic coding model.",
                                  "visibility": "list",
                                  "supported_in_api": true,
                                  "supported_reasoning_levels": [
                                    { "effort": "low" },
                                    { "effort": "high" }
                                  ]
                                },
                                {
                                  "slug": "gpt-5.1-codex",
                                  "display_name": "gpt-5.1-codex",
                                  "visibility": "hidden",
                                  "supported_in_api": false,
                                  "supported_reasoning_levels": []
                                },
                                {
                                  "display_name": "missing-slug"
                                }
                              ]
                            }
                            """;

        using var document = JsonDocument.Parse(json);
        var parsed = CodexCliMetadataReader.ParseModelsCache(document.RootElement);

        await Assert.That(parsed).HasCount(2);
        await Assert.That(parsed[0].Slug).IsEqualTo("gpt-5.3-codex");
        await Assert.That(parsed[0].IsListed).IsTrue();
        await Assert.That(parsed[0].IsApiSupported).IsTrue();
        await Assert.That(parsed[0].SupportedReasoningEfforts).IsEquivalentTo(["low", "high"]);

        await Assert.That(parsed[1].Slug).IsEqualTo("gpt-5.1-codex");
        await Assert.That(parsed[1].IsListed).IsFalse();
        await Assert.That(parsed[1].IsApiSupported).IsFalse();
    }
}
