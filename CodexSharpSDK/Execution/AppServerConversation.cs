using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Internal;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Execution;

/// <summary>Drives one native app-server turn through the SDK's existing bounded process owner.</summary>
internal static class AppServerConversation
{
    public static async IAsyncEnumerable<ThreadEvent> RunAsync(CodexExec exec, CodexExecArgs args,
        ThreadOptions options, TurnOptions turn, [EnumeratorCancellation] CancellationToken token)
    {
        var input = Channel.CreateBounded<string>(new BoundedChannelOptions(8) { SingleReader = true, SingleWriter = true });
        await SendAsync(input.Writer, 0, "initialize", new JsonObject
        {
            ["clientInfo"] = new JsonObject { ["name"] = "managedcode_codex_sdk", ["title"] = "ManagedCode Codex SDK", ["version"] = "1.0" },
        }, token).ConfigureAwait(false);
        string? threadId = null;
        var terminal = false;
        var usage = new Usage(0, 0, 0);
        try
        {
            await foreach (var line in exec.RunAppServerAsync(args, input.Reader.ReadAllAsync(token)).WithCancellation(token).ConfigureAwait(false))
            {
                var message = JsonNode.Parse(line)?.AsObject() ?? throw new InvalidDataException("Codex app-server emitted an empty message.");
                if (terminal)
                { continue; }
                if (message["id"] is { } requestId)
                {
                    if (message["method"] is { } requestMethod)
                    {
                        await RespondAsync(input.Writer, requestId, requestMethod.GetValue<string>(), token).ConfigureAwait(false);
                        continue;
                    }
                    if (message["error"] is { } error)
                    {
                        yield return new TurnFailedEvent(new ThreadError(error["message"]?.GetValue<string>() ?? "Codex app-server request failed."));
                        terminal = true;
                        input.Writer.TryComplete();
                        continue;
                    }
                    var id = requestId.GetValue<int>();
                    if (id == 0)
                    {
                        await input.Writer.WriteAsync(new JsonObject { ["method"] = "initialized" }.ToJsonString(), token).ConfigureAwait(false);
                        await SendAsync(input.Writer, 1, args.ThreadId is null ? "thread/start" : "thread/resume",
                            AppServerOptions.Thread(options, args.ThreadId), token).ConfigureAwait(false);
                    }
                    else if (id == 1)
                    {
                        threadId = message["result"]?["thread"]?["id"]?.GetValue<string>()
                            ?? throw new InvalidDataException("Codex app-server did not return its thread identity.");
                        yield return new ThreadStartedEvent(threadId);
                        await SendAsync(input.Writer, 2, "turn/start", AppServerOptions.Turn(args, options, turn, threadId), token).ConfigureAwait(false);
                    }
                    continue;
                }
                if (terminal || message["params"] is not JsonObject parameters)
                {
                    continue;
                }
                if (parameters["threadId"] is { } owner && owner.GetValue<string>() != threadId)
                {
                    continue;
                }
                switch (message["method"]?.GetValue<string>())
                {
                    case "turn/started":
                        yield return new TurnStartedEvent();
                        break;
                    case CodexProtocolConstants.EventTypes.AgentMessageDelta:
                        yield return new AgentMessageDeltaEvent(parameters["itemId"]!.GetValue<string>(), parameters["delta"]!.GetValue<string>());
                        break;
                    case "item/started":
                        yield return new ItemStartedEvent(AppServerItemMapper.Parse(parameters["item"]!.AsObject()));
                        break;
                    case "item/completed":
                        yield return new ItemCompletedEvent(AppServerItemMapper.Parse(parameters["item"]!.AsObject()));
                        break;
                    case "thread/tokenUsage/updated":
                        var total = parameters["tokenUsage"]?["total"];
                        if (total is not null)
                        {
                            usage = new Usage(total["inputTokens"]?.GetValue<int>() ?? 0,
                                total["cachedInputTokens"]?.GetValue<int>() ?? 0, total["outputTokens"]?.GetValue<int>() ?? 0);
                        }
                        break;
                    case "turn/completed":
                        var completed = parameters["turn"]!;
                        terminal = true;
                        input.Writer.TryComplete();
                        yield return completed["status"]?.GetValue<string>() == "completed"
                            ? new TurnCompletedEvent(usage)
                            : new TurnFailedEvent(new ThreadError(completed["error"]?["message"]?.GetValue<string>() ?? "Codex turn was interrupted."));
                        break;
                }
            }
        }
        finally
        {
            input.Writer.TryComplete();
        }
        if (!terminal)
        {
            throw CliExecutionFailureException.FromProviderFailure("Codex app-server exited without a terminal turn result.");
        }
    }

    private static ValueTask SendAsync(ChannelWriter<string> writer, int id, string method, JsonObject parameters, CancellationToken token) =>
        writer.WriteAsync(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString(), token);

    private static ValueTask RespondAsync(ChannelWriter<string> writer, JsonNode id, string method, CancellationToken token)
    {
        // A headless SDK never grants a new interactive permission on the user's behalf.
        var response = new JsonObject { ["id"] = id.DeepClone() };
        if (method is "item/commandExecution/requestApproval" or "item/fileChange/requestApproval")
        {
            response["result"] = new JsonObject { ["decision"] = "decline" };
        }
        else
        {
            response["error"] = new JsonObject { ["code"] = -32601, ["message"] = "This headless client does not support interactive requests." };
        }
        return writer.WriteAsync(response.ToJsonString(), token);
    }
}
