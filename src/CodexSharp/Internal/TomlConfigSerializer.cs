using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ManagedCode.CodexSharp.Internal;

internal static partial class TomlConfigSerializer
{
    private static readonly Regex BareTomlKeyRegex = BareTomlKeyRegexFactory();

    public static IReadOnlyList<string> Serialize(JsonObject configOverrides)
    {
        ArgumentNullException.ThrowIfNull(configOverrides);

        var overrides = new List<string>();
        Flatten(configOverrides, string.Empty, overrides);
        return overrides;
    }

    private static void Flatten(JsonNode value, string prefix, List<string> overrides)
    {
        if (value is not JsonObject jsonObject)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                throw new InvalidOperationException("Codex config overrides must be a JSON object");
            }

            overrides.Add($"{prefix}={ToTomlValue(value, prefix)}");
            return;
        }

        if (string.IsNullOrEmpty(prefix) && jsonObject.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrEmpty(prefix) && jsonObject.Count == 0)
        {
            overrides.Add($"{prefix}={{}}");
            return;
        }

        foreach (var (key, child) in jsonObject)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidOperationException("Codex config override keys must be non-empty strings");
            }

            if (child is null)
            {
                continue;
            }

            var path = string.IsNullOrEmpty(prefix) ? key : $"{prefix}.{key}";
            if (child is JsonObject)
            {
                Flatten(child, path, overrides);
            }
            else
            {
                overrides.Add($"{path}={ToTomlValue(child, path)}");
            }
        }
    }

    private static string ToTomlValue(JsonNode node, string path)
    {
        using var document = JsonDocument.Parse(node.ToJsonString());
        return ToTomlValue(document.RootElement, path);
    }

    private static string ToTomlValue(JsonElement element, string path)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => QuoteAsJsonString(element.GetString() ?? string.Empty),
            JsonValueKind.Number => RenderNumber(element, path),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => RenderArray(element, path),
            JsonValueKind.Object => RenderObject(element, path),
            JsonValueKind.Null => throw new InvalidOperationException($"Codex config override at {path} cannot be null"),
            _ => throw new InvalidOperationException(
                $"Unsupported Codex config override value at {path}: {element.ValueKind}")
        };
    }

    private static string RenderNumber(JsonElement element, string path)
    {
        var raw = element.GetRawText();

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value))
        {
            throw new InvalidOperationException($"Codex config override at {path} must be a finite number");
        }

        return raw;
    }

    private static string RenderArray(JsonElement element, string path)
    {
        var items = new List<string>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            items.Add(ToTomlValue(item, $"{path}[{index}]") );
            index += 1;
        }

        return $"[{string.Join(", ", items)}]";
    }

    private static string RenderObject(JsonElement element, string path)
    {
        var parts = new List<string>();
        foreach (var property in element.EnumerateObject())
        {
            if (string.IsNullOrEmpty(property.Name))
            {
                throw new InvalidOperationException("Codex config override keys must be non-empty strings");
            }

            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            parts.Add($"{FormatTomlKey(property.Name)} = {ToTomlValue(property.Value, $"{path}.{property.Name}")}");
        }

        return $"{{{string.Join(", ", parts)}}}";
    }

    private static string FormatTomlKey(string key)
    {
        return BareTomlKeyRegex.IsMatch(key)
            ? key
            : QuoteAsJsonString(key);
    }

    private static string QuoteAsJsonString(string value)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStringValue(value);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex BareTomlKeyRegexFactory();
}
