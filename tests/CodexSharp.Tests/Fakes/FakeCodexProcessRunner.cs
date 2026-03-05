using System.Runtime.CompilerServices;

namespace ManagedCode.CodexSharp.Tests.Fakes;

internal sealed class FakeCodexProcessRunner : ICodexProcessRunner
{
    private readonly Func<CodexProcessInvocation, CancellationToken, IAsyncEnumerable<string>> _handler;

    public FakeCodexProcessRunner(
        Func<CodexProcessInvocation, CancellationToken, IAsyncEnumerable<string>>? handler = null)
    {
        _handler = handler ?? ((_, cancellationToken) => Empty(cancellationToken));
    }

    public List<CodexProcessInvocation> Invocations { get; } = [];

    public IAsyncEnumerable<string> RunAsync(CodexProcessInvocation invocation, CancellationToken cancellationToken)
    {
        Invocations.Add(invocation);
        return _handler(invocation, cancellationToken);
    }

    public static IAsyncEnumerable<string> FromLines(params string[] lines)
    {
        return YieldLines(lines, CancellationToken.None);
    }

    public static async IAsyncEnumerable<string> YieldLines(
        IEnumerable<string> lines,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return line;
        }
    }

    private static async IAsyncEnumerable<string> Empty([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }
}
