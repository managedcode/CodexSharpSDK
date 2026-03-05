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

[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(RepositorySummaryResponse))]
internal sealed partial class IntegrationOutputJsonContext : JsonSerializerContext;
