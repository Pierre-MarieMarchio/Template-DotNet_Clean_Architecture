using System.Text.Json.Nodes;
using CleanArch.Cli.Joins;
using CleanArch.Tooling.Additions;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class JoinTests : IDisposable
{
    private const string _portal = "Src/Presentation/AS.Portal";
    private const string _worker = "Src/Presentation/AS.Worker";

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];

    public static TheoryData<string, string, string?, string?> ListedEdits => new()
    {
        {
            Edit("after", "include:", "  - path: a") + Edit("before", "services:", "  - path: b"),
            "include:\n  - path: a\nservices:",
            "include:\n  - path: a\n  - path: b\nservices:",
            null
        },
        {
            Edit("before", "services:", "include:") + Edit("after", "include:", "  - path: a"),
            "services:",
            "include:\n  - path: a\nservices:",
            null
        },
        {
            Edit("replace", "  old: 1", "  new: 2") + Edit("remove", "  gone: 3", null),
            "services:\n  old: 1\n  gone: 3",
            "services:\n  new: 2",
            null
        },
        {
            "- in `compose.yml`, at the rank of its number, add:\n\n  ```markdown\n  | x |\n  ```\n",
            "services:",
            null,
            "names no file and no 'after', 'before', 'replace' or 'remove'"
        },
        {
            Edit("after", "services:", "x") + Edit("after", "missing", "y"),
            "services:",
            null,
            $"edit 2 of {_portal}/README.md, after 1 line(s) of compose.yml: the lines it is placed by occur 0 times"
        },
    };

    [Theory]
    [MemberData(nameof(ListedEdits))]
    public void PresentationJoin_MakesTheEditsItsReadmeLists_AllOrNone(string edits, string compose, string? joined, string? refusal)
    {
        using var project = new SampleProject(SampleProject.Minimal);
        Write(project, "compose.yml", compose);
        Write(project, $"{_portal}/README.md", Readme(edits));
        Write(project, $"{_portal}/scaffold.json", """{ "framework": "blazor-server" }""");

        int exit = PresentationJoin.Run(Located(project), "Acme.Shop.Portal", Processes(), _output, _error);

        if (refusal is not null)
        {
            exit.ShouldBe(CleanArchCommand.Failed);
            _error.ToString().ShouldContain(refusal);
            Read(project, "compose.yml").ShouldBe(compose);
            Read(project, ProjectLocator.FileName).ShouldBe(SampleProject.Minimal);
            return;
        }

        exit.ShouldBe(CleanArchCommand.Succeeded, _error.ToString());
        Read(project, "compose.yml").ShouldBe(joined);
        _output.ToString().ShouldContain("Run `dotnet run Tools/Tasks.cs presentation-scaffold Acme.Shop.Portal` next");
        Recorded(project)["presentation"]!.ToJsonString().ShouldBe("""[{"name":"Portal","framework":"blazor-server"}]""");
    }

    [Theory]
    [InlineData("Acme.Shop", "Acme.Shop.Api.Features.Region.Api.Jobs", "using Acme.Shop.Api.Features.Region.Api.Jobs;", "using Acme.Shop.Worker.Features.Region.Api.Jobs;")]
    [InlineData("n", "n.Api.UnitTests.Features.Invoices.Jobs", "namespace n.Api.UnitTests.Features.Invoices.Jobs;", "namespace n.Worker.UnitTests.Features.Invoices.Jobs;")]
    [InlineData("n", "n.Api.Features.Invoices.Jobs", "using n.Api.Features.Invoices.Jobs.Purge;", "using n.Worker.Features.Invoices.Jobs.Purge;")]
    [InlineData("n", "n.Api.Features.Invoices.Jobs", "using Common.n.Api.Features.Invoices.Jobs;", "using Common.n.Api.Features.Invoices.Jobs;")]
    [InlineData("n", "n.Api.Features.Invoices.Jobs", "using n.Api.Features.Invoices.JobsArchive;", "using n.Api.Features.Invoices.JobsArchive;")]
    public void TheWorker_TakesAJobsNamespace_KeepingWhatFollowsTheApi_WhereItStandsWhole(string product, string api, string line, string expected) =>
        JobMoves.Renamed(line, [new NamespaceRename(api, JobMoves.WorkerNamespace(product, api))]).ShouldBe(expected);

    [Fact]
    public void WorkerJoin_MovesTheJobs_RenamesTheirNamespaces_MakesTheEdits_AndRecordsTheWorker()
    {
        using var project = WorkerProject();

        PresentationJoin.Run(Located(project), "Acme.Shop.Worker", Processes(), _output, _error).ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        File.Exists(Path.Combine(project.Root, "Src/Presentation/AS.Api/Common/Jobs/Purge.cs")).ShouldBeFalse();
        Directory.Exists(Path.Combine(project.Root, "Src/Presentation/AS.Api/Common")).ShouldBeFalse();
        Read(project, $"{_worker}/Common/Jobs/Purge.cs").ShouldBe("namespace Acme.Shop.Worker.Common.Jobs;\n");
        Read(project, "Tests/Presentation/AS.Worker.UnitTests/Features/Invoices/Jobs/RemindTests.cs")
            .ShouldBe("namespace Acme.Shop.Worker.UnitTests.Features.Invoices.Jobs;\n");
        Read(project, "Src/Presentation/AS.Api/Program.cs").ShouldBe("using Acme.Shop.Worker.Common.Jobs;\nusing Acme.Shop.Worker.Features.Invoices.Jobs;\n");
        Read(project, "compose.yml").ShouldBe("services:\n  worker:\n  api:\n");
        Read(project, $"{_worker}/README.md").ShouldNotContain(ItemAdditions.JoiningSection);
        Read(project, $"{_worker}/README.md").ShouldContain("## Create the project");
        _calls.Count(call => call.Arguments is ["build", ..]).ShouldBe(2);
        Recorded(project)["presentation"]!.AsArray().Select(entry => entry!["name"]!.GetValue<string>()).ShouldBe(["Api", "Worker"]);
    }

    [Theory]
    [InlineData(128, "", "this project is no git repository")]
    [InlineData(0, " M compose.yml\n", "differ from what git last committed")]
    [InlineData(0, "?? Src/Presentation/AS.Api/Common/Jobs/Purge.cs\n", "differ from what git last committed")]
    [InlineData(0, "?? Src/Presentation/AS.Worker/README.md\n", null)]
    public void WorkerJoin_RefusesATreeThatDiffersFromItsLastCommit_BeforeWritingAnything(int insideExit, string status, string? refusal)
    {
        using var project = WorkerProject();
        string compose = Read(project, "compose.yml");

        int exit = PresentationJoin.Run(Located(project), "Acme.Shop.Worker", Processes(insideExit, status), _output, _error);

        if (refusal is null)
        {
            exit.ShouldBe(CleanArchCommand.Succeeded, _error.ToString());
            return;
        }

        exit.ShouldBe(CleanArchCommand.Failed);
        _error.ToString().ShouldContain(refusal);
        Read(project, "compose.yml").ShouldBe(compose);
        File.Exists(Path.Combine(project.Root, "Src/Presentation/AS.Api/Common/Jobs/Purge.cs")).ShouldBeTrue();
        _calls.ShouldNotContain(call => call.FileName == "dotnet");
    }

    [Theory]
    [InlineData(false, "8083", null, null)]
    [InlineData(false, "8083", "8083", null)]
    [InlineData(true, "8083", null, null)]
    [InlineData(false, "8083", "8085", "--web-port 8085 is not the port Src/Web/apps/Site/compose.yml holds, 8083")]
    [InlineData(false, null, null, "Src/Web/apps/Site/compose.yml holds no WEB_<NAME>_HOST_PORT default")]
    public void WebJoin_RecordsTheAppOnThePortItsComposeFileHolds_KeepingTheKeysRecordedAlready(bool recordedWithPort, string? composePort, string? givenPort, string? refusal)
    {
        string port = recordedWithPort ? ""","port":8083""" : string.Empty;
        string recorded = """{"template":{"package":"P","version":"1.0.0"},"name":"Acme.Shop","options":{"auth":"none"},"web":{"scope":"acme-shop","apps":[{"name":"Site","framework":"vite-react","audience":"public","rendering":"spa","owner":"kept"<port>}],"packages":[]}}"""
            .Replace("<port>", port, StringComparison.Ordinal);
        using var project = new SampleProject(recorded);
        Write(project, "Src/Web/package.json", """{ "name": "@acme-shop/web" }""");
        Write(project, "Src/Web/apps/Site/compose.yml", composePort is null ? "services:\n" : $"ports:\n  - \"127.0.0.1:${{WEB_SITE_HOST_PORT:-{composePort}}}:8080\"\n");

        int exit = WebJoin.Run(Located(project), "Site", new WebJoinOptions("vite-react", Port: givenPort), _output, _error);

        if (refusal is not null)
        {
            exit.ShouldBe(CleanArchCommand.Failed);
            _error.ToString().ShouldContain(refusal);
            Read(project, ProjectLocator.FileName).ShouldBe(recorded);
            return;
        }

        exit.ShouldBe(CleanArchCommand.Succeeded, _error.ToString());
        JsonObject app = Recorded(project)["web"]!["apps"]![0]!.AsObject();
        app.Select(property => property.Key).ShouldBe(["name", "framework", "audience", "rendering", "owner", "port"]);
        app["owner"]!.GetValue<string>().ShouldBe("kept");
        app["port"]!.GetValue<int>().ShouldBe(8083);
        if (recordedWithPort)
        {
            Read(project, ProjectLocator.FileName).ShouldBe(recorded);
            _output.ToString().ShouldContain("records the web app Site already");
        }
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private FakeProcesses Processes(int insideExit = 0, string status = "") =>
        new((call, output, _) =>
        {
            _calls.Add(call);
            if (call.Arguments is ["rev-parse", ..])
            {
                return insideExit;
            }

            if (call.Arguments is ["status", ..])
            {
                output.Write(status);
            }

            return CleanArchCommand.Succeeded;
        });

    private static SampleProject WorkerProject()
    {
        var project = new SampleProject(SampleProject.Api);
        Write(project, "compose.yml", "services:\n  api:\n");
        Write(project, "Src/Presentation/AS.Api/Program.cs", "using Acme.Shop.Api.Common.Jobs;\nusing Acme.Shop.Api.Features.Invoices.Jobs;\n");
        Write(project, "Src/Presentation/AS.Api/Common/Jobs/Purge.cs", "namespace Acme.Shop.Api.Common.Jobs;\n");
        Write(project, "Src/Presentation/AS.Api/Features/Invoices/Jobs/Remind.cs", "namespace Acme.Shop.Api.Features.Invoices.Jobs;\n");
        Write(project, "Tests/Presentation/AS.Api.UnitTests/Features/Invoices/Jobs/RemindTests.cs", "namespace Acme.Shop.Api.UnitTests.Features.Invoices.Jobs;\n");
        Write(project, $"{_worker}/scaffold.json", """{ "framework": "worker" }""");
        Write(project, $"{_worker}/README.md", Readme(Edit("after", "services:", "  worker:")));
        return project;
    }

    private static string Edit(string mode, string anchor, string? lines) =>
        $"- in `compose.yml`, {mode} the line:\n\n  ```yaml\n  {anchor}\n  ```\n\n"
        + (lines is null ? string.Empty : $"  add:\n\n  ```yaml\n  {lines}\n  ```\n\n");

    private static string Readme(string edits) =>
        $"# Part\n\n{ItemAdditions.JoiningSection}\n\nThe edits:\n\n{edits}## Create the project\n\nThen scaffold it.\n";

    private static LocatedProject Located(SampleProject project) => ProjectLocator.Find(project.Root).ShouldNotBeNull();

    private static void Write(SampleProject project, string relative, string text)
    {
        string path = Path.Combine(project.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Read(SampleProject project, string relative) => File.ReadAllText(Path.Combine(project.Root, relative));

    private static JsonObject Recorded(SampleProject project) => JsonNode.Parse(Read(project, ProjectLocator.FileName))!.AsObject();
}
