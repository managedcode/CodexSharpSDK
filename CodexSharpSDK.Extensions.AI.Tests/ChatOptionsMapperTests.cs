using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Extensions.AI.Internal;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Tests;

public class ChatOptionsMapperTests
{
    [Test]
    public async Task ToThreadOptions_NullOptions_UsesDefaults()
    {
        var clientOptions = new CodexChatClientOptions { DefaultModel = "test-model" };
        var result = ChatOptionsMapper.ToThreadOptions(null, clientOptions);
        await Assert.That(result.Model).IsEqualTo("test-model");
    }

    [Test]
    public async Task ToThreadOptions_ModelId_MapsToModel()
    {
        var chatOptions = new ChatOptions { ModelId = "gpt-5" };
        var clientOptions = new CodexChatClientOptions { DefaultModel = "default" };
        var result = ChatOptionsMapper.ToThreadOptions(chatOptions, clientOptions);
        await Assert.That(result.Model).IsEqualTo("gpt-5");
    }

    [Test]
    public async Task ToThreadOptions_AdditionalProperties_MapsCodexKeys()
    {
        var chatOptions = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatOptionsMapper.SandboxModeKey] = SandboxMode.WorkspaceWrite,
                [ChatOptionsMapper.FullAutoKey] = true,
                [ChatOptionsMapper.ProfileKey] = "strict",
                [ChatOptionsMapper.ReasoningEffortKey] = ModelReasoningEffort.High,
            },
        };
        var result = ChatOptionsMapper.ToThreadOptions(chatOptions, new CodexChatClientOptions());
        await Assert.That(result.SandboxMode).IsEqualTo(SandboxMode.WorkspaceWrite);
        await Assert.That(result.FullAuto).IsTrue();
        await Assert.That(result.Profile).IsEqualTo("strict");
        await Assert.That(result.ModelReasoningEffort).IsEqualTo(ModelReasoningEffort.High);
    }

    [Test]
    public async Task ToTurnOptions_SetsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var result = ChatOptionsMapper.ToTurnOptions(null, cts.Token);
        await Assert.That(result.CancellationToken).IsEqualTo(cts.Token);
    }
}
