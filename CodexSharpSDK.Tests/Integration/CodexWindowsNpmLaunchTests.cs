using System.Text.Json;
using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Execution;
using ManagedCode.CodexSharpSDK.Extensions.AI;
using Microsoft.Extensions.AI;

namespace ManagedCode.CodexSharpSDK.Tests.Integration;

public sealed class CodexWindowsNpmLaunchTests
{
    private const string SkipReason = "The npm shim launch fixture requires Windows and an installed Node.js runtime.";
    private const string PackageName = "@openai/codex";
    private const string PackageVersion = "0.160.1";
    private const string PackageBin = "bin/codex.js";
    private const string CaptureEnvironmentName = "CODEX_SDK_LAUNCH_CAPTURE";
    private const string SystemRootEnvironmentName = "SystemRoot";
    private const string PathEnvironmentName = "PATH";
    private const string FixturePrefix = "Codex Windows npm shim fixture ";
    private const string EntryScript = "const fs = require('node:fs');\n" +
        "const args = process.argv.slice(2);\n" +
        "const input = fs.readFileSync(0, 'utf8');\n" +
        "fs.writeFileSync(process.env.CODEX_SDK_LAUNCH_CAPTURE, JSON.stringify({args, input}));\n" +
        "if (args.includes('--version')) { console.log('codex-cli 0.160.1'); process.exit(0); }\n" +
        "console.log(JSON.stringify({type:'thread.started', thread_id:'fixture'}));\n" +
        "console.log(JSON.stringify({type:'item.completed', item:{id:'message', type:'agent_message', text:input}}));\n" +
        "console.log(JSON.stringify({type:'turn.completed',usage:{input_tokens:1,cached_input_tokens:0,output_tokens:1}}));\n";

    [Test]
    public async Task WindowsNpmShim_UsesNodeAndLiteralArgumentsForMetadataCoreAndMeai()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test(SkipReason);
            return;
        }

        var nodePath = FindNodeExecutable();
        foreach (var isGlobalLayout in new[] { false, true })
        {
            using var fixture = new NpmFixture(isGlobalLayout, nodePath);
            var options = fixture.CreateOptions(inheritEnvironmentVariables: isGlobalLayout);
            using var client = new CodexClient(options);

            var launchCommand = client.GetCliLaunchCommand();
            await Assert.That(Path.GetFileName(launchCommand.ExecutablePath)).IsEqualTo("node.exe");
            await Assert.That(launchCommand.PrefixArguments).Count().IsEqualTo(1);
            await Assert.That(launchCommand.PrefixArguments[0]).IsEqualTo(fixture.EntryPoint);

            var explicitScriptLaunch = fixture.CreateOptions(inheritEnvironmentVariables: false) with
            {
                CodexExecutablePath = fixture.EntryPoint,
            };
            await Assert.That(explicitScriptLaunch.GetCliLaunchCommand().ExecutablePath).IsEqualTo(nodePath);
            await Assert.That(explicitScriptLaunch.GetCliLaunchCommand().PrefixArguments[0]).IsEqualTo(fixture.EntryPoint);

            var metadata = client.GetCliMetadata();
            await Assert.That(metadata.InstalledVersion).IsEqualTo(PackageVersion);

            var prompt = string.Concat("prompt with spaces ; & $() ", new string('x', 256 * 1024));
            const string literalArgument = "value with spaces ; & $()";
            using var exec = new CodexExec(fixture.ShimPath, fixture.EnvironmentVariables);
            var output = await DrainAsync(exec.RunAsync(new CodexExecArgs
            {
                Input = prompt,
                AdditionalCliArguments = ["--fixture-value", literalArgument],
            }));

            await Assert.That(output.Any(line => line.Contains("\"type\":\"turn.completed\"", StringComparison.Ordinal))).IsTrue();
            var capture = ReadCapture(fixture.CapturePath);
            await Assert.That(capture.Input).IsEqualTo(prompt);
            await Assert.That(capture.Arguments).Contains("--fixture-value");
            await Assert.That(capture.Arguments).Contains(literalArgument);

            const string meaiPrompt = "MEAI prompt with spaces ; & $()";
            using var chatClient = new CodexChatClient(new CodexChatClientOptions { CodexOptions = options });
            var response = await chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, meaiPrompt)]);
            var text = response.Messages.SelectMany(message => message.Contents).OfType<TextContent>()
                .Select(content => content.Text).FirstOrDefault();
            await Assert.That(text).IsEqualTo(meaiPrompt);
            await Assert.That(ReadCapture(fixture.CapturePath).Input).IsEqualTo(meaiPrompt);
        }
    }

    [Test]
    public async Task WindowsResolver_RejectsMissingExplicitNameAndUnrelatedOrUnsupportedShims()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test(SkipReason);
            return;
        }

        var nodePath = FindNodeExecutable();
        using var missingNameFixture = new NpmFixture(isGlobalLayout: true, nodePath, createPackage: false);
        var decoyCliPath = Path.Combine(Path.GetDirectoryName(missingNameFixture.ShimPath)!, "codex.exe");
        File.Copy(nodePath, decoyCliPath);
        var missingNameOptions = missingNameFixture.CreateOptions(inheritEnvironmentVariables: false) with
        {
            CodexExecutablePath = "missing-codex-cli",
        };
        var missingNameAction = () => missingNameOptions.GetCliLaunchCommand();
        var missingNameError = await Assert.That(missingNameAction).ThrowsException();
        await Assert.That(missingNameError).IsTypeOf<FileNotFoundException>();

        using var unrelatedPackageFixture = new NpmFixture(isGlobalLayout: true, nodePath);
        var unrelatedEnvironment = missingNameFixture.EnvironmentVariables.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        unrelatedEnvironment[PathEnvironmentName] = string.Join(Path.PathSeparator,
            missingNameFixture.EnvironmentVariables[PathEnvironmentName],
            Path.GetDirectoryName(unrelatedPackageFixture.ShimPath));
        var unrelatedPathOptions = missingNameFixture.CreateOptions(inheritEnvironmentVariables: false) with
        {
            EnvironmentVariables = unrelatedEnvironment,
        };
        var unrelatedWrapperOptions = unrelatedPathOptions with { CodexExecutablePath = missingNameFixture.ShimPath };
        var unrelatedWrapperAction = () => unrelatedWrapperOptions.GetCliLaunchCommand();
        var unrelatedWrapperError = await Assert.That(unrelatedWrapperAction).ThrowsException();
        await Assert.That(unrelatedWrapperError).IsTypeOf<InvalidOperationException>();

        using var unsupportedBinFixture = new NpmFixture(isGlobalLayout: true, nodePath, binEntry: "bin/unsupported.cmd");
        var unsupportedBinOptions = unsupportedBinFixture.CreateOptions(inheritEnvironmentVariables: false);
        var unsupportedBinAction = () => unsupportedBinOptions.GetCliLaunchCommand();
        var unsupportedBinError = await Assert.That(unsupportedBinAction).ThrowsException();
        await Assert.That(unsupportedBinError).IsTypeOf<InvalidOperationException>();

        using var oversizedManifestFixture = new NpmFixture(isGlobalLayout: true, nodePath, manifestPaddingCharacters: 256);
        var oversizedManifestOptions = oversizedManifestFixture.CreateOptions(inheritEnvironmentVariables: false) with
        {
            CliMetadataMaximumFileCharacters = 128,
        };
        var oversizedManifestAction = () => oversizedManifestOptions.GetCliLaunchCommand();
        var oversizedManifestError = await Assert.That(oversizedManifestAction).ThrowsException();
        await Assert.That(oversizedManifestError).IsTypeOf<InvalidOperationException>();
        await Assert.That(oversizedManifestError!.Message).Contains("configured character limit");
    }

    private static string FindNodeExecutable()
    {
        foreach (var entry in (Environment.GetEnvironmentVariable(PathEnvironmentName) ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(entry.Trim('"'), "node.exe");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException("Node.js must be available on PATH for this Windows CLI launch regression.");
    }

    private static async Task<List<string>> DrainAsync(IAsyncEnumerable<string> lines)
    {
        var result = new List<string>();
        await foreach (var line in lines)
        {
            result.Add(line);
        }

        return result;
    }

    private static CapturedInvocation ReadCapture(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        return new CapturedInvocation(
            root.GetProperty("args").EnumerateArray().Select(value => value.GetString()!).ToArray(),
            root.GetProperty("input").GetString()!);
    }

    private sealed record CapturedInvocation(IReadOnlyList<string> Arguments, string Input);

    private sealed class NpmFixture : IDisposable
    {
        private readonly string _root;

        internal NpmFixture(bool isGlobalLayout, string nodePath, bool createPackage = true, string binEntry = PackageBin,
            int manifestPaddingCharacters = 0)
        {
            _root = Path.Combine(Environment.CurrentDirectory, "tests", ".sandbox", FixturePrefix + Guid.NewGuid().ToString("N"));
            var npmBinDirectory = isGlobalLayout ? _root : Path.Combine(_root, "node_modules", ".bin");
            var packageDirectory = Path.Combine(_root, "node_modules", "@openai", "codex");
            Directory.CreateDirectory(npmBinDirectory);
            EntryPoint = Path.Combine(packageDirectory, binEntry.Replace('/', Path.DirectorySeparatorChar));
            if (createPackage)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(EntryPoint)!);
                File.WriteAllText(Path.Combine(packageDirectory, "package.json"),
                    "{\"name\":\"" + PackageName + "\",\"version\":\"" + PackageVersion +
                    "\",\"bin\":{\"codex\":\"" + binEntry + "\"},\"metadataPadding\":\"" +
                    new string('x', manifestPaddingCharacters) + "\"}");
                File.WriteAllText(EntryPoint, EntryScript);
            }
            ShimPath = Path.Combine(npmBinDirectory, "codex.cmd");
            File.WriteAllText(ShimPath, "@ECHO off\r\nexit /b 91\r\n");
            CapturePath = Path.Combine(_root, "captured invocation.json");
            var nodeDirectory = Path.GetDirectoryName(nodePath)!;
            EnvironmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [PathEnvironmentName] = string.Join(Path.PathSeparator, npmBinDirectory, nodeDirectory),
                [SystemRootEnvironmentName] = Environment.GetEnvironmentVariable(SystemRootEnvironmentName) ?? string.Empty,
                [CaptureEnvironmentName] = CapturePath,
            };
        }

        internal string EntryPoint { get; }
        internal string ShimPath { get; }
        internal string CapturePath { get; }
        internal Dictionary<string, string> EnvironmentVariables { get; }

        internal CodexOptions CreateOptions(bool inheritEnvironmentVariables) => new()
        {
            CodexExecutablePath = ShimPath,
            InheritEnvironmentVariables = inheritEnvironmentVariables,
            EnvironmentVariables = EnvironmentVariables,
        };

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
