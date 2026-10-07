using System.Text;

namespace ManagedCode.CodexSharpSDK.Internal;

internal static class BoundedMetadataFileReader
{
    private const int BufferCharacters = 4096;
    private const string MetadataFileExceededMaximumCharactersMessage = "CLI metadata file exceeded the configured character limit.";

    internal static string ReadAllText(string path, int maximumCharacters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var content = new StringBuilder(Math.Min(maximumCharacters, BufferCharacters));
        var buffer = new char[BufferCharacters];

        while (true)
        {
            var remainingCharacters = maximumCharacters - content.Length;
            var requestedCharacters = remainingCharacters >= buffer.Length
                ? buffer.Length
                : remainingCharacters + 1;
            var read = reader.Read(buffer, 0, requestedCharacters);
            if (read == 0)
            {
                return content.ToString();
            }

            if (read > remainingCharacters)
            {
                throw new InvalidOperationException(MetadataFileExceededMaximumCharactersMessage);
            }

            content.Append(buffer, 0, read);
        }
    }
}
