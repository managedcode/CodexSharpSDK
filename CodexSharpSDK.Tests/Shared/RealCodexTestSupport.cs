using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Internal;

namespace ManagedCode.CodexSharpSDK.Tests.Shared;

internal static class RealCodexTestSupport
{
    private const string ModelEnvVar = "CODEX_TEST_MODEL";

    public static RealCodexTestSettings GetRequiredSettings()
    {
        if (!IsCodexAvailable())
        {
            throw new InvalidOperationException(
                "Real Codex tests require the codex CLI. Install it first and ensure it is available in PATH.");
        }

        return new RealCodexTestSettings(ResolveModel());
    }

    public static CodexClient CreateClient()
    {
        return new CodexClient(new CodexOptions());
    }

    private static string ResolveModel()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ModelEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        var fromConfig = TryReadModelFromCodexConfig();
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig;
        }

        throw new InvalidOperationException(
            $"Real Codex tests require a model. Set {ModelEnvVar} or define model in '~/.codex/config.toml'.");
    }

    private static string? TryReadModelFromCodexConfig()
    {
        var configPath = GetCodexConfigPath();
        if (configPath is null || !File.Exists(configPath))
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines(configPath))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("model", StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = trimmed[..separatorIndex].Trim();
                if (!string.Equals(key, "model", StringComparison.Ordinal))
                {
                    continue;
                }

                var rawValue = trimmed[(separatorIndex + 1)..].Trim();
                if (rawValue.Length >= 2
                    && rawValue.StartsWith('"')
                    && rawValue.EndsWith('"'))
                {
                    rawValue = rawValue[1..^1];
                }

                return string.IsNullOrWhiteSpace(rawValue)
                    ? null
                    : rawValue;
            }
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException($"Failed to read Codex config at '{configPath}'.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException($"Failed to read Codex config at '{configPath}'.", exception);
        }

        return null;
    }

    private static string? GetCodexConfigPath()
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            return null;
        }

        return Path.Combine(homeDirectory, ".codex", "config.toml");
    }

    private static bool IsCodexAvailable()
    {
        var resolvedPath = CodexCliLocator.FindCodexPath(null);
        if (Path.IsPathRooted(resolvedPath))
        {
            return File.Exists(resolvedPath);
        }

        return CodexCliLocator.TryResolvePathExecutable(
            Environment.GetEnvironmentVariable("PATH"),
            OperatingSystem.IsWindows(),
            out _);
    }
}

internal sealed record RealCodexTestSettings(string Model);
