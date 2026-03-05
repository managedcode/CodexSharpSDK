using System.Text.Json.Nodes;
using ManagedCode.CodexSharp.Internal;

namespace ManagedCode.CodexSharp.Tests;

public class TomlConfigSerializerTests
{
    [Test]
    public async Task Serialize_FlattensNestedObjectsAndArrays()
    {
        var config = new JsonObject
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
            ["empty_object"] = new JsonObject(),
            ["complex keys"] = new JsonObject
            {
                ["my-key"] = "value",
            },
        };

        var overrides = TomlConfigSerializer.Serialize(config);

        await Assert.That(overrides).Contains("approval_policy=\"never\"");
        await Assert.That(overrides).Contains("sandbox_workspace_write.network_access=true");
        await Assert.That(overrides).Contains("retry_budget=3");
        await Assert.That(overrides).Contains("tool_rules.allow=[\"git status\", \"git diff\"]");
        await Assert.That(overrides).Contains("empty_object={}");
        await Assert.That(overrides).Contains("complex keys.my-key=\"value\"");
    }

    [Test]
    public async Task Serialize_ReturnsEmptyForEmptyRoot()
    {
        var overrides = TomlConfigSerializer.Serialize(new JsonObject());
        await Assert.That(overrides).IsEmpty();
    }

    [Test]
    public async Task Serialize_ThrowsForNullArrayItem()
    {
        var config = new JsonObject
        {
            ["bad"] = new JsonArray(1, null),
        };

        var action = () => TomlConfigSerializer.Serialize(config);
        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception!.Message).Contains("cannot be null");
    }

    [Test]
    public async Task Serialize_ThrowsForNonFiniteNumber()
    {
        var config = new JsonObject
        {
            ["bad"] = JsonNode.Parse("1e999")!,
        };

        var action = () => TomlConfigSerializer.Serialize(config);
        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception!.Message).Contains("finite number");
    }
}
