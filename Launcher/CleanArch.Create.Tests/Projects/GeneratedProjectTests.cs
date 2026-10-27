using CleanArch.Create.Projects;
using Shouldly;
using Xunit;

namespace CleanArch.Create.Tests.Projects;

public sealed class GeneratedProjectTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cleanarch-create-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Find_ReadsTheProjectAroundTheFolder_FromItsFiles()
    {
        Write("Acme.Shop.sln", string.Empty);
        Write("Tools/Tasks.cs", string.Empty);
        Write("Src/Web/package.json", """{ "name": "@acme-store/web", "private": true }""");
        Write(".github/workflows/ci.yml", string.Empty);
        Directory.CreateDirectory(Path.Combine(_root, "Src", "Application", "Acme.Shop.Application.Auth"));
        string inside = Directory.CreateDirectory(Path.Combine(_root, "Src", "Web", "apps")).FullName;

        GeneratedProject project = GeneratedProject.Find(inside).ShouldNotBeNull();

        project.Root.ShouldBe(_root);
        project.Name.ShouldBe("Acme.Shop");
        project.HasWorkspace.ShouldBeTrue();
        project.Scope.ShouldBe("acme-store");
        project.Auth.ShouldBe("local");
        project.Ci.ShouldBeTrue();
    }

    [Fact]
    public void Find_NamesTheScopeAsWebWould_AndTellsExternalFromNoAuthentication()
    {
        Write("Acme.Shop.sln", string.Empty);
        Write("Tools/Tasks.cs", string.Empty);
        Write("Directory.Packages.props", """<PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.13" />""");

        GeneratedProject project = GeneratedProject.Find(_root).ShouldNotBeNull();

        project.HasWorkspace.ShouldBeFalse();
        project.Scope.ShouldBe("acme-shop");
        project.Auth.ShouldBe("external");
        project.Ci.ShouldBeFalse();

        File.Delete(Path.Combine(_root, "Directory.Packages.props"));
        GeneratedProject.Find(_root).ShouldNotBeNull().Auth.ShouldBe("none");
    }

    [Theory]
    [InlineData("all", "TodoLists", "Reminders", "Notes", "Maintenance")]
    [InlineData("notes", "Notes", "Maintenance")]
    [InlineData("none", "Maintenance")]
    public void Find_ReadsTheExampleFeatures_FromTheApplicationFeatures(string examples, params string[] features)
    {
        AProject();
        foreach (string feature in features)
        {
            Directory.CreateDirectory(Path.Combine(_root, "Src", "Application", "Acme.Shop.Application", "Features", feature));
        }

        GeneratedProject.Find(_root).ShouldNotBeNull().Examples.ShouldBe(examples);
    }

    [Fact]
    public void Find_ReadsTheFilesModule_FromItsStorageProject()
    {
        AProject();
        GeneratedProject.Find(_root).ShouldNotBeNull().Files.ShouldBeFalse();

        Directory.CreateDirectory(Path.Combine(_root, "Src", "Infrastructure", "Acme.Shop.Infrastructure.Storage"));

        GeneratedProject.Find(_root).ShouldNotBeNull().Files.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", "postgresql")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "sqlserver")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", "sqlite")]
    public void Find_ReadsTheDatabase_FromTheProviderTheCoreProjectReferences(string package, string database)
    {
        AProject();
        Write(
            "Src/Infrastructure/Acme.Shop.Infrastructure.Core/Acme.Shop.Infrastructure.Core.csproj",
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore" />
                <!-- <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" /> -->
                <PackageReference Include="{package}" />
              </ItemGroup>
            </Project>
            """);

        GeneratedProject.Find(_root).ShouldNotBeNull().Database.ShouldBe(database);
    }

    [Fact]
    public void Find_ReadsTheAgentFiles_FromAgentsMd()
    {
        AProject();
        GeneratedProject.Find(_root).ShouldNotBeNull().AgentFiles.ShouldBeFalse();

        Write("AGENTS.md", string.Empty);

        GeneratedProject.Find(_root).ShouldNotBeNull().AgentFiles.ShouldBeTrue();
    }

    [Fact]
    public void Find_SeesNoProject_WithoutTheTasksBesideTheSolution()
    {
        Write("Acme.Shop.sln", string.Empty);

        GeneratedProject.Find(_root).ShouldBeNull();
    }

    private void AProject()
    {
        Write("Acme.Shop.sln", string.Empty);
        Write("Tools/Tasks.cs", string.Empty);
    }

    private void Write(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
