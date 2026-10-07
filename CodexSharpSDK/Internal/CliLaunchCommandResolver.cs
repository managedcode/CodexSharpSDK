using System.Collections.Immutable;
using System.Text.Json;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Internal;

internal static class CliLaunchCommandResolver
{
    private const string NodeWindowsExecutableName = "node.exe";
    private const string BunWindowsExecutableName = "bun.exe";
    private const string NodeUnixExecutableName = "node";
    private const string BunUnixExecutableName = "bun";
    private const string NpmPackageName = "npm";
    private const string NpmCommandName = "npm";
    private const string NpmEntryPoint = "bin/npm-cli.js";
    private const string NpmShimName = "npm.cmd";
    private const string PackageName = "@openai/codex";
    private const string CommandName = "codex";
    private const string WindowsComName = "codex.com";
    private const string ManifestFileName = "package.json";
    private const string BinPropertyName = "bin";
    private const string NamePropertyName = "name";
    private const string WindowsPowerShellName = "codex.ps1";
    private const string UnsupportedShimMessage = "The Codex CLI wrapper cannot be launched safely. Install the supported @openai/codex npm package with Node.js or Bun, or configure CodexExecutablePath to a native executable.";

    internal static CliLaunchCommand Resolve(string? executablePath, string? pathVariable, int maximumManifestCharacters)
    {
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            var hasDirectory = Path.IsPathRooted(executablePath) || executablePath.Contains(Path.DirectorySeparatorChar) ||
                executablePath.Contains(Path.AltDirectorySeparatorChar);
            if (hasDirectory)
            {
                var explicitPath = Path.GetFullPath(executablePath);
                if (IsWindowsWrapper(explicitPath))
                {
                    return ResolveKnownPackageForWrapper(explicitPath, pathVariable, maximumManifestCharacters);
                }

                if (IsWindowsWrapperFormat(explicitPath))
                {
                    throw new InvalidOperationException(UnsupportedShimMessage);
                }

                if (!File.Exists(explicitPath))
                {
                    throw new FileNotFoundException("The configured Codex CLI executable was not found.", explicitPath);
                }

                return ResolveExistingPath(explicitPath, pathVariable);
            }

            return ResolveExplicitName(executablePath, pathVariable, maximumManifestCharacters);
        }

        foreach (var pathEntry in SplitPath(pathVariable))
        {
            foreach (var candidateName in GetNativeCandidates())
            {
                var candidate = Path.Combine(pathEntry, candidateName);
                if (File.Exists(candidate))
                {
                    return ResolveExistingPath(candidate, pathVariable);
                }
            }

            if (OperatingSystem.IsWindows())
            {
                foreach (var wrapperName in new[] { CodexCliLocator.CodexWindowsCommandName, CodexCliLocator.CodexWindowsBatchName, WindowsPowerShellName })
                {
                    var wrapper = Path.Combine(pathEntry, wrapperName);
                    if (File.Exists(wrapper))
                    {
                        return ResolveKnownPackageForWrapper(wrapper, pathVariable, maximumManifestCharacters);
                    }
                }
            }
        }

        throw new FileNotFoundException("Codex CLI was not found at the configured path or on the effective PATH.",
            executablePath ?? (OperatingSystem.IsWindows() ? CodexCliLocator.CodexWindowsExecutableName : CodexCliLocator.CodexExecutableName));
    }

    private static CliLaunchCommand ResolveExplicitName(string executableName, string? pathVariable, int maximumManifestCharacters)
    {
        var candidateNames = OperatingSystem.IsWindows() && string.IsNullOrEmpty(Path.GetExtension(executableName))
            ? new[] { executableName, executableName + ".exe", executableName + ".com", executableName + ".cmd", executableName + ".bat", executableName + ".ps1" }
            : new[] { executableName };
        foreach (var pathEntry in SplitPath(pathVariable))
        {
            foreach (var candidateName in candidateNames)
            {
                var candidate = Path.Combine(pathEntry, candidateName);
                if (!File.Exists(candidate))
                {
                    continue;
                }

                if (IsWindowsWrapperFormat(candidate))
                {
                    return IsWindowsWrapper(candidate)
                        ? ResolveKnownPackageForWrapper(candidate, pathVariable, maximumManifestCharacters)
                        : throw new InvalidOperationException(UnsupportedShimMessage);
                }

                return ResolveExistingPath(candidate, pathVariable);
            }
        }

        throw new FileNotFoundException("The configured Codex CLI executable was not found by its exact name on the effective PATH.", executableName);
    }

    private static CliLaunchCommand ResolveExistingPath(string path, string? pathVariable)
    {
        var absolutePath = Path.GetFullPath(path);
        if (string.Equals(Path.GetExtension(absolutePath), ".js", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryFindJavaScriptRuntime(pathVariable, out var runtimePath))
            {
                throw new InvalidOperationException("The configured Codex JavaScript entrypoint requires Node.js or Bun on the effective PATH.");
            }

            return new CliLaunchCommand(runtimePath, ImmutableArray.Create(absolutePath));
        }

        return new CliLaunchCommand(absolutePath, ImmutableArray<string>.Empty);
    }

    internal static CliLaunchCommand ResolveNpm(string? pathVariable, int maximumManifestCharacters)
    {
        foreach (var pathEntry in SplitPath(pathVariable))
        {
            var npmExecutable = Path.Combine(pathEntry, OperatingSystem.IsWindows() ? "npm.exe" : "npm");
            if (File.Exists(npmExecutable))
            {
                return new CliLaunchCommand(Path.GetFullPath(npmExecutable), ImmutableArray<string>.Empty);
            }

            if (OperatingSystem.IsWindows())
            {
                var npmShim = Path.Combine(pathEntry, NpmShimName);
                if (File.Exists(npmShim) && TryResolveNpmPackage(npmShim, pathVariable, maximumManifestCharacters, out var launchCommand))
                {
                    return launchCommand;
                }

                if (File.Exists(npmShim))
                {
                    throw new InvalidOperationException("The npm command wrapper cannot be launched safely from its adjacent npm package.");
                }
            }
        }

        throw new FileNotFoundException("A safely launchable npm CLI was not found on the effective PATH.", NpmShimName);
    }

    private static CliLaunchCommand ResolveKnownPackageForWrapper(string wrapperPath, string? pathVariable, int maximumManifestCharacters)
    {
        foreach (var packageDirectory in GetPackageDirectoriesForWrapper(wrapperPath, PackageName))
        {
            if (TryResolvePackage(packageDirectory, pathVariable, maximumManifestCharacters, out var launchCommand))
            {
                return launchCommand;
            }
        }

        throw new InvalidOperationException(UnsupportedShimMessage);
    }

    private static IEnumerable<string> GetPackageDirectoriesForWrapper(string wrapperPath, string packageName)
    {
        var wrapperDirectory = Path.GetDirectoryName(wrapperPath)!;
        var packageSubpath = packageName == PackageName ? Path.Combine("@openai", "codex") : packageName;
        if (wrapperDirectory.Contains(Path.Combine(".bun", "bin"), StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.GetFullPath(Path.Combine(wrapperDirectory, "..", "install", "global", "node_modules", packageSubpath));
            yield break;
        }

        if (string.Equals(Path.GetFileName(wrapperDirectory), ".bin", StringComparison.OrdinalIgnoreCase))
        {
            var nodeModulesDirectory = Directory.GetParent(wrapperDirectory)?.FullName;
            if (nodeModulesDirectory is not null)
            {
                yield return Path.GetFullPath(Path.Combine(nodeModulesDirectory, packageSubpath));
            }

            yield break;
        }

        var packageRoot = string.Equals(Path.GetFileName(wrapperDirectory), "bin", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(wrapperDirectory, "..", "node_modules", packageSubpath)
            : Path.Combine(wrapperDirectory, "node_modules", packageSubpath);
        yield return Path.GetFullPath(packageRoot);
    }

    private static bool TryResolvePackage(string packageDirectory, string? pathVariable, int maximumManifestCharacters, out CliLaunchCommand launchCommand)
    {
        launchCommand = null!;
        var manifestPath = Path.Combine(packageDirectory, ManifestFileName);
        if (!TryReadManifest(manifestPath, maximumManifestCharacters, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetUniqueProperty(root, NamePropertyName, out var packageName) || packageName.ValueKind != JsonValueKind.String || packageName.GetString() != PackageName ||
                !TryGetUniqueProperty(root, BinPropertyName, out var bin))
            {
                return false;
            }

            var entryName = bin.ValueKind == JsonValueKind.Object && TryGetUniqueCommandEntry(bin, CommandName, out var commandEntry)
                ? commandEntry.ValueKind == JsonValueKind.String ? commandEntry.GetString() : null
                : null;
            if (string.IsNullOrWhiteSpace(entryName) || Path.IsPathRooted(entryName))
            {
                return false;
            }

            var absolutePackageDirectory = Path.GetFullPath(packageDirectory);
            var entryPath = Path.GetFullPath(Path.Combine(absolutePackageDirectory, entryName));
            var packagePrefix = absolutePackageDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!entryPath.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(entryPath))
            {
                return false;
            }

            var extension = Path.GetExtension(entryPath);
            if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".com", StringComparison.OrdinalIgnoreCase))
            {
                launchCommand = new CliLaunchCommand(entryPath, ImmutableArray<string>.Empty);
                return true;
            }

            if (!string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase) ||
                !TryFindJavaScriptRuntime(pathVariable, out var nodePath))
            {
                return false;
            }

            launchCommand = new CliLaunchCommand(nodePath, ImmutableArray.Create(entryPath));
            return true;
        }
    }

    private static bool TryResolveNpmPackage(string wrapperPath, string? pathVariable, int maximumManifestCharacters, out CliLaunchCommand launchCommand)
    {
        launchCommand = null!;
        foreach (var packageDirectory in GetPackageDirectoriesForWrapper(wrapperPath, NpmPackageName))
        {
            if (!TryReadManifest(Path.Combine(packageDirectory, ManifestFileName), maximumManifestCharacters, out var document))
            {
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !TryGetUniqueProperty(root, NamePropertyName, out var packageName) || packageName.ValueKind != JsonValueKind.String || packageName.GetString() != NpmPackageName ||
                    !TryGetUniqueProperty(root, BinPropertyName, out var bin) || bin.ValueKind != JsonValueKind.Object ||
                    !TryGetUniqueCommandEntry(bin, NpmCommandName, out var commandEntry) || commandEntry.ValueKind != JsonValueKind.String || commandEntry.GetString() != NpmEntryPoint)
                {
                    continue;
                }

                var scriptPath = Path.GetFullPath(Path.Combine(packageDirectory, NpmEntryPoint.Replace('/', Path.DirectorySeparatorChar)));
                var packageRoot = Path.GetFullPath(packageDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!scriptPath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(scriptPath) ||
                    !TryFindJavaScriptRuntime(pathVariable, out var runtimePath))
                {
                    continue;
                }

                launchCommand = new CliLaunchCommand(runtimePath, ImmutableArray.Create(scriptPath));
                return true;
            }
        }

        return false;
    }

    private static bool TryReadManifest(string path, int maximumManifestCharacters, out JsonDocument document)
    {
        document = null!;
        try
        {
            var manifestText = BoundedMetadataFileReader.ReadAllText(path, maximumManifestCharacters);
            document = JsonDocument.Parse(manifestText);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindJavaScriptRuntime(string? pathVariable, out string executablePath)
    {
        foreach (var entry in SplitPath(pathVariable))
        {
            foreach (var name in OperatingSystem.IsWindows()
                         ? new[] { NodeWindowsExecutableName, BunWindowsExecutableName }
                         : new[] { NodeUnixExecutableName, BunUnixExecutableName })
            {
                var candidate = Path.Combine(entry, name);
                if (File.Exists(candidate))
                {
                    executablePath = Path.GetFullPath(candidate);
                    return true;
                }
            }
        }

        executablePath = string.Empty;
        return false;
    }

    private static bool TryGetUniqueProperty(JsonElement element, string name, out JsonElement property)
    {
        property = default;
        var found = false;
        foreach (var candidate in element.EnumerateObject())
        {
            if (!candidate.NameEquals(name))
            {
                continue;
            }

            if (found)
            {
                property = default;
                return false;
            }

            property = candidate.Value;
            found = true;
        }

        return found;
    }

    private static bool TryGetUniqueCommandEntry(JsonElement bin, string commandName, out JsonElement entry)
    {
        entry = default;
        var found = false;
        foreach (var candidate in bin.EnumerateObject())
        {
            if (!string.Equals(candidate.Name, commandName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (found)
            {
                entry = default;
                return false;
            }

            entry = candidate.Value;
            found = true;
        }

        return found;
    }

    private static IEnumerable<string> GetNativeCandidates() => OperatingSystem.IsWindows()
        ? [CodexCliLocator.CodexWindowsExecutableName, WindowsComName]
        : ["codex"];

    private static bool IsWindowsWrapper(string path) => OperatingSystem.IsWindows() &&
        (string.Equals(Path.GetFileName(path), CodexCliLocator.CodexWindowsCommandName, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Path.GetFileName(path), CodexCliLocator.CodexWindowsBatchName, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Path.GetFileName(path), WindowsPowerShellName, StringComparison.OrdinalIgnoreCase));

    private static bool IsWindowsWrapperFormat(string path) => OperatingSystem.IsWindows() &&
        (string.Equals(Path.GetExtension(path), ".cmd", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Path.GetExtension(path), ".bat", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(Path.GetExtension(path), ".ps1", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> SplitPath(string? pathVariable)
    {
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            yield break;
        }

        foreach (var entry in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return entry.Trim('"');
        }
    }

}
