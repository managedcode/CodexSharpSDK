using ManagedCode.CodexSharpSDK.Extensions.AI.Internal;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Tests;

public class ChatMessageMapperTests
{
    [Test]
    public async Task ToCodexInput_TextOnly_ReturnsPrompt()
    {
        var messages = new[] { new ChatMessage(ChatRole.User, "Hello world") };
        var (prompt, images) = ChatMessageMapper.ToCodexInput(messages);
        await Assert.That(prompt).IsEqualTo("Hello world");
        await Assert.That(images).Count().IsEqualTo(0);
    }

    [Test]
    public async Task ToCodexInput_SystemAndUser_PrependsSystemPrefix()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.System, "You are helpful"),
            new ChatMessage(ChatRole.User, "Help me"),
        };
        var (prompt, _) = ChatMessageMapper.ToCodexInput(messages);
        await Assert.That(prompt).Contains("[System] You are helpful");
        await Assert.That(prompt).Contains("Help me");
    }

    [Test]
    public async Task ToCodexInput_AssistantMessage_AppendsAssistantPrefix()
    {
        var messages = new[]
        {
            new ChatMessage(ChatRole.User, "Question"),
            new ChatMessage(ChatRole.Assistant, "Answer"),
            new ChatMessage(ChatRole.User, "Follow up"),
        };
        var (prompt, _) = ChatMessageMapper.ToCodexInput(messages);
        await Assert.That(prompt).Contains("[Assistant] Answer");
        await Assert.That(prompt).Contains("Follow up");
    }

    [Test]
    public async Task ToCodexInput_ImageContent_ExtractedSeparately()
    {
        var imageData = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        var messages = new[]
        {
            new ChatMessage(ChatRole.User,
            [
                new TextContent("Describe this"),
                new DataContent(imageData, "image/png"),
            ]),
        };
        var (prompt, images) = ChatMessageMapper.ToCodexInput(messages);
        await Assert.That(prompt).Contains("Describe this");
        await Assert.That(images).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ToCodexInput_EmptyMessages_ReturnsEmpty()
    {
        var (prompt, images) = ChatMessageMapper.ToCodexInput([]);
        await Assert.That(prompt).IsEqualTo(string.Empty);
        await Assert.That(images).Count().IsEqualTo(0);
    }

    [Test]
    public async Task BuildUserInput_NoImages_ReturnsSingleTextInput()
    {
        var result = ChatMessageMapper.BuildUserInput("Hello", []);
        await Assert.That(result).Count().IsEqualTo(1);
        await Assert.That(result[0]).IsTypeOf<TextInput>();
    }

    [Test]
    public async Task BuildUserInput_WithImages_ReturnsTextAndImageInputs()
    {
        var imageData = new byte[] { 0xFF, 0xD8, 0xFF }; // JPEG header
        var images = new List<DataContent> { new(imageData, "image/jpeg") };
        var result = ChatMessageMapper.BuildUserInput("Look at this", images);
        await Assert.That(result.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(result[0]).IsTypeOf<TextInput>();
    }
}
