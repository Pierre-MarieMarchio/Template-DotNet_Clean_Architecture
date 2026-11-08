using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Processes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class ProjectItemStepsTests
{
    private readonly List<ExternalCall> _calls = [];

    [Fact]
    public void AJoin_IsDoneOnceTheProjectFileRecordsTheItem_AndAScaffoldOnceItsDescriptorIsGone()
    {
        using var project = new SampleProject(SampleProject.Full);
        string site = project.Folder("src/Web/apps/Site");
        project.Folder("src/Presentation/AS.Portal");
        File.WriteAllText(Path.Combine(site, "scaffold.json"), "{}");
        ProjectItemSteps steps = Steps();

        string[] done =
        [
            .. new (string Step, bool Done)[]
            {
                ("web-join Site", steps.WebJoin(project.Root, "Site", new WebJoinOptions()).IsDone()),
                ("web-join ui", steps.WebJoin(project.Root, "ui", new WebJoinOptions()).IsDone()),
                ("web-join Other", steps.WebJoin(project.Root, "Other", new WebJoinOptions()).IsDone()),
                ("presentation-join Portal", steps.PresentationJoin(project.Root, "Acme.Shop.Portal").IsDone()),
                ("presentation-join Desktop", steps.PresentationJoin(project.Root, "Acme.Shop.Desktop").IsDone()),
                ("web-scaffold Site", steps.WebScaffold(project.Root, "Site").IsDone()),
                ("presentation-scaffold Portal", steps.PresentationScaffold(project.Root, "Acme.Shop.Portal").IsDone()),
            }.Where(step => step.Done).Select(step => step.Step),
        ];

        done.ShouldBe(["web-join Site", "web-join ui", "presentation-join Portal", "presentation-scaffold Portal"]);
    }

    [Fact]
    public void AStep_RunsInTheTool_ReturningWhatItPrinted_AndItsRefusalAsAFailure()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        string portal = project.Folder("src/Presentation/AS.Portal");
        File.WriteAllText(Path.Combine(project.Root, "compose.yml"), "services:\n");
        File.WriteAllText(Path.Combine(portal, "scaffold.json"), """{ "framework": "blazor-server" }""");
        ItemStep join = Steps().PresentationJoin(project.Root, "Acme.Shop.Portal");
        var relayed = new List<string>();

        ProcessResult refused = join.Run(relayed.Add);
        File.WriteAllText(
            Path.Combine(portal, "README.md"),
            $"# Portal\n\n{ItemAdditions.JoiningSection}\n\n- in `compose.yml`, after the line:\n\n  ```yaml\n  services:\n  ```\n\n  add:\n\n  ```yaml\n    portal:\n  ```\n\n");
        ProcessResult joined = join.Run(null);

        refused.ExitCode.ShouldBe(CleanArchCommand.Failed);
        refused.Output.ShouldContain("src/Presentation/AS.Portal/README.md does not exist");
        relayed.ShouldContain(line => line.Contains("README.md does not exist", StringComparison.Ordinal));
        joined.ExitCode.ShouldBe(CleanArchCommand.Succeeded, joined.Output);
        joined.Output.ShouldContain("Joined src/Presentation/AS.Portal to the project.");
        File.ReadAllText(Path.Combine(project.Root, "compose.yml")).ShouldBe("services:\n  portal:\n");
        join.IsDone().ShouldBeTrue();
        _calls.ShouldBeEmpty();
    }

    private ProjectItemSteps Steps() =>
        new(new FakeProcesses((call, _, _) =>
        {
            _calls.Add(call);
            return CleanArchCommand.Succeeded;
        }));
}
