using System.Text.Json.Nodes;
using ManagedCode.CodexSharpSDK.Internal;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Client;

public sealed partial class CliLoginSession
{
    private const string AppServerCommand = "app-server";
    private const string ConfigFlag = "-c";
    private const string ClientInfo = "clientInfo";
    private const string ClientName = "name";
    private const string ClientTitle = "title";
    private const string ClientVersion = "version";
    private const string SdkClientName = "managedcode_codex_sdk";
    private const string SdkClientTitle = "ManagedCode Codex SDK";
    private const string InitializedMethod = "initialized";
    private const string LoginStartMethod = "account/login/start";
    private const string LoginCompletedMethod = "account/login/completed";
    private const string LoginType = "type";
    private const string DeviceCodeType = "chatgptDeviceCode";
    private const string LoginId = "loginId";
    private const string VerificationUrl = "verificationUrl";
    private const string UserCode = "userCode";
    private const string Success = "success";
    private const string InvalidInstructionsMessage = "Codex did not return complete device-code sign-in instructions.";
    private string? _loginId;

    internal static Task<CliLoginSession> StartAsync(CliLaunchCommand launchCommand,
        IReadOnlyDictionary<string, string> environment, JsonObject? config, TimeSpan terminationTimeout,
        int maximumOutputCharacters, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { AppServerCommand };
        if (config is not null)
        {
            foreach (var value in TomlConfigSerializer.Serialize(config))
            {
                arguments.Add(ConfigFlag);
                arguments.Add(value);
            }
        }
        return StartAsync(CreateStartInfo(launchCommand, environment, arguments.ToArray()), terminationTimeout,
            maximumOutputCharacters, cancellationToken);
    }

    private Task InitializeNativeAsync() => SendRpcAsync(InitializeRequestId, InitializeMethod, new JsonObject
    {
        [ClientInfo] = new JsonObject
        {
            [ClientName] = SdkClientName,
            [ClientTitle] = SdkClientTitle,
            [ClientVersion] = typeof(CodexClient).Assembly.GetName().Version!.ToString(),
        },
    });

    private async Task ProcessNativeLineAsync(bool standardError, string line)
    {
        if (standardError || !line.StartsWith('{'))
        {
            return;
        }
        var message = JsonNode.Parse(line)!.AsObject();
        if (message[RpcMethod] is { } method)
        {
            if (method.GetValue<string>() == LoginCompletedMethod && message[RpcParams] is JsonObject parameters &&
                parameters[LoginId]?.GetValue<string>() == _loginId)
            {
                if (parameters[Success]?.GetValue<bool>() != true)
                {
                    throw new InvalidOperationException(parameters[RpcError]?.GetValue<string>() ?? NativeProtocolFailedMessage);
                }
                _authenticated = true;
                _process.StandardInput.Close();
            }
            return;
        }
        if (message[RpcId] is not { } id)
        {
            return;
        }
        if (message[RpcError] is { } error)
        {
            throw new InvalidOperationException(error[RpcMessage]?.GetValue<string>() ?? NativeProtocolFailedMessage);
        }
        if (id.GetValue<int>() == InitializeRequestId)
        {
            await WriteRpcAsync(new JsonObject { [RpcMethod] = InitializedMethod }).ConfigureAwait(false);
            await SendRpcAsync(LoginRequestId, LoginStartMethod, new JsonObject { [LoginType] = DeviceCodeType }).ConfigureAwait(false);
        }
        else if (id.GetValue<int>() == LoginRequestId)
        {
            var result = message[RpcResult];
            _loginId = result?[LoginId]?.GetValue<string>();
            var url = result?[VerificationUrl]?.GetValue<string>();
            var userCode = result?[UserCode]?.GetValue<string>();
            if (result?[LoginType]?.GetValue<string>() != DeviceCodeType || string.IsNullOrWhiteSpace(_loginId) ||
                string.IsNullOrWhiteSpace(userCode) || !Uri.TryCreate(url, UriKind.Absolute, out var verificationUrl))
            {
                throw new InvalidDataException(InvalidInstructionsMessage);
            }
            PublishInstructions(verificationUrl, userCode, false);
        }
    }

}
