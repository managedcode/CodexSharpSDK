using ManagedCode.CodexSharpSDK.Extensions.AI.Content;
using ManagedCode.CodexSharpSDK.Extensions.AI.Internal;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Tests;

public class ChatResponseMapperTests
{
    [Test]
    public async Task ToChatResponse_BasicResult_MapsCorrectly()
    {
        var result = new RunResult([], "Hello from Codex", new Usage(100, 10, 50));
        var response = ChatResponseMapper.ToChatResponse(result, "thread-123");

        await Assert.That(response.Text).Contains("Hello from Codex");
        await Assert.That(response.ConversationId).IsEqualTo("thread-123");
        await Assert.That(response.Usage).IsNotNull();
        await Assert.That(response.Usage!.InputTokenCount).IsEqualTo(100);
        await Assert.That(response.Usage!.OutputTokenCount).IsEqualTo(50);
        await Assert.That(response.Usage!.TotalTokenCount).IsEqualTo(150);
    }

    [Test]
    public async Task ToChatResponse_NullUsage_NoUsageSet()
    {
        var result = new RunResult([], "Response", null);
        var response = ChatResponseMapper.ToChatResponse(result, null);
        await Assert.That(response.Usage).IsNull();
        await Assert.That(response.ConversationId).IsNull();
    }

    [Test]
    public async Task ToChatResponse_WithReasoningItem_MapsToTextReasoningContent()
    {
        var items = new List<ThreadItem>
        {
            new ReasoningItem("r1", "thinking about this..."),
        };
        var result = new RunResult(items, "Final answer", null);
        var response = ChatResponseMapper.ToChatResponse(result, null);

        var contents = response.Messages[0].Contents;
        await Assert.That(contents.OfType<TextReasoningContent>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task ToChatResponse_WithCommandExecution_MapsToCustomContent()
    {
        var items = new List<ThreadItem>
        {
            new CommandExecutionItem("c1", "npm test", "all passed", 0, CommandExecutionStatus.Completed),
        };
        var result = new RunResult(items, "Done", null);
        var response = ChatResponseMapper.ToChatResponse(result, null);

        var cmdContent = response.Messages[0].Contents.OfType<CommandExecutionContent>().Single();
        await Assert.That(cmdContent.Command).IsEqualTo("npm test");
        await Assert.That(cmdContent.ExitCode).IsEqualTo(0);
    }

    [Test]
    public async Task ToChatResponse_WithFileChange_MapsToCustomContent()
    {
        var items = new List<ThreadItem>
        {
            new FileChangeItem("f1", [new FileUpdateChange("src/app.cs", PatchChangeKind.Update)], PatchApplyStatus.Completed),
        };
        var result = new RunResult(items, "Fixed", null);
        var response = ChatResponseMapper.ToChatResponse(result, null);

        var fileContent = response.Messages[0].Contents.OfType<FileChangeContent>().Single();
        await Assert.That(fileContent.Changes).Count().IsEqualTo(1);
    }

    [Test]
    public async Task ToChatResponse_CachedTokens_IncludedInUsage()
    {
        var result = new RunResult([], "Response", new Usage(100, 50, 25));
        var response = ChatResponseMapper.ToChatResponse(result, null);
        await Assert.That(response.Usage!.CachedInputTokenCount).IsEqualTo(50);
    }
}
