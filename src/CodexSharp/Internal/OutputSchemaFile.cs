using System.Text;
using System.Text.Json.Nodes;

namespace ManagedCode.CodexSharp.Internal;

internal sealed class OutputSchemaFile : IAsyncDisposable
{
    private readonly string? _schemaDirectory;

    private OutputSchemaFile(string? schemaPath, string? schemaDirectory)
    {
        SchemaPath = schemaPath;
        _schemaDirectory = schemaDirectory;
    }

    public string? SchemaPath { get; }

    public static async Task<OutputSchemaFile> CreateAsync(JsonObject? schema, CancellationToken cancellationToken)
    {
        if (schema is null)
        {
            return new OutputSchemaFile(null, null);
        }

        var schemaDirectory = Path.Combine(Path.GetTempPath(), $"codex-output-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(schemaDirectory);

        var schemaPath = Path.Combine(schemaDirectory, "schema.json");
        try
        {
            await File.WriteAllTextAsync(schemaPath, schema.ToJsonString(), Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
            return new OutputSchemaFile(schemaPath, schemaDirectory);
        }
        catch
        {
            TryDeleteDirectory(schemaDirectory);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_schemaDirectory is not null)
        {
            TryDeleteDirectory(_schemaDirectory);
        }

        return ValueTask.CompletedTask;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Suppress cleanup failures.
        }
    }
}
