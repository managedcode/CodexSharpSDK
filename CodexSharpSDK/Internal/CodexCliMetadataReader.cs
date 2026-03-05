using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Internal;

internal static class CodexCliMetadataReader
{
    private const string VersionFlag = "--version";
    private const string CliVersionPrefix = "codex-cli";
    private const string NpmExecutableName = "npm";
    private const string NpmViewCommand = "view";
    private const string NpmPackageName = "@openai/codex";
    private const string NpmVersionProperty = "version";
    private const string NpmSilentFlag = "--silent";
    private const string NpmGlobalUpdateCommand = "npm install --global @openai/codex@latest";
    private const string BunGlobalUpdateCommand = "bun add --global @openai/codex@latest";
    private const string NpmUserAgentEnvironmentVariable = "npm_config_user_agent";
    private const string BunInstallEnvironmentVariable = "BUN_INSTALL";
    private const string BunUserAgentPrefix = "bun/";
    private const string BunPathMarker = ".bun";
    private const string UpdateAvailableMessagePrefix = "Codex CLI update is available:";
    private const string UpdateCheckFailedMessagePrefix = "Failed to check latest Codex CLI version from npm:";

    private const string DotCodexDirectory = ".codex";
    private const string ModelsCacheFileName = "models_cache.json";
    private const string ConfigFileName = "config.toml";

    private const string ModelsPropertyName = "models";
    private const string SlugPropertyName = "slug";
    private const string DisplayNamePropertyName = "display_name";
    private const string DescriptionPropertyName = "description";
    private const string VisibilityPropertyName = "visibility";
    private const string VisibilityListValue = "list";
    private const string SupportedInApiPropertyName = "supported_in_api";
    private const string SupportedReasoningLevelsPropertyName = "supported_reasoning_levels";
    private const string EffortPropertyName = "effort";

    private const string ModelKeyName = "model";
    private const char AssignmentSeparator = '=';
    private const char CommentPrefix = '#';
    private const char Quote = '"';
    private const char Apostrophe = '\'';
    private const char Escape = '\\';
    private const char SectionPrefix = '[';

    public static CodexCliMetadata Read(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var installedVersion = ReadInstalledVersion(executablePath);
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            return new CodexCliMetadata(installedVersion, null, []);
        }

        var defaultModel = ReadDefaultModel(homeDirectory);
        var models = ReadModels(homeDirectory);
        return new CodexCliMetadata(installedVersion, defaultModel, models);
    }

    public static CodexCliUpdateStatus ReadUpdateStatus(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var installedVersion = ReadInstalledVersion(executablePath);
        var probe = ProbeLatestPublishedVersion();
        if (!string.IsNullOrWhiteSpace(probe.ErrorMessage))
        {
            var failureMessage = $"{UpdateCheckFailedMessagePrefix} {probe.ErrorMessage}";
            return new CodexCliUpdateStatus(installedVersion, null, false, failureMessage, null);
        }

        if (string.IsNullOrWhiteSpace(probe.LatestVersion))
        {
            return new CodexCliUpdateStatus(installedVersion, null, false, null, null);
        }

        var isUpdateAvailable = IsNewerVersion(probe.LatestVersion, installedVersion);
        if (!isUpdateAvailable)
        {
            return new CodexCliUpdateStatus(installedVersion, probe.LatestVersion, false, null, null);
        }

        var updateCommand = ResolveUpdateCommand(executablePath);
        var message =
            $"{UpdateAvailableMessagePrefix} installed {installedVersion}, latest {probe.LatestVersion}. Run '{updateCommand}'.";
        return new CodexCliUpdateStatus(
            installedVersion,
            probe.LatestVersion,
            true,
            message,
            updateCommand);
    }

    internal static string ParseInstalledVersion(string versionOutput)
    {
        if (string.IsNullOrWhiteSpace(versionOutput))
        {
            throw new InvalidOperationException("Codex CLI version output is empty.");
        }

        var trimmedOutput = versionOutput.Trim();
        var prefix = $"{CliVersionPrefix} ";
        if (!trimmedOutput.StartsWith(prefix, StringComparison.Ordinal))
        {
            return trimmedOutput;
        }

        var parts = trimmedOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
        {
            throw new InvalidOperationException($"Failed to parse Codex CLI version output: '{trimmedOutput}'.");
        }

        return parts[1];
    }

    internal static string? ParseLatestPublishedVersion(string npmOutput)
    {
        if (string.IsNullOrWhiteSpace(npmOutput))
        {
            return null;
        }

        var tokens = npmOutput.Split(
            [Environment.NewLine, "\n", "\r", "\t", " "],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            var candidate = token.Trim(Quote, Apostrophe, ',');
            if (TryParseSemanticVersion(candidate, out var version))
            {
                return version.ToNormalizedString();
            }
        }

        return null;
    }

    internal static bool IsNewerVersion(string latestVersion, string installedVersion)
    {
        if (!TryParseSemanticVersion(latestVersion, out var latest))
        {
            return false;
        }

        if (!TryParseSemanticVersion(installedVersion, out var installed))
        {
            return false;
        }

        return CompareSemanticVersion(latest, installed) > 0;
    }

    internal static string ResolveUpdateCommand(
        string executablePath,
        string? npmUserAgent = null,
        string? bunInstallRoot = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return NpmGlobalUpdateCommand;
        }

        if (IsLikelyBunManagedPath(executablePath))
        {
            return BunGlobalUpdateCommand;
        }

        var resolvedUserAgent = string.IsNullOrWhiteSpace(npmUserAgent)
            ? Environment.GetEnvironmentVariable(NpmUserAgentEnvironmentVariable)
            : npmUserAgent;
        if (IsBunUserAgent(resolvedUserAgent))
        {
            return BunGlobalUpdateCommand;
        }

        var resolvedBunInstallRoot = string.IsNullOrWhiteSpace(bunInstallRoot)
            ? Environment.GetEnvironmentVariable(BunInstallEnvironmentVariable)
            : bunInstallRoot;
        if (IsPathUnderRoot(executablePath, resolvedBunInstallRoot))
        {
            return BunGlobalUpdateCommand;
        }

        return NpmGlobalUpdateCommand;
    }

    internal static string? ParseDefaultModelFromTomlLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var insideSection = false;
        foreach (var rawLine in lines)
        {
            var stripped = StripInlineComment(rawLine);
            if (string.IsNullOrWhiteSpace(stripped))
            {
                continue;
            }

            var trimmed = stripped.Trim();
            if (trimmed.StartsWith(SectionPrefix))
            {
                insideSection = true;
                continue;
            }

            if (insideSection)
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf(AssignmentSeparator);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            if (!string.Equals(key, ModelKeyName, StringComparison.Ordinal))
            {
                continue;
            }

            var rawValue = trimmed[(separatorIndex + 1)..].Trim();
            var value = Unquote(rawValue);
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value;
        }

        return null;
    }

    internal static IReadOnlyList<CodexModelMetadata> ParseModelsCache(JsonElement rootElement)
    {
        if (rootElement.ValueKind != JsonValueKind.Object
            || !rootElement.TryGetProperty(ModelsPropertyName, out var modelsElement)
            || modelsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<CodexModelMetadata>();
        foreach (var modelElement in modelsElement.EnumerateArray())
        {
            if (modelElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var slug = ReadStringProperty(modelElement, SlugPropertyName);
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            var displayName = ReadStringProperty(modelElement, DisplayNamePropertyName);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = slug;
            }

            var description = ReadStringProperty(modelElement, DescriptionPropertyName);
            var visibility = ReadStringProperty(modelElement, VisibilityPropertyName);
            var isListed = string.Equals(visibility, VisibilityListValue, StringComparison.Ordinal);
            var isApiSupported = ReadBooleanProperty(modelElement, SupportedInApiPropertyName);
            var reasoningEfforts = ReadReasoningEfforts(modelElement);

            models.Add(new CodexModelMetadata(
                slug,
                displayName,
                description,
                isListed,
                isApiSupported,
                reasoningEfforts));
        }

        return models;
    }

    private static string ReadInstalledVersion(string executablePath)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(VersionFlag);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start Codex CLI at '{executablePath}' to read version.");
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed to start Codex CLI at '{executablePath}' to read version.", exception);
        }

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to read Codex CLI version from '{executablePath}'. Exit code {process.ExitCode}: {standardError}");
        }

        var versionOutput = string.IsNullOrWhiteSpace(standardOutput)
            ? standardError
            : standardOutput;
        return ParseInstalledVersion(versionOutput);
    }

    private static LatestVersionProbe ProbeLatestPublishedVersion()
    {
        var startInfo = new ProcessStartInfo(NpmExecutableName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(NpmViewCommand);
        startInfo.ArgumentList.Add(NpmPackageName);
        startInfo.ArgumentList.Add(NpmVersionProperty);
        startInfo.ArgumentList.Add(NpmSilentFlag);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return LatestVersionProbe.WithError("npm process did not start.");
            }
        }
        catch (Exception exception)
        {
            return LatestVersionProbe.WithError(exception.Message);
        }

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var errorText = string.IsNullOrWhiteSpace(standardError)
                ? standardOutput
                : standardError;
            return LatestVersionProbe.WithError(errorText.Trim());
        }

        var latestVersion = ParseLatestPublishedVersion(standardOutput);
        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            return LatestVersionProbe.WithError("npm output did not contain a valid semantic version.");
        }

        return LatestVersionProbe.WithLatest(latestVersion);
    }

    private static string? ReadDefaultModel(string homeDirectory)
    {
        var configPath = Path.Combine(homeDirectory, DotCodexDirectory, ConfigFileName);
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            return ParseDefaultModelFromTomlLines(File.ReadLines(configPath));
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Failed to read Codex config at '{configPath}'.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"Failed to read Codex config at '{configPath}'.", exception);
        }
    }

    private static IReadOnlyList<CodexModelMetadata> ReadModels(string homeDirectory)
    {
        var modelsCachePath = Path.Combine(homeDirectory, DotCodexDirectory, ModelsCacheFileName);
        if (!File.Exists(modelsCachePath))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(modelsCachePath);
            using var document = JsonDocument.Parse(stream);
            return ParseModelsCache(document.RootElement);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Failed to read Codex model cache at '{modelsCachePath}'.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"Failed to read Codex model cache at '{modelsCachePath}'.", exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Failed to parse Codex model cache at '{modelsCachePath}'.", exception);
        }
    }

    private static List<string> ReadReasoningEfforts(JsonElement modelElement)
    {
        if (!modelElement.TryGetProperty(SupportedReasoningLevelsPropertyName, out var levelsElement)
            || levelsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var efforts = new List<string>();
        foreach (var levelElement in levelsElement.EnumerateArray())
        {
            if (levelElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var effort = ReadStringProperty(levelElement, EffortPropertyName);
            if (!string.IsNullOrWhiteSpace(effort))
            {
                efforts.Add(effort);
            }
        }

        return efforts;
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement)
            || valueElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return valueElement.GetString();
    }

    private static bool ReadBooleanProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement)
            || (valueElement.ValueKind != JsonValueKind.True && valueElement.ValueKind != JsonValueKind.False))
        {
            return false;
        }

        return valueElement.GetBoolean();
    }

    private static bool IsLikelyBunManagedPath(string executablePath)
    {
        return executablePath.Contains(BunPathMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBunUserAgent(string? npmUserAgent)
    {
        if (string.IsNullOrWhiteSpace(npmUserAgent))
        {
            return false;
        }

        return npmUserAgent.StartsWith(BunUserAgentPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathUnderRoot(string executablePath, string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (normalizedRoot.Length == 0)
        {
            return false;
        }

        return executablePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseSemanticVersion(string value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith('v'))
        {
            candidate = candidate[1..];
        }

        var metadataSeparatorIndex = candidate.IndexOf('+');
        if (metadataSeparatorIndex >= 0)
        {
            candidate = candidate[..metadataSeparatorIndex];
        }

        var preReleaseSeparatorIndex = candidate.IndexOf('-');
        var core = preReleaseSeparatorIndex >= 0
            ? candidate[..preReleaseSeparatorIndex]
            : candidate;
        var preRelease = preReleaseSeparatorIndex >= 0
            ? candidate[(preReleaseSeparatorIndex + 1)..]
            : null;

        var coreParts = core.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (coreParts.Length < 3)
        {
            return false;
        }

        if (!int.TryParse(coreParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major))
        {
            return false;
        }

        if (!int.TryParse(coreParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }

        if (!int.TryParse(coreParts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(preRelease))
        {
            preRelease = null;
        }

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    private static int CompareSemanticVersion(SemanticVersion left, SemanticVersion right)
    {
        var majorComparison = left.Major.CompareTo(right.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = left.Minor.CompareTo(right.Minor);
        if (minorComparison != 0)
        {
            return minorComparison;
        }

        var patchComparison = left.Patch.CompareTo(right.Patch);
        if (patchComparison != 0)
        {
            return patchComparison;
        }

        if (left.PreRelease is null && right.PreRelease is null)
        {
            return 0;
        }

        if (left.PreRelease is null)
        {
            return 1;
        }

        if (right.PreRelease is null)
        {
            return -1;
        }

        return ComparePreRelease(left.PreRelease, right.PreRelease);
    }

    private static int ComparePreRelease(string left, string right)
    {
        var leftIdentifiers = left.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rightIdentifiers = right.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var maxLength = Math.Max(leftIdentifiers.Length, rightIdentifiers.Length);

        for (var index = 0; index < maxLength; index += 1)
        {
            if (index >= leftIdentifiers.Length)
            {
                return -1;
            }

            if (index >= rightIdentifiers.Length)
            {
                return 1;
            }

            var leftIdentifier = leftIdentifiers[index];
            var rightIdentifier = rightIdentifiers[index];

            var leftIsNumeric = int.TryParse(leftIdentifier, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightIsNumeric = int.TryParse(rightIdentifier, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

            if (leftIsNumeric && rightIsNumeric)
            {
                var numericComparison = leftNumber.CompareTo(rightNumber);
                if (numericComparison != 0)
                {
                    return numericComparison;
                }

                continue;
            }

            if (leftIsNumeric && !rightIsNumeric)
            {
                return -1;
            }

            if (!leftIsNumeric && rightIsNumeric)
            {
                return 1;
            }

            var textComparison = string.CompareOrdinal(leftIdentifier, rightIdentifier);
            if (textComparison != 0)
            {
                return textComparison;
            }
        }

        return 0;
    }

    private static string StripInlineComment(string line)
    {
        var insideQuotes = false;
        for (var index = 0; index < line.Length; index += 1)
        {
            var current = line[index];
            if (current == Quote)
            {
                var escaped = index > 0 && line[index - 1] == Escape;
                if (!escaped)
                {
                    insideQuotes = !insideQuotes;
                }

                continue;
            }

            if (current == CommentPrefix && !insideQuotes)
            {
                return line[..index];
            }
        }

        return line;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == Quote && value[^1] == Quote)
        {
            return value[1..^1];
        }

        return value;
    }

    private readonly record struct LatestVersionProbe(string? LatestVersion, string? ErrorMessage)
    {
        public static LatestVersionProbe WithLatest(string latestVersion) => new(latestVersion, null);

        public static LatestVersionProbe WithError(string errorMessage)
        {
            return new(
                null,
                string.IsNullOrWhiteSpace(errorMessage)
                    ? "unknown error"
                    : errorMessage);
        }
    }

    private readonly record struct SemanticVersion(
        int Major,
        int Minor,
        int Patch,
        string? PreRelease)
    {
        public string ToNormalizedString()
        {
            return PreRelease is null
                ? $"{Major}.{Minor}.{Patch}"
                : $"{Major}.{Minor}.{Patch}-{PreRelease}";
        }
    }
}
