using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Tests.Shared;

internal sealed record StatusResponse(string Status);

internal sealed record RepositorySummaryResponse(string Summary, string Status);

internal static class IntegrationOutputSchemas
{
    public static StructuredOutputSchema StatusOnly()
    {
        return StructuredOutputSchema.Map<StatusResponse>(
            additionalProperties: false,
            (response => response.Status, StructuredOutputSchema.PlainText()));
    }

    public static StructuredOutputSchema SummaryAndStatus()
    {
        return StructuredOutputSchema.Map<RepositorySummaryResponse>(
            additionalProperties: false,
            (response => response.Summary, StructuredOutputSchema.PlainText()),
            (response => response.Status, StructuredOutputSchema.PlainText()));
    }
}

internal static class IntegrationOutputDeserializer
{
    public static T Deserialize<T>(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        if (typeof(T) == typeof(StatusResponse))
        {
            return (T)(object)(JsonSerializer.Deserialize(payload, IntegrationOutputJsonContext.Default.StatusResponse)
                ?? throw new InvalidOperationException("Failed to deserialize integration payload to StatusResponse."));
        }

        if (typeof(T) == typeof(RepositorySummaryResponse))
        {
            return (T)(object)(JsonSerializer.Deserialize(payload, IntegrationOutputJsonContext.Default.RepositorySummaryResponse)
                ?? throw new InvalidOperationException("Failed to deserialize integration payload to RepositorySummaryResponse."));
        }

        throw new NotSupportedException($"IntegrationOutputDeserializer does not support type {typeof(T).Name}.");
    }
}

[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(RepositorySummaryResponse))]
internal sealed partial class IntegrationOutputJsonContext : JsonSerializerContext;
