using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class BootstrapCommandTests : IDisposable
{
    private const string _apiWithClientAndWeb = """
        {
          "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
          "name": "Acme.Shop",
          "options": { "auth": "local" },
          "client": true,
          "web": {
            "scope": "acme-shop",
            "apps": [ { "name": "Site", "framework": "vite-react", "audience": "public", "rendering": "spa" } ],
            "packages": []
          },
          "presentation": [ { "name": "Api", "framework": "api" } ]
        }
        """;

    private static readonly string _persistence = Path.Combine("Src", "Infrastructure", "AS.Infrastructure.Persistence");
    private static readonly string _auth = Path.Combine("Src", "Infrastructure", "AS.Infrastructure.Auth");

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];
    private string? _failing;

    private readonly FakeProcesses _processes;

    public BootstrapCommandTests()
    {
        _processes = new FakeProcesses((call, _, _) =>
        {
            _calls.Add(call);
            return Fake(call);
        });
    }

    public static TheoryData<string, string[]> ProjectFiles => new()
    {
        {
            _apiWithClientAndWeb,
            ["format", "openapi", "tool restore", "migration", "migration", "client", "install", "api"]
        },
        {
            SampleProject.ApiWith("none", client: false),
            ["format", "openapi", "tool restore", "migration"]
        },
        {
            SampleProject.NoApi,
            ["format", "tool restore", "migration", "migration"]
        },
    };

    [Theory]
    [MemberData(nameof(ProjectFiles))]
    public void Bootstrap_RunsTheStepsTheProjectFileMakesTrue_InTheirOrder(string projectFile, string[] steps)
    {
        using var project = new SampleProject(projectFile);
        project.Folder(Path.Combine("Src", "Presentation", "AS.Presentation.Client"));
        File.WriteAllText(Path.Combine(project.Folder(Path.Combine("Src", "Web")), "package.json"), "{}");

        Run(project).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Select(StepOf).ShouldBe(steps);
        _output.ToString().ShouldContain("Commit this before anything else");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Bootstrap_WritesTheDocumentationIndexInTheTree()
    {
        using var project = new SampleProject(SampleProject.NoApi);
        string docs = project.Folder("docs");
        File.WriteAllText(Path.Combine(docs, "page.md"), "---\ntitle: Page\ndescription: A page.\n---\n");

        Run(project).ShouldBe(CleanArchCommand.Succeeded);

        File.ReadAllText(Path.Combine(docs, "index.md")).ShouldContain("- [Page](page.md) - A page.");
    }

    [Fact]
    public void Bootstrap_CreatesAPresentationProjectThatHoldsADescriptor_BeforeFormatting()
    {
        using var project = new SampleProject(SampleProject.NoApi);
        string desktop = project.Folder(Path.Combine("Src", "Presentation", "AS.Desktop"));
        File.WriteAllText(
            Path.Combine(desktop, "scaffold.json"),
            """{ "kind": "dotnet", "name": "Acme.Shop.Desktop", "package": "Avalonia.Templates", "version": "12.1.4", "template": "avalonia.mvvm", "arguments": [], "keep": [] }""");

        Run(project);

        _calls[0].Arguments.Take(2).ShouldBe(["new", "install"]);
    }

    [Fact]
    public void Bootstrap_LeavesAContextThatAlreadyHasItsMigrationsAlone()
    {
        using var project = new SampleProject(SampleProject.WithOptions("local", sonar: false));
        File.WriteAllText(Path.Combine(project.Folder(Path.Combine(_persistence, "Migrations")), "PersistenceModelSnapshot.cs"), string.Empty);

        Run(project).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Where(call => StepOf(call) == "migration").Select(call => call.Arguments[^1]).ShouldBe([_auth]);
        _output.ToString().ShouldContain("AS.Infrastructure.Persistence already has its migrations; they are left as they are.");
    }

    [Fact]
    public void Bootstrap_StopsAtAFailingStep_SayingWhatIsDoneAndWhatIsNot()
    {
        using var project = new SampleProject(SampleProject.ApiWith("local", client: true));
        _failing = "tool restore";

        Run(project).ShouldBe(7);

        _calls.Select(StepOf).ShouldNotContain("migration");
        _error.ToString().ShouldContain(
            "Done: the presentation projects' pages, the formatting, the documentation index and the OpenAPI reference. "
            + "Not done: the local tools' restore, each context's initial migration and the .NET client. "
            + "The local tools are not restored, so the initial migrations are not created");
        _output.ToString().ShouldNotContain("Commit this before anything else");
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(SampleProject project) => BuiltInCommands.Run(["bootstrap"], project.Root, _output, _error, _processes);

    private int Fake(ExternalCall call)
    {
        if (call.Arguments[0] == "build")
        {
            const string prefix = "-p:OpenApiDocumentsDirectory=";
            string documents = call.Arguments.Single(argument => argument.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..];
            Directory.CreateDirectory(documents);
            File.WriteAllText(Path.Combine(documents, "v1.json"), "{}");
        }

        if (call.Arguments.Contains("kiota"))
        {
            string target = call.Arguments[call.Arguments.ToList().IndexOf("--output") + 1];
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, ".kiota.log"), string.Empty);
            File.WriteAllText(Path.Combine(target, "kiota-lock.json"), "{}");
        }

        return StepOf(call) == _failing ? 7 : CleanArchCommand.Succeeded;
    }

    private static string StepOf(ExternalCall call) => call.Arguments switch
    {
        ["run", _, var task, ..] => task,
        ["build", ..] => "openapi",
        ["tool", "restore"] => "tool restore",
        ["tool", "run", "kiota", ..] => "client",
        ["ef", "migrations", "add", ..] => "migration",
        [var first, ..] => first,
        _ => string.Empty,
    };
}
