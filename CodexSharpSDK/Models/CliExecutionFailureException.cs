namespace ManagedCode.CodexSharpSDK.Models;

/// <summary>Reports a failed CLI turn after the SDK confirmed the root process exit and redirected stream EOF.</summary>
/// <remarks>This confirmation covers the SDK-owned root process only; it does not attest that detached descendants or external side effects stopped.</remarks>
public sealed class CliExecutionFailureException : InvalidOperationException
{
    internal CliExecutionFailureException(string message, int? exitCode)
        : base(message)
    {
        ExitCode = exitCode;
    }

    /// <summary>Gets the native CLI exit code, or <see langword="null"/> when the CLI reported a provider failure event.</summary>
    public int? ExitCode { get; }

    /// <summary>Gets whether the SDK confirmed exit of its root CLI process and natural EOF for its redirected streams.</summary>
    public bool RootProcessExitConfirmed => true;

    internal static CliExecutionFailureException FromProcessExit(int exitCode, string message) => new(message, exitCode);

    internal static CliExecutionFailureException FromProviderFailure(string message) => new(message, null);
}
