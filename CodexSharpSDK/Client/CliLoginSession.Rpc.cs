using System.Text.Json.Nodes;

namespace ManagedCode.CodexSharpSDK.Client;

public sealed partial class CliLoginSession
{
    private const string RpcId = "id";
    private const string RpcMethod = "method";
    private const string RpcParams = "params";
    private const string RpcResult = "result";
    private const string RpcError = "error";
    private const string RpcMessage = "message";
    private const string RpcJsonVersion = "jsonrpc";
    private const string RpcVersion = "2.0";
    private const string InitializeMethod = "initialize";
    private const string NativeProtocolFailedMessage = "The CLI rejected the sign-in request.";
    private const string NativeSuccessMissingMessage = "The CLI exited without a successful sign-in result.";
    private const int InitializeRequestId = 0;
    private const int LoginRequestId = 1;

    private Task SendRpcAsync(int id, string method, JsonObject parameters) => WriteRpcAsync(new JsonObject
    {
        [RpcJsonVersion] = RpcVersion,
        [RpcId] = id,
        [RpcMethod] = method,
        [RpcParams] = parameters,
    });

    private async Task WriteRpcAsync(JsonObject message)
    {
        await _process.StandardInput.WriteLineAsync(message.ToJsonString().AsMemory(), _lifetime.Token).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(_lifetime.Token).ConfigureAwait(false);
    }

    private void ValidateNativeExit()
    {
        if (_process.ExitCode != 0 || !_authenticated)
        {
            throw new InvalidOperationException(NativeSuccessMissingMessage);
        }
    }
}
