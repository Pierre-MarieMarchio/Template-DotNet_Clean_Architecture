using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CleanArch.Tooling;
using Shouldly;
using Spectre.Console.Testing;
using Xunit;

namespace CleanArch.Create.Tests;

public sealed partial class EndToEndTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cleanarch-create-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void TheLauncher_WritesWhatDotnetNewAndTheJoinsWrite_ForAProjectItsFirstWebAppAPackageAndAPresentationProject()
    {
        string package = PackTheTemplates();
        string hive = Path.Combine(_root, "hive");
        string launched = Path.Combine(_root, "launched");
        string direct = Path.Combine(_root, "direct");
        string[] options = ["--auth", "external", "--examples", "notes", "--sonar", "false"];

        Launch(_root, ["new", "-n", "Acme.Shop", "-o", "launched", .. options, "--yes", "--template-source", package, "--hive", hive]);
        Launch(launched, ["web-app", "-n", "BackOffice", "--framework", "vite-react", "--audience", "admin", "--yes", "--template-source", package, "--hive", hive]);
        Launch(launched, ["web-package", "-n", "tokens", "--yes", "--template-source", package, "--hive", hive]);
        Launch(launched, ["presentation", "--framework", "blazor-server", "--yes", "--template-source", package, "--hive", hive]);

        DotNet(_root, ["new", "cleanarch-webapi", "-n", "Acme.Shop", "-o", direct, .. options, "--presentation", "api", "--presentation", "blazor-server", "--debug:custom-hive", hive]);
        DotNet(direct, ["new", "cleanarch-webapp", "-n", "BackOffice", "--framework", "vite-react", "--audience", "admin", "--auth", "external", "--project-name", "Acme.Shop", "--scope", "acme-shop", "--workspace", "--debug:custom-hive", hive]);
        DotNet(direct, ["new", "cleanarch-webpackage", "-n", "tokens", "--scope", "acme-shop", "--debug:custom-hive", hive]);
        DotNet(direct, ["run", "Tools/Tasks.cs", "web-join", "BackOffice", "--web", "vite-react", "--web-audience", "admin", "--web-rendering", "spa"]);
        DotNet(direct, ["run", "Tools/Tasks.cs", "web-join", "tokens"]);

        Dictionary<string, string> written = Tree(launched);
        Dictionary<string, string> expected = Tree(direct);
        written.Keys.Order(StringComparer.Ordinal).ShouldBe(expected.Keys.Order(StringComparer.Ordinal));
        written.Where(file => file.Value != expected[file.Key]).Select(file => file.Key).ShouldBeEmpty("These files differ from what dotnet new wrote.");
        Directory.Exists(Path.Combine(launched, "Src", "Web", "apps", "BackOffice")).ShouldBeTrue();
        Directory.Exists(Path.Combine(launched, "Src", "Web", "packages", "tokens")).ShouldBeTrue();
        File.Exists(Path.Combine(launched, "Src", "Presentation", "AS.Portal", "Acme.Shop.Portal.csproj")).ShouldBeTrue();
    }

    [Fact]
    public void AFirstWebAppAddedWithItsWorkspace_ThenJoined_WritesWhatWebWrites_ButTheOptionsTheProjectWasGeneratedWith()
    {
        string package = PackTheTemplates();
        string hive = Path.Combine(_root, "hive");
        DotNet(_root, ["new", "install", package, "--debug:custom-hive", hive]);

        foreach ((string label, string framework, string audience, string rendering, string[] options) in _firstWebApps)
        {
            string generated = Path.Combine(_root, label, "generated");
            string added = Path.Combine(_root, label, "added");
            string[] app = ["--framework", framework, "--audience", audience, "--rendering", rendering];

            DotNet(_root, ["new", "cleanarch-webapi", "-n", "Acme.Shop", "-o", generated, .. options, "--web", framework, "--web-name", "Site", "--web-audience", audience, "--web-rendering", rendering, "--debug:custom-hive", hive]);
            DotNet(_root, ["new", "cleanarch-webapi", "-n", "Acme.Shop", "-o", added, .. options, "--debug:custom-hive", hive]);
            DotNet(added, ["new", "cleanarch-webapp", "-n", "Site", .. app, .. options, "--project-name", "Acme.Shop", "--scope", "acme-shop", "--workspace", "--debug:custom-hive", hive]);
            DotNet(added, ["run", "Tools/Tasks.cs", "web-join", "Site", "--web", framework, "--web-audience", audience, "--web-rendering", rendering]);

            Dictionary<string, string> written = Tree(added);
            Dictionary<string, string> expected = Tree(generated);
            expected[_overview] = WithoutTheOptions(expected[_overview]);
            written[_overview] = WithoutTheOptions(written[_overview]);
            written.Keys.Order(StringComparer.Ordinal).ShouldBe(expected.Keys.Order(StringComparer.Ordinal), label);
            written.Where(file => file.Value != expected[file.Key]).Select(file => file.Key).ShouldBeEmpty(
                $"{label}: these files differ from what --web writes, though web-join made the lines the workspace's and the app's README list.");
        }
    }

    [Fact]
    public void TheWorkerAddedLater_AndJoined_WritesWhatPresentationWorkerWrites()
    {
        string package = PackTheTemplates();
        string hive = Path.Combine(_root, "hive");
        string generated = Path.Combine(_root, "generated");
        string added = Path.Combine(_root, "added");
        DotNet(_root, ["new", "install", package, "--debug:custom-hive", hive]);

        DotNet(_root, ["new", "cleanarch-webapi", "-n", "Acme.Shop", "-o", generated, "--presentation", "api", "--presentation", "worker", "--debug:custom-hive", hive]);
        DotNet(_root, ["new", "cleanarch-webapi", "-n", "Acme.Shop", "-o", added, "--debug:custom-hive", hive]);
        Run(added, "git", ["init", "--quiet"]);
        Run(added, "git", ["add", "--all"]);
        Run(added, "git", ["-c", "user.name=Launcher tests", "-c", "user.email=launcher-tests@example.invalid", "commit", "--quiet", "--message", "The project as generated"]);
        DotNet(added, ["new", "cleanarch-presentation", "-n", "Worker", "--framework", "worker", "--project-name", "Acme.Shop", "--debug:custom-hive", hive]);
        DotNet(added, ["run", "Tools/Tasks.cs", "presentation-join", "Acme.Shop.Worker"]);

        Dictionary<string, string> written = Tree(added);
        Dictionary<string, string> expected = Tree(generated);

        written.Remove(_apisJobsDecision, out string? decision).ShouldBeTrue("The API's decision on its jobs stays in a project that ran them there.");
        decision.ShouldContain($"Status: Superseded by [0025]({_workersJobsDecision})");
        string[] index = written[_decisionIndex].Split('\n');
        index.ShouldContain(_supersededRow);
        written[_decisionIndex] = string.Join('\n', index.Where(line => line != _supersededRow));

        written.Keys.Order(StringComparer.Ordinal).ShouldBe(expected.Keys.Order(StringComparer.Ordinal));
        written.Where(file => file.Value != expected[file.Key]).Select(file => file.Key).ShouldBeEmpty(
            "These files differ from what --presentation worker writes, though presentation-join moved the jobs and made the lines the worker's README lists.");
    }

    [Fact]
    public void TheMcpServerAddedLater_AndJoined_PutsItsDecisionsAtTheirRankInTheIndex()
    {
        string package = PackTheTemplates();
        string hive = Path.Combine(_root, "hive");
        string generated = Path.Combine(_root, "generated");
        string added = Path.Combine(_root, "added");
        DotNet(_root, ["new", "install", package, "--debug:custom-hive", hive]);

        DotNet(_root, ["new", "cleanarch-webapi", "-n", "Acme.Shop", "-o", generated, "--presentation", "api", "--presentation", "mcp", "--debug:custom-hive", hive]);
        DotNet(_root, ["new", "cleanarch-webapi", "-n", "Acme.Shop", "-o", added, "--debug:custom-hive", hive]);
        Run(added, "git", ["init", "--quiet"]);
        Run(added, "git", ["add", "--all"]);
        Run(added, "git", ["-c", "user.name=Launcher tests", "-c", "user.email=launcher-tests@example.invalid", "commit", "--quiet", "--message", "The project as generated"]);
        DotNet(added, ["new", "cleanarch-presentation", "-n", "Mcp", "--framework", "mcp", "--project-name", "Acme.Shop", "--debug:custom-hive", hive]);
        DotNet(added, ["run", "Tools/Tasks.cs", "presentation-join", "Acme.Shop.Mcp"]);

        File.ReadAllText(Path.Combine(added, _decisionIndex)).ShouldBe(
            File.ReadAllText(Path.Combine(generated, _decisionIndex)),
            "The decisions index of a project that added the MCP server differs from one generated with it: each row the starter's README adds must land at its number's rank.");
    }

    [Fact]
    public void TheWorkerAddedLater_ToAProjectWithAServerRenderedApp_PutsItsDecisionAtItsRankInTheIndex()
    {
        string package = PackTheTemplates();
        string hive = Path.Combine(_root, "hive");
        string generated = Path.Combine(_root, "generated");
        string added = Path.Combine(_root, "added");
        string[] app = ["--web", "angular", "--web-rendering", "ssr"];
        DotNet(_root, ["new", "install", package, "--debug:custom-hive", hive]);

        DotNet(_root, ["new", "cleanarch-webapi", "-n", "Acme.Shop", "-o", generated, .. app, "--presentation", "api", "--presentation", "worker", "--debug:custom-hive", hive]);
        DotNet(_root, ["new", "cleanarch-webapi", "-n", "Acme.Shop", "-o", added, .. app, "--debug:custom-hive", hive]);
        Run(added, "git", ["init", "--quiet"]);
        Run(added, "git", ["add", "--all"]);
        Run(added, "git", ["-c", "user.name=Launcher tests", "-c", "user.email=launcher-tests@example.invalid", "commit", "--quiet", "--message", "The project as generated"]);
        DotNet(added, ["new", "cleanarch-presentation", "-n", "Worker", "--framework", "worker", "--project-name", "Acme.Shop", "--debug:custom-hive", hive]);
        DotNet(added, ["run", "Tools/Tasks.cs", "presentation-join", "Acme.Shop.Worker"]);

        string[] index = File.ReadAllText(Path.Combine(added, _decisionIndex)).Split('\n');
        string.Join('\n', index.Where(line => line != _supersededRow)).ShouldBe(
            File.ReadAllText(Path.Combine(generated, _decisionIndex)),
            "The decisions index of a project that added the worker after its server-rendered app differs from one generated with both: the worker's row must land at its number's rank, after 0024.");
    }

    private const string _overview = "docs/getting-started/overview.md";

    private const string _apisJobsDecision = "docs/decisions/0011-jobs-run-in-the-api-under-a-lease.md";

    private const string _workersJobsDecision = "0025-jobs-run-in-a-worker-under-a-lease.md";

    private const string _decisionIndex = "docs/decisions/README.md";

    private const string _supersededRow =
        "| [0011](0011-jobs-run-in-the-api-under-a-lease.md) | Jobs run in the API, under a lease, superseded by [0025](0025-jobs-run-in-a-worker-under-a-lease.md) | Background work |";

    private static readonly (string Label, string Framework, string Audience, string Rendering, string[] Options)[] _firstWebApps =
    [
        ("vite-react", "vite-react", "public", "spa", []),
        ("angular-ssr-external", "angular", "admin", "ssr", ["--auth", "external", "--examples", "notes"]),
    ];

    private static string WithoutTheOptions(string overview)
    {
        const string section = "## How this project was generated";
        int start = overview.IndexOf(section, StringComparison.Ordinal);
        int end = overview.IndexOf("\n## ", start + section.Length, StringComparison.Ordinal);

        return overview[..start] + overview[end..];
    }

    private static void Launch(string workingDirectory, string[] arguments)
    {
        var console = new TestConsole();
        console.Profile.Width = 300;

        int exitCode = new Launcher(console, workingDirectory, interactive: false, new WithoutThePages()).Run(arguments);

        exitCode.ShouldBe(0, console.Output);
    }

    private static Dictionary<string, string> Tree(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => Guid().Replace(File.ReadAllText(path), "<guid>"),
                StringComparer.Ordinal);

    private string PackTheTemplates()
    {
        string output = Path.Combine(_root, "package");
        DotNet(Repository.Root, [
            "pack", Path.Combine(Repository.Root, ".template.config", "TemplatePackage.csproj"),
            "--output", output,
            $"-p:ArtifactsPath={Path.Combine(_root, "artifacts")}/",
            "-m:1"]);

        return Directory.EnumerateFiles(output, "*.nupkg").Single();
    }

    private static void DotNet(string workingDirectory, string[] arguments) => Run(workingDirectory, "dotnet", arguments);

    private static void Run(string workingDirectory, string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        process.WaitForExit();

        process.ExitCode.ShouldBe(0, $"{executable} {string.Join(' ', arguments)}{Environment.NewLine}{output.Result}{error.Result}");
    }

    private sealed class WithoutThePages : IProcessRunner
    {
        private readonly ProcessRunner _processes = new();

        public ProcessResult Run(ProcessCall command, Action<string>? line) =>
            command.Arguments.Contains(Launcher.PresentationScaffoldTask)
                ? new ProcessResult(0, string.Empty)
                : _processes.Run(command, line);
    }

    [Fact]
    public void ServerRenderedApps_JoinedToASinglePageAppsWorkspace_BringTheirDecisionOnce_AsServerRenderingWritesIt()
    {
        string package = PackTheTemplates();
        string hive = Path.Combine(_root, "hive");
        string generated = Path.Combine(_root, "generated");
        string added = Path.Combine(_root, "added");
        DotNet(_root, ["new", "install", package, "--debug:custom-hive", hive]);

        DotNet(_root, ["new", "cleanarch-webapi", "-n", "Acme.Shop", "-o", generated, "--web", "angular", "--web-rendering", "ssr", "--debug:custom-hive", hive]);
        DotNet(_root, ["new", "cleanarch-webapi", "-n", "Acme.Shop", "-o", added, "--web", "vite-react", "--debug:custom-hive", hive]);
        foreach ((string app, string framework, string port) in ((string, string, string)[])[("BackOffice", "angular", "8082"), ("Shop", "react-router", "8083")])
        {
            DotNet(added, ["new", "cleanarch-webapp", "-n", app, "--framework", framework, "--rendering", "ssr", "--port", port, "--scope", "acme-shop", "--debug:custom-hive", hive]);
            DotNet(added, ["run", "Tools/Tasks.cs", "web-join", app, "--web", framework, "--web-rendering", "ssr", "--web-port", port]);
            File.Exists(Path.Combine(added, "Src", "Web", "apps", app, _serverRenderingDecision)).ShouldBeFalse($"web-join leaves no copy of the decision in {app}'s folder.");
        }

        Dictionary<string, string> written = Tree(added);
        Dictionary<string, string> expected = Tree(generated);
        JsonNode?[] ports = [.. JsonNode.Parse(written["cleanarch.json"])!["web"]!["apps"]!.AsArray().Select(app => app!["port"])];
        ports.Select(port => port!.GetValue<int>()).ShouldBe([5173, 8082, 8083], "cleanarch.json records each app's port: the first one's framework default, and the two added with --port.");
        foreach (string file in (string[])[$"docs/decisions/{_serverRenderingDecision}", "docs/decisions/README.md", "docs/features/authentication.md", "docs/decisions/0016-browser-refresh-token-in-a-cookie.md"])
        {
            written[file].ShouldBe(expected[file], $"{file} differs from what --web-rendering ssr writes.");
        }

        foreach (string file in (string[])["docs/features/web-frontend.md", "Src/Web/AGENTS.md"])
        {
            Regex.Count(written[file], _serverRenderingDecision).ShouldBe(Regex.Count(expected[file], _serverRenderingDecision), $"{file} cites the decision otherwise than --web-rendering ssr writes it.");
        }
    }

    private const string _serverRenderingDecision = "0024-a-server-rendered-web-app-runs-on-its-own-server.md";

    [GeneratedRegex("[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")]
    private static partial Regex Guid();
}
