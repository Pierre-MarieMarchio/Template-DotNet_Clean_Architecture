using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class ScaffoldTests : IDisposable
{
    private const string _ownProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><OutputType>WinExe</OutputType></PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Avalonia" />
            <ProjectReference Include="../AS.Presentation.Client/Acme.Shop.Presentation.Client.csproj" />
          </ItemGroup>
        </Project>
        """;

    private const string _generatedProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>WinExe</OutputType></PropertyGroup>
          <ItemGroup><PackageReference Include="Avalonia" Version="12.1.4" /></ItemGroup>
        </Project>
        """;

    private static readonly string _desktop = Path.Combine("src", "Presentation", "AS.Desktop");
    private static readonly string _desktopProject = Path.Combine(_desktop, "Acme.Shop.Desktop.csproj");
    private static readonly string _desktopTests = Path.Combine("tests", "Presentation", "AS.Desktop.UnitTests", "Acme.Shop.Desktop.UnitTests.csproj");
    private static readonly string _clientProject = Path.Combine("src", "Presentation", "AS.Presentation.Client", "Acme.Shop.Presentation.Client.csproj");
    private static readonly string _reference = Path.Combine("tests", "Integration", "AS.Api.IntegrationTests", "OpenApi", "v1.json");
    private static readonly string _site = Path.Combine("src", "Web", "apps", "Site");

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];
    private readonly SampleProject _project = new(SampleProject.Api);
    private string _generated = _generatedProject;

    [Fact]
    public void APresentation_IsGeneratedAside_KeepsWhatItsDescriptorNames_AndJoinsTheSolutionFolderOfItsLayer()
    {
        WriteDescriptor(_desktop, """
            {
              "kind": "dotnet", "name": "Acme.Shop.Desktop", "package": "Avalonia.Templates", "version": "12.1.4",
              "template": "avalonia.mvvm", "arguments": ["--name", "{name}", "--output", "{directory}"],
              "keep": ["Views", "App.axaml"]
            }
            """);
        Write(_desktopProject, _ownProject);
        Write(Path.Combine(_desktop, "App.axaml"), "own");
        Write(_desktopTests, "<Project />");
        Write(_clientProject, "<Project />");
        Write(_reference, "{}");

        Scaffolds.Presentations(Located(), Shell(), "Acme.Shop.Desktop").ShouldBe(ExitCode.Succeeded, _error.ToString());

        string scratch = _calls[0].WorkingDirectory;
        string[] hive = ["--debug:custom-hive", Path.Combine(scratch, "hive"), "--debug:disable-sdk-templates"];
        _calls.Select(call => call.Arguments).ShouldBe(
        [
            ["new", "install", "Avalonia.Templates@12.1.4", .. hive],
            ["new", "avalonia.mvvm", "--help", .. hive],
            ["new", "avalonia.mvvm", "--name", "Acme.Shop.Desktop", "--output", "Acme.Shop.Desktop", "--allow-scripts", "no", .. hive],
            ["sln", Path.Combine(_project.Root, "Acme.Shop.slnx"), "add", "--solution-folder", "src/Presentation", Full(_desktopProject), Full(_clientProject)],
            ["sln", Path.Combine(_project.Root, "Acme.Shop.slnx"), "add", "--solution-folder", "tests/Presentation", Full(_desktopTests)],
            ["format", Full(_desktopProject)],
            ["format", Full(_desktopTests)],
            ["format", Full(_clientProject)],
            ["tool", "restore"],
            ["tool", "run", "kiota", "generate", "--language", "CSharp", "--openapi", Full(_reference), "--output", Path.Combine(_project.Root, "src", "Presentation", "AS.Presentation.Client", "Generated"), "--class-name", "ApiClient", "--namespace-name", "Acme.Shop.Client.Generated", "--exclude-backward-compatible", "--clean-output", "--log-level", "Warning"],
        ]);
        _calls.Take(3).ShouldAllBe(call => call.CloseInput);
        File.ReadAllText(Full(Path.Combine(_desktop, "Views", "Main.axaml"))).ShouldBe("<Window>\n</Window>\n");
        File.ReadAllText(Full(Path.Combine(_desktop, "App.axaml"))).ShouldBe("own");
        File.Exists(Full(Path.Combine(_desktop, "Program.cs"))).ShouldBeFalse();
        File.Exists(Full(Path.Combine(_desktop, ScaffoldDescriptor.FileName))).ShouldBeFalse();
        Directory.Exists(scratch).ShouldBeFalse();
    }

    [Fact]
    public void APresentation_WhoseGeneratedProjectDeclaresMoreThanItsOwn_IsRefused_AndLeftUnchanged()
    {
        WriteDescriptor(_desktop, """
            { "kind": "dotnet", "name": "Acme.Shop.Desktop", "package": "Avalonia.Templates", "version": "12.1.4", "template": "avalonia.mvvm", "keep": ["Views"] }
            """);
        Write(_desktopProject, """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="Avalonia" /></ItemGroup></Project>""");
        _generated = _generatedProject.Replace("12.1.4\" />", "12.1.4\" /><PackageReference Include=\"Avalonia.Desktop\" />", StringComparison.Ordinal);

        Scaffolds.Presentations(Located(), Shell(), only: null).ShouldBe(ExitCode.Failed);

        _error.ToString().ShouldContain("declares the package Avalonia.Desktop, the property OutputType, which Acme.Shop.Desktop.csproj does not");
        File.Exists(Full(Path.Combine(_desktop, ScaffoldDescriptor.FileName))).ShouldBeTrue();
        Directory.Exists(Full(Path.Combine(_desktop, "Views"))).ShouldBeFalse();
        _calls.ShouldNotContain(call => call.Arguments[0] == "sln");
    }

    [Fact]
    public void APresentation_WhoseGeneratedProjectDeclaresAPackageItsDescriptorDrops_IsCreated()
    {
        WriteDescriptor(_desktop, """
            { "kind": "dotnet", "name": "Acme.Shop.Desktop", "package": "Avalonia.Templates", "version": "12.1.4", "template": "avalonia.mvvm", "keep": ["Views"], "drop": ["AvaloniaUI.DiagnosticsSupport"] }
            """);
        Write(_desktopProject, _ownProject);
        Write(_desktopTests, "<Project />");
        _generated = _generatedProject.Replace("12.1.4\" />", "12.1.4\" /><PackageReference Include=\"AvaloniaUI.DiagnosticsSupport\" />", StringComparison.Ordinal);

        Scaffolds.Presentations(Located(), Shell(), only: null).ShouldBe(ExitCode.Succeeded, _error.ToString());

        Directory.Exists(Full(Path.Combine(_desktop, "Views"))).ShouldBeTrue();
        File.Exists(Full(Path.Combine(_desktop, ScaffoldDescriptor.FileName))).ShouldBeFalse();
    }

    [Fact]
    public void AWebApp_IsCreatedByItsOfficialCli_ShapedByItsOverlayRemovalsAndMerges_ThenPinnedToTheVersionsInstalled()
    {
        Write(Path.Combine("src", "Web", "package.json"), "{}");
        WriteDescriptor(_site, """
            {
              "name": "site", "package": "create-vite", "version": "9.3.1",
              "arguments": ["{directory}", "--template", "react-ts"],
              "remove": ["README.md", "public"],
              "merge": { "package.json": { "name": "site", "packageManager": null, "dependencies": { "@acme-shop/api-client": "workspace:*" } } }
            }
            """);
        Write(Path.Combine(_site, "overlay", "src", "main.tsx"), "overlay");

        Scaffolds.WebApps(Located(), Shell(), only: null).ShouldBe(ExitCode.Succeeded, _error.ToString());

        string web = Full(Path.Combine("src", "Web"));
        _calls.Select(call => (call.WorkingDirectory == web ? "workspace" : Path.GetDirectoryName(call.WorkingDirectory), string.Join(' ', call.Arguments))).ShouldBe(
        [
            (web, "dlx create-vite@9.3.1 site --template react-ts"),
            ("workspace", "install --no-frozen-lockfile"),
            ("workspace", "install --no-frozen-lockfile"),
            ("workspace", "api"),
        ]);
        _calls[0].CloseInput.ShouldBeTrue();
        JsonObject package = JsonNode.Parse(File.ReadAllText(Full(Path.Combine(_site, "package.json"))))!.AsObject();
        package["name"]!.GetValue<string>().ShouldBe("site");
        package.ContainsKey("packageManager").ShouldBeFalse();
        package["dependencies"]!["react"]!.GetValue<string>().ShouldBe("19.2.1");
        package["dependencies"]!["@acme-shop/api-client"]!.GetValue<string>().ShouldBe("workspace:*");
        package["devDependencies"]!["vite"]!.GetValue<string>().ShouldBe("8.0.0");
        File.ReadAllText(Full(Path.Combine(_site, "src", "main.tsx"))).ShouldBe("overlay");
        File.Exists(Full(Path.Combine(_site, "README.md"))).ShouldBeFalse();
        Directory.Exists(Full(Path.Combine(_site, "public"))).ShouldBeFalse();
        Directory.Exists(Full(Path.Combine(_site, "overlay"))).ShouldBeFalse();
        File.Exists(Full(Path.Combine(_site, ScaffoldDescriptor.FileName))).ShouldBeFalse();
        Directory.EnumerateDirectories(web, ".web-scaffold-*").ShouldBeEmpty();
    }

    [Fact]
    public void ScaffoldedProjects_JoinTheSolutionFolderOfTheirLayer_OneCallPerFolder()
    {
        string root = Path.Combine(Path.GetTempPath(), "repo");
        string desktop = Path.Combine(root, "src", "Presentation", "X.Desktop", "App.Desktop.csproj");
        string client = Path.Combine(root, "src", "Presentation", "X.Presentation.Client", "App.Presentation.Client.csproj");
        string tests = Path.Combine(root, "tests", "Presentation", "X.Desktop.UnitTests", "App.Desktop.UnitTests.csproj");

        ProjectScaffold.SolutionAddArguments(root, "X.slnx", [desktop, tests, client]).ShouldBe(
        [
            ["sln", "X.slnx", "add", "--solution-folder", "src/Presentation", desktop, client],
            ["sln", "X.slnx", "add", "--solution-folder", "tests/Presentation", tests],
        ]);
    }

    [Theory]
    [InlineData(new byte[] { (byte)'a', (byte)'\r', (byte)'\n', (byte)'b', (byte)'\r', (byte)'c', (byte)'\n' }, new byte[] { (byte)'a', (byte)'\n', (byte)'b', (byte)'\r', (byte)'c', (byte)'\n' })]
    [InlineData(new byte[] { 0x89, 0, (byte)'\r', (byte)'\n' }, new byte[] { 0x89, 0, (byte)'\r', (byte)'\n' })]
    public void AGeneratedFile_HasEachCrlfTurnedIntoLf_UnlessItIsBinary(byte[] content, byte[] expected) =>
        ScaffoldFiles.WithLfEndings(content).ShouldBe(expected);

    public void Dispose()
    {
        _project.Dispose();
        _output.Dispose();
        _error.Dispose();
    }

    private LocatedProject Located() => ProjectLocator.Find(_project.Root).ShouldNotBeNull();

    private Shell Shell() => new(new FakeProcesses((call, output, _) =>
    {
        _calls.Add(call);
        Fake(call, output);
        return ExitCode.Succeeded;
    }), _output, _error);

    private void Fake(ExternalCall call, TextWriter output)
    {
        string here = call.WorkingDirectory;
        switch (call.Arguments)
        {
            case ["new", _, "--help", ..]:
                output.WriteLine("  --allow-scripts <no|prompt|yes>  Whether post action scripts run.");
                break;
            case ["new", _, "--name", var name, ..]:
                Generate(Path.Combine(here, name), name);
                break;
            case ["new", "avalonia.mvvm", ..]:
                Generate(Path.Combine(here, "Acme.Shop.Desktop"), "Acme.Shop.Desktop");
                break;
            case ["dlx", _, var directory, ..]:
                Starter(Path.Combine(here, directory));
                break;
            case ["install", ..]:
                Write(Path.Combine(_site, "node_modules", "react", "package.json"), """{ "version": "19.2.1" }""");
                break;
            case ["tool", "run", "kiota", ..]:
                Directory.CreateDirectory(call.Arguments[call.Arguments.ToList().IndexOf("--output") + 1]);
                break;
        }
    }

    private void Generate(string folder, string name)
    {
        WriteAt(Path.Combine(folder, name + ".csproj"), _generated);
        WriteAt(Path.Combine(folder, "Views", "Main.axaml"), "<Window>\r\n</Window>\r\n");
        WriteAt(Path.Combine(folder, "App.axaml"), "generated");
        WriteAt(Path.Combine(folder, "Program.cs"), "generated");
    }

    private static void Starter(string folder)
    {
        WriteAt(Path.Combine(folder, "package.json"), """
            { "name": "starter", "packageManager": "pnpm@10.0.0", "dependencies": { "react": "^19.0.0" }, "devDependencies": { "vite": "8.0.0" } }
            """);
        WriteAt(Path.Combine(folder, "README.md"), "starter");
        WriteAt(Path.Combine(folder, "public", "icon.svg"), "<svg />");
        WriteAt(Path.Combine(folder, "src", "main.tsx"), "starter");
    }

    private void WriteDescriptor(string folder, string json) => Write(Path.Combine(folder, ScaffoldDescriptor.FileName), json);

    private void Write(string relative, string content) => WriteAt(Full(relative), content);

    private static void WriteAt(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Full(string relative) => Path.Combine(_project.Root, relative);
}
