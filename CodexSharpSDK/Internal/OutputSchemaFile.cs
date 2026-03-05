using System.Text;
using ManagedCode.CodexSharpSDK.Logging;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ManagedCode.CodexSharpSDK.Internal;

internal sealed class OutputSchemaFile : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string? _schemaDirectory;
    private readonly ILogger _logger;

    private OutputSchemaFile(string? schemaPath, string? schemaDirectory, ILogger? logger)
    {
        SchemaPath = schemaPath;
        _schemaDirectory = schemaDirectory;
        _logger = logger ?? NullLogger.Instance;
    }

    public string? SchemaPath { get; }

    public static async Task<OutputSchemaFile> CreateAsync(
        StructuredOutputSchema? schema,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (schema is null)
        {
            return new OutputSchemaFile(null, null, logger);
        }

        var schemaDirectory = Path.Combine(Path.GetTempPath(), $"codex-output-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(schemaDirectory);

        var schemaPath = Path.Combine(schemaDirectory, "schema.json");
        try
        {
            await File.WriteAllTextAsync(schemaPath, schema.ToJsonObject().ToJsonString(), Utf8WithoutBom, cancellationToken)
                .ConfigureAwait(false);
            return new OutputSchemaFile(schemaPath, schemaDirectory, logger);
        }
        catch (Exception exception)
        {
            TryDeleteDirectory(schemaDirectory, logger ?? NullLogger.Instance);
            throw new InvalidOperationException($"Failed to write output schema file '{schemaPath}'.", exception);
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_schemaDirectory is not null)
        {
            TryDeleteDirectory(_schemaDirectory, _logger);
        }

        return ValueTask.CompletedTask;
    }

    private static void TryDeleteDirectory(string path, ILogger logger)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException exception)
        {
            CodexThreadLog.OutputSchemaDeleteFailed(logger, path, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            CodexThreadLog.OutputSchemaDeleteFailed(logger, path, exception);
        }
    }
}
