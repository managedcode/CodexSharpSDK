using System.Diagnostics;
using ManagedCode.CodexSharpSDK.Client;
using ManagedCode.CodexSharpSDK.Configuration;
using ManagedCode.CodexSharpSDK.Models;

namespace ManagedCode.CodexSharpSDK.Tests.Integration;

[NotInParallel(CliProcessConstraint)]
public class CliLoginTests
{
    private const string CliProcessConstraint = "CliProcess";
    private const string RequiresCodexAuthProperty = "RequiresCodexAuth";
    private const string GuidFormat = "N";
    private const string SolutionFile = "ManagedCode.CodexSharpSDK.slnx";
    private const string TestsDirectory = "tests";
    private const string SandboxDirectory = ".sandbox";
    private const string SandboxPrefix = "CliLogin-";
    private const string PathVariable = "PATH";
    private const string HomeVariable = "HOME";
    private const string UserProfileVariable = "USERPROFILE";
    private const string ProviderHomeVariable = "CODEX_HOME";
    private const string ConfigHomeVariable = "XDG_CONFIG_HOME";
    private const string AppDataVariable = "APPDATA";
    private const string LocalAppDataVariable = "LOCALAPPDATA";
    private const string SystemRootVariable = "SystemRoot";
    private const string CiVariable = "CI";
    private const string BrowserVariable = "BROWSER";
    private const string NoBrowserCommand = "false";
    private const string TrueValue = "true";
    private const string HttpsScheme = "https";
    private const string HelpFlag = "--help";
    private const string CredentialFile = "auth.json";
    private const string PlainCode = "invalid-code";
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(60);

    [Test]
    public async Task BeginLogin_AlreadyCancelled_DoesNotWriteCredentials()
    {
        using var profile = new LoginProfile();
        using var client = new CodexClient(profile.CreateOptions());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await client.BeginLoginAsync(cancellation.Token));
        await Assert.That(profile.HasCredentials).IsFalse();
    }

    [Test]
    public async Task BeginLogin_NativeHelpExit_IsNotAuthenticationSuccess()
    {
        using var profile = new LoginProfile();
        var options = profile.CreateOptions();
        var launch = options.GetCliLaunchCommand();
        using var client = new CodexClient(options with
        {
            LaunchCommand = new CliLaunchCommand(launch.ExecutablePath, launch.PrefixArguments.Add(HelpFlag)),
        });
        using var deadline = new CancellationTokenSource(LoginTimeout);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await client.BeginLoginAsync(deadline.Token));
        await Assert.That(profile.HasCredentials).IsFalse();
    }

    [Property(RequiresCodexAuthProperty, TrueValue)]
    [Test]
    public async Task BeginLogin_NativeInstructions_WaitCancellationAndSessionCancellationHaveDifferentEffects()
    {
        using var profile = new LoginProfile();
        using var client = new CodexClient(profile.CreateOptions());
        using var deadline = new CancellationTokenSource(LoginTimeout);
        await using var login = await client.BeginLoginAsync(deadline.Token);
        var processId = login.ProcessId;
        var instructions = login.Instructions;
        await Assert.That(instructions).IsNotNull();
        await Assert.That(instructions!.LoginUrl.Scheme).IsEqualTo(HttpsScheme);
        await Assert.That(instructions.RequiresAuthorizationCode).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(instructions.UserCode)).IsFalse();
        using var observation = new CancellationTokenSource();
        await observation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await login.WaitForCompletionAsync(observation.Token));
        using (var native = Process.GetProcessById(processId))
        {
            await Assert.That(native.HasExited).IsFalse();
        }
        await Assert.ThrowsAsync<NotSupportedException>(async () => await login.SubmitAuthorizationCodeAsync(PlainCode));
        await login.CancelAsync(deadline.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await login.WaitForCompletionAsync());
        await Assert.That(IsRunning(processId)).IsFalse();
        await Assert.That(profile.HasCredentials).IsFalse();
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed class LoginProfile : IDisposable
    {
        private readonly string _root;
        private readonly CliLaunchCommand _launch;

        public LoginProfile()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, SolutionFile)))
            {
                current = current.Parent;
            }
            _root = Path.Combine(current!.FullName, TestsDirectory, SandboxDirectory,
                string.Concat(SandboxPrefix, Guid.NewGuid().ToString(GuidFormat)));
            Directory.CreateDirectory(_root);
            _launch = new CodexOptions().GetCliLaunchCommand();
        }

        public bool HasCredentials => Directory.GetFiles(_root, CredentialFile, SearchOption.AllDirectories).Length != 0;

        public CodexOptions CreateOptions()
        {
            var variables = new Dictionary<string, string>
            {
                [PathVariable] = Environment.GetEnvironmentVariable(PathVariable)!,
                [HomeVariable] = _root,
                [UserProfileVariable] = _root,
                [ProviderHomeVariable] = _root,
                [ConfigHomeVariable] = _root,
                [AppDataVariable] = _root,
                [LocalAppDataVariable] = _root,
                [CiVariable] = TrueValue,
                [BrowserVariable] = NoBrowserCommand,
            };
            if (Environment.GetEnvironmentVariable(SystemRootVariable) is { } systemRoot)
            {
                variables[SystemRootVariable] = systemRoot;
            }
            return new CodexOptions { LaunchCommand = _launch, EnvironmentVariables = variables, InheritEnvironmentVariables = false };
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
