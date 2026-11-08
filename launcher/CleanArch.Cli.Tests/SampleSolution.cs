namespace CleanArch.Cli.Tests;

internal static class SampleSolution
{
    public const string Name = "Acme.Shop";

    public const string UnitProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <IsTestProject>true</IsTestProject>
          </PropertyGroup>
        </Project>
        """;

    public const string IntegrationProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <IsTestProject>true</IsTestProject>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Testcontainers" />
          </ItemGroup>
        </Project>
        """;

    public const string WindowsProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0-windows</TargetFramework>
            <IsTestProject>true</IsTestProject>
          </PropertyGroup>
        </Project>
        """;

    public static SampleProject WithSolution(string projectFile = SampleProject.Full)
    {
        var project = new SampleProject(projectFile);
        File.WriteAllText(Path.Combine(project.Root, Name + ".slnx"), """
            <Solution>
              <Folder Name="/tests/">
                <Project Path="tests/Unit/A.Unit.csproj" />
                <Project Path="tests/Windows/A.Windows.csproj" />
              </Folder>
            </Solution>
            """);
        return project;
    }

    public static void AddTestProject(SampleProject project, string folder, string name, string content) =>
        File.WriteAllText(Path.Combine(project.Folder(folder), name), content);

    public static void AddUnitAndIntegrationTests(SampleProject project)
    {
        AddTestProject(project, "tests/Unit", "A.Unit.csproj", UnitProject);
        AddTestProject(project, "tests/Integration", "A.Integration.csproj", IntegrationProject);
    }

    public static void WithCoverageFloor(SampleProject project, string floor = "80")
    {
        File.WriteAllText(Path.Combine(project.Root, "coverage.minimum"), $"# Line-coverage floor in percent.\n{floor}\n");
        File.WriteAllText(Path.Combine(project.Root, "coverage.runsettings"), "<RunSettings />\n");
    }
}
