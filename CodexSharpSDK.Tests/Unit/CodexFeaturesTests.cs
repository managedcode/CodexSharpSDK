using System.Reflection;
using System.Text.Json;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

public class CodexFeaturesTests
{
    private const string SolutionFileName = "ManagedCode.CodexSharpSDK.slnx";
    private const string BundledConfigSchemaFileName = "config.schema.json";

    [Test]
    public async Task CodexFeatures_NewUpstreamFlags_ArePresent()
    {
        // These two flags were added in upstream commit 3b5fe5c and are the primary
        // motivation for this sync. Verify the constants are present and map to the correct
        // upstream key strings by resolving them via reflection (avoiding a constant-vs-constant
        // comparison that the analyzer rightly flags as a no-op assertion).
        var sdkValues = GetSdkFeatureValues();
        await Assert.That(sdkValues).Contains("guardian_approval");
        await Assert.That(sdkValues).Contains("tool_call_mcp_elicitation");
    }

    [Test]
    public async Task CodexFeatures_AllConstantsAreValidUpstreamFeatureKeys()
    {
        var schemaFeatureKeys = await ReadBundledSchemaFeatureKeysAsync();
        var sdkFeatureValues = GetSdkFeatureValues();
        var invalidKeys = sdkFeatureValues
            .Except(schemaFeatureKeys, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(invalidKeys).IsEmpty();
    }

    [Test]
    public async Task CodexFeatures_CoversAllCanonicalUpstreamFeatureKeys()
    {
        // The canonical (non-alias) keys from features.rs must all have an SDK constant so
        // that callers can reference them without magic strings.
        var schemaFeatureKeys = await ReadBundledSchemaFeatureKeysAsync();
        var sdkFeatureValues = GetSdkFeatureValues();

        // Legacy alias keys that exist in config.schema.json but are NOT canonical feature
        // keys in features.rs; they are intentionally excluded from CodexFeatures.
        var knownAliases = new HashSet<string>(StringComparer.Ordinal)
        {
            "collab",
            "connectors",
            "enable_experimental_windows_sandbox",
            "experimental_use_freeform_apply_patch",
            "experimental_use_unified_exec_tool",
            "include_apply_patch_tool",
            "memory_tool",
            "web_search",
        };

        var canonicalKeys = schemaFeatureKeys
            .Except(knownAliases, StringComparer.Ordinal)
            .ToArray();

        var missingKeys = canonicalKeys
            .Except(sdkFeatureValues, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(missingKeys).IsEmpty();
    }

    private static string[] GetSdkFeatureValues()
    {
        return typeof(CodexFeatures)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();
    }

    private static async Task<string[]> ReadBundledSchemaFeatureKeysAsync()
    {
        var schemaPath = ResolveBundledConfigSchemaFilePath();
        using var stream = File.OpenRead(schemaPath);
        using var document = await JsonDocument.ParseAsync(stream);

        return document.RootElement
            .GetProperty("properties")
            .GetProperty("features")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(p => p.Name)
            .ToArray();
    }

    private static string ResolveBundledConfigSchemaFilePath()
    {
        return Path.Combine(
            ResolveRepositoryRootPath(),
            "submodules",
            "openai-codex",
            "codex-rs",
            "core",
            BundledConfigSchemaFileName);
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
}
