using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Extensions;

public static class CodexServiceCollectionExtensions
{
    public static IServiceCollection AddCodexChatClient(
        this IServiceCollection services,
        Action<CodexChatClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new CodexChatClientOptions();
        configure?.Invoke(options);
        services.AddSingleton<IChatClient>(new CodexChatClient(options));
        return services;
    }

    public static IServiceCollection AddKeyedCodexChatClient(
        this IServiceCollection services,
        object serviceKey,
        Action<CodexChatClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceKey);

        var options = new CodexChatClientOptions();
        configure?.Invoke(options);
        services.AddKeyedSingleton<IChatClient>(serviceKey, new CodexChatClient(options));
        return services;
    }
}
