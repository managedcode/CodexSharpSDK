using ManagedCode.CodexSharpSDK.Internal;

namespace ManagedCode.CodexSharpSDK.Tests.Unit;

public sealed class CliVersionAcceptanceTests
{
    private const string VersionOutputPrefix = "codex-cli ";
    private const string OlderVersion = "0.160.1";
    private const string CurrentVersion = "0.163.0";
    private const string FutureVersion = "9999.0.1-preview.1";

    [Test]
    [Arguments(OlderVersion)]
    [Arguments(CurrentVersion)]
    [Arguments(FutureVersion)]
    public async Task InstalledVersionDoesNotNeedToMatchTheSdkTarget(string version)
    {
        var output = string.Concat(VersionOutputPrefix, version);
        var observed = CodexCliMetadataReader.ParseInstalledVersion(output);

        await Assert.That(observed).IsEqualTo(version);
    }
}
