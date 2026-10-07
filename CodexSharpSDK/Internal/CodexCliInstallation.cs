using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text.Json;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Internal;

internal static class CodexCliInstallation
{
    private const string PackageName = "@openai/codex";
    private const string PackageScopeDirectoryName = "@openai";
    private const string PackageDirectoryName = "codex";
    private const string PackageJsonFileName = "package.json";
    private const string PackageNameProperty = "name";
    private const string PackageVersionProperty = "version";
    private const string NpmMarker = ".managedcode-cli-installation";
    private const string MarkerValue = "ManagedCode.CodexSharpSDK";
    private const string RootNotEmptyMessage = "The installation root is not an SDK-owned Codex CLI installation.";
    private const string MarkerMismatchMessage = "The installation root belongs to a different SDK-managed CLI installation.";
    private const string ManagerMessage = "The package-manager executable and script configuration is invalid.";
    private const string LocalApplicationDataRootMessage = "The local application-data root must be an absolute path.";
    private const string LockMessage = "The CLI installation root remained busy past its configured lock timeout.";
    private const string VersionMessage = "The installed Codex CLI version does not match the SDK compatibility target.";
    private const string NpmInstall = "install";
    private const string BunAdd = "install";
    private const string PrefixOption = "--prefix";
    private const string CwdOption = "--cwd";
    private const string NoSave = "--no-save";
    private const string NoAudit = "--no-audit";
    private const string NoFund = "--no-fund";
    private const string NoLock = "--package-lock=false";
    private const string DirectoryName = "ManagedCode.CodexSharpSDK";
    private const string ProcessPathName = "PATH";
    private const string ManagedCodeDirectoryName = "ManagedCode";
    private const string InstallDirectoryName = "cli";
    private const string NodeModulesDirectoryName = "node_modules";
    private const string BinDirectoryName = ".bin";
    private const string CommandName = "codex";
    private const string WindowsCommandName = "codex.cmd";
    private const string InstallLockFileName = ".install.lock";
    private const string PackageSeparator = "@";
    private const string BunWindowsExecutableName = "bun.exe";
    private const string BunExecutableName = "bun";
    private const string NodeWindowsExecutableName = "node.exe";
    private const string NodeExecutableName = "node";
    private const string NpmCliEntryPoint = "npm-cli.js";
    private const string NpmBinDirectoryName = "bin";
    private const string NpmDirectoryName = "npm";
    private const string NpmPackageName = "npm";
    private const int LockRetryMilliseconds = 25;

    internal static async IAsyncEnumerable<CliInstallationUpdate> InstallOrUpdateAsync(
        CliInstallationOptions options,
        string localApplicationDataRoot,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataRoot);
        if (!Path.IsPathFullyQualified(localApplicationDataRoot))
        {
            throw new ArgumentException(LocalApplicationDataRootMessage, nameof(localApplicationDataRoot));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var root = Validate(options, localApplicationDataRoot);
        ValidatePackageManager(options);
        CliInstallationEnvironment.Validate(options.EnvironmentVariables);
        await using var rootLock = await AcquireRootLockAsync(root, options.InstallationLockTimeout, cancellationToken)
            .ConfigureAwait(false);
        EnsureOwnedRoot(root);
        var environment = CliInstallationEnvironment.Create(options.EnvironmentVariables, root);
        var manager = options.PackageManager == CliPackageManager.Npm
            ? new CliLaunchCommand(options.PackageManagerExecutablePath, [options.NpmCliScriptPath!])
            : new CliLaunchCommand(options.PackageManagerExecutablePath, []);
        var packageVersion = PackageName + PackageSeparator + CodexCliCompatibility.TargetVersion;
        var arguments = options.PackageManager switch
        {
            CliPackageManager.Npm => new[] { NpmInstall, PrefixOption, root, NoSave, NoAudit, NoFund, NoLock, packageVersion },
            CliPackageManager.Bun => new[] { BunAdd, CwdOption, root, NoSave, packageVersion },
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.PackageManager, ManagerMessage)
        };
        await foreach (var update in CliInstallationProcessRunner.RunAsync(manager, arguments, environment, root,
                           options.InstallTimeout, options.ProcessTerminationTimeout, options.MaximumOutputCharacters,
                           cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }

        yield return new CliInstallationUpdate(CliInstallationStage.Verifying, 0, 0);
        var shim = Path.Combine(root, NodeModulesDirectoryName, BinDirectoryName,
            OperatingSystem.IsWindows() ? WindowsCommandName : CommandName);
        var launch = CliLaunchCommandResolver.Resolve(shim, environment.GetValueOrDefault(ProcessPathName),
            options.MaximumMetadataFileCharacters);
        var packageManifest = Path.Combine(root, NodeModulesDirectoryName, PackageScopeDirectoryName,
            PackageDirectoryName, PackageJsonFileName);
        var installedVersion = ReadInstalledPackageVersion(packageManifest, options.MaximumMetadataFileCharacters);
        if (!string.Equals(installedVersion, CodexCliCompatibility.TargetVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(VersionMessage);
        }

        yield return new CliInstallationUpdate(CliInstallationStage.Installed, 0, 0,
            new CliInstallationResult(installedVersion, CodexCliCompatibility.TargetVersion, root, launch));
    }

    private static string Validate(CliInstallationOptions options, string localApplicationDataRoot)
    {
        var npmInvalid = options.PackageManager == CliPackageManager.Npm &&
                         (string.IsNullOrWhiteSpace(options.NpmCliScriptPath) || !Path.IsPathFullyQualified(options.NpmCliScriptPath) ||
                          !File.Exists(options.NpmCliScriptPath));
        if (!Enum.IsDefined(options.PackageManager) || !Path.IsPathFullyQualified(options.PackageManagerExecutablePath) ||
            !File.Exists(options.PackageManagerExecutablePath) || npmInvalid ||
            (options.PackageManager == CliPackageManager.Bun && options.NpmCliScriptPath is not null))
        {
            throw new ArgumentException(ManagerMessage, nameof(options));
        }

        ArgumentNullException.ThrowIfNull(options.EnvironmentVariables);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.InstallTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ProcessTerminationTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.InstallationLockTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumOutputCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumMetadataFileCharacters);
        return Path.GetFullPath(Path.Combine(localApplicationDataRoot, ManagedCodeDirectoryName, DirectoryName,
            InstallDirectoryName));
    }

    private static void ValidatePackageManager(CliInstallationOptions options)
    {
        var expectedExecutable = options.PackageManager == CliPackageManager.Bun
            ? OperatingSystem.IsWindows() ? BunWindowsExecutableName : BunExecutableName
            : OperatingSystem.IsWindows() ? NodeWindowsExecutableName : NodeExecutableName;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetFileName(options.PackageManagerExecutablePath), expectedExecutable, comparison))
        {
            throw new ArgumentException(ManagerMessage, nameof(options));
        }

        if (options.PackageManager != CliPackageManager.Npm)
        {
            return;
        }

        var script = Path.GetFullPath(options.NpmCliScriptPath!);
        var binDirectory = Path.GetDirectoryName(script)!;
        var npmRoot = Directory.GetParent(binDirectory)?.FullName;
        if (!string.Equals(Path.GetFileName(script), NpmCliEntryPoint, comparison) ||
            !string.Equals(Path.GetFileName(binDirectory), NpmBinDirectoryName, comparison) || npmRoot is null ||
            !string.Equals(Path.GetFileName(npmRoot), NpmDirectoryName, comparison))
        {
            throw new ArgumentException(ManagerMessage, nameof(options));
        }

        using var manifest = JsonDocument.Parse(BoundedMetadataFileReader.ReadAllText(
            Path.Combine(npmRoot, PackageJsonFileName), options.MaximumMetadataFileCharacters));
        var names = manifest.RootElement.ValueKind == JsonValueKind.Object
            ? manifest.RootElement.EnumerateObject().Where(property => property.NameEquals(PackageNameProperty)).ToArray()
            : [];
        if (names.Length != 1 || names[0].Value.ValueKind != JsonValueKind.String ||
            !string.Equals(names[0].Value.GetString(), NpmPackageName, StringComparison.Ordinal))
        {
            throw new ArgumentException(ManagerMessage, nameof(options));
        }
    }

    private static string ReadInstalledPackageVersion(string manifestPath, int maximumCharacters)
    {
        using var manifest = JsonDocument.Parse(BoundedMetadataFileReader.ReadAllText(manifestPath, maximumCharacters));
        if (manifest.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(VersionMessage);
        }

        var names = manifest.RootElement.EnumerateObject().Where(property => property.NameEquals(PackageNameProperty)).ToArray();
        var versions = manifest.RootElement.EnumerateObject().Where(property => property.NameEquals(PackageVersionProperty)).ToArray();
        if (names.Length != 1 || versions.Length != 1 || names[0].Value.ValueKind != JsonValueKind.String ||
            !string.Equals(names[0].Value.GetString(), PackageName, StringComparison.Ordinal) ||
            versions[0].Value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(VersionMessage);
        }

        return versions[0].Value.GetString()!;
    }

    private static async Task<FileStream> AcquireRootLockAsync(string root, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, InstallLockFileName);
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (timer.Elapsed < timeout)
            {
                await Task.Delay(LockRetryMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                throw new TimeoutException(LockMessage);
            }
        }
    }

    private static void EnsureOwnedRoot(string root)
    {
        var marker = Path.Combine(root, NpmMarker);
        if (File.Exists(marker))
        {
            if (new FileInfo(marker).Length != MarkerValue.Length ||
                !string.Equals(File.ReadAllText(marker), MarkerValue, StringComparison.Ordinal))
            {
                throw new SecurityException(MarkerMismatchMessage);
            }

            return;
        }

        if (Directory.EnumerateFileSystemEntries(root).Any(path =>
                !string.Equals(path, Path.Combine(root, InstallLockFileName), StringComparison.Ordinal)))
        {
            throw new SecurityException(RootNotEmptyMessage);
        }

        File.WriteAllText(marker, MarkerValue);
    }
}

internal static class CliInstallationEnvironment
{
    private const string PathName = "PATH";
    private const string HomeName = "HOME";
    private const string UserProfileName = "USERPROFILE";
    private const string SystemRootName = "SYSTEMROOT";
    private const string TempName = "TEMP";
    private const string TmpName = "TMP";
    private const string AppDataName = "APPDATA";
    private const string LocalAppDataName = "LOCALAPPDATA";
    private const string XdgConfigName = "XDG_CONFIG_HOME";
    private const string XdgCacheName = "XDG_CACHE_HOME";
    private const string LanguageName = "LANG";
    private const string LocaleName = "LC_ALL";
    private const string CertificateFileName = "SSL_CERT_FILE";
    private const string CertificateDirectoryName = "SSL_CERT_DIR";
    private const string HomeDirectoryName = ".home";
    private const string TempDirectoryName = ".tmp";
    private const string ConfigDirectoryName = ".config";
    private const string CacheDirectoryName = ".cache";
    private const string EnvironmentMessage = "The package-manager environment contains a key outside the supported safe allowlist.";
    private const string PathMessage = "An explicit PATH value is required for CLI installation.";
    private const string SystemRootMessage = "An explicit SystemRoot value is required for Windows CLI installation.";
    private static readonly string[] AllowedNames =
    [PathName, HomeName, UserProfileName, SystemRootName, TempName, TmpName, AppDataName, LocalAppDataName,
        XdgConfigName, XdgCacheName, LanguageName, LocaleName, CertificateFileName, CertificateDirectoryName];

    internal static Dictionary<string, string> Create(IReadOnlyDictionary<string, string> configured, string root)
    {
        Validate(configured);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in configured)
        {
            var name = AllowedNames.FirstOrDefault(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
            if (name is null || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(EnvironmentMessage, nameof(configured));
            }

            result[name] = value;
        }

        var home = Path.Combine(root, HomeDirectoryName);
        var temp = Path.Combine(root, TempDirectoryName);
        result[HomeName] = home;
        result[UserProfileName] = home;
        result[TempName] = temp;
        result[TmpName] = temp;
        result[XdgConfigName] = Path.Combine(root, ConfigDirectoryName);
        result[XdgCacheName] = Path.Combine(root, CacheDirectoryName);
        result[AppDataName] = result[XdgConfigName];
        result[LocalAppDataName] = result[XdgCacheName];
        foreach (var directory in new[] { home, temp, result[XdgConfigName], result[XdgCacheName] })
        {
            Directory.CreateDirectory(directory);
        }

        return result;
    }

    internal static void Validate(IReadOnlyDictionary<string, string> configured)
    {
        var pathFound = false;
        var systemRootFound = false;
        foreach (var (key, value) in configured)
        {
            var name = AllowedNames.FirstOrDefault(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase));
            if (name is null || string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(EnvironmentMessage, nameof(configured));
            }

            pathFound |= string.Equals(name, PathName, StringComparison.Ordinal);
            systemRootFound |= string.Equals(name, SystemRootName, StringComparison.Ordinal);
        }

        if (!pathFound)
        {
            throw new ArgumentException(PathMessage, nameof(configured));
        }

        if (OperatingSystem.IsWindows() && !systemRootFound)
        {
            throw new ArgumentException(SystemRootMessage, nameof(configured));
        }
    }
}
