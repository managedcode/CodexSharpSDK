using Microsoft.Extensions.Logging;

namespace ManagedCode.CodexSharpSDK.Logging;

internal static partial class CodexExecLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Starting Codex CLI '{ExecutablePath}' with {ArgumentCount} arguments.")]
    public static partial void Starting(ILogger logger, string executablePath, int argumentCount);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Warning,
        Message = "Codex CLI execution was cancelled.")]
    public static partial void Cancelled(ILogger logger);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Error,
        Message = "Codex CLI execution failed.")]
    public static partial void Failed(ILogger logger);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Codex CLI finished successfully with {LineCount} output lines.")]
    public static partial void Completed(ILogger logger, int lineCount);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Warning,
        Message = "Failed to terminate Codex CLI process '{ExecutablePath}' during cleanup.")]
    public static partial void ProcessKillFailed(ILogger logger, string executablePath);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Warning,
        Message = "Failed to finish reading standard error for Codex CLI process '{ExecutablePath}' during cleanup.")]
    public static partial void StandardErrorReadFailed(ILogger logger, string executablePath);
}
