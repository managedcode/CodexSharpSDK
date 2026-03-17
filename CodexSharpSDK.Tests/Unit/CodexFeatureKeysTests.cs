using System.Reflection;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

public class CodexFeatureKeysTests
{
    [Test]
    public async Task ToolCallMcpElicitation_HasCorrectValue()
    {
        var key = CodexFeatureKeys.ToolCallMcpElicitation;
        await Assert.That(key).IsEqualTo("tool_call_mcp_elicitation");
    }

    [Test]
    public async Task AllFeatureKeys_AreNonEmptyStrings()
    {
        var keys = GetAllFeatureKeyValues();
        foreach (var key in keys)
        {
            await Assert.That(string.IsNullOrWhiteSpace(key)).IsFalse();
        }
    }

    [Test]
    public async Task AllFeatureKeys_AreUnique()
    {
        var keys = GetAllFeatureKeyValues();
        var duplicates = keys
            .GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        await Assert.That(duplicates).IsEmpty();
    }

    private static string[] GetAllFeatureKeyValues()
    {
        return typeof(CodexFeatureKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false, FieldType: not null } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();
    }
}
