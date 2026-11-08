using System.Xml.Linq;
using CleanArch.Tooling;

namespace CleanArch.Cli.Commands.Kernel;

internal static class ProjectScaffold
{
    private static readonly string[] _generatedPropertiesTheBuildSets = ["TargetFramework", "Nullable", "ImplicitUsings"];

    public static bool Create(LocatedProject project, Shell shell, string folder)
    {
        string shown = ProjectPath.Relative(project.Root, folder);
        bool web = shown.StartsWith(ProjectPath.Shown(ProjectLayout.Web) + "/", StringComparison.Ordinal);
        ScaffoldDescriptor descriptor = ScaffoldDescriptor.Read(folder);

        if (descriptor.Kind is not ("dotnet" or "written"))
        {
            throw new InvalidOperationException(
                $"{shown}/{ScaffoldDescriptor.FileName} is of kind '{descriptor.Kind}', and a .NET project is created by dotnet new or written by the template.");
        }

        string name = descriptor.Name;
        if (descriptor.Kind == "dotnet")
        {
            Generate(shell, descriptor, folder, shown);
        }

        ScaffoldDescriptor.Delete(folder);

        string source = Path.Combine(folder, name + ".csproj");
        bool client = UsesTheClient(project, source);
        string[] projects =
        [
            source,
            .. new[] { UnitTests(project, web ? "Web" : "Presentation", name) }.Where(File.Exists),
            .. client ? ClientProjects(project) : [],
        ];

        AddToTheSolution(project, shell, projects);

        string origin = descriptor.Kind == "dotnet"
            ? descriptor.Package
            : $"the template, in the shape {descriptor.Generator} gives it at {descriptor.Package}";
        shell.Output.WriteLine($"Created {shown} from {origin}. Commit it: the generated files are the project's own from now on.");
        return client;
    }

    public static string FolderOfProject(LocatedProject project, string projectName) =>
        projectName.StartsWith(project.File.Name + ".", StringComparison.Ordinal)
            ? ProjectNames.FolderOf(project.File.Name, projectName[(project.File.Name.Length + 1)..])
            : throw new InvalidOperationException($"'{projectName}' is not a project of {project.File.Name}: its name starts with {project.File.Name}.");

    public static string[][] SolutionAddArguments(string root, string solution, IEnumerable<string> projects) =>
        [.. projects
            .GroupBy(added => ProjectPath.Relative(root, Path.GetDirectoryName(Path.GetDirectoryName(added))!), StringComparer.Ordinal)
            .Select(group => (string[])["sln", solution, "add", "--solution-folder", group.Key, .. group])];

    private static void AddToTheSolution(LocatedProject project, Shell shell, string[] projects)
    {
        string solution = Path.Combine(project.Root, ProjectLayout.Solution(project));
        foreach (string[] arguments in SolutionAddArguments(project.Root, solution, projects))
        {
            Scaffolds.Required(shell, Dotnet.Call(project.Root, arguments));
        }

        foreach (string added in projects)
        {
            Scaffolds.Required(shell, Dotnet.Call(project.Root, "format", added));
        }
    }

    private static bool UsesTheClient(LocatedProject project, string source) =>
        Directory.Exists(Path.Combine(project.Root, ProjectLayout.Client(project)))
        && File.ReadAllText(source).Contains($"{project.File.Name}.Presentation.Client.csproj", StringComparison.Ordinal);

    private static string UnitTests(LocatedProject project, string layer, string name) =>
        Path.Combine(project.Root, "tests", layer, FolderOfProject(project, name + ".UnitTests"), name + ".UnitTests.csproj");

    private static string[] ClientProjects(LocatedProject project) =>
        [.. new[]
        {
            Path.Combine(project.Root, ProjectLayout.Client(project), $"{project.File.Name}.Presentation.Client.csproj"),
            UnitTests(project, "Presentation", $"{project.File.Name}.Presentation.Client"),
        }.Where(File.Exists)];

    private static void Generate(Shell shell, ScaffoldDescriptor descriptor, string folder, string shown)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "cleanarch-scaffold-" + Guid.NewGuid().ToString("N")[..12]);
        string generated = Path.Combine(scratch, descriptor.Name);
        try
        {
            Directory.CreateDirectory(scratch);
            Install(shell, descriptor, scratch, shown);
            RefuseWhatTheProjectLacks(Path.Combine(generated, descriptor.Name + ".csproj"), Path.Combine(folder, descriptor.Name + ".csproj"), descriptor.Drop);
            ScaffoldFiles.NormaliseLineEndings(generated);
            Keep(descriptor, generated, folder);
        }
        finally
        {
            ScaffoldFiles.DeleteFolder(scratch);
        }
    }

    private static void Install(Shell shell, ScaffoldDescriptor descriptor, string scratch, string shown)
    {
        string[] hive = ["--debug:custom-hive", Path.Combine(scratch, "hive"), "--debug:disable-sdk-templates"];
        string why =
            $"Creating {shown} installs {descriptor.Package}, the framework's own templates, from NuGet, so this step needs the network "
            + "and that package on a NuGet source; the output above says what failed. The project's folder is unchanged, "
            + "so running this again once the cause is fixed creates it.";

        Scaffolds.Required(shell, Closed(scratch, ["new", "install", descriptor.Package, .. hive]), why);
        Scaffolds.Required(
            shell,
            Closed(scratch, ["new", descriptor.Template, .. descriptor.Arguments(descriptor.Name), .. DeclinedScriptsWhereOffered(shell, scratch, descriptor.Template, hive), .. hive]),
            why);
    }

    private static string[] DeclinedScriptsWhereOffered(Shell shell, string scratch, string template, string[] hive) =>
        shell.Capture(Closed(scratch, ["new", template, "--help", .. hive]), echo: false).Output.Contains("--allow-scripts", StringComparison.Ordinal)
            ? ["--allow-scripts", "no"]
            : [];

    private static ExternalCall Closed(string directory, IReadOnlyList<string> arguments) => Dotnet.Call(directory, arguments) with { CloseInput = true };

    private static void Keep(ScaffoldDescriptor descriptor, string generated, string folder)
    {
        foreach (string kept in descriptor.Keep)
        {
            string source = Path.Combine(generated, kept);
            string target = Path.Combine(folder, kept);
            if (!File.Exists(source))
            {
                ScaffoldFiles.Copy(source, target, overwrite: false);
            }
            else if (!File.Exists(target))
            {
                File.Copy(source, target);
            }
        }
    }

    private static void RefuseWhatTheProjectLacks(string generated, string own, IReadOnlyList<string> dropped)
    {
        if (!File.Exists(generated))
        {
            throw new InvalidOperationException($"dotnet new wrote no {Path.GetFileName(generated)}, so {Path.GetDirectoryName(own)} is left unchanged.");
        }

        XDocument template = XDocument.Load(generated);
        XDocument project = XDocument.Load(own);
        string[] lacking = [.. LackingPackages(template, project, dropped), .. LackingProperties(template, project)];
        if (lacking.Length > 0)
        {
            throw new InvalidOperationException(
                $"The project file dotnet new wrote declares {string.Join(", ", lacking)}, which {Path.GetFileName(own)} does not, "
                + "so the files it generated may need it: add each to the project file, a package's version to Directory.Packages.props, "
                + $"or change the template's arguments in {ScaffoldDescriptor.FileName}, then run the task again.");
        }
    }

    private static IEnumerable<string> LackingPackages(XDocument template, XDocument project, IReadOnlyList<string> dropped)
    {
        HashSet<string> own = [.. Packages(project), .. dropped];
        return Packages(template).Where(package => !own.Contains(package)).Select(package => $"the package {package}");
    }

    private static IEnumerable<string> Packages(XDocument project) =>
        project.Descendants("PackageReference").Select(reference => (string?)reference.Attribute("Include")).OfType<string>();

    private static IEnumerable<string> LackingProperties(XDocument template, XDocument project) =>
        template.Descendants("PropertyGroup").Elements()
            .Select(property => property.Name.LocalName)
            .Where(property => !_generatedPropertiesTheBuildSets.Contains(property, StringComparer.Ordinal) && !project.Descendants(property).Any())
            .Select(property => $"the property {property}");
}
