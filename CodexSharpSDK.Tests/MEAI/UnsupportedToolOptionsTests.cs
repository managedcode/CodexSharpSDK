using System.Collections.Immutable;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Tests;

public sealed class UnsupportedToolOptionsTests
{
    private const string ToolName = "sample_function";
    private const string ToolResult = "unused";
    private const string Prompt = "validate before creating a session";
    private const string EmptyConversationId = "";
    private const string NoneModeName = "none";
    private const string RequireAnyModeName = "require-any";
    private const string RequireSpecificModeName = "require-specific";
    private const string SandboxDirectoryName = ".sandbox";
    private const string TestDirectoryName = "tests";
    private const string MarkerFileName = "cli-started.marker";
    private const string NodeExecutableName = "node";
    private const string WindowsNodeExecutableName = "node.exe";
    private const string PathEnvironmentVariable = "PATH";
    private const string GuidFormat = "N";
    private const char PathQuoteCharacter = '"';
    private const string NodeEvalArgument = "-e";
    private const string NodeMarkerScript = "require('node:fs').writeFileSync(process.argv[1], 'started');";

    [Test]
    public async Task NonemptyMeaiToolsAreRejectedBeforeLaunchingOrCreatingAThread()
    {
        var options = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(static () => ToolResult, ToolName)],
        };

        await AssertRejectedWithoutLaunchingAsync(options);
    }

    [Test]
    [Arguments(NoneModeName)]
    [Arguments(RequireAnyModeName)]
    [Arguments(RequireSpecificModeName)]
    public async Task UnsupportedToolModeIsRejectedBeforeLaunchingOrCreatingAThread(string modeName)
    {
        ChatToolMode mode = modeName switch
        {
            NoneModeName => ChatToolMode.None,
            RequireAnyModeName => ChatToolMode.RequireAny,
            RequireSpecificModeName => ChatToolMode.RequireSpecific(ToolName),
            _ => throw new ArgumentOutOfRangeException(nameof(modeName)),
        };

        await AssertRejectedWithoutLaunchingAsync(new ChatOptions { ToolMode = mode });
    }

    private static async Task AssertRejectedWithoutLaunchingAsync(ChatOptions options)
    {
        var directory = CreateSandboxDirectory();
        var markerPath = Path.Combine(directory, MarkerFileName);

        try
        {
            using var client = new CodexChatClient(new CodexChatClientOptions
            {
                CodexOptions = new CodexOptions { LaunchCommand = CreateLaunchCommand(markerPath) },
            });

            var responseError = await Assert.That(async () =>
            {
                _ = await client.GetResponseAsync(CreateMessages(), options);
            }).ThrowsException();
            await Assert.That(responseError).IsTypeOf<NotSupportedException>();
            await Assert.That(File.Exists(markerPath)).IsFalse();

            var streamError = await Assert.That(async () => await ConsumeAsync(
                    client.GetStreamingResponseAsync(CreateMessages(), options)))
                .ThrowsException();
            await Assert.That(streamError).IsTypeOf<NotSupportedException>();
            await Assert.That(File.Exists(markerPath)).IsFalse();

            var resumeOptions = new ChatOptions
            {
                ConversationId = EmptyConversationId,
                Tools = options.Tools,
                ToolMode = options.ToolMode,
            };
            var resumeError = await Assert.That(async () =>
            {
                _ = await client.GetResponseAsync(CreateMessages(), resumeOptions);
            }).ThrowsException();
            await Assert.That(resumeError).IsTypeOf<NotSupportedException>();

            var resumeStreamError = await Assert.That(async () => await ConsumeAsync(
                    client.GetStreamingResponseAsync(CreateMessages(), resumeOptions)))
                .ThrowsException();
            await Assert.That(resumeStreamError).IsTypeOf<NotSupportedException>();
            await Assert.That(File.Exists(markerPath)).IsFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static CliLaunchCommand CreateLaunchCommand(string markerPath) => new(
        FindNodeExecutable(),
        ImmutableArray.Create(NodeEvalArgument, NodeMarkerScript, markerPath));

    private static string FindNodeExecutable()
    {
        var executableName = OperatingSystem.IsWindows() ? WindowsNodeExecutableName : NodeExecutableName;
        var path = Environment.GetEnvironmentVariable(PathEnvironmentVariable) ?? string.Empty;
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(entry.Trim(PathQuoteCharacter), executableName);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException("Node.js is required for the CLI launch-marker regression.");
    }

    private static string CreateSandboxDirectory()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, TestDirectoryName, SandboxDirectoryName,
            Guid.NewGuid().ToString(GuidFormat));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static IEnumerable<ChatMessage> CreateMessages() => [new(ChatRole.User, Prompt)];

    private static async Task ConsumeAsync(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        await foreach (var _ in updates)
        {
        }
    }
}
