using System.Runtime.InteropServices;

namespace ManagedCode.CodexSharpSDK.Internal;

internal static class CodexCliLocator
{
    private const string PathEnvironmentVariable = "PATH";
    private const string CmdScriptExtension = ".cmd";
    private const string BatScriptExtension = ".bat";
    private const string NpmScopePrefix = "@openai/";
    private const string NodeModulesDirectory = "node_modules";
    private const string OpenAiScopeDirectory = "@openai";
    private const string VendorDirectory = "vendor";
    private const string TargetCodexDirectory = "codex";
    private const string NestedCodexPackageDirectory = "codex";

    private const string TargetLinuxX64 = "x86_64-unknown-linux-musl";
    private const string TargetLinuxArm64 = "aarch64-unknown-linux-musl";
    private const string TargetDarwinX64 = "x86_64-apple-darwin";
    private const string TargetDarwinArm64 = "aarch64-apple-darwin";
    private const string TargetWindowsX64 = "x86_64-pc-windows-msvc";
    private const string TargetWindowsArm64 = "aarch64-pc-windows-msvc";

    private const string PackageCodexLinuxX64 = "@openai/codex-linux-x64";
    private const string PackageCodexLinuxArm64 = "@openai/codex-linux-arm64";
    private const string PackageCodexDarwinX64 = "@openai/codex-darwin-x64";
    private const string PackageCodexDarwinArm64 = "@openai/codex-darwin-arm64";
    private const string PackageCodexWindowsX64 = "@openai/codex-win32-x64";
    private const string PackageCodexWindowsArm64 = "@openai/codex-win32-arm64";

    internal const string CodexExecutableName = "codex";
    internal const string CodexWindowsExecutableName = "codex.exe";
    internal const string CodexWindowsCommandName = CodexExecutableName + CmdScriptExtension;
    internal const string CodexWindowsBatchName = CodexExecutableName + BatScriptExtension;

    private static readonly string[] WindowsPathExecutableCandidates =
    [
        CodexWindowsExecutableName,
        CodexWindowsCommandName,
        CodexWindowsBatchName,
        CodexExecutableName,
    ];

    private static readonly string[] UnixPathExecutableCandidates =
    [
        CodexExecutableName,
    ];

    private static readonly Dictionary<string, string> PlatformPackageByTarget =
        new(StringComparer.Ordinal)
        {
            [TargetLinuxX64] = PackageCodexLinuxX64,
            [TargetLinuxArm64] = PackageCodexLinuxArm64,
            [TargetDarwinX64] = PackageCodexDarwinX64,
            [TargetDarwinArm64] = PackageCodexDarwinArm64,
            [TargetWindowsX64] = PackageCodexWindowsX64,
            [TargetWindowsArm64] = PackageCodexWindowsArm64,
        };

    public static string FindCodexPath(string? codexPathOverride)
    {
        if (!string.IsNullOrWhiteSpace(codexPathOverride))
        {
            return codexPathOverride;
        }

        if (TryResolveNpmInstalledBinary(out var resolvedPath))
        {
            return resolvedPath;
        }

        if (TryResolvePathExecutable(Environment.GetEnvironmentVariable(PathEnvironmentVariable), OperatingSystem.IsWindows(), out var pathExecutable))
        {
            return pathExecutable;
        }

        return OperatingSystem.IsWindows()
            ? CodexWindowsExecutableName
            : CodexExecutableName;
    }

    internal static bool TryResolvePathExecutable(string? pathVariable, bool isWindows, out string executablePath)
    {
        executablePath = string.Empty;

        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return false;
        }

        var candidateNames = GetPathExecutableCandidates(isWindows);
        foreach (var pathEntry in SplitPathVariable(pathVariable))
        {
            foreach (var candidateName in candidateNames)
            {
                var candidatePath = Path.Combine(pathEntry, candidateName);
                if (File.Exists(candidatePath))
                {
                    executablePath = candidatePath;
                    return true;
                }
            }
        }

        return false;
    }

    internal static IReadOnlyList<string> GetPathExecutableCandidates(bool isWindows)
    {
        return isWindows
            ? WindowsPathExecutableCandidates
            : UnixPathExecutableCandidates;
    }

    private static bool TryResolveNpmInstalledBinary(out string binaryPath)
    {
        binaryPath = string.Empty;

        var targetTriple = GetTargetTriple();
        if (targetTriple is null)
        {
            return false;
        }

        if (!PlatformPackageByTarget.TryGetValue(targetTriple, out var packageName))
        {
            return false;
        }

        if (!packageName.StartsWith(NpmScopePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var packageDirectory = packageName[NpmScopePrefix.Length..];
        var executableName = OperatingSystem.IsWindows() ? CodexWindowsExecutableName : CodexExecutableName;

        foreach (var root in EnumerateSearchRoots())
        {
            var primaryPath = Path.Combine(
                root,
                NodeModulesDirectory,
                OpenAiScopeDirectory,
                packageDirectory,
                VendorDirectory,
                targetTriple,
                TargetCodexDirectory,
                executableName);

            if (File.Exists(primaryPath))
            {
                binaryPath = primaryPath;
                return true;
            }

            var nestedPath = Path.Combine(
                root,
                NodeModulesDirectory,
                OpenAiScopeDirectory,
                NestedCodexPackageDirectory,
                NodeModulesDirectory,
                OpenAiScopeDirectory,
                packageDirectory,
                VendorDirectory,
                targetTriple,
                TargetCodexDirectory,
                executableName);

            if (File.Exists(nestedPath))
            {
                binaryPath = nestedPath;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SplitPathVariable(string pathVariable)
    {
        foreach (var rawPathEntry in pathVariable.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmedPathEntry = rawPathEntry.Trim('"');
            if (string.IsNullOrWhiteSpace(trimmedPathEntry))
            {
                continue;
            }

            yield return trimmedPathEntry;
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in EnumerateUpwards(Environment.CurrentDirectory))
        {
            if (seen.Add(root))
            {
                yield return root;
            }
        }

        foreach (var root in EnumerateUpwards(AppContext.BaseDirectory))
        {
            if (seen.Add(root))
            {
                yield return root;
            }
        }
    }

    private static IEnumerable<string> EnumerateUpwards(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            yield break;
        }

        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static string? GetTargetTriple()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => TargetLinuxX64,
                Architecture.Arm64 => TargetLinuxArm64,
                _ => null,
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => TargetDarwinX64,
                Architecture.Arm64 => TargetDarwinArm64,
                _ => null,
            };
        }

        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => TargetWindowsX64,
                Architecture.Arm64 => TargetWindowsArm64,
                _ => null,
            };
        }

        return null;
    }
}
