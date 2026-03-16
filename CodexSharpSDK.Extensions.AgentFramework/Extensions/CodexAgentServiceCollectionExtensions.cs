using ManagedCode.CodexSharpSDK.Extensions.AI;
using ManagedCode.CodexSharpSDK.Extensions.AI.Extensions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ManagedCode.CodexSharpSDK.Extensions.AgentFramework.Extensions;

public static class CodexAgentServiceCollectionExtensions
{
    public static IServiceCollection AddCodexAIAgent(
        this IServiceCollection services,
        Action<CodexChatClientOptions>? configureChatClient = null,
        Action<ChatClientAgentOptions>? configureAgent = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCodexChatClient(configureChatClient);
        services.AddSingleton<AIAgent>(serviceProvider => CreateAgent(serviceProvider, serviceKey: null, configureAgent));
        return services;
    }

    public static IServiceCollection AddKeyedCodexAIAgent(
        this IServiceCollection services,
        object serviceKey,
        Action<CodexChatClientOptions>? configureChatClient = null,
        Action<ChatClientAgentOptions>? configureAgent = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceKey);

        services.AddKeyedCodexChatClient(serviceKey, configureChatClient);
        services.AddKeyedSingleton<AIAgent>(
            serviceKey,
            (serviceProvider, key) => CreateAgent(serviceProvider, key, configureAgent));
        return services;
    }

    private static ChatClientAgent CreateAgent(
        IServiceProvider serviceProvider,
        object? serviceKey,
        Action<ChatClientAgentOptions>? configureAgent)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var options = new ChatClientAgentOptions();
        configureAgent?.Invoke(options);

        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        var chatClient = serviceKey is null
            ? serviceProvider.GetRequiredService<IChatClient>()
            : serviceProvider.GetRequiredKeyedService<IChatClient>(serviceKey);

        return chatClient.AsAIAgent(options, loggerFactory, serviceProvider);
    }
}
