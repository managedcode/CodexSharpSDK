using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Execution;
using ManagedCode.CodexSharpSDK.Models;
using ManagedCode.CodexSharpSDK.Tests.Shared;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

public class CodexThreadTests
{
    [Test]
    public async Task RunAsync_WithRealCodexCli_ReturnsCompletedTurnAndUpdatesThreadId()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var result = await thread.RunAsync(
            "Reply with short plain text: ok.",
            new TurnOptions { CancellationToken = cancellation.Token });

        await Assert.That(thread.Id).IsNotNull();
        await Assert.That(result.FinalResponse).IsNotNull();
        await Assert.That(result.Usage).IsNotNull();
    }

    [Test]
    public async Task RunAsync_WithStructuredInput_ReturnsTypedJson()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var schema = IntegrationOutputSchemas.StatusOnly();

        var result = await thread.RunAsync<StatusResponse>(
        [
            new TextInput("Reply with a JSON object."),
            new TextInput("Set status exactly to \"ok\"."),
        ],
        schema,
        IntegrationOutputJsonContext.Default.StatusResponse,
        cancellation.Token);

        await Assert.That(result.TypedResponse.Status).IsEqualTo("ok");
    }

    [Test]
    public async Task RunAsync_GenericStructuredOutput_ReturnsTypedResponse()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var schema = IntegrationOutputSchemas.SummaryAndStatus();

        var result = await thread.RunAsync<RepositorySummaryResponse>(
        [
            new TextInput("Reply with a JSON object."),
            new TextInput("Set status exactly to \"ok\" and summary to \"done\"."),
        ],
        schema,
        IntegrationOutputJsonContext.Default.RepositorySummaryResponse,
        cancellation.Token);

        await Assert.That(result.TypedResponse.Status).IsEqualTo("ok");
        await Assert.That(string.IsNullOrWhiteSpace(result.TypedResponse.Summary)).IsFalse();
        await Assert.That(result.FinalResponse).IsNotNull();
    }

    [Test]
    public async Task RunAsync_GenericStructuredOutput_WithSchemaShortcutAndStringInput_ReturnsTypedResponse()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var schema = IntegrationOutputSchemas.StatusOnly();

        var result = await thread.RunAsync<StatusResponse>(
            "Reply with a JSON object where status is exactly \"ok\".",
            schema,
            IntegrationOutputJsonContext.Default.StatusResponse,
            cancellation.Token);

        await Assert.That(result.TypedResponse.Status).IsEqualTo("ok");
        await Assert.That(result.FinalResponse).IsNotNull();
    }

    [Test]
    public async Task RunAsync_SecondTurnKeepsThreadId_WithRealCodexCli()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var first = await thread.RunAsync(
            "Reply with short plain text: first.",
            new TurnOptions { CancellationToken = cancellation.Token });

        var firstThreadId = thread.Id;
        await Assert.That(firstThreadId).IsNotNull();
        await Assert.That(first.Usage).IsNotNull();

        var second = await thread.RunAsync(
            "Reply with short plain text: second.",
            new TurnOptions { CancellationToken = cancellation.Token });

        await Assert.That(second.Usage).IsNotNull();
        await Assert.That(thread.Id).IsEqualTo(firstThreadId);
    }

    [Test]
    public async Task RunStreamedAsync_YieldsCompletedTurnEvent_WithRealCodexCli()
    {
        var settings = RealCodexTestSupport.GetRequiredSettings();

        using var client = RealCodexTestSupport.CreateClient();
        var thread = StartRealIntegrationThread(client, settings.Model);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var streamed = await thread.RunStreamedAsync(
            "Reply with short plain text: ok.",
            new TurnOptions { CancellationToken = cancellation.Token });

        var hasTurnCompleted = false;
        var hasTurnFailed = false;
        var hasCompletedItem = false;

        await foreach (var threadEvent in streamed.Events.WithCancellation(cancellation.Token))
        {
            hasTurnCompleted |= threadEvent is TurnCompletedEvent;
            hasTurnFailed |= threadEvent is TurnFailedEvent;
            hasCompletedItem |= threadEvent is ItemCompletedEvent;
        }

        await Assert.That(hasCompletedItem).IsTrue();
        await Assert.That(hasTurnCompleted).IsTrue();
        await Assert.That(hasTurnFailed).IsFalse();
        await Assert.That(thread.Id).IsNotNull();
    }

    [Test]
    public async Task RunAsync_HonorsCancellationToken()
    {
        var exec = new CodexExec("codex");
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var action = async () => await thread.RunAsync("cancel", new TurnOptions
        {
            CancellationToken = cancellationSource.Token,
        });

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<OperationCanceledException>();
    }

    [Test]
    public async Task RunAsync_GenericStructuredOutput_ThrowsWhenSchemaMissing()
    {
        var exec = new CodexExec("codex");
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        var action = async () => await thread.RunAsync<StatusResponse>("typed output without schema");
        var exception = await Assert.That(action).ThrowsException();

        await Assert.That(exception).IsTypeOf<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains(nameof(TurnOptions.OutputSchema));
    }

    [Test]
    public async Task RunAsync_GenericStructuredOutput_WithSchemaShortcut_ThrowsWhenSchemaNull()
    {
        var exec = new CodexExec("codex");
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        var action = async () => await thread.RunAsync<StatusResponse>(
            "typed output with null schema",
            null!,
            IntegrationOutputJsonContext.Default.StatusResponse);

        var exception = await Assert.That(action).ThrowsException();
        await Assert.That(exception).IsTypeOf<ArgumentNullException>();
        await Assert.That(((ArgumentNullException)exception!).ParamName).IsEqualTo("outputSchema");
    }

    [Test]
    public async Task RunAsync_GenericOverloadsWithoutJsonTypeInfo_AreMarkedAsAotUnsafe()
    {
        MethodInfo[] methods =
        [
            FindGenericRunAsync(typeof(string), typeof(TurnOptions)),
            FindGenericRunAsync(typeof(IReadOnlyList<UserInput>), typeof(TurnOptions)),
            FindGenericRunAsync(typeof(string), typeof(StructuredOutputSchema), typeof(CancellationToken)),
            FindGenericRunAsync(typeof(IReadOnlyList<UserInput>), typeof(StructuredOutputSchema), typeof(CancellationToken)),
        ];

        foreach (var method in methods)
        {
            await Assert.That(HasAttribute<RequiresDynamicCodeAttribute>(method)).IsTrue();
            await Assert.That(HasAttribute<RequiresUnreferencedCodeAttribute>(method)).IsTrue();
        }
    }

    [Test]
    public async Task RunAsync_GenericOverloadsWithJsonTypeInfo_AreAotSafe()
    {
        MethodInfo[] methods =
        [
            FindGenericRunAsync(typeof(string), typeof(JsonTypeInfo<>), typeof(TurnOptions)),
            FindGenericRunAsync(typeof(IReadOnlyList<UserInput>), typeof(JsonTypeInfo<>), typeof(TurnOptions)),
            FindGenericRunAsync(typeof(string), typeof(StructuredOutputSchema), typeof(JsonTypeInfo<>), typeof(CancellationToken)),
            FindGenericRunAsync(typeof(IReadOnlyList<UserInput>), typeof(StructuredOutputSchema), typeof(JsonTypeInfo<>), typeof(CancellationToken)),
        ];

        foreach (var method in methods)
        {
            await Assert.That(HasAttribute<RequiresDynamicCodeAttribute>(method)).IsFalse();
            await Assert.That(HasAttribute<RequiresUnreferencedCodeAttribute>(method)).IsFalse();
        }
    }

    [Test]
    public async Task RunAsync_ThrowsObjectDisposedExceptionAfterDispose()
    {
        var exec = new CodexExec("codex");
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());
        thread.Dispose();

        async Task<RunResult> Action() => await thread.RunAsync("after-dispose");
        var exception = await Assert.That(Action!).ThrowsException();
        await Assert.That(exception).IsTypeOf<ObjectDisposedException>();
    }

    [Test]
    public Task Dispose_CanBeCalledMultipleTimes()
    {
        var exec = new CodexExec("codex");
        var thread = new CodexThread(exec, new CodexOptions(), new ThreadOptions());

        thread.Dispose();
        thread.Dispose();

        return Task.CompletedTask;
    }

    private static CodexThread StartRealIntegrationThread(CodexClient client, string model)
    {
        return client.StartThread(new ThreadOptions
        {
            Model = model,
            ModelReasoningEffort = ModelReasoningEffort.Medium,
            WebSearchMode = WebSearchMode.Disabled,
            SandboxMode = SandboxMode.WorkspaceWrite,
            NetworkAccessEnabled = true,
        });
    }

    private static MethodInfo FindGenericRunAsync(params Type[] parameterTypes)
    {
        var method = typeof(CodexThread)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(method =>
                method.Name == nameof(CodexThread.RunAsync)
                && method.IsGenericMethodDefinition
                && ParameterTypesMatch(method.GetParameters(), parameterTypes));

        return method
            ?? throw new InvalidOperationException("Failed to resolve generic RunAsync overload by parameter types.");
    }

    private static bool ParameterTypesMatch(ParameterInfo[] parameters, Type[] expectedTypes)
    {
        if (parameters.Length != expectedTypes.Length)
        {
            return false;
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            var actualType = parameters[index].ParameterType;
            var expectedType = expectedTypes[index];

            if (expectedType.IsGenericTypeDefinition)
            {
                if (!actualType.IsGenericType
                    || actualType.GetGenericTypeDefinition() != expectedType)
                {
                    return false;
                }

                continue;
            }

            if (actualType != expectedType)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAttribute<TAttribute>(MemberInfo memberInfo)
        where TAttribute : Attribute
    {
        return memberInfo.GetCustomAttribute<TAttribute>() is not null;
    }
}
