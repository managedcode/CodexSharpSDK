namespace AotSmoke;

using System.Text.Json.Serialization;
using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Extensions.AI;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;
using CodexRunResult = ManagedCode.CodexSharpSDK.Models.RunResult<Payload>;

internal static class Program
{
    public static void Main()
    {
        var schema = StructuredOutputSchema.Map<Payload>(properties: [(payload => payload.Value, StructuredOutputSchema.PlainText())]);
        using var client = new CodexChatClient();
        Func<CodexThread, Task<CodexRunResult>> typedRun = thread => thread.RunAsync<Payload>("AOT smoke", schema, AotJsonContext.Default.Payload);
        Func<IChatClient, IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> response =
            (chatClient, messages, options, token) => chatClient.GetResponseAsync(messages, options, token);
        Func<IChatClient, IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> stream =
            (chatClient, messages, options, token) => chatClient.GetStreamingResponseAsync(messages, options, token);
        GC.KeepAlive(typedRun);
        GC.KeepAlive(response);
        GC.KeepAlive(stream);
        Console.WriteLine(AotJsonContext.Default.Payload.Type.Name);
    }
}

internal sealed class Payload { public string? Value { get; set; } }
[JsonSerializable(typeof(Payload))]
internal sealed partial class AotJsonContext : JsonSerializerContext;
