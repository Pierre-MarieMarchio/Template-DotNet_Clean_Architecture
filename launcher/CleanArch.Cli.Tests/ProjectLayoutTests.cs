using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class ProjectLayoutTests
{
    [Fact]
    public void EveryPathItNames_ExistsInThisRepositorysOwnTree()
    {
        LocatedProject repository = ProjectLocator.Find(Repository.Root).ShouldNotBeNull();
        string[] paths =
        [
            ProjectLayout.Solution(repository),
            ProjectLayout.ApiProject(repository),
            .. ProjectLayout.Contexts(repository),
            ProjectLayout.OpenApiReference(repository),
            ProjectLayout.Client(repository),
            ProjectLayout.EndToEndTests(repository),
        ];

        paths.Where(path => !Path.Exists(Path.Combine(repository.Root, path))).ShouldBeEmpty();
        ProjectLayout.Contexts(repository).Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("Acme.Shop", "acme-shop-end-to-end")]
    [InlineData("AcmeShop", "acmeshop-end-to-end")]
    public void TheEndToEndStack_IsNamedAsTheTemplateNamesItsKebabForm(string name, string stack)
    {
        using var project = new SampleProject(SampleProject.Minimal.Replace("\"Acme.Shop\"", $"\"{name}\"", StringComparison.Ordinal));

        ProjectLayout.EndToEndComposeProject(ProjectLocator.Find(project.Root).ShouldNotBeNull()).ShouldBe(stack);
    }
}
