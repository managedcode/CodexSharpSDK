using System.Runtime.CompilerServices;
using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Extensions.AI.Internal;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI;

public sealed class CodexChatClient : IChatClient
{
    private readonly CodexClient _client;
    private readonly CodexChatClientOptions _options;

    public CodexChatClient(CodexChatClientOptions? options = null)
    {
        _options = options ?? new CodexChatClientOptions();
        _client = new CodexClient(new CodexClientOptions
        {
            CodexOptions = _options.CodexOptions,
            AutoStart = true,
        });
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var (prompt, imageContents) = ChatMessageMapper.ToCodexInput(messages);
        var threadOptions = ChatOptionsMapper.ToThreadOptions(options, _options);
        var turnOptions = ChatOptionsMapper.ToTurnOptions(options, cancellationToken);

        var thread = options?.ConversationId is { } threadId
            ? _client.ResumeThread(threadId, threadOptions)
            : _client.StartThread(threadOptions);

        using (thread)
        {
            var userInput = ChatMessageMapper.BuildUserInput(prompt, imageContents);
            var result = await thread.RunAsync(userInput, turnOptions).ConfigureAwait(false);
            return ChatResponseMapper.ToChatResponse(result, thread.Id);
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var (prompt, imageContents) = ChatMessageMapper.ToCodexInput(messages);
        var threadOptions = ChatOptionsMapper.ToThreadOptions(options, _options);
        var turnOptions = ChatOptionsMapper.ToTurnOptions(options, cancellationToken);

        var thread = options?.ConversationId is { } threadId
            ? _client.ResumeThread(threadId, threadOptions)
            : _client.StartThread(threadOptions);

        using (thread)
        {
            var userInput = ChatMessageMapper.BuildUserInput(prompt, imageContents);
            var streamed = await thread.RunStreamedAsync(userInput, turnOptions)
                .ConfigureAwait(false);

            await foreach (var update in StreamingEventMapper.ToUpdates(streamed.Events, cancellationToken)
                               .WithCancellation(cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return update;
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(ChatClientMetadata))
        {
            return new ChatClientMetadata(
                providerName: "CodexCLI",
                providerUri: null,
                defaultModelId: _options.DefaultModel);
        }

        if (serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        return null;
    }

    public void Dispose() => _client.Dispose();
}
