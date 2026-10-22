using ManagedCode.CodexSharpSDK.Configuration;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Tests;

public sealed class SharedChatClientConcurrencyTests
{
    private const string FixtureFileName = "shared-client-concurrency.js";
    private const string TestDirectoryName = "tests";
    private const string SandboxDirectoryName = ".sandbox";
    private const string ActiveMarkerSearchPattern = "*.active";
    private const string GuidFormat = "N";
    private const string MarkerDirectoryEnvironmentName = "CODEX_SHARED_CLIENT_MARKERS";
    private const string FirstPrompt = "independent fresh chat alpha";
    private const string SecondPrompt = "independent fresh chat beta";
    private const string NodeScript = """
        const fs = require('node:fs');
        const path = require('node:path');
        const markerDirectory = process.env.CODEX_SHARED_CLIENT_MARKERS;
        const markerPath = path.join(markerDirectory, `${process.pid}.active`);
        fs.writeFileSync(markerPath, 'active');
        const threadId = String(process.pid);
        const send = value => console.log(JSON.stringify(value));
        require('node:readline').createInterface({input:process.stdin}).on('line', line => {
            const request = JSON.parse(line);
            if (request.method === 'initialize') send({id:request.id,result:{}});
            if (request.method === 'thread/start') send({id:request.id,result:{thread:{id:threadId}}});
            if (request.method !== 'turn/start') return;
            const startedAt = Date.now();
            const waitCell = new Int32Array(new SharedArrayBuffer(4));
            while (Date.now() - startedAt < 5000) {
                if (fs.readdirSync(markerDirectory).filter(name => name.endsWith('.active')).length >= 2) break;
                Atomics.wait(waitCell, 0, 0, 20);
            }
            if (fs.readdirSync(markerDirectory).filter(name => name.endsWith('.active')).length < 2) process.exit(31);
            const text = request.params.input[0].text;
            send({id:request.id,result:{turn:{id:'turn'}}});
            send({method:'item/agentMessage/delta',params:{threadId,itemId:threadId,delta:text}});
            send({method:'item/completed',params:{threadId,item:{id:threadId,type:'agentMessage',text}}});
            send({method:'turn/completed',params:{threadId,turn:{id:'turn',status:'completed'}}});
        });
        """;

    [Test]
    public async Task SharedClient_ConcurrentCallsUseIndependentFreshChats()
    {
        var directory = CreateSandboxDirectory();
        var scriptPath = Path.Combine(directory, FixtureFileName);
        await File.WriteAllTextAsync(scriptPath, NodeScript);

        try
        {
            using var client = new CodexChatClient(new CodexChatClientOptions
            {
                CodexOptions = new CodexOptions
                {
                    CodexExecutablePath = scriptPath,
                    InheritEnvironmentVariables = true,
                    EnvironmentVariables = new Dictionary<string, string>
                    {
                        [MarkerDirectoryEnvironmentName] = directory,
                    },
                },
            });
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

            var responses = await Task.WhenAll(
                CollectUpdatesAsync(client, FirstPrompt, timeout.Token, new ChatOptions { ToolMode = ChatToolMode.Auto }),
                CollectUpdatesAsync(client, SecondPrompt, timeout.Token));

            await Assert.That(ReadText(responses[0])).IsEqualTo(FirstPrompt);
            await Assert.That(ReadText(responses[1])).IsEqualTo(SecondPrompt);
            await Assert.That(ReadConversationId(responses[0])).IsNotEqualTo(ReadConversationId(responses[1]));
            await Assert.That(Directory.GetFiles(directory, ActiveMarkerSearchPattern)).Count().IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string ReadText(IEnumerable<ChatResponseUpdate> updates) => updates
        .SelectMany(static update => update.Contents)
        .OfType<TextContent>()
        .Single().Text;

    private static async Task<List<ChatResponseUpdate>> CollectUpdatesAsync(
        CodexChatClient client,
        string prompt,
        CancellationToken cancellationToken,
        ChatOptions? options = null)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, prompt)], options: options, cancellationToken: cancellationToken))
        {
            updates.Add(update);
        }

        return updates;
    }

    private static string? ReadConversationId(IEnumerable<ChatResponseUpdate> updates) =>
        updates.Select(static update => update.ConversationId).FirstOrDefault(static id => id is not null);

    private static string CreateSandboxDirectory()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, TestDirectoryName, SandboxDirectoryName,
            Guid.NewGuid().ToString(GuidFormat));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
