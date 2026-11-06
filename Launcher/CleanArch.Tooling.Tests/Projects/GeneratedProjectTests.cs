using CleanArch.Tooling.Projects;
using Shouldly;
using Xunit;

namespace CleanArch.Tooling.Tests.Projects;

public sealed class GeneratedProjectTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cleanarch-create-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Find_ReadsTheProjectAroundTheFolder_FromItsFiles()
    {
        Write("Acme.Shop.slnx", string.Empty);
        Write("Tools/Tasks.cs", string.Empty);
        Write("Src/Web/package.json", """{ "name": "@acme-store/web", "private": true }""");
        Write(".github/workflows/ci.yml", string.Empty);
        Directory.CreateDirectory(Path.Combine(_root, "Src", "Application", "AS.Application.Auth"));
        string inside = Directory.CreateDirectory(Path.Combine(_root, "Src", "Web", "apps")).FullName;

        ProjectFacts project = GeneratedProject.Find(inside).ShouldNotBeNull();

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
        Write("Acme.Shop.slnx", string.Empty);
        Write("Tools/Tasks.cs", string.Empty);
        Write("Directory.Packages.props", """<PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.13" />""");

        ProjectFacts project = GeneratedProject.Find(_root).ShouldNotBeNull();

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
            Directory.CreateDirectory(Path.Combine(_root, "Src", "Application", "AS.Application", "Features", feature));
        }

        GeneratedProject.Find(_root).ShouldNotBeNull().Examples.ShouldBe(examples);
    }

    [Fact]
    public void Find_ReadsTheFilesModule_FromItsStorageProject()
    {
        AProject();
        GeneratedProject.Find(_root).ShouldNotBeNull().Files.ShouldBeFalse();

        Directory.CreateDirectory(Path.Combine(_root, "Src", "Infrastructure", "AS.Infrastructure.Storage"));

        GeneratedProject.Find(_root).ShouldNotBeNull().Files.ShouldBeTrue();
    }

    [Fact]
    public void Find_ReadsTheClient_FromItsProject()
    {
        AProject();
        Directory.CreateDirectory(Path.Combine(_root, "Src", "Presentation", "AS.Presentation.Client"));
        GeneratedProject.Find(_root).ShouldNotBeNull().HasClient.ShouldBeFalse();

        Write("Src/Presentation/AS.Presentation.Client/Acme.Shop.Presentation.Client.csproj", string.Empty);

        GeneratedProject.Find(_root).ShouldNotBeNull().HasClient.ShouldBeTrue();
    }

    [Fact]
    public void Find_ReadsTheBlazorWebAssemblyApp_FromAProjectInAnAppFolder()
    {
        AProject();
        Write("Src/Web/apps/Admin/package.json", string.Empty);
        GeneratedProject.Find(_root).ShouldNotBeNull().BlazorWebAssemblyApp.ShouldBeNull();

        Write("Src/Web/apps/Wasm/Acme.Shop.Wasm.csproj", string.Empty);

        GeneratedProject.Find(_root).ShouldNotBeNull().BlazorWebAssemblyApp.ShouldBe("Wasm");
    }

    [Theory]
    [InlineData("Npgsql.EntityFrameworkCore.PostgreSQL", "postgresql")]
    [InlineData("Microsoft.EntityFrameworkCore.SqlServer", "sqlserver")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite", "sqlite")]
    public void Find_ReadsTheDatabase_FromTheProviderTheCoreProjectReferences(string package, string database)
    {
        AProject();
        Write(
            "Src/Infrastructure/AS.Infrastructure.Core/Acme.Shop.Infrastructure.Core.csproj",
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
    public void Find_ReadsTheApi_FromItsPresentationProject()
    {
        AProject();
        Directory.CreateDirectory(Path.Combine(_root, "Src", "Presentation", "AS.Worker"));
        Directory.CreateDirectory(Path.Combine(_root, "Src", "Presentation", "AS.Apis"));
        GeneratedProject.Find(_root).ShouldNotBeNull().HasApi.ShouldBeFalse();

        Directory.CreateDirectory(Path.Combine(_root, "Src", "Presentation", "AS.Api"));

        GeneratedProject.Find(_root).ShouldNotBeNull().HasApi.ShouldBeTrue();
    }

    [Fact]
    public void Find_ReadsSonarAndAspire_FromTheFilesTheyBring()
    {
        AProject();
        GeneratedProjectFacts.Sonar(GeneratedProject.Find(_root).ShouldNotBeNull()).ShouldBeFalse();
        GeneratedProjectFacts.Aspire(GeneratedProject.Find(_root).ShouldNotBeNull()).ShouldBeFalse();

        Write("Tools/sonar-scanner.Dockerfile", string.Empty);
        Directory.CreateDirectory(Path.Combine(_root, "Src", "AppHost", "AS.AppHost"));

        GeneratedProjectFacts.Sonar(GeneratedProject.Find(_root).ShouldNotBeNull()).ShouldBeTrue();
        GeneratedProjectFacts.Aspire(GeneratedProject.Find(_root).ShouldNotBeNull()).ShouldBeTrue();
    }

    [Fact]
    public void Find_ReadsTheProvenance_FromTheOverviewSentence()
    {
        AProject();
        GeneratedProjectFacts.Provenance(GeneratedProject.Find(_root).ShouldNotBeNull().Root).ShouldBeNull();

        Write("docs/getting-started/overview.md", "This project was generated by version 1.0.0 of\n`CleanArch.WebApi.Templates`, the package.");

        GeneratedProjectFacts.Provenance(GeneratedProject.Find(_root).ShouldNotBeNull().Root).ShouldBe(new TemplateProvenance("CleanArch.WebApi.Templates", "1.0.0"));
    }

    [Theory]
    [InlineData("scaffold.json", """{ "package": "create-vite", "arguments": ["{directory}", "--template", "vue-ts"] }""", "vite-vue")]
    [InlineData("scaffold.json", """{ "package": "create-vite", "arguments": ["{directory}", "--template", "svelte-ts"] }""", "vite-svelte")]
    [InlineData("scaffold.json", """{ "package": "create-react-router", "arguments": [] }""", "react-router")]
    [InlineData("scaffold.json", """{ "package": "@angular/cli", "arguments": [] }""", "angular")]
    [InlineData("scaffold.json", """{ "kind": "dotnet", "framework": "blazor-wasm" }""", "blazor-wasm")]
    [InlineData("Acme.Shop.Site.csproj", "<Project />", "blazor-wasm")]
    [InlineData("angular.json", "{}", "angular")]
    [InlineData("react-router.config.ts", "", "react-router")]
    [InlineData("package.json", """{ "dependencies": { "react": "19.2.0" } }""", "vite-react")]
    [InlineData("package.json", """{ "dependencies": { "vue": "3.5.0" } }""", "vite-vue")]
    [InlineData("package.json", """{ "devDependencies": { "svelte": "5.0.0" } }""", "vite-svelte")]
    [InlineData("package.json", """{ "name": "placeholder-site", "private": true }""", "placeholder")]
    [InlineData("package.json", """{ "dependencies": { "solid-js": "1.9.0" } }""", null)]
    public void Find_ReadsAWebAppsFramework_FromItsFiles(string file, string content, string? framework)
    {
        AProject();
        Write($"Src/Web/apps/Site/{file}", content);

        GeneratedProjectFacts.WebApps(GeneratedProject.Find(_root).ShouldNotBeNull().WebFolder).Single().Framework.ShouldBe(framework);
    }

    [Fact]
    public void Find_ReadsAWebAppsAudienceRenderingAndPort()
    {
        AProject();
        Write("Src/Web/apps/Admin/package.json", """{ "dependencies": { "react": "19.2.0" } }""");
        Write("Src/Web/apps/Admin/README.md", "# Admin\n\nA React back office over the API, for administrators.\n");
        Write("Src/Web/apps/Admin/compose.yml", "      - \"${WEB_ADMIN_HOST_PORT:-8082}:3000\"");
        Write("Src/Web/apps/Site/package.json", """{ "dependencies": { "react": "19.2.0" } }""");
        Write("Src/Web/apps/Site/nginx.conf", string.Empty);

        GeneratedProjectFacts.WebApps(GeneratedProject.Find(_root).ShouldNotBeNull().WebFolder).ShouldBe(
        [
            new GeneratedWebApp("Admin", "vite-react", "admin", "ssr", 8082),
            new GeneratedWebApp("Site", "vite-react", "public", "spa", null),
        ]);
    }

    [Fact]
    public void Find_ReadsTheWebPackages_LeavingOutTheWorkspacesOwn()
    {
        AProject();
        foreach (string package in new[] { "api-client", "browser-smoke", "eslint-config", "tsconfig", "ui" })
        {
            Directory.CreateDirectory(Path.Combine(_root, "Src", "Web", "packages", package));
        }

        GeneratedProjectFacts.WebPackages(GeneratedProject.Find(_root).ShouldNotBeNull().WebFolder).ShouldBe(["ui"]);
    }

    [Theory]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk.Worker" />""", "worker")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><UseWPF>true</UseWPF></PropertyGroup></Project>""", "wpf")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="Avalonia" /></ItemGroup></Project>""", "avalonia")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk.Web"><ItemGroup><PackageReference Include="ModelContextProtocol.AspNetCore" /></ItemGroup></Project>""", "mcp")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk.Web" />""", "blazor-server")]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk" />""", null)]
    public void Find_ReadsAPresentationProjectsFramework_FromItsProject(string project, string? framework)
    {
        AProject();
        Write("Src/Presentation/AS.PresentationAppPortal/Acme.Shop.PresentationAppPortal.csproj", project);

        GeneratedProjectFacts.Presentations(GeneratedProject.Find(_root).ShouldNotBeNull()).ShouldBe([new GeneratedPresentation("PresentationAppPortal", framework)]);
    }

    [Fact]
    public void Find_ReadsThePresentationProjects_TheApiByItsFolder_AndAScaffoldByItsDescriptor()
    {
        AProject();
        Directory.CreateDirectory(Path.Combine(_root, "Src", "Presentation", "AS.Api"));
        Directory.CreateDirectory(Path.Combine(_root, "Src", "Presentation", "AS.Presentation.Core"));
        Write("Src/Presentation/AS.PresentationAppMcp/scaffold.json", """{ "kind": "written", "framework": "mcp" }""");

        GeneratedProjectFacts.Presentations(GeneratedProject.Find(_root).ShouldNotBeNull()).ShouldBe(
        [
            new GeneratedPresentation("Api", "api"),
            new GeneratedPresentation("PresentationAppMcp", "mcp"),
        ]);
    }

    [Theory]
    [InlineData("Acme.Shop.slnx")]
    [InlineData("Acme.Shop.sln")]
    public void Find_NamesTheProjectAfterItsSolution_InEitherFormat(string solution)
    {
        Write(solution, string.Empty);
        Write("Tools/Tasks.cs", string.Empty);

        GeneratedProject.Find(_root).ShouldNotBeNull().Name.ShouldBe("Acme.Shop");
    }

    [Fact]
    public void Find_SeesNoProject_WithoutTheTasksBesideTheSolution()
    {
        Write("Acme.Shop.slnx", string.Empty);

        GeneratedProject.Find(_root).ShouldBeNull();
    }

    private void AProject()
    {
        Write("Acme.Shop.slnx", string.Empty);
        Write("Tools/Tasks.cs", string.Empty);
    }

    private void Write(string relative, string content)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
