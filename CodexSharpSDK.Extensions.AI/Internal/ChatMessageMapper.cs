using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Internal;

internal static class ChatMessageMapper
{
    private const string SystemPrefix = "[System] ";
    private const string AssistantPrefix = "[Assistant] ";
    private const string ParagraphSeparator = "\n\n";
    private const string ImageMediaPrefix = "image/";

    internal static (string Prompt, List<DataContent> ImageContents) ToCodexInput(IEnumerable<ChatMessage> messages, string? instructions = null)
    {
        var promptParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            promptParts.Add(string.Concat(SystemPrefix, instructions));
        }
        var imageContents = new List<DataContent>();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                if (message.Text is { } systemText)
                {
                    promptParts.Add(string.Concat(SystemPrefix, systemText));
                }

                continue;
            }

            if (message.Role == ChatRole.User)
            {
                var userTextParts = new List<string>();
                foreach (var content in message.Contents)
                {
                    if (content is TextContent textContent && textContent.Text is { } text)
                    {
                        userTextParts.Add(text);
                    }
                    else if (content is DataContent dataContent && IsImageMediaType(dataContent.MediaType))
                    {
                        imageContents.Add(dataContent);
                    }
                }

                if (userTextParts.Count > 0)
                {
                    promptParts.Add(string.Join(ParagraphSeparator, userTextParts));
                }

                continue;
            }

            if (message.Role == ChatRole.Assistant && message.Text is { } assistantText)
            {
                promptParts.Add(string.Concat(AssistantPrefix, assistantText));
            }
        }

        return (string.Join(ParagraphSeparator, promptParts), imageContents);
    }

    internal static IReadOnlyList<UserInput> BuildUserInput(string prompt, IReadOnlyList<DataContent> imageContents)
    {
        if (imageContents.Count == 0)
        {
            return [new TextInput(prompt)];
        }

        var inputs = new List<UserInput> { new TextInput(prompt) };

        foreach (var dc in imageContents)
        {
            var fileName = dc.Name ?? GenerateFileName(dc.MediaType);
            if (dc.Data.Length > 0)
            {
                var stream = new MemoryStream(dc.Data.ToArray());
                inputs.Add(new LocalImageInput(stream, fileName, leaveOpen: false));
            }
        }

        return inputs;
    }

    private static bool IsImageMediaType(string? mediaType) =>
        mediaType is not null && mediaType.StartsWith(ImageMediaPrefix, StringComparison.OrdinalIgnoreCase);

    private static string GenerateFileName(string? mediaType)
    {
        var extension = mediaType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            _ => ".bin",
        };

        return $"image_{Guid.NewGuid():N}{extension}";
    }
}
