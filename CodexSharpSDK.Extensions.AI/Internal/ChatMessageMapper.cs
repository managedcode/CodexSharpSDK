using System.Text;
using ManagedCode.CodexSharpSDK.Models;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Extensions.AI.Internal;

internal static class ChatMessageMapper
{
    internal static (string Prompt, List<DataContent> ImageContents) ToCodexInput(IEnumerable<ChatMessage> messages)
    {
        var prompt = new StringBuilder();
        var userTextParts = new List<string>();
        var imageContents = new List<DataContent>();

        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System)
            {
                if (message.Text is { } systemText)
                {
                    prompt.Append("[System] ").Append(systemText).Append("\n\n");
                }
            }
            else if (message.Role == ChatRole.User)
            {
                foreach (var content in message.Contents)
                {
                    if (content is TextContent tc && tc.Text is not null)
                    {
                        userTextParts.Add(tc.Text);
                    }
                    else if (content is DataContent dc && dc.MediaType is not null && dc.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        imageContents.Add(dc);
                    }
                }
            }
            else if (message.Role == ChatRole.Assistant)
            {
                if (message.Text is { } assistantText)
                {
                    prompt.Append("[Assistant] ").Append(assistantText).Append("\n\n");
                }
            }
        }

        if (userTextParts.Count > 0)
        {
            prompt.Append(string.Join("\n\n", userTextParts));
        }

        return (prompt.ToString(), imageContents);
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
