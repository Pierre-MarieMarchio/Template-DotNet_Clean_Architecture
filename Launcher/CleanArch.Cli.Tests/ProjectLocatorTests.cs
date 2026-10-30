using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class ProjectLocatorTests
{
    [Fact]
    public void Parse_ReadsEveryFieldOfTheContract()
    {
        ProjectFile file = ProjectLocator.Parse(SampleProject.Full);

        file.Template.ShouldBe(new TemplateReference("CleanArch.WebApi.Templates", "1.2.3"));
        file.Name.ShouldBe("Acme.Shop");
        file.Options.ShouldBe(new ProjectOptions("local", "postgresql", "all", Files: true, Jobs: "api", Ci: true, Sonar: false, AgentFiles: true));
        file.Client.ShouldBeTrue();
        file.Web.ShouldNotBeNull().Scope.ShouldBe("acme-shop");
        file.Web.Apps.ShouldBe([new WebApp("Site", "vite-react", "public", "spa")]);
        file.Web.Packages.ShouldBe([new WebPackage("ui")]);
        file.Presentation.ShouldBe(new PresentationProject("Portal", "blazor-server"));
    }

    [Fact]
    public void Parse_LeavesOutWhatTheProjectHasNot()
    {
        ProjectFile file = ProjectLocator.Parse(SampleProject.Minimal);

        file.Client.ShouldBeFalse();
        file.Web.ShouldBeNull();
        file.Presentation.ShouldBeNull();
        file.Options.Auth.ShouldBe("none");
        file.Options.Database.ShouldBeNull();
    }

    [Fact]
    public void Parse_IgnoresAKeyAProjectAddedItself() =>
        ProjectLocator.Parse(SampleProject.Minimal.Replace("\"name\"", "\"owner\": \"platform\", \"name\"", StringComparison.Ordinal))
            .Name.ShouldBe("Acme.Shop");

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{ not json")]
    [InlineData("""{ "name": "Acme.Shop", "options": {} }""")]
    [InlineData("""{ "template": { "package": "P", "version": "1" }, "options": {} }""")]
    [InlineData("""{ "template": { "package": "P" }, "name": "Acme.Shop", "options": {} }""")]
    [InlineData("""{ "template": { "package": "P", "version": "1" }, "name": null, "options": {} }""")]
    public void Parse_RefusesWhatIsNotAProjectFile(string json) =>
        Should.Throw<ProjectFileException>(() => ProjectLocator.Parse(json));

    [Fact]
    public void Find_StopsAtTheWorkingDirectory_WhenItHoldsTheFile()
    {
        using var project = new SampleProject(SampleProject.Full);

        LocatedProject located = ProjectLocator.Find(project.Root).ShouldNotBeNull();

        located.Root.ShouldBe(project.Root);
        located.File.Name.ShouldBe("Acme.Shop");
    }

    [Fact]
    public void Find_ClimbsToTheNearestFolderHoldingTheFile()
    {
        using var project = new SampleProject(SampleProject.Full);
        string nested = project.Folder("Src/Web/apps");

        ProjectLocator.Find(nested).ShouldNotBeNull().Root.ShouldBe(project.Root);
    }

    [Fact]
    public void Find_PrefersTheNearestFile()
    {
        using var outer = new SampleProject(SampleProject.Full);
        using var inner = new SampleProject(SampleProject.Minimal);
        string folder = Path.Combine(outer.Root, "inner");
        Directory.CreateDirectory(folder);
        File.Copy(Path.Combine(inner.Root, ProjectLocator.FileName), Path.Combine(folder, ProjectLocator.FileName));

        ProjectLocator.Find(folder).ShouldNotBeNull().Root.ShouldBe(folder);
    }

    [Fact]
    public void Find_ReturnsNothing_OutsideAProject()
    {
        string folder = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        try
        {
            ProjectLocator.Find(folder).ShouldBeNull();
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public void Find_NamesTheFileItCouldNotRead()
    {
        using var project = new SampleProject("{ not json");

        ProjectFileException error = Should.Throw<ProjectFileException>(() => ProjectLocator.Find(project.Root));

        error.Message.ShouldStartWith(Path.Combine(project.Root, ProjectLocator.FileName));
    }
}
