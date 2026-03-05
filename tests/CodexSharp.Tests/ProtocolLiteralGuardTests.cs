namespace ManagedCode.CodexSharp.Tests;

public class ProtocolLiteralGuardTests
{
    [Test]
    public async Task ItemsFile_DoesNotContainInlineThreadItemTypeLiterals()
    {
        var content = await File.ReadAllTextAsync(ResolveRepoFilePath("src/CodexSharp/Items.cs"));
        await Assert.That(content.Contains("ThreadItem(Id, \"", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task EventsFile_DoesNotContainInlineThreadEventTypeLiterals()
    {
        var content = await File.ReadAllTextAsync(ResolveRepoFilePath("src/CodexSharp/Events.cs"));
        await Assert.That(content.Contains("ThreadEvent(\"", StringComparison.Ordinal)).IsFalse();
    }

    private static string ResolveRepoFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CodexSharp.slnx")))
            {
                return Path.Combine(current.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test execution directory.");
    }
}
